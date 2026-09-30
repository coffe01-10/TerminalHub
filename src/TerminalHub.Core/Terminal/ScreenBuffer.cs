using System.Text;

namespace TerminalHub.Core.Terminal;

/// <summary>
/// Scrollable cell grid fed by <see cref="VtParser"/>. Keeps a scrollback history,
/// an optional alternate screen, scroll regions, and a DEC line-drawing charset.
/// </summary>
public sealed class ScreenBuffer
{
    public object SyncRoot { get; } = new();
    public bool BracketedPaste { get; set; }
    public int MouseTracking { get; set; } // 0, 1000 (click), 1002 (drag), 1003 (all motion)
    public bool SgrMouse { get; set; }
    public bool FocusReporting { get; set; }
    public bool SynchronizedOutput { get; private set; }
    private long _syncStarted;
    private TerminalFrame? _heldFrame;

    /// <summary>Incremented once per parser batch / resize; frames are cached against it.</summary>
    public int Version => _version;
    private int _version;
    private int _frameCacheVersion = -1;
    private readonly Dictionary<int, TerminalFrame> _frameCache = new(2);
    // Main/preview share offset 0; keep a few history views without retaining
    // a complete cell array for every offset visited while output is idle.
    private const int MaxCachedFrames = 4;
    internal void BumpVersion() => _version++;

    public void SetSynchronizedOutput(bool enabled)
    {
        if (enabled)
        {
            // A timed-out BSU leaves the flag set. Drop it before capturing, or the
            // next enable would keep showing the frame from the previous window.
            ExpireSynchronizedOutput();
            if (!SynchronizedOutput)
            {
                _heldFrame = CaptureFrame();
                // The held frame just landed in _frameCache[0]; drop it so that after
                // the sync window expires CaptureFrame re-reads the live grid at the
                // same version instead of replaying the pre-sync image forever.
                _frameCache.Clear();
                _syncStarted = Environment.TickCount64;
            }
        }
        SynchronizedOutput = enabled;
        if (!enabled) _heldFrame = null;
    }

    /// <summary>150ms without an end-sync: show the live grid and stop the redraw flag.</summary>
    private void ExpireSynchronizedOutput()
    {
        if (!SynchronizedOutput || Environment.TickCount64 - _syncStarted < 150) return;
        SynchronizedOutput = false;
        _heldFrame = null;
    }

    public TerminalFrame CaptureFrame(int offset = 0)
    {
        lock (SyncRoot)
        {
            // A crashed/unfinished synchronized update must not freeze the screen indefinitely.
            ExpireSynchronizedOutput();
            if (SynchronizedOutput && _heldFrame is { } held)
                return held;
            offset = OnAlternateScreen ? 0 : Math.Clamp(offset, 0, ScrollbackCount);
            if (_frameCacheVersion != _version) { _frameCache.Clear(); _frameCacheVersion = _version; }
            if (_frameCache.TryGetValue(offset, out var cached))
                return cached;
            var cells = new TerminalCell[Rows * Columns];
            Array.Fill(cells, TerminalCell.Blank(TerminalColor.Default));
            for (var row = 0; row < Rows; row++)
            {
                var source = GetLine(ScrollbackCount + row - offset);
                source[..Math.Min(source.Length, Columns)].CopyTo(cells.AsSpan(row * Columns));
            }
            var frame = new TerminalFrame(Columns, Rows, cells, CursorX, CursorY,
                CursorVisible && offset == 0, OnAlternateScreen, ScrollbackCount - offset);
            if (_frameCache.Count == MaxCachedFrames)
            {
                foreach (var oldOffset in _frameCache.Keys)
                    if (oldOffset != 0) { _frameCache.Remove(oldOffset); break; }
            }
            _frameCache[offset] = frame;
            return frame;
        }
    }
    private readonly List<TerminalCell[]> _scrollback = new();
    private TerminalCell[] _screen;
    private TerminalCell[]? _savedScreen;
    /// <summary>Per screen row: this row auto-wrapped into the next one (soft wrap).</summary>
    private bool[] _wrapped;
    private bool[]? _savedWrapped;
    private int _primaryX, _primaryY;
    private bool _primaryPendingWrap;
    /// <summary>Physical row coordinates changed (reflow or screen switch).</summary>
    public int LayoutVersion { get; private set; }
    /// <summary>Soft-wrap flag for each scrollback line (parallel to <see cref="_scrollback"/>).</summary>
    private readonly List<bool> _scrollWrapped = new();
    /// <summary>Cell that a following zero-width rune joins (set by the last PutCluster).</summary>
    private int _lastGlyphRow = -1, _lastGlyphCol;
    private int _scrollbackLimit = 2000;
    /// <summary>Overshoot allowed before a trim runs: lets removal batch instead
    /// of memmoving the whole list once per scrolled line.</summary>
    private const int ScrollbackSlack = 128;

    /// <summary>Drop oldest scrollback lines down to the limit, in batches.
    /// The negative delta keeps scrolled-up views anchored to the same content
    /// (previously trims never notified, so the viewport silently drifted).</summary>
    private void TrimScrollbackIfNeeded()
    {
        if (_scrollback.Count <= _scrollbackLimit + ScrollbackSlack) return;
        var drop = _scrollback.Count - _scrollbackLimit;
        _scrollback.RemoveRange(0, drop);
        _scrollWrapped.RemoveRange(0, drop);
        RemovedLineCount += drop;
        ScrollbackChanged?.Invoke(-drop);
    }

    // Cursor + saved state
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    private struct SavedCursor
    {
        public int X, Y;
        public bool PendingWrap;
        public CellAttrs Attrs;
        public TerminalColor Fg, Bg;
        public bool OriginMode;
    }
    // DECSC on the primary screen must survive a save made on the alternate screen.
    private SavedCursor _savedPrimary, _savedAlt;

    // Parser-visible state
    public CellAttrs CurrentAttrs = CellAttrs.None;
    public string? CurrentHyperlink;
    public TerminalColor CurrentFg = TerminalColor.Default;
    public TerminalColor CurrentBg = TerminalColor.Default;
    public bool AutoWrap = true;
    public bool InsertMode;
    public bool OriginMode;
    public bool ApplicationCursorKeys;
    /// <summary>DECKPAM (ESC =) / DECKPNM (ESC &gt;). Independent of <see cref="ApplicationCursorKeys"/>.</summary>
    public bool ApplicationKeypad;

    private bool _pendingWrap;
    private int _scrollTop, _scrollBottom; // inclusive
    private int _activeCharset;            // 0=G0 1=G1
    private readonly bool[] _decSpecial = new bool[2];

    /// <summary>kitty progressive-enhancement keyboard flags in effect
    /// (CSI &gt;u push / CSI &lt;u pop / CSI =u set). Bit 0 = disambiguate escape codes.</summary>
    public int KittyKeyboardFlags { get; private set; }
    private readonly List<int> _kittyFlagStack = new();

    /// <summary>CSI &gt;flags u — save the current flags and switch to <paramref name="flags"/>.</summary>
    public void KittyPush(int flags)
    {
        _kittyFlagStack.Add(KittyKeyboardFlags);
        KittyKeyboardFlags = flags;
    }

    /// <summary>CSI &lt;count u — pop up to <paramref name="count"/> saved flag sets.</summary>
    public void KittyPop(int count)
    {
        for (var i = 0; i < count && _kittyFlagStack.Count > 0; i++)
        {
            KittyKeyboardFlags = _kittyFlagStack[^1];
            _kittyFlagStack.RemoveAt(_kittyFlagStack.Count - 1);
        }
    }

    /// <summary>CSI =flags;mode u — mode 1 assign, 2 set bits, 3 clear bits.</summary>
    public void KittySet(int flags, int mode) => KittyKeyboardFlags = mode switch
    {
        2 => KittyKeyboardFlags | flags,
        3 => KittyKeyboardFlags & ~flags,
        _ => flags,
    };

    public void ResetKittyKeyboard()
    {
        KittyKeyboardFlags = 0;
        _kittyFlagStack.Clear();
    }

    /// <summary>Rows touched since last <see cref="ClearDirty"/>.</summary>
    public int DirtyRowMin { get; private set; } = int.MaxValue;
    public int DirtyRowMax { get; private set; } = -1;

    /// <summary>Scrollback line-count delta: +1 per appended line, -count on clear.
    /// A scrolled view adds the delta to its offset to stay on the same content.</summary>
    public event Action<int>? ScrollbackChanged;

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int ScrollbackCount => _scrollback.Count;
    /// <summary>Total oldest lines removed; views use the difference to rebase selections.</summary>
    public long RemovedLineCount { get; private set; }
    public bool OnAlternateScreen { get; private set; }
    public string Title { get; private set; } = "";
    public event Action<string>? TitleChanged;
    /// <summary>Last CWD reported by the shell (OSC 7), if any.</summary>
    public string? Cwd { get; private set; }
    public event Action<string>? CwdChanged;

    public ScreenBuffer(int columns = 80, int rows = 24)
    {
        Columns = columns;
        Rows = rows;
        _scrollTop = 0;
        _scrollBottom = rows - 1;
        _screen = NewBlankScreen(columns, rows);
        _wrapped = new bool[rows];
    }

    private TerminalCell[] NewBlankScreen(int cols, int rows)
    {
        var cells = new TerminalCell[cols * rows];
        var blank = TerminalCell.Blank(TerminalColor.Default);
        for (var i = 0; i < cells.Length; i++) cells[i] = blank;
        return cells;
    }

    public ref TerminalCell CellAt(int row, int col) => ref _screen[row * Columns + col];

    /// <summary>History line (index into scrollback) or screen line accessor for rendering.</summary>
    public ReadOnlySpan<TerminalCell> GetScreenRow(int row)
        => _screen.AsSpan(row * Columns, Columns);

    public ReadOnlySpan<TerminalCell> GetScrollbackRow(int index)
        => _scrollback[index].AsSpan();

    /// <summary>Total displayable lines: scrollback + screen (for scroll-view).</summary>
    public int TotalLines => _scrollback.Count + Rows;

    public ReadOnlySpan<TerminalCell> GetLine(int absoluteLine)
        => absoluteLine < _scrollback.Count
            ? _scrollback[absoluteLine].AsSpan()
            : GetScreenRow(absoluteLine - _scrollback.Count);

    public void ClearDirty()
    {
        DirtyRowMin = int.MaxValue;
        DirtyRowMax = -1;
    }

    private void Touch(int row)
    {
        if (row < DirtyRowMin) DirtyRowMin = row;
        if (row > DirtyRowMax) DirtyRowMax = row;
    }

    private void TouchAll()
    {
        DirtyRowMin = 0;
        DirtyRowMax = Rows - 1;
    }

    // ---------- writing ----------

    public void PutChar(char ch)
    {
        if (_decSpecial[_activeCharset] && ch is >= '`' and <= '~')
            ch = DecSpecialMap(ch);
        PutCluster(ch.ToString(), Math.Max(1, GraphemeWidth.OfRune(ch)));
    }

    /// <summary>Write one full code point; runes above the BMP are stored as a
    /// UTF-16 pair inside the cell instead of degrading to a fallback glyph.</summary>
    public void PutRune(int rune)
    {
        if (!IsUnicodeScalar(rune)) rune = 0xFFFD;
        var text = rune <= 0xFFFF ? ((char)rune).ToString() : char.ConvertFromUtf32(rune);
        PutCluster(text, Math.Clamp(GraphemeWidth.OfRune(rune), 1, 2));
    }

    private static bool IsUnicodeScalar(int rune)
        => (uint)rune <= 0x10FFFF && rune is not (>= 0xD800 and <= 0xDFFF);

    /// <summary>
    /// Extend the most recently written cell with a zero-width rune (combining
    /// mark, variation selector, ZWJ member or the second half of a flag pair).
    /// Returns false when no glyph can absorb it — the caller writes it standalone.
    /// </summary>
    public bool AppendToLastGrapheme(int rune)
    {
        if (_lastGlyphRow < 0) return false;
        if (!IsUnicodeScalar(rune)) rune = 0xFFFD;
        ref var cell = ref CellAt(_lastGlyphRow, _lastGlyphCol);
        if (cell.Char is ' ' or '\0') return false;
        cell.Tail = string.Concat(cell.Tail,
            rune <= 0xFFFF ? new string((char)rune, 1) : char.ConvertFromUtf32(rune));
        // VS16 / enclosing keycap flip a text-presentation base into a 2-cell emoji.
        if (!cell.IsWide && GraphemeWidth.EmojiWidthTrigger(rune) && GraphemeWidth.IsEmojiBase(cell.Char))
            TryWidenCell(_lastGlyphRow, _lastGlyphCol);
        Touch(_lastGlyphRow);
        return true;
    }

    /// <summary>True while the last written cell holds a single regional indicator —
    /// a second RI joins it to form one flag glyph.</summary>
    internal bool LastGlyphIsRegionalIndicator
    {
        get
        {
            if (_lastGlyphRow < 0) return false;
            ref var cell = ref CellAt(_lastGlyphRow, _lastGlyphCol);
            return cell.Tail is { Length: 1 }
                && char.IsHighSurrogate(cell.Char)
                && GraphemeWidth.IsRegionalIndicator(char.ConvertToUtf32(cell.Char, cell.Tail[0]));
        }
    }

    /// <summary>Grow the 1-cell glyph at (row,col) into 2 cells when the cell to
    /// its right is free; nudges the cursor past the widened glyph.</summary>
    private void TryWidenCell(int row, int col)
    {
        if (col + 1 >= Columns) return;
        ref var next = ref CellAt(row, col + 1);
        if (next.IsWideContinuation || next.Char is not (' ' or '\0') || next.Tail is not null) return;
        ref var cell = ref CellAt(row, col);
        cell.IsWide = true;
        next.IsWideContinuation = true;
        next.IsWide = false;
        next.Char = '\0';
        next.Tail = null;
        next.Attrs = cell.Attrs;
        next.Hyperlink = cell.Hyperlink;
        next.Fg = cell.Fg;
        next.Bg = cell.Bg;
        if (CursorY == row && CursorX == col + 1)
        {
            if (CursorX + 1 >= Columns) { CursorX = Columns - 1; _pendingWrap = true; }
            else CursorX++;
        }
    }

    private void PutCluster(string text, int width)
    {
        if (_pendingWrap)
        {
            if (AutoWrap)
            {
                _wrapped[CursorY] = true;
                LineFeed();
                CarriageReturn();
            }
            _pendingWrap = false;
        }

        // A wide glyph at the last column only wraps when DECAWM is on; with
        // autowrap disabled it overwrites the last column and stays there.
        if (width == 2 && CursorX == Columns - 1 && AutoWrap)
        {
            _wrapped[CursorY] = true;
            LineFeed();
            CarriageReturn();
        }

        if (InsertMode)
            ShiftRight(CursorY, CursorX, width);

        ref var cell = ref CellAt(CursorY, CursorX);
        // Overwriting half of a wide glyph orphans its other half — clear it.
        if (cell.IsWide && CursorX + 1 < Columns)
            ClearContinuationCell(CursorY, CursorX + 1);
        else if (cell.IsWideContinuation && CursorX > 0)
            ClearWideLead(CursorY, CursorX - 1);

        // A wide glyph with no room for its continuation must not stay IsWide:
        // the renderer would walk one cell past this row.
        var wide = width == 2 && CursorX + 1 < Columns;
        cell.Char = text[0];
        cell.Tail = text.Length > 1 ? text[1..] : null;
        cell.Hyperlink = CurrentHyperlink;
        cell.Attrs = CurrentAttrs;
        cell.Fg = CurrentFg;
        cell.Bg = CurrentBg;
        cell.IsWide = wide;
        cell.IsWideContinuation = false;
        Touch(CursorY);
        _lastGlyphRow = CursorY;
        _lastGlyphCol = CursorX;

        if (wide)
        {
            ref var next = ref CellAt(CursorY, CursorX + 1);
            next.Char = '\0';
            next.Tail = null;
            next.Hyperlink = CurrentHyperlink;
            next.IsWideContinuation = true;
            next.IsWide = false;
            next.Attrs = CurrentAttrs;
            next.Fg = CurrentFg;
            next.Bg = CurrentBg;
        }

        var newX = CursorX + (wide ? 2 : 1);
        if (newX >= Columns)
        {
            CursorX = Columns - 1;
            _pendingWrap = true;
        }
        else
        {
            CursorX = newX;
        }
    }

    private void ClearContinuationCell(int row, int col)
    {
        ref var cont = ref CellAt(row, col);
        cont.Char = '\0';
        cont.Tail = null;
        cont.Hyperlink = null;
        cont.IsWideContinuation = false;
        cont.IsWide = false;
    }

    private void ClearWideLead(int row, int col)
    {
        ref var lead = ref CellAt(row, col);
        lead.Char = ' ';
        lead.Tail = null;
        lead.Hyperlink = null;
        lead.IsWide = false;
    }

    private void ShiftRight(int row, int col, int count)
    {
        _lastGlyphRow = -1;
        var baseIdx = row * Columns;
        for (var i = Columns - 1; i >= col + count; i--)
            _screen[baseIdx + i] = _screen[baseIdx + i - count];
        var blank = TerminalCell.Blank(CurrentBg);
        for (var i = 0; i < count && col + i < Columns; i++)
            _screen[baseIdx + col + i] = blank;
    }

    private void ShiftLeft(int row, int col, int count)
    {
        _lastGlyphRow = -1;
        var baseIdx = row * Columns;
        for (var i = col; i < Columns - count; i++)
            _screen[baseIdx + i] = _screen[baseIdx + i + count];
        var blank = TerminalCell.Blank(CurrentBg);
        for (var i = Math.Max(col, Columns - count); i < Columns; i++)
            _screen[baseIdx + i] = blank;
    }

    public void CarriageReturn()
    {
        _pendingWrap = false;
        CursorX = 0;
    }

    public void Backspace()
    {
        _pendingWrap = false;
        if (CursorX > 0) CursorX--;
        // stepping back over a wide-char continuation: land on glyph start
        ref var cell = ref CellAt(CursorY, CursorX);
        if (cell.IsWideContinuation && CursorX > 0) CursorX--;
    }

    public void Tab()
    {
        _pendingWrap = false;
        CursorX = Math.Min(Columns - 1, (CursorX + 8) / 8 * 8);
    }

    /// <summary>LF/VT/FF: move down; scroll at region bottom.</summary>
    public void LineFeed()
    {
        _pendingWrap = false;
        if (CursorY == _scrollBottom)
            ScrollUpRegion(_scrollTop, _scrollBottom, 1);
        else if (CursorY < Rows - 1)
            CursorY++;
    }

    /// <summary>Reverse index: move up; scroll down at region top.</summary>
    public void ReverseIndex()
    {
        _pendingWrap = false;
        if (CursorY == _scrollTop)
            ScrollDownRegion(_scrollTop, _scrollBottom, 1);
        else if (CursorY > 0)
            CursorY--;
    }

    public void Index() => LineFeed();
    public void NextLine() { LineFeed(); CarriageReturn(); }

    // ---------- scrolling ----------

    /// <summary>Current DECSTBM bounds, 0-based inclusive — CSI S/T scroll this region.</summary>
    public int ScrollRegionTop => _scrollTop;
    public int ScrollRegionBottom => _scrollBottom;

    /// <summary>Scroll [top..bottom] up n lines; top lines of the *full screen* go to scrollback.</summary>
    public void ScrollUpRegion(int top, int bottom, int n)
    {
        if (bottom <= top || n <= 0) return;
        // Scrolling more than the region height equals clearing it — clamp so a
        // runaway "CSI 999999S" cannot loop full-screen copies for minutes.
        n = Math.Min(n, bottom - top + 1);
        _lastGlyphRow = -1;
        for (var i = 0; i < n; i++)
        {
            var rowArr = new TerminalCell[Columns];
            Array.Copy(_screen, top * Columns, rowArr, 0, Columns);
            var leavingWrapped = _wrapped[top];
            Array.Copy(_screen, (top + 1) * Columns, _screen, top * Columns, (bottom - top) * Columns);
            Array.Copy(_wrapped, top + 1, _wrapped, top, bottom - top);
            _wrapped[bottom] = false;
            var blank = TerminalCell.Blank(CurrentBg);
            for (var c = 0; c < Columns; c++) _screen[bottom * Columns + c] = blank;

            if (!OnAlternateScreen && top == 0 && bottom == Rows - 1)
            {
                _scrollback.Add(rowArr);
                _scrollWrapped.Add(leavingWrapped);
                TrimScrollbackIfNeeded();
                ScrollbackChanged?.Invoke(1);
            }
        }
        TouchAll();
    }

    public void ScrollDownRegion(int top, int bottom, int n)
    {
        if (bottom <= top || n <= 0) return;
        n = Math.Min(n, bottom - top + 1);   // same clamp as ScrollUpRegion
        _lastGlyphRow = -1;
        var blank = TerminalCell.Blank(CurrentBg);
        for (var i = 0; i < n; i++)
        {
            Array.Copy(_screen, top * Columns, _screen, (top + 1) * Columns, (bottom - top) * Columns);
            Array.Copy(_wrapped, top, _wrapped, top + 1, bottom - top);
            _wrapped[top] = false;
            for (var c = 0; c < Columns; c++) _screen[top * Columns + c] = blank;
        }
        TouchAll();
    }

    // ---------- cursor ----------

    public void CursorUp(int n)
    {
        _pendingWrap = false;
        // Even below the region, moving up stops at its top. A cursor already
        // above the top can move to the screen edge without being pulled down.
        var min = CursorY >= _scrollTop ? _scrollTop : 0;
        CursorY = Math.Max(min, CursorY - Math.Max(1, n));
    }

    public void CursorDown(int n)
    {
        _pendingWrap = false;
        var max = CursorY <= _scrollBottom ? _scrollBottom : Rows - 1;
        CursorY = Math.Min(max, CursorY + Math.Max(1, n));
    }
    public void CursorForward(int n) { _pendingWrap = false; CursorX = Math.Min(Columns - 1, CursorX + Math.Max(1, n)); }
    public void CursorBack(int n) { _pendingWrap = false; CursorX = Math.Max(0, CursorX - Math.Max(1, n)); }
    public void CursorNextLine(int n) { CursorDown(n); CursorX = 0; }
    public void CursorPrevLine(int n) { CursorUp(n); CursorX = 0; }
    public void CursorHorizontalAbsolute(int col) { _pendingWrap = false; CursorX = Math.Clamp(col - 1, 0, Columns - 1); }
    public void CursorVerticalAbsolute(int row) { _pendingWrap = false; CursorY = Math.Clamp((OriginMode ? row + _scrollTop : row) - 1, 0, RegionBottom()); }

    public void CursorPosition(int row, int col)
    {
        _pendingWrap = false;
        var top = OriginMode ? _scrollTop : 0;
        CursorY = Math.Clamp(row - 1 + top, top, RegionBottom());
        CursorX = Math.Clamp(col - 1, 0, Columns - 1);
    }

    private int RegionTop() => OriginMode ? _scrollTop : 0;
    private int RegionBottom() => OriginMode ? _scrollBottom : Rows - 1;

    public void SaveCursor()
    {
        ref var slot = ref ActiveSavedCursor();
        slot.X = CursorX;
        slot.Y = CursorY;
        slot.PendingWrap = _pendingWrap;
        slot.Attrs = CurrentAttrs;
        slot.Fg = CurrentFg;
        slot.Bg = CurrentBg;
        slot.OriginMode = OriginMode;
    }

    public void RestoreCursor()
    {
        ref var slot = ref ActiveSavedCursor();
        CursorX = Math.Min(slot.X, Columns - 1);
        CursorY = Math.Min(slot.Y, Rows - 1);
        CurrentAttrs = slot.Attrs;
        CurrentFg = slot.Fg;
        CurrentBg = slot.Bg;
        OriginMode = slot.OriginMode;
        _pendingWrap = slot.PendingWrap;
    }

    private ref SavedCursor ActiveSavedCursor()
    {
        if (OnAlternateScreen) return ref _savedAlt;
        return ref _savedPrimary;
    }

    public void ResetSavedCursor(bool bothScreens = false)
    {
        if (bothScreens) _savedPrimary = _savedAlt = default;
        else ActiveSavedCursor() = default;
    }

    public void SetScrollRegion(int top, int bottom)
    {
        _scrollTop = Math.Clamp(top - 1, 0, Rows - 1);
        _scrollBottom = Math.Clamp(bottom - 1, 0, Rows - 1);
        if (_scrollBottom < _scrollTop) (_scrollTop, _scrollBottom) = (_scrollBottom, _scrollTop);
        CursorPosition(1, 1);
    }

    public void ResetScrollRegion()
    {
        _scrollTop = 0;
        _scrollBottom = Rows - 1;
    }

    // ---------- erase ----------

    public void EraseInDisplay(int mode)
    {
        var blank = TerminalCell.Blank(CurrentBg);
        switch (mode)
        {
            case 0: Fill(CursorY, CursorX, Rows - 1, Columns - 1, blank); break;
            case 1: Fill(0, 0, CursorY, CursorX, blank); break;
            case 2:
                var all = NewBlankScreen(Columns, Rows);
                Array.Copy(all, _screen, all.Length);
                Array.Clear(_wrapped);
                TouchAll();
                break;
            case 3: // xterm ED3: scrollback only — the visible screen stays put
                ClearScrollback();
                break;
        }
        _pendingWrap = false;
        _lastGlyphRow = -1;
    }

    public void EraseInLine(int mode)
    {
        var blank = TerminalCell.Blank(CurrentBg);
        switch (mode)
        {
            case 0: Fill(CursorY, CursorX, CursorY, Columns - 1, blank); break;
            case 1: Fill(CursorY, 0, CursorY, CursorX, blank); break;
            case 2: Fill(CursorY, 0, CursorY, Columns - 1, blank); break;
        }
        Touch(CursorY);
        _pendingWrap = false;
    }

    public void EraseChars(int n)
    {
        var blank = TerminalCell.Blank(CurrentBg);
        var end = Math.Min(Columns - 1, CursorX + Math.Max(1, n) - 1);
        Fill(CursorY, CursorX, CursorY, end, blank);
        Touch(CursorY);
    }

    private void Fill(int r1, int c1, int r2, int c2, TerminalCell blank)
    {
        _lastGlyphRow = -1;
        for (var r = r1; r <= r2; r++)
        {
            var cStart = r == r1 ? c1 : 0;
            var cEnd = r == r2 ? c2 : Columns - 1;
            for (var c = cStart; c <= cEnd; c++)
                _screen[r * Columns + c] = blank;
            if (cStart == 0 && cEnd == Columns - 1) _wrapped[r] = false;
            Touch(r);
        }
    }

    // ---------- insert/delete ----------

    public void InsertLines(int n)
    {
        if (CursorY < _scrollTop || CursorY > _scrollBottom) return;
        n = Math.Min(n, _scrollBottom - CursorY + 1);
        ScrollDownRegion(CursorY, _scrollBottom, n);
    }

    public void DeleteLines(int n)
    {
        if (CursorY < _scrollTop || CursorY > _scrollBottom) return;
        n = Math.Min(n, _scrollBottom - CursorY + 1);
        ScrollUpRegion(CursorY, _scrollBottom, n);
    }

    public void DeleteChars(int n) { ShiftLeft(CursorY, CursorX, Math.Max(1, n)); Touch(CursorY); }
    public void InsertChars(int n) { ShiftRight(CursorY, CursorX, Math.Max(1, n)); Touch(CursorY); }

    // ---------- charsets / modes ----------

    public void DesignateCharset(int target, bool decSpecial) => _decSpecial[target] = decSpecial;
    public void SelectCharset(int index) => _activeCharset = index;
    public void ShiftOut() => _activeCharset = 1;   // SO
    public void ShiftIn() => _activeCharset = 0;    // SI

    public void SetCursorVisible(bool visible) => CursorVisible = visible;
    public void SetOriginMode(bool on) { OriginMode = on; CursorPosition(1, 1); }

    public void UseAlternateScreen(bool on)
    {
        if (on == OnAlternateScreen) return;
        OnAlternateScreen = on;
        LayoutVersion++;
        _lastGlyphRow = -1;
        if (on)
        {
            _primaryX = CursorX;
            _primaryY = CursorY;
            _primaryPendingWrap = _pendingWrap;
            _savedScreen = _screen;
            _savedWrapped = _wrapped;
            _screen = NewBlankScreen(Columns, Rows);
            _wrapped = new bool[Rows];
        }
        else
        {
            _screen = _savedScreen ?? NewBlankScreen(Columns, Rows);
            _wrapped = _savedWrapped ?? new bool[Rows];
            _savedScreen = null;
            _savedWrapped = null;
            CursorX = _primaryX;
            CursorY = _primaryY;
            _pendingWrap = _primaryPendingWrap;
        }
        TouchAll();
    }

    public void ClearScrollback()
    {
        RemovedLineCount += _scrollback.Count;
        ScrollbackChanged?.Invoke(-_scrollback.Count);
        _scrollback.Clear();
        _scrollWrapped.Clear();
    }

    public void SetTitle(string title)
    {
        Title = title;
        TitleChanged?.Invoke(title);
    }

    public void SetCwd(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (string.Equals(Cwd, path, StringComparison.Ordinal)) return;
        Cwd = path;
        CwdChanged?.Invoke(path);
    }

    public void AlignTest() // DECALN: fill screen with 'E'
    {
        _lastGlyphRow = -1;
        Array.Clear(_wrapped);
        var cell = new TerminalCell { Char = 'E', Attrs = CellAttrs.None, Fg = TerminalColor.Default, Bg = TerminalColor.Default };
        for (var i = 0; i < _screen.Length; i++) _screen[i] = cell;
        TouchAll();
    }

    // ---------- resize ----------

    public void Resize(int columns, int rows)
    {
        lock (SyncRoot)
        {
            if (columns == Columns && rows == Rows) return;
            if (OnAlternateScreen)
            {
                // TUIs address fixed grid coordinates. Only the inactive primary
                // screen/history reflows; the application redraws its own grid.
                var primaryScreen = _savedScreen!;
                var primaryWrapped = _savedWrapped!;
                ResizePrimary(ref primaryScreen, ref primaryWrapped, columns, rows,
                    ref _primaryX, ref _primaryY, ref _primaryPendingWrap);
                _savedScreen = primaryScreen;
                _savedWrapped = primaryWrapped;
                var grid = NewBlankScreen(columns, rows);
                for (var r = 0; r < Math.Min(rows, Rows); r++)
                {
                    Array.Copy(_screen, r * Columns, grid, r * columns, Math.Min(columns, Columns));
                    // A fixed-grid crop must not leave a lead without its wide
                    // continuation; drawing that run would cross into the next row.
                    if (columns < Columns && grid[(r + 1) * columns - 1].IsWide)
                        grid[(r + 1) * columns - 1] = TerminalCell.Blank(TerminalColor.Default);
                }
                _screen = grid;
                _wrapped = new bool[rows];
                CursorX = Math.Min(CursorX, columns - 1);
                CursorY = Math.Min(CursorY, rows - 1);
                _pendingWrap = false;
            }
            else
            {
                var x = CursorX;
                var y = CursorY;
                ResizePrimary(ref _screen, ref _wrapped, columns, rows,
                    ref x, ref y, ref _pendingWrap);
                CursorX = x;
                CursorY = y;
            }
            Columns = columns;
            Rows = rows;
            _savedAlt.X = Math.Min(_savedAlt.X, columns - 1);
            _savedAlt.Y = Math.Min(_savedAlt.Y, rows - 1);
            _scrollTop = 0;
            _scrollBottom = rows - 1;
            _lastGlyphRow = -1;
            LayoutVersion++;
            BumpVersion();
            TouchAll();
        }
    }

    private void ResizePrimary(ref TerminalCell[] screen, ref bool[] wrapped,
        int columns, int rows, ref int cursorX, ref int cursorY, ref bool pendingWrap)
    {
        var oldHistory = _scrollback.Count;
        var lines = new List<TerminalCell[]>(_scrollback);
        var flags = new List<bool>(_scrollWrapped);
        var lastRow = Rows - 1;
        if (columns != Columns)
        {
            // Unused rows below the prompt must not push it into history when
            // narrowing. Hard blank lines above the cursor remain real lines.
            while (lastRow > Math.Max(cursorY, _savedPrimary.Y) && !wrapped[lastRow]
                && screen.AsSpan(lastRow * Columns, Columns).ToArray().All(ScreenReflow.IsPadding)) lastRow--;
        }
        for (var r = 0; r <= lastRow; r++)
        {
            lines.Add(screen.AsSpan(r * Columns, Columns).ToArray());
            flags.Add(wrapped[r]);
        }
        var cursorLine = oldHistory + cursorY;
        var savedLine = oldHistory + _savedPrimary.Y;
        if (columns != Columns)
        {
            var result = ScreenReflow.Rewrap(lines, flags, columns,
                new(cursorLine, cursorX, pendingWrap), new(savedLine, _savedPrimary.X, _savedPrimary.PendingWrap));
            lines = result.Lines;
            flags = result.Wrapped;
            cursorLine = result.Cursor.Line;
            cursorX = result.Cursor.Column;
            pendingWrap = result.Cursor.Pending;
            savedLine = result.SavedCursor.Line;
            _savedPrimary.X = result.SavedCursor.Column;
            _savedPrimary.PendingWrap = result.SavedCursor.Pending;
        }
        var historyCount = Math.Max(0, lines.Count - rows);
        _scrollback.Clear();
        _scrollWrapped.Clear();
        _scrollback.AddRange(lines.Take(historyCount));
        _scrollWrapped.AddRange(flags.Take(historyCount));
        screen = NewBlankScreen(columns, rows);
        wrapped = new bool[rows];
        for (var r = historyCount; r < lines.Count; r++)
        {
            Array.Copy(lines[r], 0, screen, (r - historyCount) * columns, columns);
            wrapped[r - historyCount] = flags[r];
        }
        cursorY = Math.Clamp(cursorLine - historyCount, 0, rows - 1);
        cursorX = Math.Min(cursorX, columns - 1);
        _savedPrimary.Y = Math.Clamp(savedLine - historyCount, 0, rows - 1);
        _savedPrimary.X = Math.Min(_savedPrimary.X, columns - 1);
        ScrollbackChanged?.Invoke(historyCount - oldHistory);
        TrimScrollbackIfNeeded();
    }

    // ---------- helpers ----------

    /// <summary>Cell width of a single UTF-16 unit — delegates to the shared
    /// <see cref="GraphemeWidth"/> table used by renderer, cursor and selection.</summary>
    public static int CharWidth(char ch) => Math.Max(1, GraphemeWidth.OfChar(ch));

    /// <summary>True when absolute line <paramref name="line"/> soft-wrapped onto the
    /// next line (long logical line split by autowrap — no real newline).</summary>
    public bool IsLineWrapped(int line)
        => line < _scrollback.Count
            ? line < _scrollWrapped.Count && _scrollWrapped[line]
            : _wrapped[line - _scrollback.Count];

    /// <summary>
    /// Flatten a cell row into plain text plus the cell column of each text char
    /// (cluster members share the cell's start column; continuation cells and
    /// unwritten cells contribute a ' ' so column indices stay aligned).
    /// </summary>
    public static (string Text, int[] Cols) FlattenRow(ReadOnlySpan<TerminalCell> row)
    {
        var sb = new StringBuilder(row.Length);
        var cols = new List<int>(row.Length);
        for (var c = 0; c < row.Length; c++)
        {
            ref readonly var cell = ref row[c];
            if (cell.IsWideContinuation) continue;
            if (cell.Char == '\0') { sb.Append(' '); cols.Add(c); continue; }
            sb.Append(cell.Char);
            cols.Add(c);
            if (cell.Tail is { } tail)
                for (var i = 0; i < tail.Length; i++) { sb.Append(tail[i]); cols.Add(c); }
        }
        return (sb.ToString(), cols.ToArray());
    }

    /// <summary>
    /// Extract display text of absolute lines [startLine..endLine] over columns
    /// [startCol..endCol]; soft-wrapped lines join without a newline.
    /// </summary>
    public string ExtractText(int startLine, int startCol, int endLine, int endCol)
    {
        var sb = new StringBuilder();
        for (var line = startLine; line <= endLine; line++)
        {
            var row = GetLine(line);
            var lineStart = sb.Length;
            var c1 = line == startLine ? startCol : 0;
            var c2 = line == endLine ? endCol : Columns - 1;
            c2 = Math.Min(c2, row.Length - 1);
            for (var c = Math.Max(0, c1); c <= c2; c++)
            {
                ref readonly var cell = ref row[c];
                if (cell.IsWideContinuation) continue;
                if (c == row.Length - 1 && line < endLine && IsLineWrapped(line)
                    && ScreenReflow.IsPadding(cell) && GetLine(line + 1)[0].IsWide) continue;
                cell.AppendText(sb);
            }
            // A space at an ordinary soft-wrap boundary belongs to the command.
            if (!IsLineWrapped(line))
                while (sb.Length > lineStart && sb[^1] == ' ') sb.Length--;
            if (line < endLine && !IsLineWrapped(line)) sb.Append('\n');
        }
        return sb.ToString();
    }

    private static char DecSpecialMap(char ch) => ch switch
    {
        '`' => '◆', 'a' => '▒', 'b' => '␉', 'c' => '␌', 'd' => '␍', 'e' => '␊',
        'f' => '°', 'g' => '±', 'h' => '␤', 'i' => '␋', 'j' => '┘', 'k' => '┐',
        'l' => '┌', 'm' => '└', 'n' => '┼', 'o' => '⎺', 'p' => '⎻', 'q' => '─',
        'r' => '⎼', 's' => '⎽', 't' => '├', 'u' => '┤', 'v' => '┴', 'w' => '┬',
        'x' => '│', 'y' => '≤', 'z' => '≥', '{' => 'π', '|' => '≠', '}' => '£',
        '~' => '·', _ => ch,
    };

    /// <summary>
    /// Text of the <paramref name="count"/> screen rows ending at the cursor row —
    /// the interesting region for thumbnails (prompt + recent output).
    /// </summary>
    public string TailText(int count)
    {
        var sb = new StringBuilder();
        var end = Math.Min(Rows - 1, CursorY);
        var start = Math.Max(0, end - count + 1);
        for (var r = start; r <= end; r++)
        {
            var row = GetScreenRow(r);
            var len = row.Length;
            while (len > 0 && (row[len - 1].Char == ' ' || row[len - 1].Char == '\0')) len--;
            for (var c = 0; c < len; c++)
            {
                if (row[c].IsWideContinuation) continue;
                row[c].AppendText(sb);
            }
            if (r < end) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>One preview line: trimmed text + hex of its dominant foreground (null = default).</summary>
    public readonly record struct PreviewLine(string Text, string? FgHex);

    /// <summary>
    /// Same tail region as <see cref="TailText"/> but split per row with each line's
    /// dominant (most frequent non-default) foreground color — for colored thumbnails.
    /// </summary>
    public List<PreviewLine> TailLines(int count)
    {
        var lines = new List<PreviewLine>();
        var end = Math.Min(Rows - 1, CursorY);
        var start = Math.Max(0, end - count + 1);
        var hist = new Dictionary<TerminalColor, int>();
        for (var r = start; r <= end; r++)
        {
            var row = GetScreenRow(r);
            var len = row.Length;
            while (len > 0 && (row[len - 1].Char == ' ' || row[len - 1].Char == '\0')) len--;
            var sb = new StringBuilder(len);
            hist.Clear();
            for (var c = 0; c < len; c++)
            {
                if (!row[c].IsWideContinuation) row[c].AppendText(sb);
                if (!row[c].Fg.IsDefault && len > 0)
                    hist[row[c].Fg] = hist.GetValueOrDefault(row[c].Fg) + 1;
            }
            TerminalColor dom = TerminalColor.Default;
            var best = 0;
            foreach (var kv in hist)
                if (kv.Value > best) { best = kv.Value; dom = kv.Key; }
            lines.Add(new PreviewLine(sb.ToString(), dom.ToRgbHex()));
        }
        return lines;
    }

    /// <summary>Full text of a screen row, trimmed.</summary>
    public string RowText(int row)
    {
        var span = GetScreenRow(row);
        var sb = new StringBuilder(span.Length);
        foreach (ref readonly var cell in span)
        {
            if (cell.IsWideContinuation) continue;
            cell.AppendText(sb);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Trimmed text of scrollback row <paramref name="index"/>.</summary>
    public string ScrollbackText(int index)
    {
        var span = _scrollback[index].AsSpan();
        var sb = new StringBuilder(span.Length);
        foreach (ref readonly var cell in span)
        {
            if (cell.IsWideContinuation) continue;
            cell.AppendText(sb);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>One search hit: <paramref name="Line"/> is a global index (scrollback, then screen).</summary>
    public readonly record struct SearchHit(int Line, string Text, long RemovedLines = 0, int LayoutVersion = 0);

    /// <summary>
    /// Case-insensitive substring search over scrollback + screen, oldest first.
    /// Returns at most <paramref name="max"/> hits.
    /// </summary>
    public List<SearchHit> SearchLines(string term, int max = 200)
    {
        var hits = new List<SearchHit>();
        if (string.IsNullOrEmpty(term)) return hits;
        var idx = 0;
        for (var i = 0; i < _scrollback.Count && hits.Count < max; i++, idx++)
        {
            var t = ScrollbackText(i);
            if (t.Contains(term, StringComparison.OrdinalIgnoreCase))
                hits.Add(new SearchHit(idx, t, RemovedLineCount, LayoutVersion));
        }
        for (var r = 0; r < Rows && hits.Count < max; r++, idx++)
        {
            var t = RowText(r);
            if (t.Contains(term, StringComparison.OrdinalIgnoreCase))
                hits.Add(new SearchHit(idx, t, RemovedLineCount, LayoutVersion));
        }
        return hits;
    }
}
