using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Controls;

/// <summary>
/// Monospace grid renderer + input surface for a <see cref="TerminalEmulator"/>.
/// Batches same-styled cells into runs; supports scrollback via wheel/PgUp.
/// </summary>
public sealed class TerminalView : Control
{
    public static readonly StyledProperty<TerminalEmulator?> EmulatorProperty =
        AvaloniaProperty.Register<TerminalView, TerminalEmulator?>(nameof(Emulator));

    public static readonly StyledProperty<FontFamily> TerminalFontFamilyProperty =
        AvaloniaProperty.Register<TerminalView, FontFamily>(nameof(TerminalFontFamily),
            new FontFamily("Cascadia Code, Consolas, Menlo, DejaVu Sans Mono, monospace"));

    public static readonly StyledProperty<double> TerminalFontSizeProperty =
        AvaloniaProperty.Register<TerminalView, double>(nameof(TerminalFontSize), 13.0);

    private TerminalEmulator? _emulator;
    private Typeface _typeface = new("Cascadia Code, Consolas, Menlo, DejaVu Sans Mono, monospace");
    private Typeface _boldTypeface;
    private double _cellW = 8, _cellH = 16;
    private int _viewOffset;          // lines scrolled up into scrollback
    private bool _cursorOn = true;
    private readonly DispatcherTimer _blink;
    private int _lastCols, _lastRows;

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
        // A terminal takes raw keystrokes — an IME (e.g. Chinese pinyin) would
        // swallow lowercase letters into composition candidates, turning "echo"
        // into committed CJK text before it ever reaches the shell.
        InputMethod.SetIsInputMethodEnabled(this, false);
        _blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _blink.Tick += (_, _) => { _cursorOn = !_cursorOn; if (_emulator is not null) InvalidateVisual(); };
        _blink.Start();
    }

    static TerminalView()
    {
        EmulatorProperty.Changed.AddClassHandler<TerminalView>((v, e) => v.OnEmulatorChanged(e.OldValue as TerminalEmulator, e.NewValue as TerminalEmulator));
        TerminalFontFamilyProperty.Changed.AddClassHandler<TerminalView>((v, _) => { v.MeasureGlyphs(); v.InvalidateVisual(); });
        TerminalFontSizeProperty.Changed.AddClassHandler<TerminalView>((v, _) => { v.MeasureGlyphs(); v.InvalidateVisual(); });
        AffectsRender<TerminalView>(TerminalFontSizeProperty);
    }

    private void OnEmulatorChanged(TerminalEmulator? old, TerminalEmulator? next)
    {
        if (old is not null) old.Changed -= OnBufferChanged;
        _emulator = next;
        if (next is not null) next.Changed += OnBufferChanged;
        _viewOffset = 0;
        MeasureGlyphs();
        InvalidateVisual();
    }

    private void OnBufferChanged()
        => Dispatcher.UIThread.Post(() =>
        {
            if (_viewOffset == 0) InvalidateVisual();
            else InvalidateVisual(); // keep scroll position; just repaint
        });

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
        if (change.Property == BoundsProperty)
            TryResizeEmulator();
    }

    private void TryResizeEmulator()
    {
        if (_emulator is null || Bounds.Width < 10 || Bounds.Height < 10) return;
        var cols = Math.Max(8, (int)(Bounds.Width / _cellW));
        var rows = Math.Max(2, (int)(Bounds.Height / _cellH));
        if (cols == _lastCols && rows == _lastRows) return;
        _lastCols = cols; _lastRows = rows;
        if (cols != _emulator.Buffer.Columns || rows != _emulator.Buffer.Rows)
            _emulator.Resize(cols, rows);
    }

    // ---------- rendering ----------

    public override void Render(DrawingContext ctx)
    {
        var b = _emulator?.Buffer;
        if (b is null)
        {
            ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            return;
        }

        var bg = TerminalPalette.Resolve(TerminalColor.Default, false);
        ctx.DrawRectangle(new SolidColorBrush(bg), null, new Rect(Bounds.Size));

        var scrollback = b.ScrollbackCount;
        for (var r = 0; r < b.Rows; r++)
        {
            var lineIdx = scrollback + r - _viewOffset;
            if (lineIdx < 0) continue;
            var line = lineIdx < scrollback ? b.GetScrollbackRow(lineIdx) : b.GetScreenRow(lineIdx - scrollback);
            RenderRow(ctx, line, r, b.Columns);
        }

        // Cursor
        if (_viewOffset == 0 && b.CursorVisible && _cursorOn && IsFocused)
        {
            var cx = b.CursorX * _cellW;
            var cy = b.CursorY * _cellH;
            ctx.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(180, 0x38, 0xBD, 0xF8)),
                null, new Rect(cx, cy, _cellW * (b.CellAt(b.CursorY, b.CursorX).IsWide ? 2 : 1), _cellH));
            // repaint glyph under cursor in dark
            var cell = b.CellAt(b.CursorY, b.CursorX);
            if (cell.Char is not (' ' or '\0'))
                DrawRun(ctx, cell.Char.ToString(), b.CursorX, b.CursorY,
                    new SolidColorBrush(bg), CellAttrs.None);
        }
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

            // Inverse swaps fg/bg; inverse-default bg becomes light gray.
            var eFg = attrs.HasFlag(CellAttrs.Inverse)
                ? (bg.IsDefault ? TerminalColor.Indexed(7) : bg) : fg;
            var eBg = attrs.HasFlag(CellAttrs.Inverse) ? fg : bg;

            var bgC = TerminalPalette.Resolve(eBg, false);
            var rect = new Rect(startCol * _cellW, row2 * _cellH, cellCount * _cellW, _cellH);
            if (!eBg.IsDefault)
                c2.DrawRectangle(new SolidColorBrush(bgC), null, rect);

            var fgC = TerminalPalette.Resolve(eFg, true);
            if (attrs.HasFlag(CellAttrs.Dim))
                fgC = Color.FromArgb((byte)(fgC.A * 0.6), fgC.R, fgC.G, fgC.B);
            DrawRun(c2, trimmed, startCol, row2, new SolidColorBrush(fgC), attrs);
        }
    }

    private void DrawRun(DrawingContext ctx, string text, int col, int row, IBrush fg, CellAttrs attrs)
    {
        if (string.IsNullOrEmpty(text)) return;
        var tf = attrs.HasFlag(CellAttrs.Bold) ? _boldTypeface : _typeface;
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            tf, TerminalFontSize, fg);
        ft.MaxTextWidth = Bounds.Width;
        ctx.DrawText(ft, new Point(col * _cellW, row * _cellH + (_cellH - ft.Height) / 2));

        if (attrs.HasFlag(CellAttrs.Underline) || attrs.HasFlag(CellAttrs.Strike))
        {
            var pen = new Pen(fg, 1);
            var w = text.Length * _cellW;
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
        if (_emulator is null || e.Text is null) return;
        _viewOffset = 0;
        _emulator.SendText(e.Text);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_emulator is null) return;
        var app = _emulator.Buffer.ApplicationCursorKeys;
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

        if (e.Key == Key.PageUp) { ScrollBy(_emulator.Buffer.Rows - 2); e.Handled = true; return; }
        if (e.Key == Key.PageDown) { ScrollBy(-(_emulator.Buffer.Rows - 2)); e.Handled = true; return; }

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
                _emulator?.SendText(text.Replace("\r\n", "\r").Replace("\n", "\r"));
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
        Focus();
    }
}
