using System.Text;

namespace TerminalHub.Core.Terminal;

/// <summary>
/// Scrollable cell grid fed by <see cref="VtParser"/>. Keeps a scrollback history,
/// an optional alternate screen, scroll regions, and a DEC line-drawing charset.
/// </summary>
public sealed class ScreenBuffer
{
    private readonly List<TerminalCell[]> _scrollback = new();
    private TerminalCell[] _screen;
    private TerminalCell[]? _savedScreen;
    private int _scrollbackLimit = 2000;

    // Cursor + saved state
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    private int _savedX, _savedY;
    private CellAttrs _savedAttrs;
    private TerminalColor _savedFg, _savedBg;
    private bool _savedOriginMode;

    // Parser-visible state
    public CellAttrs CurrentAttrs = CellAttrs.None;
    public TerminalColor CurrentFg = TerminalColor.Default;
    public TerminalColor CurrentBg = TerminalColor.Default;
    public bool AutoWrap = true;
    public bool InsertMode;
    public bool OriginMode;
    public bool ApplicationCursorKeys;

    private bool _pendingWrap;
    private int _scrollTop, _scrollBottom; // inclusive
    private int _activeCharset;            // 0=G0 1=G1
    private readonly bool[] _decSpecial = new bool[2];

    /// <summary>Rows touched since last <see cref="ClearDirty"/>.</summary>
    public int DirtyRowMin { get; private set; } = int.MaxValue;
    public int DirtyRowMax { get; private set; } = -1;

    public event Action? ScrollbackChanged;

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int ScrollbackCount => _scrollback.Count;
    public bool OnAlternateScreen { get; private set; }
    public string Title { get; private set; } = "";
    public event Action<string>? TitleChanged;

    public ScreenBuffer(int columns = 80, int rows = 24)
    {
        Columns = columns;
        Rows = rows;
        _scrollTop = 0;
        _scrollBottom = rows - 1;
        _screen = NewBlankScreen(columns, rows);
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
        if (_pendingWrap)
        {
            if (AutoWrap)
            {
                LineFeed();
                CarriageReturn();
            }
            _pendingWrap = false;
        }

        if (_decSpecial[_activeCharset] && ch is >= '`' and <= '~')
            ch = DecSpecialMap(ch);

        var width = CharWidth(ch);
        if (width == 2 && CursorX == Columns - 1)
        {
            LineFeed();
            CarriageReturn();
        }

        if (InsertMode)
            ShiftRight(CursorY, CursorX, width);

        ref var cell = ref CellAt(CursorY, CursorX);
        cell.Char = ch;
        cell.Attrs = CurrentAttrs;
        cell.Fg = CurrentFg;
        cell.Bg = CurrentBg;
        cell.IsWide = width == 2;
        cell.IsWideContinuation = false;
        Touch(CursorY);

        if (width == 2 && CursorX + 1 < Columns)
        {
            ref var next = ref CellAt(CursorY, CursorX + 1);
            next.Char = '\0';
            next.IsWideContinuation = true;
            next.IsWide = false;
            next.Attrs = CurrentAttrs;
            next.Fg = CurrentFg;
            next.Bg = CurrentBg;
        }

        var newX = CursorX + width;
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

    private void ShiftRight(int row, int col, int count)
    {
        var baseIdx = row * Columns;
        for (var i = Columns - 1; i >= col + count; i--)
            _screen[baseIdx + i] = _screen[baseIdx + i - count];
        var blank = TerminalCell.Blank(CurrentBg);
        for (var i = 0; i < count && col + i < Columns; i++)
            _screen[baseIdx + col + i] = blank;
    }

    private void ShiftLeft(int row, int col, int count)
    {
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

    /// <summary>Scroll [top..bottom] up n lines; top lines of the *full screen* go to scrollback.</summary>
    public void ScrollUpRegion(int top, int bottom, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var rowArr = new TerminalCell[Columns];
            Array.Copy(_screen, top * Columns, rowArr, 0, Columns);
            Array.Copy(_screen, (top + 1) * Columns, _screen, top * Columns, (bottom - top) * Columns);
            var blank = TerminalCell.Blank(CurrentBg);
            for (var c = 0; c < Columns; c++) _screen[bottom * Columns + c] = blank;

            if (!OnAlternateScreen && top == 0 && bottom == Rows - 1)
            {
                _scrollback.Add(rowArr);
                if (_scrollback.Count > _scrollbackLimit)
                    _scrollback.RemoveAt(0);
                ScrollbackChanged?.Invoke();
            }
        }
        TouchAll();
    }

    public void ScrollDownRegion(int top, int bottom, int n)
    {
        var blank = TerminalCell.Blank(CurrentBg);
        for (var i = 0; i < n; i++)
        {
            Array.Copy(_screen, top * Columns, _screen, (top + 1) * Columns, (bottom - top) * Columns);
            for (var c = 0; c < Columns; c++) _screen[top * Columns + c] = blank;
        }
        TouchAll();
    }

    // ---------- cursor ----------

    public void CursorUp(int n) { _pendingWrap = false; CursorY = Math.Max(RegionTop(), CursorY - Math.Max(1, n)); }
    public void CursorDown(int n) { _pendingWrap = false; CursorY = Math.Min(RegionBottom(), CursorY + Math.Max(1, n)); }
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
        _savedX = CursorX; _savedY = CursorY;
        _savedAttrs = CurrentAttrs; _savedFg = CurrentFg; _savedBg = CurrentBg;
        _savedOriginMode = OriginMode;
    }

    public void RestoreCursor()
    {
        CursorX = Math.Min(_savedX, Columns - 1);
        CursorY = Math.Min(_savedY, Rows - 1);
        CurrentAttrs = _savedAttrs; CurrentFg = _savedFg; CurrentBg = _savedBg;
        OriginMode = _savedOriginMode;
        _pendingWrap = false;
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
            case 3:
                var all = NewBlankScreen(Columns, Rows);
                Array.Copy(all, _screen, all.Length);
                if (mode == 3) { _scrollback.Clear(); ScrollbackChanged?.Invoke(); }
                TouchAll();
                break;
        }
        _pendingWrap = false;
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
        for (var r = r1; r <= r2; r++)
        {
            var cStart = r == r1 ? c1 : 0;
            var cEnd = r == r2 ? c2 : Columns - 1;
            for (var c = cStart; c <= cEnd; c++)
                _screen[r * Columns + c] = blank;
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
        if (on)
        {
            _savedScreen = _screen;
            _screen = NewBlankScreen(Columns, Rows);
        }
        else
        {
            _screen = _savedScreen ?? NewBlankScreen(Columns, Rows);
            _savedScreen = null;
        }
        TouchAll();
    }

    public void ClearScrollback()
    {
        _scrollback.Clear();
        ScrollbackChanged?.Invoke();
    }

    public void SetTitle(string title)
    {
        Title = title;
        TitleChanged?.Invoke(title);
    }

    public void AlignTest() // DECALN: fill screen with 'E'
    {
        var cell = new TerminalCell { Char = 'E', Attrs = CellAttrs.None, Fg = TerminalColor.Default, Bg = TerminalColor.Default };
        for (var i = 0; i < _screen.Length; i++) _screen[i] = cell;
        TouchAll();
    }

    // ---------- resize ----------

    public void Resize(int columns, int rows)
    {
        if (columns == Columns && rows == Rows) return;
        var newScreen = NewBlankScreen(columns, rows);
        var copyRows = Math.Min(rows, Rows);
        var copyCols = Math.Min(columns, Columns);
        for (var r = 0; r < copyRows; r++)
            Array.Copy(_screen, r * Columns, newScreen, r * columns, copyCols);
        _screen = newScreen;
        Columns = columns;
        Rows = rows;
        CursorX = Math.Min(CursorX, columns - 1);
        CursorY = Math.Min(CursorY, rows - 1);
        _scrollTop = 0;
        _scrollBottom = rows - 1;
        _pendingWrap = false;
        TouchAll();
    }

    // ---------- helpers ----------

    public static int CharWidth(char ch)
    {
        var c = (int)ch;
        // Common double-width ranges (CJK, fullwidth, Hangul, emoji blocks).
        if (c >= 0x1100 &&
           (c <= 0x115F || c == 0x2329 || c == 0x232A ||
           (c >= 0x2E80 && c <= 0xA4CF && c != 0x303F) ||
           (c >= 0xAC00 && c <= 0xD7A3) ||
           (c >= 0xF900 && c <= 0xFAFF) ||
           (c >= 0xFE30 && c <= 0xFE6F) ||
           (c >= 0xFF00 && c <= 0xFF60) ||
           (c >= 0xFFE0 && c <= 0xFFE6) ||
           (c >= 0x1F300 && c <= 0x1FAFF)))
            return 2;
        return 1;
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

    /// <summary>Text of the last <paramref name="count"/> screen rows (for thumbnails).</summary>
    public string TailText(int count)
    {
        var sb = new StringBuilder();
        var start = Math.Max(0, Rows - count);
        for (var r = start; r < Rows; r++)
        {
            var row = GetScreenRow(r);
            var end = row.Length;
            while (end > 0 && (row[end - 1].Char == ' ' || row[end - 1].Char == '\0')) end--;
            for (var c = 0; c < end; c++)
                sb.Append(row[c].Char == '\0' ? ' ' : row[c].Char);
            if (r < Rows - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Full text of a screen row, trimmed.</summary>
    public string RowText(int row)
    {
        var span = GetScreenRow(row);
        var sb = new StringBuilder(span.Length);
        foreach (var cell in span)
            sb.Append(cell.Char == '\0' ? ' ' : cell.Char);
        return sb.ToString().TrimEnd();
    }
}
