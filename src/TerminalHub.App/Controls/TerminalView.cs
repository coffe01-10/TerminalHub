using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Controls;

/// <summary>
/// Monospace grid renderer + input surface for a <see cref="TerminalEmulator"/>.
/// Batches same-styled cells into runs; supports scrollback via wheel/PgUp.
/// </summary>
public partial class TerminalView : Control
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

    /// <summary>Text highlighted inside the viewport (search, find). Null/empty = off.</summary>
    public static readonly StyledProperty<string?> SearchQueryProperty =
        AvaloniaProperty.Register<TerminalView, string?>(nameof(SearchQuery));
    public string? SearchQuery { get => GetValue(SearchQueryProperty); set => SetValue(SearchQueryProperty, value); }

    /// <summary>True while the viewport is scrolled above the bottom — drives the
    /// "back to bottom" affordance. Read-only, updated as the view scrolls.</summary>
    public static readonly DirectProperty<TerminalView, bool> IsScrolledUpProperty =
        AvaloniaProperty.RegisterDirect<TerminalView, bool>(nameof(IsScrolledUp), v => v.IsScrolledUp);
    private bool _isScrolledUp;
    public bool IsScrolledUp => _isScrolledUp;

    private TerminalEmulator? _emulator;
    private int _dirty = 1;
    private long _lastBlink;
    private bool _attached;
    private Typeface _typeface = new("Cascadia Code, Consolas, Menlo, DejaVu Sans Mono, monospace");
    private Typeface _boldTypeface;
    private double _cellW = 8, _cellH = 16;
    private int _viewOffset;          // lines scrolled up into scrollback
    private int _scrollDrift;         // scrollback lines appended while scrolled up
    private bool _cursorOn = true;
    private readonly DispatcherTimer _blink;
    private readonly Dictionary<Color, IBrush> _brushCache = new();
    private readonly TerminalImeClient _imeClient;
    private string? _preedit;         // IME composition text (null = not composing)
    private int _preeditCaret;        // caret index inside _preedit, in chars
    private Rect _lastImeRect;        // last rect reported to the IME
    private (ScreenBuffer? Buffer, int Version, int Offset, double Width, double Height,
        string TypedTail, string? Preedit, int Caret, bool Sync) _lastImeInputs;
    private bool _imeRefreshPending = true;
    private string _typedTail = "";   // fallback for hidden-cursor TUIs without a bordered prompt
    private int _imeAnchorCol, _imeAnchorRow;
    private bool _imeAnchorValid;     // _imeAnchorCol/Row resolved against the latest frame
    private readonly record struct InputRegion(int FirstRow, int EndRow, int FirstCol);
    private readonly record struct InputPosition(int Col, int Row, bool Valid, InputRegion? Region = null);
    private TerminalFrame? _inputFrame;
    private string? _inputTail;
    private InputPosition _inputPosition;
    private (TerminalFrame? Frame, InputPosition Input, string? Text, int Caret) _compositionKey;
    private PreeditLayout? _compositionLayout;

    // Mouse selection — absolute buffer lines (scrollback indexes stay stable).
    private (int line, int col)? _selAnchor, _selEnd;
    private bool _selecting;
    /// <summary>Double- or triple-click kept a selection whose inclusive ends may be the same cell.</summary>
    private bool _wordSelection;
    private Point _lastPointer;
    private int _applicationMouseButton = -1;
    private (int column, int row)? _lastMouseCell;
    private IPointer? _applicationPointer;
    // The pointer holding the local-selection capture — a second pointer (pen,
    // touch, another mouse) must not move or release someone else's selection.
    private IPointer? _selectionPointer;
    private long _seenRemovedLines;
    private int _seenLayoutVersion;

    // Search state — hits are recomputed lazily against buffer version.
    private int _hitLine = -1, _hitCol = -1, _hitLen;  // current match marker
    private int _hitsVersion = -1;
    private string? _hitsQuery;
    private List<(int line, int col, int len)>? _hits;

    // Text-layout caches (FormattedText is costly; terminal rows repeat heavily).
    private readonly Dictionary<(string text, bool bold, Color fg), FormattedText> _textCache = new();
    private readonly System.Text.StringBuilder _runText = new();
    private sealed record PaintedRow(TerminalCell[] Cells, DrawingGroup Drawing);
    private readonly List<PaintedRow> _paintedRows = new();
    private bool _cacheRows;
    private TerminalColors _colors = TerminalPalette.ThemeColors;
    private TerminalFrame? _colorsFrame;
    private TerminalColorScheme _colorsScheme;

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
        InitializeContentActions();
        Focusable = true;
        // IME stays enabled so CJK input methods can compose; committed text
        // arrives via TextInput and inline preedit is drawn at the cursor cell.
        _imeClient = new TerminalImeClient(this);
        _blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _blink.Tick += OnRefreshTick;
        MeasureGlyphs();
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (!IsEffectivelyVisible) return;
        var buf = _emulator?.Buffer;
        var dirty = Interlocked.Exchange(ref _dirty, 0) != 0;
        var drift = Interlocked.Exchange(ref _scrollDrift, 0);
        // Idle ticks do not need the buffer lock. Render and immediate selection
        // queries still synchronize from the current frame before using coordinates.
        if (dirty || drift != 0 || buf?.LayoutVersion != _seenLayoutVersion
            || buf?.RemovedLineCount != _seenRemovedLines)
            SynchronizeCoordinates();
        if (drift != 0 && buf is not null)
        {
            _viewOffset = _viewOffset > 0
                ? Math.Clamp(_viewOffset + drift, 0, buf.ScrollbackCount)
                : 0;
            SetScrolledUp(_viewOffset > 0);
            dirty = true;
        }
        var blink = !IsPreview && IsFocused
            && Environment.TickCount64 - _lastBlink >= 530;
        if (blink) { _lastBlink = Environment.TickCount64; _cursorOn = !_cursorOn; }
        if (_selecting) DragScrollStep();
        var sync = buf?.SynchronizedOutput == true;
        if (!IsPreview && IsFocused)
        {
            var inputs = (buf, buf?.Version ?? -1, _viewOffset, _cellW, _cellH,
                _typedTail, _preedit, _preeditCaret, sync);
            if (_imeRefreshPending || sync || inputs != _lastImeInputs)
            {
                _imeRefreshPending = false;
                _lastImeInputs = inputs;
                var imeRect = _imeClient.CursorRectangle;
                if (imeRect != _lastImeRect)
                {
                    _lastImeRect = imeRect;
                    _imeClient.NotifyCursorRectangleChanged();
                }
            }
        }
        if (dirty || blink || sync) InvalidateVisual();
    }

    static TerminalView()
    {
        EmulatorProperty.Changed.AddClassHandler<TerminalView>((v, e) => v.OnEmulatorChanged(e.OldValue as TerminalEmulator, e.NewValue as TerminalEmulator));
        TerminalFontFamilyProperty.Changed.AddClassHandler<TerminalView>((v, _) => { v.MeasureGlyphs(); v.InvalidateVisual(); });
        TerminalFontSizeProperty.Changed.AddClassHandler<TerminalView>((v, _) => { v.MeasureGlyphs(); v.InvalidateVisual(); });
        TextInputMethodClientRequestedEvent.AddClassHandler<TerminalView>(
            (v, e) => { if (!v.IsPreview) { e.Client = v._imeClient; e.Handled = true; } },
            handledEventsToo: true);
        SearchQueryProperty.Changed.AddClassHandler<TerminalView>((v, _) =>
        {
            v._hits = null;
            v._hitLine = -1;
            v.InvalidateVisual();
        });
        AffectsRender<TerminalView>(TerminalFontSizeProperty);
    }

    private void OnEmulatorChanged(TerminalEmulator? old, TerminalEmulator? next)
    {
        if (old is not null)
        {
            old.Changed -= OnBufferChanged;
            old.Buffer.ScrollbackChanged -= OnScrollbackChanged;
        }
        _applicationPointer?.Capture(null);
        _selectionPointer?.Capture(null);
        if (!IsPreview && IsFocused) old?.SendFocus(false);
        _emulator = next;
        _seenRemovedLines = next?.Buffer.RemovedLineCount ?? 0;
        _seenLayoutVersion = next?.Buffer.LayoutVersion ?? 0;
        if (!IsPreview && IsFocused) next?.SendFocus(true);
        _applicationPointer = null;
        _selectionPointer = null;
        _applicationMouseButton = -1;
        _lastMouseCell = null;
        _selecting = false;
        _typedTail = "";
        _preedit = null;
        _preeditCaret = 0;
        _inputFrame = null;
        _compositionLayout = null;
        _compositionKey = default;
        _paintedRows.Clear();
        _cacheRows = false;
        _imeAnchorValid = false;
        _selAnchor = _selEnd = null;
        _wordSelection = false;
        _hits = null;
        _hitLine = -1;
        if (next is not null && _attached) next.Changed += OnBufferChanged;
        if (next is not null)
        {
            // The PTY belongs to the session, so previews and split/popout views
            // must answer color queries with the same session appearance.
            next.Parser.DefaultColorQuery = foreground => TerminalPalette.QueryDefaultColor(foreground,
                TerminalPalette.ForFrame(next.Buffer.CaptureFrame(), next.ColorScheme));
            // Conditional like Changed above — a detached StagePreview must not
            // stay in the buffer's delegate list after it leaves the tree.
            if (_attached) next.Buffer.ScrollbackChanged += OnScrollbackChanged;
        }
        ResetScroll();
        TryResizeEmulator();
        NotifyImePositionChanged();
        InvalidateVisual();
    }

    private void OnBufferChanged() => Interlocked.Exchange(ref _dirty, 1);

    private void OnScrollbackChanged(int delta) => Interlocked.Add(ref _scrollDrift, delta);

    private void SynchronizeCoordinates()
    {
        var buf = _emulator?.Buffer;
        if (buf is null) return;
        lock (buf.SyncRoot)
        {
            var removed = buf.RemovedLineCount - _seenRemovedLines;
            _seenRemovedLines = buf.RemovedLineCount;
            if (_seenLayoutVersion != buf.LayoutVersion)
            {
                // Physical rows no longer identify the same text after reflow
                // or a primary/alternate screen switch. Never copy stale cells.
                _seenLayoutVersion = buf.LayoutVersion;
                _selAnchor = _selEnd = null;
                _wordSelection = false;
                _hitLine = -1;
                _hits = null;
                ResetScroll();
                return;
            }
            if (removed == 0) return;
            if (_selAnchor is { } a && _selEnd is { } e)
            {
                // If either endpoint was discarded, the complete selection is
                // gone. Clear it rather than silently copying replacement lines.
                if (a.line < removed || e.line < removed)
                {
                    _selAnchor = _selEnd = null;
                    _wordSelection = false;
                }
                else
                {
                    _selAnchor = (a.line - (int)removed, a.col);
                    _selEnd = (e.line - (int)removed, e.col);
                }
            }
            _hitLine = _hitLine < removed ? -1 : _hitLine - (int)removed;
            _hits = null;
        }
    }

    private void SetScrolledUp(bool value) => SetAndRaise(IsScrolledUpProperty, ref _isScrolledUp, value);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        OnThemeChanged(); // A detached pane may have missed a theme change.
        if (_emulator is not null) _emulator.Changed += OnBufferChanged;
        if (_emulator is not null) _emulator.Buffer.ScrollbackChanged += OnScrollbackChanged;
        ThemeManager.Changed += OnThemeChanged;
        // A hidden ancestor flip (single↔split, pop-out dock) arranges us again
        // without touching Bounds/IsVisible — recheck the emulator size on every
        // layout pass so the grid can never stay stuck at a stale width.
        LayoutUpdated += OnLayoutUpdated;
        _blink.Interval = TimeSpan.FromMilliseconds(IsPreview ? 50 : 16);
        _blink.Start();
        TryResizeEmulator();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        ClearComposition();
        _inputFrame = null;
        _paintedRows.Clear();
        _blink.Stop();
        LayoutUpdated -= OnLayoutUpdated;
        if (_emulator is not null) _emulator.Changed -= OnBufferChanged;
        // Without this a detached StagePreview (session popped out) stays in the
        // buffer's delegate list until the session closes — repeated popout/
        // reattach accumulates dead visual subtrees (and their text caches).
        if (_emulator is not null) _emulator.Buffer.ScrollbackChanged -= OnScrollbackChanged;
        ThemeManager.Changed -= OnThemeChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private Matrix? _lastImeTransform;
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        TryResizeEmulator();
        if (IsPreview) return;
        var root = TopLevel.GetTopLevel(this);
        var transform = root is null ? null : this.TransformToVisual(root);
        if (transform != _lastImeTransform)
        {
            _lastImeTransform = transform;
            NotifyImePositionChanged();
        }
    }

    private void NotifyImePositionChanged()
    {
        if (IsPreview) return;
        _imeRefreshPending = true;
        _imeClient.NotifyCursorRectangleChanged();
    }

    private void ClearComposition()
    {
        if (_preedit is null) return;
        _preedit = null;
        _preeditCaret = 0;
        _compositionLayout = null;
        _compositionKey = default;
        NotifyImePositionChanged();
        InvalidateVisual();
    }

    private void OnThemeChanged()
    {
        _colorsFrame = null;
        _textCache.Clear();   // cached runs carry resolved theme colors
        _paintedRows.Clear();
        Interlocked.Exchange(ref _dirty, 1);
    }

    private void MeasureGlyphs()
    {
        _typeface = new Typeface(TerminalFontFamily, FontStyle.Normal, FontWeight.Normal);
        _boldTypeface = new Typeface(TerminalFontFamily, FontStyle.Normal, FontWeight.Bold);
        var probe = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            _typeface, TerminalFontSize, Brushes.White);
        _cellW = Math.Max(4, probe.Width);
        _cellH = Math.Max(8, probe.Height * 1.0 + 2);
        _textCache.Clear();   // typeface/size changed — cached layouts are stale
        _paintedRows.Clear();
        TryResizeEmulator();
        NotifyImePositionChanged();
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
        {
            _emulator.Resize(cols, rows);
            NotifyImePositionChanged();
        }
    }

    // ---------- rendering ----------

    public override void Render(DrawingContext ctx)
    {
        SynchronizeCoordinates();
        using var clip = ctx.PushClip(new Rect(Bounds.Size));
        var b = _emulator?.Buffer.CaptureFrame(_viewOffset);
        if (b is null)
        {
            ctx.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            return;
        }

        if (!ReferenceEquals(_colorsFrame, b) || _colorsScheme != _emulator!.ColorScheme)
        {
            var colors = TerminalPalette.ForFrame(b, _emulator!.ColorScheme);
            if (colors != _colors) { _paintedRows.Clear(); _textCache.Clear(); }
            _colors = colors;
            _colorsFrame = b;
            _colorsScheme = _emulator.ColorScheme;
        }
        var bg = _colors.Background;
        ctx.DrawRectangle(Brush(bg), null, new Rect(Bounds.Size));

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
        var cacheRows = _cacheRows;
        _cacheRows = true;
        for (var r = row0; r < row0 + rows; r++)
            // A quick switch needs its first image immediately. Build retained
            // row drawings only when the same session is rendered again.
            if (cacheRows) RenderCachedRow(ctx, b, r);
            else RenderRow(ctx, b.Cells, r, b.Columns);

        // Mouse selection — translucent overlay on top of the glyphs.
        if (!IsPreview && _selAnchor is { } sa && _selEnd is { } se && (sa != se || _wordSelection))
        {
            var (sl, sc0) = sa.CompareTo(se) <= 0 ? sa : se;
            var (el, ec1) = sa.CompareTo(se) <= 0 ? se : sa;
            var selBrush = Brush(_colors.Selection);
            for (var v = row0; v < row0 + rows; v++)
            {
                var abs = b.BaseLine + v;
                if (abs < sl || abs > el) continue;
                var c0 = abs == sl ? sc0 : 0;
                var c1 = abs == el ? ec1 : b.Columns - 1;
                // snap to glyph edges: never split a wide glyph in half
                var span = b.Cells.AsSpan(v * b.Columns, b.Columns);
                if (c0 > 0 && c0 < span.Length && span[c0].IsWideContinuation) c0--;
                if (c1 >= 0 && c1 < span.Length - 1 && span[c1].IsWide) c1++;
                ctx.DrawRectangle(selBrush, null,
                    new Rect(c0 * _cellW, v * _cellH, (c1 - c0 + 1) * _cellW, _cellH));
            }
        }

        // Search matches — flat highlight per hit, current match stronger.
        if (!IsPreview && SearchQuery is { Length: > 0 } query)
        {
            var matchBrush = Brush(_colors.Match);
            var currentBrush = Brush(_colors.CurrentMatch);
            for (var v = row0; v < row0 + rows; v++)
            {
                var span = b.Cells.AsSpan(v * b.Columns, b.Columns);
                var (text, hitCols) = ScreenBuffer.FlattenRow(span);
                var absLine = b.BaseLine + v;
                var from = 0;
                while (from <= text.Length - query.Length)
                {
                    var hit = text.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
                    if (hit < 0) break;
                    var c0 = hitCols[hit];
                    var cEnd = hitCols[hit + query.Length - 1];
                    var cw = cEnd + GlyphColumns(span, cEnd);
                    var isCurrent = _hitLine == absLine && _hitCol == c0;
                    ctx.DrawRectangle(isCurrent ? currentBrush : matchBrush, null,
                        new Rect(c0 * _cellW, v * _cellH, (cw - c0) * _cellW, _cellH));
                    from = hit + Math.Max(1, query.Length);
                }
            }
        }

        // Cursor
        if (_viewOffset == 0 && b.CursorVisible
            && b.CursorY >= row0 && b.CursorY < row0 + rows
            && b.CursorX >= col0 && b.CursorX < col0 + cols
            && (IsPreview || (_cursorOn && IsFocused)))
        {
            var cx = b.CursorX * _cellW;
            var cy = b.CursorY * _cellH;
            var cursorIndex = b.CursorY * b.Columns + b.CursorX;
            ctx.DrawRectangle(
                Brush(_colors.Cursor),
                null, new Rect(cx, cy, _cellW * GlyphColumns(b.Cells, b.Columns, cursorIndex), _cellH));
            // repaint glyph under cursor in dark
            var cell = b.Cells[b.CursorY * b.Columns + b.CursorX];
            if (cell.Char is not (' ' or '\0'))
                DrawRun(ctx, cell.Text, b.CursorX, b.CursorY,
                    Brush(bg), CellAttrs.None, bg);
        }

        // Use the same anchor resolver for the inline composition and the OS
        // candidate window, including TUIs that park a hidden cursor in a footer.
        var input = IsPreview ? new InputPosition(b.CursorX, b.CursorY, false) : ResolveInputPosition(b);
        var anchorCol = input.Col;
        var anchorRow = input.Row;
        var anchorFound = input.Valid;
        _imeAnchorCol = anchorCol; _imeAnchorRow = anchorRow; _imeAnchorValid = anchorFound;

        // Blinking caret at the input position — the typing indicator a hidden-
        // cursor TUI's input box otherwise lacks. Drawn when the hardware cursor
        // is hidden, or when the echoed input clearly lives somewhere else.
        if (!IsPreview && _imeAnchorValid && _preedit is null
            && _viewOffset == 0 && _cursorOn && IsFocused
            && _imeAnchorRow >= row0 && _imeAnchorRow < row0 + rows
            && _imeAnchorCol >= col0 && _imeAnchorCol < col0 + cols
            && !b.Cells[_imeAnchorRow * b.Columns + _imeAnchorCol].Attrs.HasFlag(CellAttrs.Inverse)
            && (!b.CursorVisible || _imeAnchorCol != b.CursorX || _imeAnchorRow != b.CursorY))
        {
            var ax = _imeAnchorCol * _cellW;
            var ay = _imeAnchorRow * _cellH;
            var cc = _colors.Cursor;
            ctx.DrawLine(new Pen(Brush(Color.FromArgb(0xFF, cc.R, cc.G, cc.B)), 2),
                new Point(ax + 1, ay + 2), new Point(ax + 1, ay + _cellH - 3));
        }

        // IME composition (preedit) overlaid at the input position, with a
        // blinking caret — also visible when the app hides the hardware cursor.
        if (!IsPreview && _preedit is { Length: > 0 } preedit && _viewOffset == 0
            && anchorRow >= row0 && anchorRow < row0 + rows)
        {
            var cc = _colors.Cursor;
            var accent = Brush(Color.FromArgb(0xFF, cc.R, cc.G, cc.B));
            var accentFill = Brush(Color.FromArgb(40, cc.R, cc.G, cc.B));
            var composition = GetPreeditLayout(b, input, preedit, _preeditCaret);
            var fgC = _colors.Foreground;
            foreach (var glyph in composition.Glyphs)
            {
                var px = glyph.Col * _cellW;
                var py = glyph.Row * _cellH;
                var pw = glyph.Width * _cellW;
                var glyphRect = new Rect(px, py, pw, _cellH);
                // Preedit replaces the underlying cell temporarily; a tint alone
                // leaves the CLI's old glyph visible underneath the new text.
                ctx.DrawRectangle(Brush(bg), null, glyphRect);
                ctx.DrawRectangle(accentFill, null, glyphRect);
                DrawRun(ctx, glyph.Text, glyph.Col, glyph.Row, Brush(fgC), CellAttrs.None, fgC);
                ctx.DrawLine(new Pen(accent, 1),
                    new Point(px, py + _cellH - 1.5), new Point(px + pw, py + _cellH - 1.5));
            }
            if (_cursorOn)
            {
                var caretX = composition.CaretCol * _cellW;
                var py = composition.CaretRow * _cellH;
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
    private InputPosition ResolveInputPosition(TerminalFrame frame)
    {
        if (ReferenceEquals(_inputFrame, frame) && _inputTail == _typedTail) return _inputPosition;
        _inputFrame = frame;
        _inputTail = _typedTail;
        return _inputPosition = FindInputPosition(frame);
    }

    private InputPosition FindInputPosition(TerminalFrame f)
    {
        // An application-provided caret is authoritative, including after moving
        // left into previously typed text. Echo matching would jump to its end.
        if (f.CursorVisible) return new(f.CursorX, f.CursorY, true);

        // Claude Code hides the terminal cursor and parks it on its footer. Its
        // input is the prompt between two horizontal rules, even before any text
        // has been committed (or after Enter/Backspace clears the typed trail).
        var region = FindInputRegion(f);
        if (region is { } prompt)
        {
            // One reversed glyph marks the caret; multiple glyphs mark selection.
            var softwareCursor = FindSoftwareCursor(f, prompt.FirstRow, prompt.EndRow, prompt.FirstCol);
            if (softwareCursor is { } position)
                return new(position.col, position.row, true, prompt);
            var lastRow = prompt.EndRow - 1;
            while (lastRow > prompt.FirstRow && RowInkEnd(f, lastRow) == 0) lastRow--;
            var col = Math.Max(lastRow == prompt.FirstRow ? prompt.FirstCol : 0, RowInkEnd(f, lastRow));
            return new(Math.Min(col, f.Columns - 1), lastRow, true, prompt);
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
                var inkEnd = RowInkEnd(f, r);
                var (text, cols) = ScreenBuffer.FlattenRow(cells);
                var from = 0;
                while (from <= text.Length - needle.Length)
                {
                    var ti = text.IndexOf(needle, from, StringComparison.Ordinal);
                    if (ti < 0) break;
                    if (cols[ti] >= inkEnd) break;   // matches never start in blank padding
                    // End cell after the matched cluster — never inside a wide
                    // glyph's padding and never into blank space past the ink.
                    var lastCol = cols[ti + needle.Length - 1];
                    var ci = Math.Min(lastCol + (cells[lastCol].IsWide ? 2 : 1), inkEnd);
                    var dist = Math.Abs(r - f.CursorY) * 4096L + Math.Abs(ci - f.CursorX);
                    if (dist < anyDist) { anyDist = dist; anyCol = ci; anyRow = r; }
                    if ((r != f.CursorY || ci != f.CursorX) && dist < offDist)
                    { offDist = dist; offCol = ci; offRow = r; }
                    from = ti + 1;
                }
            }
            if (!f.CursorVisible && offCol >= 0)
                return new(Math.Min(offCol, f.Columns - 1), offRow, true);
            if (anyCol >= 0)
                return new(Math.Min(anyCol, f.Columns - 1), anyRow, true);
        }
        return new(Math.Min(f.CursorX, RowInkEnd(f, f.CursorY)), f.CursorY, false);
    }

    private static InputRegion? FindInputRegion(TerminalFrame frame)
    {
        for (var row = frame.Rows - 2; row > 0; row--)
        {
            var col = 0;
            while (col < frame.Columns && frame.Cells[row * frame.Columns + col].Char is ' ' or '\0') col++;
            if (col == frame.Columns || frame.Cells[row * frame.Columns + col].Char is not ('>' or '❯' or '›')
                || !IsInputRule(frame, row - 1)) continue;
            for (var end = row + 1; end < frame.Rows; end++)
                if (IsInputRule(frame, end)) return new(row, end, Math.Min(col + 2, frame.Columns - 1));
        }
        return null;
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
                return Math.Min(f.Columns, i + GlyphColumns(cells, i));
        return 0;
    }

    /// <summary>Painted columns of a cell. IsWide counts as two only when the next cell in the same row is its continuation.</summary>
    private static int GlyphColumns(ReadOnlySpan<TerminalCell> row, int col)
        => col >= 0 && col + 1 < row.Length && row[col].IsWide && row[col + 1].IsWideContinuation ? 2 : 1;

    private static int GlyphColumns(TerminalCell[] cells, int columns, int index)
    {
        if ((uint)index >= (uint)cells.Length || columns <= 0) return 1;
        var col = index % columns;
        return GlyphColumns(cells.AsSpan(index - col, columns), col);
    }

    private sealed record PreeditGlyph(string Text, int Col, int Row, int Width);
    private sealed record PreeditLayout(List<PreeditGlyph> Glyphs, int CaretCol, int CaretRow);

    // Keep composition at the actual insertion cell, wrapping whole clusters.
    // A bordered Claude prompt must not paint over its lower rule or footer.
    private PreeditLayout GetPreeditLayout(TerminalFrame frame, InputPosition input, string text, int caret)
    {
        var key = (frame, input, text, caret);
        if (_compositionLayout is not null && _compositionKey == key) return _compositionLayout;
        _compositionKey = key;
        return _compositionLayout = LayoutPreedit(frame, input, text, caret);
    }

    private static PreeditLayout LayoutPreedit(TerminalFrame frame, InputPosition input, string text, int caret)
    {
        var col = input.Col;
        var row = input.Row;
        var endRow = input.Region?.EndRow ?? frame.Rows;
        var glyphs = new List<PreeditGlyph>();
        caret = Math.Clamp(caret, 0, text.Length);
        var caretCol = col;
        var caretRow = row;
        for (var i = 0; i < text.Length;)
        {
            var len = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n'
                ? 2 : ClusterLength(text, i);
            var cluster = text.Substring(i, len);
            var width = Math.Max(1, GraphemeWidth.OfCluster(cluster));
            if (cluster is "\r" or "\n" or "\r\n")
            {
                if (caret >= i && caret < i + len) { caretCol = col; caretRow = row; }
                col = 0;
                row++;
            }
            else
            {
                if (col + width > frame.Columns) { col = 0; row++; }
                if (caret >= i && caret < i + len) { caretCol = col; caretRow = row; }
                glyphs.Add(new PreeditGlyph(cluster, col, row, width));
                col += width;
            }
            i += len;
            // Positions inside a UTF-16 cluster snap to its leading cell.
            if (caret >= i)
            {
                caretCol = col;
                caretRow = row;
                if (caretCol == frame.Columns)
                { caretCol = 0; caretRow++; }
            }
        }
        // Scroll the local composition viewport, never the PTY. Keep the caret's
        // logical row visible even when the CLI has only allocated one input row.
        var scroll = Math.Max(0, caretRow - endRow + 1);
        var visible = new List<PreeditGlyph>();
        var firstRow = input.Region?.FirstRow ?? input.Row;
        foreach (var glyph in glyphs)
            if (glyph.Row - scroll >= firstRow && glyph.Row - scroll < endRow)
                visible.Add(glyph with { Row = glyph.Row - scroll });
        return new PreeditLayout(visible, Math.Clamp(caretCol, 0, frame.Columns - 1), caretRow - scroll);
    }

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

    /// <summary>Render one frame row: batch contiguous same-styled cells into runs.</summary>
    private void RenderCachedRow(DrawingContext ctx, TerminalFrame frame, int row)
    {
        var cells = frame.Cells.AsSpan(row * frame.Columns, frame.Columns);
        PaintedRow? painted = null;
        for (var index = _paintedRows.Count - 1; index >= 0; index--)
        {
            var candidate = _paintedRows[index];
            if (!SamePaintedCells(cells, candidate.Cells)) continue;
            painted = candidate;
            _paintedRows.RemoveAt(index);
            break;
        }
        if (painted is null)
        {
            var drawing = new DrawingGroup();
            using (var context = drawing.Open()) RenderRow(context, frame.Cells, 0, frame.Columns, row);
            painted = new(cells.ToArray(), drawing);
        }
        _paintedRows.Add(painted);
        // One screen plus a screen's recently scrolled rows; no scrollback drawings.
        if (_paintedRows.Count > frame.Rows * 2)
            _paintedRows.RemoveRange(0, _paintedRows.Count - frame.Rows * 2);
        using var translation = ctx.PushTransform(Matrix.CreateTranslation(0, row * _cellH));
        painted.Drawing.Draw(ctx);
    }

    private static bool SamePaintedCells(ReadOnlySpan<TerminalCell> cells, TerminalCell[] other)
    {
        if (cells.Length != other.Length) return false;
        for (var col = 0; col < cells.Length; col++)
        {
            ref readonly var a = ref cells[col];
            ref readonly var b = ref other[col];
            if (a.Char != b.Char || a.Tail != b.Tail || a.Attrs != b.Attrs
                || a.Fg != b.Fg || a.Bg != b.Bg || a.IsWide != b.IsWide
                || a.IsWideContinuation != b.IsWideContinuation) return false;
        }
        return true;
    }

    private void RenderRow(DrawingContext ctx, TerminalCell[] cells, int row, int cols, int? sourceRow = null)
    {
        var rowBase = (sourceRow ?? row) * cols;
        var runStart = -1;
        var runLen = 0;
        var curFg = default(TerminalColor);
        var curBg = default(TerminalColor);
        var curAttrs = CellAttrs.None;
        var started = false;

        for (var c = 0; c < cols; c++)
        {
            var cell = cells[rowBase + c];
            if (cell.IsWideContinuation) continue;

            var keyMatch = started && cell.Fg == curFg && cell.Bg == curBg && cell.Attrs == curAttrs;
            if (!keyMatch)
            {
                if (started && runLen > 0)
                    Flush(ctx, runStart, runLen, row, curFg, curBg, curAttrs);
                runStart = c;
                runLen = 0;
                curFg = cell.Fg; curBg = cell.Bg; curAttrs = cell.Attrs;
                started = true;
            }
            var span = GlyphColumns(cells.AsSpan(rowBase, cols), c);
            if (c + span > cols) span = 1;
            runLen += span;
        }
        if (started && runLen > 0)
            Flush(ctx, runStart, runLen, row, curFg, curBg, curAttrs);

        void Flush(DrawingContext c2, int startCol, int cellCount, int row2,
            TerminalColor fg, TerminalColor bg, CellAttrs attrs)
        {
            // Resolve defaults in their original roles before swapping. Swapping
            // the Default tokens first loses the foreground/background distinction
            // and used to omit the background of Claude's reverse-video cursor.
            var inverse = attrs.HasFlag(CellAttrs.Inverse);
            var fgC = TerminalPalette.Resolve(fg, true, _colors);
            var bgC = TerminalPalette.Resolve(bg, false, _colors);
            if (inverse) (fgC, bgC) = (bgC, fgC);
            var rect = new Rect(startCol * _cellW, row2 * _cellH, cellCount * _cellW, _cellH);
            if (inverse || !bg.IsDefault)
            {
                // Cell backgrounds are solid tiles. Antialiasing their fractional
                // edges leaks the base canvas between rows and styled runs.
                using var edges = c2.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased });
                c2.DrawRectangle(Brush(bgC), null, rect);
            }

            if (attrs.HasFlag(CellAttrs.Dim))
                fgC = Color.FromArgb((byte)(fgC.A * 0.6), fgC.R, fgC.G, fgC.B);
            var painted = Math.Min(cellCount, Math.Max(0, cols - startCol));
            if (!attrs.HasFlag(CellAttrs.Hidden))
                DrawCells(c2, cells, rowBase + startCol, startCol, painted, row2, cols, fgC, attrs);
            else if (attrs.HasFlag(CellAttrs.Underline) || attrs.HasFlag(CellAttrs.Strike))
                DrawTextDecorations(c2, startCol, painted, row2, Brush(fgC), attrs);
        }
    }

    /// <summary>
    /// Draw the cells of one run: single-byte chars batch into one layout call,
    /// multi-unit clusters and wide glyphs draw at their own grid position so
    /// fallback fonts can never drift away from the cell grid.
    /// </summary>
    private void DrawCells(DrawingContext ctx, TerminalCell[] cells, int baseIndex,
        int startCol, int cellCount, int row, int columns, Color fg, CellAttrs attrs)
    {
        var bold = attrs.HasFlag(CellAttrs.Bold);
        var brush = Brush(fg);
        var sb = _runText;
        sb.Clear();
        var asciiCol = -1;
        var drawCol = startCol;
        var rowEnd = baseIndex - startCol + columns;
        var end = Math.Min(baseIndex + cellCount, Math.Min(rowEnd, cells.Length));

        void FlushAscii()
        {
            if (asciiCol < 0 || sb.Length == 0) return;
            var ft = Layout(sb.ToString(), bold, fg);
            ctx.DrawText(ft, new Point(asciiCol * _cellW, row * _cellH + (_cellH - ft.Height) / 2));
            sb.Clear();
            asciiCol = -1;
        }

        for (var i = baseIndex; i < end; i++)
        {
            ref readonly var cell = ref cells[i];
            if (cell.IsWideContinuation) continue;
            if (cell.Tail is null && cell.Char <= '\x7f')
            {
                if (asciiCol < 0) asciiCol = drawCol;
                sb.Append(cell.Char == '\0' ? ' ' : cell.Char);
            }
            else
            {
                FlushAscii();
                var ft = Layout(cell.Text, bold, fg);
                ctx.DrawText(ft, new Point(drawCol * _cellW, row * _cellH + (_cellH - ft.Height) / 2));
            }
            drawCol += GlyphColumns(cells, columns, i);
        }
        FlushAscii();

        if (attrs.HasFlag(CellAttrs.Underline) || attrs.HasFlag(CellAttrs.Strike))
            DrawTextDecorations(ctx, startCol, cellCount, row, brush, attrs);
    }

    /// <summary>Underline/strike lines spanning <paramref name="cellCount"/> cells.</summary>
    private void DrawTextDecorations(DrawingContext ctx, int col, int cellCount, int row, IBrush fg, CellAttrs attrs)
    {
        var pen = new Pen(fg, 1);
        var w = cellCount * _cellW;
        if (attrs.HasFlag(CellAttrs.Underline))
            ctx.DrawLine(pen, new Point(col * _cellW, row * _cellH + _cellH - 2),
                new Point(col * _cellW + w, row * _cellH + _cellH - 2));
        if (attrs.HasFlag(CellAttrs.Strike))
            ctx.DrawLine(pen, new Point(col * _cellW, row * _cellH + _cellH / 2),
                new Point(col * _cellW + w, row * _cellH + _cellH / 2));
    }

    /// <summary>Reuse immutable brushes for common terminal colors.</summary>
    private IBrush Brush(Color color)
    {
        if (_brushCache.TryGetValue(color, out var brush)) return brush;
        // RGB output can introduce unlimited distinct colors. Reuse common
        // colors, but do not turn this allocation fix into another growing cache.
        if (_brushCache.Count == 128) _brushCache.Clear();
        brush = new ImmutableSolidColorBrush(color);
        _brushCache[color] = brush;
        return brush;
    }

    /// <summary>Text layout cache — same string+style+color reuses one FormattedText.</summary>
    private FormattedText Layout(string text, bool bold, Color fg)
    {
        var key = (text, bold, fg);
        if (_textCache.TryGetValue(key, out var ft)) return ft;
        if (_textCache.Count > 4096) _textCache.Clear();
        ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            bold ? _boldTypeface : _typeface, TerminalFontSize, Brush(fg));
        _textCache[key] = ft;
        return ft;
    }

    /// <summary>
    /// Draw an arbitrary string (preedit overlay, glyph under the cursor):
    /// cluster-aware — multi-code-unit graphemes render as one glyph and advance
    /// by their cell width.
    /// </summary>
    private void DrawRun(DrawingContext ctx, string text, int col, int row, IBrush fg, CellAttrs attrs, Color fgKey)
    {
        if (string.IsNullOrEmpty(text)) return;
        var bold = attrs.HasFlag(CellAttrs.Bold);
        var drawCol = col;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] <= '\x7f')
            {
                var start = i;
                while (i < text.Length && text[i] <= '\x7f') i++;
                var ft = Layout(text[start..i], bold, fgKey);
                ctx.DrawText(ft, new Point(drawCol * _cellW, row * _cellH + (_cellH - ft.Height) / 2));
                drawCol += i - start;
            }
            else
            {
                var len = ClusterLength(text, i);
                var cluster = text.Substring(i, len);
                var ft = Layout(cluster, bold, fgKey);
                ctx.DrawText(ft, new Point(drawCol * _cellW, row * _cellH + (_cellH - ft.Height) / 2));
                drawCol += Math.Max(1, GraphemeWidth.OfCluster(cluster));
                i += len;
            }
        }

        var cellWidth = Math.Max(1, GraphemeWidth.OfText(text));
        if (attrs.HasFlag(CellAttrs.Underline) || attrs.HasFlag(CellAttrs.Strike))
            DrawTextDecorations(ctx, col, cellWidth, row, fg, attrs);
    }

    /// <summary>Length in UTF-16 units of the grapheme cluster starting at
    /// <paramref name="i"/>: a base rune plus its combining marks, VS, ZWJ
    /// members and flag-pair second half.</summary>
    internal static int ClusterLength(string s, int i)
    {
        var len = char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
        var rune = len == 2 ? char.ConvertToUtf32(s[i], s[i + 1]) : s[i];
        var joinNext = rune == 0x200D;
        var singleRI = GraphemeWidth.IsRegionalIndicator(rune);
        var end = i + len;
        while (end < s.Length)
        {
            var rl = char.IsHighSurrogate(s[end]) && end + 1 < s.Length && char.IsLowSurrogate(s[end + 1]) ? 2 : 1;
            var r2 = rl == 2 ? char.ConvertToUtf32(s[end], s[end + 1]) : s[end];
            if (!(joinNext || GraphemeWidth.IsZeroWidthRune(r2) || (singleRI && GraphemeWidth.IsRegionalIndicator(r2))))
                break;
            if (singleRI && GraphemeWidth.IsRegionalIndicator(r2)) singleRI = false;
            joinNext = r2 == 0x200D;
            end += rl;
        }
        return end - i;
    }

    // ---------- input ----------

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (IsPreview || _emulator is null || e.Text is null) return;
        ResetScroll();
        _emulator.SendText(e.Text);
        _typedTail += e.Text;
        if (_typedTail.Length > 32) _typedTail = _typedTail[^32..];
        e.Handled = true;
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _imeRefreshPending = true;
        if (!IsPreview) _emulator?.SendFocus(true);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (!IsPreview) _emulator?.SendFocus(false);
        ClearComposition();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_emulator is null) return;
        if (IsPreview) return;
        // Windows paste shortcuts must read the clipboard, not send Ctrl+V (SYN)
        // or Insert to the CLI. Keep Alt combinations available to applications.
        if ((e.Key == Key.V && e.KeyModifiers is KeyModifiers.Control
                or (KeyModifiers.Control | KeyModifiers.Shift))
            || (e.Key == Key.Insert && e.KeyModifiers == KeyModifiers.Shift))
        {
            _ = PasteClipboardAsync();
            e.Handled = true;
            return;
        }
        // Keys consumed by an IME composition (VK_PROCESSKEY etc.) must not reach
        // the shell — e.g. Enter that picks a candidate would double-submit.
        if (e.Key is Key.ImeProcessed or Key.ImeAccept or Key.ImeConvert
            or Key.ImeNonConvert or Key.ImeModeChange or Key.NoName)
        {
            e.Handled = true;
            return;
        }
        // Some Windows IMEs expose candidate confirmation as a normal Enter
        // while preedit is still active. It must not also submit to the CLI.
        if (_preedit is not null && e.Key is Key.Enter or Key.Return)
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
        var modifiers = 1 + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0)
            + (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 2 : 0)
            + (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 4 : 0);
        if (e.Key == Key.Enter && modifiers > 1 && (_emulator.Buffer.KittyKeyboardFlags & 1) != 0)
        {
            _emulator.SendText($"\x1b[13;{modifiers}u");
            e.Handled = true;
            return;
        }
        // Alt+Enter / Alt+Shift+Enter: ESC CR — the Grok/Codex multiline fallback
        // when no Kitty keyboard was negotiated (that case is handled above).
        // Shift must not drop the Alt and fall through to plain "\r".
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            _emulator.SendText("\x1b\r");
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Enter)
        {
            // Outside negotiated Kitty mode keep readline's legacy Enter.
            _emulator.SendText("\r");
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Tab)
        {
            _emulator.SendText("\x1b[Z");
            e.Handled = true;
            return;
        }
        string CursorKey(char final) => modifiers > 1 ? $"\x1b[1;{modifiers}{final}"
            : app ? $"\x1bO{final}" : $"\x1b[{final}";
        string TildeKey(int code) => modifiers > 1 ? $"\x1b[{code};{modifiers}~" : $"\x1b[{code}~";
        var send = e.Key switch
        {
            Key.Enter or Key.Return => "\r",
            Key.Back => e.KeyModifiers == KeyModifiers.Control ? "\x17" :
                e.KeyModifiers == KeyModifiers.Alt ? "\x1b\x7f" : "\x7f",
            Key.Tab => "\t",
            Key.Escape => "\x1b",
            Key.Up => CursorKey('A'),
            Key.Down => CursorKey('B'),
            Key.Right => CursorKey('C'),
            Key.Left => CursorKey('D'),
            Key.Home => CursorKey('H'),
            Key.End => CursorKey('F'),
            Key.Delete => TildeKey(3),
            Key.Insert => TildeKey(2),
            >= Key.F1 and <= Key.F4 => modifiers > 1 ? $"\x1b[1;{modifiers}{(char)('P' + e.Key - Key.F1)}"
                : $"\x1bO{(char)('P' + e.Key - Key.F1)}",
            >= Key.F5 and <= Key.F12 => TildeKey(new[] { 15, 17, 18, 19, 20, 21, 23, 24 }[e.Key - Key.F5]),
            _ => null,
        };

        if (e.Key == Key.PageUp)
        {
            // Alt-screen apps (less, vim, htop) own the scrollback — the key
            // belongs to them; local scrollback scroll applies to the main screen.
            if (_emulator.Buffer.OnAlternateScreen) _emulator.SendText(TildeKey(5));
            else ScrollBy(_emulator.Buffer.Rows - 2);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.PageDown)
        {
            if (_emulator.Buffer.OnAlternateScreen) _emulator.SendText(TildeKey(6));
            else ScrollBy(-(_emulator.Buffer.Rows - 2));
            e.Handled = true;
            return;
        }

        if (send is null)
        {
            if (e.KeyModifiers == KeyModifiers.Alt && e.Key is >= Key.A and <= Key.Z)
            {
                _emulator.SendText("\x1b" + (char)('a' + e.Key - Key.A));
                e.Handled = true;
                return;
            }
            // Shift doesn't change these control bytes (Ctrl+Shift+[ is still
            // ESC). Alt is excluded — Ctrl+Alt is AltGr on many layouts and
            // produces real text that must reach TextInput.
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Alt)
                && e.Key is Key.Oem5 or Key.Oem4 or Key.Oem6 or Key.Space)
            {
                _emulator.SendBytes([(byte)(e.Key switch { Key.Oem4 => 27, Key.Oem5 => 28, Key.Oem6 => 29, _ => 0 })]);
                e.Handled = true;
                return;
            }
            // Ctrl+Shift+C copy / Ctrl+Shift+V paste, plain Ctrl+key → control byte.
            // Bare Ctrl+C keeps sending ETX — the terminal interrupt stays intact.
            // Alt is excluded here too: Ctrl+Alt is AltGr on many layouts and the
            // key must reach TextInput to produce its real character instead of
            // a spurious control byte.
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)
                && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    _ = CopySelectionAsync();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    SelectAll();
                    e.Handled = true;
                    return;
                }
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                    && e.Key >= Key.A && e.Key <= Key.Z)
                {
                    _emulator.SendBytes([(byte)(e.Key - Key.A + 1)]);
                    ResetScroll();
                    e.Handled = true;
                    return;
                }
            }
            return;
        }

        ResetScroll();
        _emulator.SendText(send);
        e.Handled = true;
    }

    private async Task PasteClipboardAsync()
    {
        try
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            var text = clip is null ? null : await clip.GetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                // Resolve the emulator after the await — a session switch during
                // the clipboard read must not paste into the previous session.
                ResetScroll();
                _typedTail = "";
                _emulator?.PasteText(text);
            }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Clipboard paste failed: {ex.Message}"); }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (IsPreview || _emulator is null) return;
        if (ApplicationOwnsMouse(e.KeyModifiers))
        {
            var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            var button = e.Delta.Y != 0 ? (delta > 0 ? 64 : 65) : (delta > 0 ? 66 : 67);
            // One PTY write for the whole gesture — a per-step write floods the
            // pipe and the CLI's input loop on high-resolution trackpads.
            var steps = Math.Min((int)Math.Ceiling(Math.Abs(delta)), 200);
            if (steps > 0) ReportMouse(e, button, count: steps);
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Ctrl+wheel = font zoom (Windows Terminal convention). TwoWay-bound
            // to Vm.FontSize so the change persists like Ctrl+=/−. Only when the
            // app didn't claim the mouse — mouse-tracking CLIs get the event.
            SetCurrentValue(TerminalFontSizeProperty,
                Math.Clamp(Math.Round(TerminalFontSize + Math.Sign(e.Delta.Y)), 8, 32));
            e.Handled = true;
            return;
        }
        if (_emulator.Buffer.OnAlternateScreen)
        {
            // The alternate screen has no scrollback — the wheel belongs to the
            // app as cursor keys (xterm alternateScroll: less/vim scroll it).
            var up = e.Delta.Y > 0;
            var seq = _emulator.Buffer.ApplicationCursorKeys
                ? (up ? "\x1bOA" : "\x1bOB")
                : (up ? "\x1b[A" : "\x1b[B");
            var steps = Math.Clamp((int)Math.Ceiling(Math.Abs(e.Delta.Y) * 3), 1, 64);
            _emulator.SendText(string.Concat(Enumerable.Repeat(seq, steps)));
            e.Handled = true;
            return;
        }
        ScrollBy((int)(-e.Delta.Y * 3));
        e.Handled = true;
    }

    private void ScrollBy(int delta)
    {
        if (_emulator is null) return;
        _viewOffset = Math.Clamp(_viewOffset + delta, 0, _emulator.Buffer.ScrollbackCount);
        SetScrolledUp(_viewOffset > 0);
        InvalidateVisual();
    }

    /// <summary>Back to the live bottom; also clears the scrolled-up flag.</summary>
    private void ResetScroll()
    {
        _viewOffset = 0;
        SetScrolledUp(false);
    }

    /// <summary>Snap the viewport back to the live bottom (the "回到底部" affordance).</summary>
    public void ScrollToBottom()
    {
        if (_viewOffset == 0) return;
        ResetScroll();
        InvalidateVisual();
    }

    // ---------- selection ----------

    // Shift retains terminal text selection/history while a TUI owns the mouse.
    private bool ApplicationOwnsMouse(KeyModifiers modifiers) =>
        !IsPreview && _emulator?.Buffer.MouseTracking > 0 && !modifiers.HasFlag(KeyModifiers.Shift);

    private void ReportMouse(PointerEventArgs e, int button, bool released = false, bool motion = false,
        int count = 1)
    {
        if (_emulator is null) return;
        var p = e.GetPosition(this);
        var cell = (Math.Clamp((int)(p.X / _cellW), 0, _emulator.Buffer.Columns - 1),
            Math.Clamp((int)(p.Y / _cellH), 0, _emulator.Buffer.Rows - 1));
        if (motion && _lastMouseCell == cell) return;
        _lastMouseCell = cell;
        var modifiers = (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 8 : 0)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 16 : 0);
        _emulator.SendMouse(button, cell.Item1, cell.Item2, released, motion, modifiers, count);
    }

    /// <summary>Map a point to (absolute buffer line, column) of the cell under it.</summary>
    private (int line, int col) PointToCell(Point p)
    {
        SynchronizeCoordinates();
        var buf = _emulator!.Buffer;
        var col = Math.Clamp((int)(p.X / _cellW), 0, buf.Columns - 1);
        var row = Math.Clamp((int)(p.Y / _cellH), 0, buf.Rows - 1);
        // frame row v shows absolute line scrollbackCount - viewOffset + v
        return (buf.ScrollbackCount - _viewOffset + row, col);
    }

    private static bool IsWordChar(char ch)
        => !char.IsWhiteSpace(ch) && ch is not (
            '"' or '\'' or '`' or '(' or ')' or '[' or ']' or '{' or '}'
            or '<' or '>' or '|' or '^' or '\0');

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsPreview) return;
        Focus();
        if (_emulator is null) return;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && !ApplicationOwnsMouse(e.KeyModifiers) && OpenContentAt(e.GetPosition(this)))
        { e.Handled = true; return; }
        if (ApplicationOwnsMouse(e.KeyModifiers))
        {
            var button = point.Properties.PointerUpdateKind switch
            {
                PointerUpdateKind.LeftButtonPressed => 0,
                PointerUpdateKind.MiddleButtonPressed => 1,
                PointerUpdateKind.RightButtonPressed => 2,
                _ => -1
            };
            if (button < 0) return;
            ResetScroll();
            _selAnchor = _selEnd = null;
            _wordSelection = false;
            _applicationMouseButton = button;
            _applicationPointer = e.Pointer;
            e.Pointer.Capture(this);
            ReportMouse(e, button);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (point.Properties.IsRightButtonPressed)
        {
            _ = PasteClipboardAsync();
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        var pos = e.GetPosition(this);
        var (line, col) = PointToCell(pos);
        _lastPointer = pos;
        var buf = _emulator.Buffer;
        lock (buf.SyncRoot)
        {
            if (e.ClickCount >= 3)
            {
                // Triple click selects the logical line, including soft-wrap continuations.
                var start = line;
                var end = line;
                var last = buf.TotalLines - 1;
                while (start > 0 && buf.IsLineWrapped(start - 1)) start--;
                while (end < last && buf.IsLineWrapped(end)) end++;
                _selAnchor = (start, 0);
                _selEnd = (end, Math.Max(0, buf.Columns - 1));
                _wordSelection = true;
                _selecting = true;
            }
            else if (e.ClickCount == 2)
            {
                // double click: expand to word boundaries on the flattened row
                var row = buf.GetLine(Math.Min(line, buf.TotalLines - 1));
                var (text, cols) = ScreenBuffer.FlattenRow(row);
                var idx = cols.Length == 0 ? -1 : TextIndexAtCol(cols, col);
                if (idx >= 0 && idx < text.Length && IsWordChar(text[idx]))
                {
                    var lo = idx;
                    var hi = idx + 1;
                    while (lo > 0 && IsWordChar(text[lo - 1])) lo--;
                    while (hi < text.Length && IsWordChar(text[hi])) hi++;
                    var lastCell = cols[hi - 1];
                    _selAnchor = (line, cols[lo]);
                    _selEnd = (line, lastCell + (row[lastCell].IsWide ? 2 : 1) - 1);
                    _wordSelection = true;
                    _selecting = true;
                }
                else { _selAnchor = _selEnd = null; _wordSelection = false; }
            }
            else
            {
                var cell = SnapToCluster((line, col));
                _selAnchor = cell;
                _selEnd = cell;
                _wordSelection = false;
                _selecting = true;
            }
        }
        // Only capture while actually selecting — a double-click miss leaves
        // _selecting false, and the release path would never release the capture.
        if (_selecting)
        {
            _selectionPointer = e.Pointer;
            e.Pointer.Capture(this);
        }
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>First flattened-text index whose cell column is ≥ col (the cell
    /// owning that column when the point lands on a wide glyph's padding).</summary>
    private static int TextIndexAtCol(int[] cols, int col)
    {
        var lo = 0;
        var hi = cols.Length - 1;
        var best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (cols[mid] <= col) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return best;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_selecting && ApplicationOwnsMouse(e.KeyModifiers))
        {
            // During a captured app-drag only the owning pointer reports motion —
            // a second pointer's move would inject phantom drag events.
            if (_applicationPointer is not null && !ReferenceEquals(e.Pointer, _applicationPointer))
                return;
            ReportMouse(e, _applicationMouseButton < 0 ? 3 : _applicationMouseButton, motion: true);
            e.Handled = true;
            return;
        }
        if (!_selecting || _emulator is null) return;
        if (_selectionPointer is not null && !ReferenceEquals(e.Pointer, _selectionPointer))
            return;
        var pos = e.GetPosition(this);
        _lastPointer = pos;
        _selEnd = SnapToCluster(PointToCell(pos));
        InvalidateVisual();
    }

    /// <summary>While dragging past the viewport edge, keep scrolling into history
    /// (or back toward the bottom) and extend the selection to the edge row.</summary>
    private void DragScrollStep()
    {
        if (_emulator is null) return;
        var overTop = _lastPointer.Y < 0;
        var overBottom = _lastPointer.Y > Bounds.Height;
        if (!overTop && !overBottom) return;
        var distance = overTop ? -_lastPointer.Y : _lastPointer.Y - Bounds.Height;
        var lines = Math.Clamp((int)(distance / (_cellH * 2)) + 1, 1, 8);
        ScrollBy(overTop ? lines : -lines);
        _selEnd = SnapToCluster(PointToCell(new Point(_lastPointer.X,
            Math.Clamp(_lastPointer.Y, 0, Bounds.Height - 1))));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_applicationMouseButton >= 0)
        {
            // A different pointer's release must not end the captured app-drag.
            if (!ReferenceEquals(e.Pointer, _applicationPointer)) return;
            ReportMouse(e, _applicationMouseButton, released: true);
            _applicationMouseButton = -1;
            _applicationPointer = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (_selecting)
        {
            if (_selectionPointer is not null && !ReferenceEquals(e.Pointer, _selectionPointer))
                return;
            _selecting = false;
            _selectionPointer = null;
            // A one-cell word has equal inclusive ends. A plain click does not.
            if (_selAnchor == _selEnd && !_wordSelection) { _selAnchor = _selEnd = null; InvalidateVisual(); }
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (ReferenceEquals(e.Pointer, _applicationPointer))
        {
            if (_applicationMouseButton >= 0 && _lastMouseCell is { } cell)
                _emulator?.SendMouse(_applicationMouseButton, cell.column, cell.row, released: true);
            _applicationMouseButton = -1;
            _applicationPointer = null;
        }
        if (ReferenceEquals(e.Pointer, _selectionPointer))
        {
            _selectionPointer = null;
            _selecting = false;
        }
    }

    /// <summary>Snap a pointer-derived cell onto a grapheme boundary: a point on
    /// a wide glyph's continuation cell belongs to the lead cell — otherwise the
    /// render expands the highlight over the glyph while the copy drops it.</summary>
    private (int line, int col) SnapToCluster((int line, int col) cell)
    {
        var buf = _emulator?.Buffer;
        if (buf is null) return cell;
        lock (buf.SyncRoot)
        {
            if (cell.line < 0 || cell.line >= buf.TotalLines) return cell;
            var row = buf.GetLine(cell.line);
            if (cell.col > 0 && cell.col < row.Length && row[cell.col].IsWideContinuation)
                return (cell.line, cell.col - 1);
            return cell;
        }
    }

    /// <summary>Selected text (normalized, soft-wrapped lines joined), or null.</summary>
    public string? GetSelectedText()
    {
        var buf = _emulator?.Buffer;
        if (buf is null) return null;
        lock (buf.SyncRoot)
        {
            SynchronizeCoordinates();
            if (_selAnchor is not { } a || _selEnd is not { } e2 || (a == e2 && !_wordSelection)) return null;
            var (sl, sc) = a.CompareTo(e2) <= 0 ? a : e2;
            var (el, ec) = a.CompareTo(e2) <= 0 ? e2 : a;
            el = Math.Min(el, buf.TotalLines - 1);
            return sl > el ? null : buf.ExtractText(sl, sc, el, ec);
        }
    }

    /// <summary>Select the whole buffer — scrollback + screen (Ctrl+Shift+A).</summary>
    public void SelectAll()
    {
        var buf = _emulator?.Buffer;
        if (buf is null) return;
        lock (buf.SyncRoot)
        {
            SynchronizeCoordinates();
            // On the alternate screen the scrollback is preserved but invisible —
            // selecting it would copy stale primary-screen text into a TUI.
            var first = buf.OnAlternateScreen ? buf.ScrollbackCount : 0;
            if (buf.TotalLines <= first) return;
            _selAnchor = (first, 0);
            _selEnd = (buf.TotalLines - 1, buf.Columns - 1);
            _wordSelection = false;
        }
        InvalidateVisual();
    }

    /// <summary>Copy the current selection to the clipboard (Ctrl+Shift+C).</summary>
    public async Task CopySelectionAsync()
    {
        var text = GetSelectedText();
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            var clip = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clip is not null) await clip.SetTextAsync(text);
        }
        catch { }
    }

    // ---------- search / history ----------

    /// <summary>All match positions (absolute line, cell col, cell len), lazy.</summary>
    private List<(int line, int col, int len)> EnsureHits()
    {
        var buf = _emulator!.Buffer;
        var query = SearchQuery;
        lock (buf.SyncRoot)
        {
            SynchronizeCoordinates();
            if (_hits is not null && _hitsVersion == buf.Version
                && string.Equals(_hitsQuery, query, StringComparison.Ordinal))
                return _hits;
            _hits = [];
            _hitsQuery = query;
            _hitsVersion = buf.Version;
            if (string.IsNullOrEmpty(query)) return _hits;
            for (var line = 0; line < buf.TotalLines && _hits.Count < 4096; line++)
            {
                var row = buf.GetLine(line);
                var (text, cols) = ScreenBuffer.FlattenRow(row);
                var from = 0;
                while (from <= text.Length - query.Length)
                {
                    var hit = text.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
                    if (hit < 0) break;
                    var c0 = cols[hit];
                    var cEnd = cols[hit + query.Length - 1];
                    _hits.Add((line, c0, cEnd + (row[cEnd].IsWide ? 2 : 1) - c0));
                    from = hit + Math.Max(1, query.Length);
                }
            }
            return _hits;
        }
    }

    /// <summary>Jump the viewport to the previous/next search match.</summary>
    /// <param name="direction">+1 next, -1 previous.</param>
    public bool GoToMatch(int direction)
    {
        if (_emulator is null || string.IsNullOrEmpty(SearchQuery)) return false;
        var hits = EnsureHits();
        if (hits.Count == 0) { _hitLine = -1; InvalidateVisual(); return false; }

        // Locate the current marker; when unset start from the viewport's first line.
        var idx = hits.FindIndex(h => h.line == _hitLine && h.col == _hitCol);
        if (idx < 0)
        {
            var buf = _emulator.Buffer;
            var topLine = buf.ScrollbackCount - _viewOffset;
            idx = direction > 0
                ? hits.FindIndex(h => h.line >= topLine)
                : hits.FindLastIndex(h => h.line <= topLine + buf.Rows - 1);
            if (idx < 0) idx = direction > 0 ? 0 : hits.Count - 1;
        }
        else idx = (idx + direction + hits.Count) % hits.Count;

        var (line, col, len) = hits[idx];
        _hitLine = line; _hitCol = col; _hitLen = len;
        RevealLine(line);
        return true;
    }

    /// <summary>Scroll so absolute buffer <paramref name="line"/> is visible
    /// (roughly upper third), keeping the bottom when it is already on screen.</summary>
    public void RevealLine(int line)
    {
        var buf = _emulator?.Buffer;
        if (buf is null) return;
        var firstVisible = buf.ScrollbackCount - _viewOffset;
        if (line >= firstVisible && line < firstVisible + buf.Rows) { InvalidateVisual(); return; }
        // visual row v shows abs (scrollback - offset + v); aim for v ≈ Rows/3
        _viewOffset = Math.Clamp(buf.ScrollbackCount - line + buf.Rows / 3, 0, buf.ScrollbackCount);
        SetScrolledUp(_viewOffset > 0);
        InvalidateVisual();
    }

    /// <summary>Scroll a command-record anchor into view. False when that line was trimmed.</summary>
    public bool TryRevealAnchor(BufferAnchor anchor)
    {
        var buf = _emulator?.Buffer;
        if (buf is null) return false;
        int? line;
        lock (buf.SyncRoot) line = buf.ResolveAnchor(anchor);
        if (line is null) return false;
        RevealLine(line.Value);
        return true;
    }

    /// <summary>A result clicked after history trimming still refers to its original line.</summary>
    public bool RevealSearchHit(ScreenBuffer.SearchHit hit)
    {
        var buf = _emulator?.Buffer;
        if (buf is null) return false;
        lock (buf.SyncRoot)
        {
            SynchronizeCoordinates();
            var line = hit.Line - (buf.RemovedLineCount - hit.RemovedLines);
            if (hit.LayoutVersion != buf.LayoutVersion || line < 0 || line >= buf.TotalLines) return false;
            RevealLine((int)line);
            return true;
        }
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
                // CaptureFrame is version-cached — this stays cheap.
                var frame = buf.CaptureFrame();
                var input = view.ResolveInputPosition(frame);
                var baseCol = input.Col;
                var baseRow = input.Row;
                // While scrolled into history the anchor row shifts down by the
                // view offset (screen row r shows at visual row r + offset).
                var row = Math.Clamp(baseRow + view._viewOffset, 0, buf.Rows - 1);
                // While composing, anchor to the caret inside the preedit overlay
                // so the candidate window tracks it.
                if (view._preedit is { Length: > 0 } p)
                {
                    var composition = view.GetPreeditLayout(frame, input, p, view._preeditCaret);
                    row = Math.Clamp(composition.CaretRow + view._viewOffset, 0, frame.Rows - 1);
                    return new Rect(composition.CaretCol * view._cellW, row * view._cellH,
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
            view._compositionLayout = null;
            view._compositionKey = default;
            NotifyCursorRectangleChanged();
            view.InvalidateVisual();
        }
    }
}
