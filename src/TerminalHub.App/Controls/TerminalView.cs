using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Controls;

/// <summary>
/// Monospace grid renderer + input surface for a <see cref="TerminalEmulator"/>.
/// Batches same-styled cells into runs; supports scrollback via wheel/PgUp.
/// </summary>
public class TerminalView : Control
{
    public static readonly StyledProperty<TerminalEmulator?> EmulatorProperty =
        AvaloniaProperty.Register<TerminalView, TerminalEmulator?>(nameof(Emulator));

    public static readonly StyledProperty<FontFamily> TerminalFontFamilyProperty =
        AvaloniaProperty.Register<TerminalView, FontFamily>(nameof(TerminalFontFamily),
            new FontFamily("Cascadia Code, Consolas, Menlo, DejaVu Sans Mono, monospace"));

    public static readonly StyledProperty<double> TerminalFontSizeProperty =
        AvaloniaProperty.Register<TerminalView, double>(nameof(TerminalFontSize), 13.0);

    public static readonly StyledProperty<bool> IsPreviewProperty =
        AvaloniaProperty.Register<TerminalView, bool>(nameof(IsPreview));
    public bool IsPreview { get => GetValue(IsPreviewProperty); set => SetValue(IsPreviewProperty, value); }
    private TerminalEmulator? _emulator;
    private int _dirty = 1;
    private long _lastBlink;
    private bool _attached;
    private Typeface _typeface = new("Cascadia Code, Consolas, Menlo, DejaVu Sans Mono, monospace");
    private Typeface _boldTypeface;
    private double _cellW = 8, _cellH = 16;
    private int _viewOffset;          // lines scrolled up into scrollback
    private bool _cursorOn = true;
    private readonly DispatcherTimer _blink;
    private readonly TerminalImeClient _imeClient;
    private string? _preedit;         // IME composition text (null = not composing)
    private int _preeditCaret;        // caret index inside _preedit, in chars
    private Rect _lastImeRect;        // last rect reported to the IME
    private string _typedTail = "";   // fallback for hidden-cursor TUIs without a bordered prompt
    private int _imeAnchorCol, _imeAnchorRow;
    private bool _imeAnchorValid;     // _imeAnchorCol/Row resolved against the latest frame

    public TerminalEmulator? Emulator
    {
        get => _emulator;
        set => SetValue(EmulatorProperty, value);
    }

    public FontFamily TerminalFontFamily
    {
        get => GetValue(TerminalFontFamilyProperty);
        set => SetValue(TerminalFontFamilyProperty, value);
    }

    public double TerminalFontSize
    {
        get => GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    public TerminalView()
    {
        Focusable = true;
        // IME stays enabled so CJK input methods can compose; committed text
        // arrives via TextInput and inline preedit is drawn at the cursor cell.
        _imeClient = new TerminalImeClient(this);
        _blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _blink.Tick += (_, _) =>
        {
            if (!IsEffectivelyVisible) return;
            var blink = !IsPreview && Environment.TickCount64 - _lastBlink >= 530;
            if (blink) { _lastBlink = Environment.TickCount64; _cursorOn = !_cursorOn; }
            if (!IsPreview && IsFocused)
            {
                var imeRect = _imeClient.CursorRectangle;
                if (imeRect != _lastImeRect)
                {
                    _lastImeRect = imeRect;
                    _imeClient.NotifyCursorRectangleChanged();
                }
            }
            if (Interlocked.Exchange(ref _dirty, 0) != 0 || blink || _emulator?.Buffer.SynchronizedOutput == true)
                InvalidateVisual();
        };
    }

    static TerminalView()
    {
        EmulatorProperty.Changed.AddClassHandler<TerminalView>((v, e) => v.OnEmulatorChanged(e.OldValue as TerminalEmulator, e.NewValue as TerminalEmulator));
        TerminalFontFamilyProperty.Changed.AddClassHandler<TerminalView>((v, _) => { v.MeasureGlyphs(); v.InvalidateVisual(); });
        TerminalFontSizeProperty.Changed.AddClassHandler<TerminalView>((v, _) => { v.MeasureGlyphs(); v.InvalidateVisual(); });
        TextInputMethodClientRequestedEvent.AddClassHandler<TerminalView>(
            (v, e) => { if (!v.IsPreview) { e.Client = v._imeClient; e.Handled = true; } },
            handledEventsToo: true);
        AffectsRender<TerminalView>(TerminalFontSizeProperty);
    }

    private void OnEmulatorChanged(TerminalEmulator? old, TerminalEmulator? next)
    {
        if (old is not null) old.Changed -= OnBufferChanged;
        _emulator = next;
        _typedTail = "";
        _imeAnchorValid = false;
        if (next is not null && _attached) next.Changed += OnBufferChanged;
        if (next is not null) next.Parser.DefaultColorQuery = TerminalPalette.QueryDefaultColor;
        _viewOffset = 0;
        MeasureGlyphs();
        InvalidateVisual();
    }

    private void OnBufferChanged() => Interlocked.Exchange(ref _dirty, 1);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (_emulator is not null) _emulator.Changed += OnBufferChanged;
        ThemeManager.Changed += OnBufferChanged;
        _blink.Interval = TimeSpan.FromMilliseconds(IsPreview ? 50 : 16);
        _blink.Start();
        TryResizeEmulator();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _blink.Stop();
        if (_emulator is not null) _emulator.Changed -= OnBufferChanged;
        ThemeManager.Changed -= OnBufferChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void MeasureGlyphs()
    {
        _typeface = new Typeface(TerminalFontFamily, FontStyle.Normal, FontWeight.Normal);
        _boldTypeface = new Typeface(TerminalFontFamily, FontStyle.Normal, FontWeight.Bold);
        var probe = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            _typeface, TerminalFontSize, Brushes.White);
        _cellW = Math.Max(4, probe.Width);
        _cellH = Math.Max(8, probe.Height * 1.0 + 2);
        TryResizeEmulator();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == IsVisibleProperty)
            TryResizeEmulator();
    }

    private void TryResizeEmulator()
    {
        if (IsPreview || !_attached || !IsEffectivelyVisible || _emulator is null || Bounds.Width < 10 || Bounds.Height < 10) return;
        var cols = Math.Max(8, (int)(Bounds.Width / _cellW));
        var rows = Math.Max(2, (int)(Bounds.Height / _cellH));
        // Compare against the emulator itself, not a view-side cache: split panes
        // share the same emulator, so a cached "last size" would go stale whenever
        // another view resized it while this one was hidden.
        if (cols != _emulator.Buffer.Columns || rows != _emulator.Buffer.Rows)
            _emulator.Resize(cols, rows);
    }

    // ---------- rendering ----------

    public override void Render(DrawingContext ctx)
    {
        using var clip = ctx.PushClip(new Rect(Bounds.Size));
        var b = _emulator?.Buffer.CaptureFrame(_viewOffset);
        if (b is null)
        {
            ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            return;
        }

        var bg = TerminalPalette.Resolve(TerminalColor.Default, false);
        ctx.DrawRectangle(new SolidColorBrush(bg), null, new Rect(Bounds.Size));

        // Preview mirrors the live terminal grid without resizing it. The frame
        // is cropped to the used cell window (all four sides) so a shell that
        // only filled part of its screen stays legible; window edges snap to
        // coarse buckets so ordinary typing doesn't re-zoom on every keystroke.
        // A screen with no slack (fullscreen TUIs) renders the whole grid.
        var row0 = 0;
        var rows = b.Rows;
        var col0 = 0;
        var cols = b.Columns;
        if (IsPreview)
        {
            var r1 = b.Rows - 1;
            while (r1 > 0 && IsBlankRow(b.Cells.AsSpan(r1 * b.Columns, b.Columns))) r1--;
            var r0i = 0;
            while (r0i < r1 && IsBlankRow(b.Cells.AsSpan(r0i * b.Columns, b.Columns))) r0i++;
            var c0i = b.Columns - 1;
            var c1 = 0;
            for (var r = r0i; r <= r1; r++)
            {
                var span = b.Cells.AsSpan(r * b.Columns, b.Columns);
                for (var c = 0; c < span.Length; c++)
                    if (HasInk(span[c])) { if (c < c0i) c0i = c; if (c > c1) c1 = c; }
            }
            if (b.CursorVisible)
            {
                if (b.CursorY > r1) r1 = b.CursorY;
                if (b.CursorY < r0i) r0i = b.CursorY;
                if (b.CursorX > c1) c1 = b.CursorX;
                if (b.CursorX < c0i) c0i = b.CursorX;
            }
            row0 = Math.Max(0, r0i / 4 * 4);
            col0 = Math.Max(0, c0i / 8 * 8);
            var endRow = Math.Min(b.Rows, Math.Max((r1 + 4) / 4 * 4, row0 + Math.Min(8, b.Rows)));
            var endCol = Math.Min(b.Columns, Math.Max((c1 + 8) / 8 * 8, col0 + Math.Min(56, b.Columns)));
            rows = endRow - row0;
            cols = endCol - col0;
        }
        var scale = IsPreview ? Math.Min(Bounds.Width / (cols * _cellW), Bounds.Height / (rows * _cellH)) : 1;
        var x = IsPreview ? (Bounds.Width - cols * _cellW * scale) / 2 - col0 * _cellW * scale : 0;
        var y = IsPreview ? (Bounds.Height - rows * _cellH * scale) / 2 - row0 * _cellH * scale : 0;
        using var transform = ctx.PushTransform(new Matrix(scale, 0, 0, scale, x, y));
        for (var r = row0; r < row0 + rows; r++)
            RenderRow(ctx, b.Cells.AsSpan(r * b.Columns, b.Columns), r, b.Columns);

        // Cursor
        if (_viewOffset == 0 && b.CursorVisible
            && b.CursorY >= row0 && b.CursorY < row0 + rows
            && b.CursorX >= col0 && b.CursorX < col0 + cols
            && (IsPreview || (_cursorOn && IsFocused)))
        {
            var cx = b.CursorX * _cellW;
            var cy = b.CursorY * _cellH;
            ctx.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(180, 0x38, 0xBD, 0xF8)),
                null, new Rect(cx, cy, _cellW * (b.Cells[b.CursorY * b.Columns + b.CursorX].IsWide ? 2 : 1), _cellH));
            // repaint glyph under cursor in dark
            var cell = b.Cells[b.CursorY * b.Columns + b.CursorX];
            if (cell.Char is not (' ' or '\0'))
                DrawRun(ctx, cell.Char.ToString(), b.CursorX, b.CursorY,
                    new SolidColorBrush(bg), CellAttrs.None);
        }

        // Use the same anchor resolver for the inline composition and the OS
        // candidate window, including TUIs that park a hidden cursor in a footer.
        var anchorFound = false;
        int anchorCol, anchorRow;
        if (IsPreview) { anchorCol = b.CursorX; anchorRow = b.CursorY; }
        else
        {
            (anchorCol, anchorRow, anchorFound) = ResolveImeAnchor(b);
        }
        _imeAnchorCol = anchorCol; _imeAnchorRow = anchorRow; _imeAnchorValid = anchorFound;

        // Blinking caret at the input position — the typing indicator a hidden-
        // cursor TUI's input box otherwise lacks. Drawn when the hardware cursor
        // is hidden, or when the echoed input clearly lives somewhere else.
        if (!IsPreview && _imeAnchorValid && _preedit is null
            && _viewOffset == 0 && _cursorOn && IsFocused
            && !b.Cells[_imeAnchorRow * b.Columns + _imeAnchorCol].Attrs.HasFlag(CellAttrs.Inverse)
            && (!b.CursorVisible || _imeAnchorCol != b.CursorX || _imeAnchorRow != b.CursorY))
        {
            var ax = _imeAnchorCol * _cellW;
            var ay = _imeAnchorRow * _cellH;
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8)), 2),
                new Point(ax + 1, ay + 2), new Point(ax + 1, ay + _cellH - 3));
        }

        // IME composition (preedit) overlaid at the input position, with a
        // blinking caret — also visible when the app hides the hardware cursor.
        if (!IsPreview && _preedit is { Length: > 0 } preedit && _viewOffset == 0
            && anchorRow >= row0 && anchorRow < row0 + rows)
        {
            var accent = new SolidColorBrush(Color.FromArgb(0xFF, 0x38, 0xBD, 0xF8));
            var startCol = PreeditStartCol(anchorCol, preedit, b.Columns);
            var px = startCol * _cellW;
            var py = anchorRow * _cellH;
            var pw = PreeditCells(preedit, preedit.Length) * _cellW;
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0x38, 0xBD, 0xF8)),
                null, new Rect(px, py, pw, _cellH));
            DrawRun(ctx, preedit, startCol, anchorRow,
                new SolidColorBrush(TerminalPalette.Resolve(TerminalColor.Default, true)), CellAttrs.None);
            ctx.DrawLine(new Pen(accent, 1),
                new Point(px, py + _cellH - 1.5), new Point(px + pw, py + _cellH - 1.5));
            if (_cursorOn)
            {
                var caretX = px + PreeditCells(preedit, Math.Clamp(_preeditCaret, 0, preedit.Length)) * _cellW;
                ctx.DrawLine(new Pen(accent, 2),
                    new Point(caretX, py + 2), new Point(caretX, py + _cellH - 3));
            }
        }
    }

    /// <summary>
    /// Uses the visible cursor or a bordered TUI input prompt. For other hidden-
    /// cursor apps, falls back to matching recently committed text, then the last
    /// output position. Hidden hardware cursors can be parked on a status bar.
    /// </summary>
    private (int col, int row, bool ok) ResolveImeAnchor(TerminalFrame f)
    {
        // An application-provided caret is authoritative, including after moving
        // left into previously typed text. Echo matching would jump to its end.
        if (f.CursorVisible) return (f.CursorX, f.CursorY, true);

        // Claude Code hides the terminal cursor and parks it on its footer. Its
        // input is the prompt between two horizontal rules, even before any text
        // has been committed (or after Enter/Backspace clears the typed trail).
        for (var r = f.Rows - 2; r > 0; r--)
        {
            var c = 0;
            while (c < f.Columns && f.Cells[r * f.Columns + c].Char is ' ' or '\0') c++;
            if (c >= f.Columns || f.Cells[r * f.Columns + c].Char is not ('>' or '❯' or '›')
                || !IsInputRule(f, r - 1)) continue;
            for (var end = r + 1; end < f.Rows; end++)
            {
                if (!IsInputRule(f, end)) continue;
                // Claude paints its editing cursor with SGR 7, including a blank
                // at end-of-input and two cells for a CJK glyph. The parked VT
                // cursor and the last nonblank cell do not track Left/Home/etc.
                var softwareCursor = FindSoftwareCursor(f, r, end, c + 2);
                if (softwareCursor is { } position)
                    return (position.col, position.row, true);
                var lastRow = end - 1;
                while (lastRow > r && RowInkEnd(f, lastRow) == 0) lastRow--;
                var col = Math.Max(lastRow == r ? c + 2 : 0, RowInkEnd(f, lastRow));
                return (Math.Min(col, f.Columns - 1), lastRow, true);
            }
        }

        var tail = _typedTail;
        for (var len = Math.Min(24, tail.Length); len >= 2; len -= 6)
        {
            var needle = tail[^len..];
            var anyDist = long.MaxValue;   // nearest occurrence overall
            var anyCol = -1; var anyRow = 0;
            var offDist = long.MaxValue;   // nearest occurrence NOT ending on the cursor
            var offCol = -1; var offRow = 0;
            for (var r = 0; r < f.Rows; r++)
            {
                var cells = f.Cells.AsSpan(r * f.Columns, f.Columns);
                var inkEnd = 0;
                for (var i = f.Columns - 1; i >= 0; i--)
                    if (cells[i].Char is not (' ' or '\0'))
                    { inkEnd = i + (cells[i].IsWide ? 2 : 1); break; }
                for (var c = 0; c < f.Columns; c++)
                {
                    if (c >= inkEnd) break;
                    var ci = c;
                    var ti = 0;
                    while (ti < needle.Length && ci < f.Columns)
                    {
                        if (cells[ci].IsWideContinuation) { ci++; continue; }
                        var ch = cells[ci].Char;
                        if (ch is '\0') ch = ' ';
                        if (ch != needle[ti]) break;
                        ci++;
                        ti++;
                    }
                    if (ti == needle.Length)
                    {
                        // Step past a trailing wide-char continuation, and never
                        // anchor into blank padding: a tail ending in spaces would
                        // otherwise drift the anchor right of the visible text.
                        if (ci < f.Columns && cells[ci].IsWideContinuation) ci++;
                        ci = Math.Min(ci, inkEnd);
                        var dist = Math.Abs(r - f.CursorY) * 4096L + Math.Abs(ci - f.CursorX);
                        if (dist < anyDist) { anyDist = dist; anyCol = ci; anyRow = r; }
                        if ((r != f.CursorY || ci != f.CursorX) && dist < offDist)
                        { offDist = dist; offCol = ci; offRow = r; }
                    }
                }
            }
            if (!f.CursorVisible && offCol >= 0)
                return (Math.Min(offCol, f.Columns - 1), offRow, true);
            if (anyCol >= 0)
                return (Math.Min(anyCol, f.Columns - 1), anyRow, true);
        }
        return (Math.Min(f.CursorX, RowInkEnd(f, f.CursorY)), f.CursorY, false);
    }

    private static (int col, int row)? FindSoftwareCursor(TerminalFrame frame, int firstRow, int endRow, int firstCol)
    {
        (int col, int row)? cursor = null;
        for (var row = firstRow; row < endRow; row++)
        {
            for (var col = row == firstRow ? firstCol : 0; col < frame.Columns; col++)
            {
                var cell = frame.Cells[row * frame.Columns + col];
                if (cell.IsWideContinuation || !cell.Attrs.HasFlag(CellAttrs.Inverse)) continue;
                // A multi-character reverse-video selection is not a caret.
                if (cursor is not null) return null;
                cursor = (col, row);
            }
        }
        return cursor;
    }

    private static bool IsInputRule(TerminalFrame frame, int row)
    {
        var ruleCells = 0;
        foreach (var cell in frame.Cells.AsSpan(row * frame.Columns, frame.Columns))
        {
            if (cell.Char is '─' or '━' or '╌' or '═' or '-') ruleCells++;
            else if (cell.Char is not (' ' or '\0')) return false;
        }
        return ruleCells >= Math.Max(4, frame.Columns / 2);
    }

    /// <summary>Cell index right after the last non-blank glyph in frame row
    /// <paramref name="r"/> — the right edge of visible text on that row.</summary>
    private static int RowInkEnd(TerminalFrame f, int r)
    {
        if (r < 0 || r >= f.Rows) return 0;
        var cells = f.Cells.AsSpan(r * f.Columns, f.Columns);
        for (var i = f.Columns - 1; i >= 0; i--)
            if (cells[i].Char is not (' ' or '\0'))
                return i + (cells[i].IsWide ? 2 : 1);
        return 0;
    }

    /// <summary>Cell width of the first <paramref name="len"/> chars of a preedit
    /// string (CJK glyphs occupy two cells, matching <see cref="ScreenBuffer.CharWidth"/>).</summary>
    private static int PreeditCells(string text, int len)
    {
        var w = 0;
        for (var i = 0; i < len && i < text.Length; i++)
            w += ScreenBuffer.CharWidth(text[i]);
        return w;
    }

    /// <summary>Column where the preedit overlay starts: the cursor column, shifted
    /// left when the composition would overflow the right edge.</summary>
    private int PreeditStartCol(int cursorCol, string preedit, int cols)
        => Math.Max(0, Math.Min(cursorCol, cols - PreeditCells(preedit, preedit.Length)));

    /// <summary>A cell carries no preview information when it is an unstyled
    /// blank — no glyph and no painted background (a colored bar still counts).</summary>
    private static bool HasInk(TerminalCell cell)
        => cell.Char is not (' ' or '\0') || !cell.Bg.IsDefault || cell.Attrs.HasFlag(CellAttrs.Inverse);

    private static bool IsBlankRow(ReadOnlySpan<TerminalCell> cells)
    {
        foreach (var cell in cells)
            if (HasInk(cell)) return false;
        return true;
    }

    private void RenderRow(DrawingContext ctx, ReadOnlySpan<TerminalCell> cells, int row, int cols)
    {
        var runStart = -1;
        var runLen = 0;
        var runText = new System.Text.StringBuilder();
        var curFg = default(TerminalColor);
        var curBg = default(TerminalColor);
        var curAttrs = CellAttrs.None;
        var started = false;

        for (var c = 0; c < cols; c++)
        {
            var cell = cells[c];
            if (cell.IsWideContinuation) continue;
            var ch = cell.Char is '\0' ? ' ' : cell.Char;

            var keyMatch = started && cell.Fg == curFg && cell.Bg == curBg && cell.Attrs == curAttrs;
            if (!keyMatch)
            {
                if (started && runLen > 0)
                    Flush(ctx, runText, runStart, runLen, row, curFg, curBg, curAttrs);
                runStart = c;
                runLen = 0;
                runText.Clear();
                curFg = cell.Fg; curBg = cell.Bg; curAttrs = cell.Attrs;
                started = true;
            }
            runText.Append(ch);
            runLen += cell.IsWide ? 2 : 1;
        }
        if (started && runLen > 0)
            Flush(ctx, runText, runStart, runLen, row, curFg, curBg, curAttrs);

        void Flush(DrawingContext c2, System.Text.StringBuilder text, int startCol, int cellCount, int row2,
            TerminalColor fg, TerminalColor bg, CellAttrs attrs)
        {
            var trimmed = text.ToString();
            if (attrs.HasFlag(CellAttrs.Hidden)) trimmed = new string(' ', trimmed.Length);

            // Resolve defaults in their original roles before swapping. Swapping
            // the Default tokens first loses the foreground/background distinction
            // and used to omit the background of Claude's reverse-video cursor.
            var inverse = attrs.HasFlag(CellAttrs.Inverse);
            var fgC = TerminalPalette.Resolve(fg, true);
            var bgC = TerminalPalette.Resolve(bg, false);
            if (inverse) (fgC, bgC) = (bgC, fgC);
            var rect = new Rect(startCol * _cellW, row2 * _cellH, cellCount * _cellW, _cellH);
            if (inverse || !bg.IsDefault)
                c2.DrawRectangle(new SolidColorBrush(bgC), null, rect);

            if (attrs.HasFlag(CellAttrs.Dim))
                fgC = Color.FromArgb((byte)(fgC.A * 0.6), fgC.R, fgC.G, fgC.B);
            DrawRun(c2, trimmed, startCol, row2, new SolidColorBrush(fgC), attrs);
        }
    }

    private void DrawRun(DrawingContext ctx, string text, int col, int row, IBrush fg, CellAttrs attrs)
    {
        if (string.IsNullOrEmpty(text)) return;
        var tf = attrs.HasFlag(CellAttrs.Bold) ? _boldTypeface : _typeface;
        // Fallback CJK fonts do not necessarily advance by exactly two terminal
        // cells. Position them on the grid explicitly, otherwise the visible text
        // drifts away from both the terminal caret and the IME composition anchor.
        var drawCol = col;
        Dictionary<char, FormattedText>? glyphs = null;
        for (var i = 0; i < text.Length;)
        {
            FormattedText ft;
            var width = 0;
            if (text[i] <= '\x7f')
            {
                var start = i;
                while (i < text.Length && text[i] <= '\x7f') i++;
                ft = Format(text[start..i]);
                width = i - start;
            }
            else
            {
                var ch = text[i++];
                glyphs ??= new();
                if (!glyphs.TryGetValue(ch, out ft!)) glyphs[ch] = ft = Format(ch.ToString());
                width = ScreenBuffer.CharWidth(ch);
            }
            ctx.DrawText(ft, new Point(drawCol * _cellW, row * _cellH + (_cellH - ft.Height) / 2));
            drawCol += width;
        }

        FormattedText Format(string value) => new(value, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, tf, TerminalFontSize, fg);

        if (attrs.HasFlag(CellAttrs.Underline) || attrs.HasFlag(CellAttrs.Strike))
        {
            var pen = new Pen(fg, 1);
            var w = text.Sum(ScreenBuffer.CharWidth) * _cellW; // wide glyphs take 2 cells
            if (attrs.HasFlag(CellAttrs.Underline))
                ctx.DrawLine(pen, new Point(col * _cellW, row * _cellH + _cellH - 2),
                    new Point(col * _cellW + w, row * _cellH + _cellH - 2));
            if (attrs.HasFlag(CellAttrs.Strike))
                ctx.DrawLine(pen, new Point(col * _cellW, row * _cellH + _cellH / 2),
                    new Point(col * _cellW + w, row * _cellH + _cellH / 2));
        }
    }

    // ---------- input ----------

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (IsPreview || _emulator is null || e.Text is null) return;
        _viewOffset = 0;
        _emulator.SendText(e.Text);
        _typedTail += e.Text;
        if (_typedTail.Length > 32) _typedTail = _typedTail[^32..];
        e.Handled = true;
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (_preedit is not null) { _preedit = null; InvalidateVisual(); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_emulator is null) return;
        if (IsPreview) return;
        // Keys consumed by an IME composition (VK_PROCESSKEY etc.) must not reach
        // the shell — e.g. Enter that picks a candidate would double-submit.
        if (e.Key is Key.ImeProcessed or Key.ImeAccept or Key.ImeConvert
            or Key.ImeNonConvert or Key.ImeModeChange or Key.NoName)
        {
            e.Handled = true;
            return;
        }
        // Maintain the typed-text trail that anchors IME feedback to the app's
        // input box. Only genuine non-text keys invalidate it — some IMEs pass
        // letters through as plain KeyDown when a composition starts, and those
        // must NOT wipe the trail.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            _typedTail = "";
        else switch (e.Key)
        {
            case Key.Back:
                if (_typedTail.Length > 0) _typedTail = _typedTail[..^1];
                break;
            case Key.Enter or Key.Escape or Key.Tab
                or Key.Up or Key.Down or Key.Left or Key.Right
                or Key.Home or Key.End or Key.Delete or Key.Insert
                or Key.PageUp or Key.PageDown
                or >= Key.F1 and <= Key.F24:
                _typedTail = "";
                break;
        }
        var app = _emulator.Buffer.ApplicationCursorKeys;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Enter)
        {
            // CSI-u is only valid once the app negotiated the kitty keyboard
            // protocol (disambiguate flag). Outside it, plain \r keeps readline
            // from eating a stray escape sequence.
            var kitty = (_emulator.Buffer.KittyKeyboardFlags & 0b1) != 0;
            _emulator.SendText(kitty ? "\x1b[13;2u" : "\r");
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Tab)
        {
            _emulator.SendText("\x1b[Z");
            e.Handled = true;
            return;
        }
        var send = e.Key switch
        {
            Key.Enter or Key.Return => "\r",
            Key.Back => "\x7f",
            Key.Tab => "\t",
            Key.Escape => "\x1b",
            Key.Up => app ? "\x1bOA" : "\x1b[A",
            Key.Down => app ? "\x1bOB" : "\x1b[B",
            Key.Right => app ? "\x1bOC" : "\x1b[C",
            Key.Left => app ? "\x1bOD" : "\x1b[D",
            Key.Home => "\x1b[H",
            Key.End => "\x1b[F",
            Key.Delete => "\x1b[3~",
            Key.Insert => "\x1b[2~",
            _ => null,
        };

        if (e.Key == Key.PageUp)
        {
            // Alt-screen apps (less, vim, htop) own the scrollback — the key
            // belongs to them; local scrollback scroll applies to the main screen.
            if (_emulator.Buffer.OnAlternateScreen) _emulator.SendText("\x1b[5~");
            else ScrollBy(_emulator.Buffer.Rows - 2);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.PageDown)
        {
            if (_emulator.Buffer.OnAlternateScreen) _emulator.SendText("\x1b[6~");
            else ScrollBy(-(_emulator.Buffer.Rows - 2));
            e.Handled = true;
            return;
        }

        if (send is null)
        {
            // Ctrl+Shift+V paste, plain Ctrl+key → control byte
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    PasteClipboard();
                    e.Handled = true;
                    return;
                }
                if (e.Key >= Key.A && e.Key <= Key.Z)
                {
                    _emulator.SendBytes([(byte)(e.Key - Key.A + 1)]);
                    _viewOffset = 0;
                    e.Handled = true;
                    return;
                }
            }
            return;
        }

        _viewOffset = 0;
        _emulator.SendText(send);
        e.Handled = true;
    }

    private async void PasteClipboard()
    {
        try
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            var text = clip is null ? null : await clip.GetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                _viewOffset = 0;
                _emulator?.PasteText(text);
            }
        }
        catch { }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ScrollBy((int)(-e.Delta.Y * 3));
        e.Handled = true;
    }

    private void ScrollBy(int delta)
    {
        if (_emulator is null) return;
        _viewOffset = Math.Clamp(_viewOffset + delta, 0, _emulator.Buffer.ScrollbackCount);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsPreview) Focus();
    }

    /// <summary>
    /// Bridges Avalonia's text-input-method plumbing to the terminal grid. The OS
    /// IME reads <see cref="TextInputMethodClient.CursorRectangle"/> to anchor its
    /// candidate window on the cursor cell and pushes the composition string via
    /// <see cref="TextInputMethodClient.SetPreeditText(string?, int?)"/> for inline
    /// display; committed text arrives as ordinary TextInput.
    /// </summary>
    private sealed class TerminalImeClient(TerminalView view) : TextInputMethodClient
    {
        public override Visual TextViewVisual => view;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => false;
        public override string SurroundingText => "";
        public override TextSelection Selection { get; set; }

        public override Rect CursorRectangle
        {
            get
            {
                var buf = view._emulator?.Buffer;
                if (buf is null || buf.Columns == 0)
                    return new Rect(0, 0, view._cellW, view._cellH);
                // The IME can query this before the next Render, immediately
                // after output or composition starts. Do not use a stale anchor.
                var (baseCol, baseRow, _) = view.ResolveImeAnchor(buf.CaptureFrame());
                var row = Math.Clamp(baseRow, 0, buf.Rows - 1);
                // While composing, anchor to the caret inside the preedit overlay
                // so the candidate window tracks it.
                if (view._preedit is { Length: > 0 } p)
                {
                    var startCol = view.PreeditStartCol(baseCol, p, buf.Columns);
                    var caret = PreeditCells(p, Math.Clamp(view._preeditCaret, 0, p.Length));
                    return new Rect((startCol + caret) * view._cellW, row * view._cellH,
                        Math.Max(2, view._cellW * 0.15), view._cellH);
                }
                return new Rect(Math.Clamp(baseCol, 0, buf.Columns - 1) * view._cellW,
                    row * view._cellH, view._cellW, view._cellH);
            }
        }

        public void NotifyCursorRectangleChanged() => RaiseCursorRectangleChanged();

        public override void SetPreeditText(string? preeditText)
            => SetPreeditText(preeditText, preeditText?.Length);

        public override void SetPreeditText(string? preeditText, int? cursorPos)
        {
            view._preedit = string.IsNullOrEmpty(preeditText) ? null : preeditText;
            view._preeditCaret = Math.Clamp(cursorPos ?? view._preedit?.Length ?? 0, 0, view._preedit?.Length ?? 0);
            NotifyCursorRectangleChanged();
            view.InvalidateVisual();
        }
    }
}
