using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Controls;

public partial class TerminalView
{
    /// <summary>The viewport origin the renderer is showing right now. While
    /// scrolled up, pending scroll drift (lines appended since the last refresh
    /// tick, which the tick adds to <c>_viewOffset</c> before rendering) must be
    /// included so the window stays on the same content — without it, output that
    /// arrived between ticks shifts the extract onto newer lines than the screen.
    /// On the alternate screen the offset is always 0, as in CaptureFrame.</summary>
    private int EffectiveViewOffset(ScreenBuffer buf)
    {
        if (buf.OnAlternateScreen || _viewOffset == 0) return 0;
        return Math.Clamp(_viewOffset + Interlocked.Add(ref _scrollDrift, 0), 0, buf.ScrollbackCount);
    }

    /// <summary>True while the mouse selection covers at least one cell.</summary>
    public bool HasSelection
    {
        get
        {
            var buf = _emulator?.Buffer;
            if (buf is null) return false;
            lock (buf.SyncRoot)
            {
                SynchronizeCoordinates();
                return _selAnchor is { } a && _selEnd is { } e && (a != e || _wordSelection);
            }
        }
    }

    /// <summary>Text of the current viewport — the live screen at the bottom,
    /// or the scrollback window while browsing history.</summary>
    public string? GetVisibleText()
    {
        var buf = _emulator?.Buffer;
        if (buf is null) return null;
        lock (buf.SyncRoot)
        {
            var top = Math.Clamp(buf.ScrollbackCount - EffectiveViewOffset(buf), 0, buf.TotalLines - 1);
            var end = Math.Min(top + buf.Rows - 1, buf.TotalLines - 1);
            return buf.ExtractText(top, 0, end, int.MaxValue).TrimEnd('\n');
        }
    }

    /// <summary>All retained output: scrollback history plus the screen. Lines
    /// already trimmed from the buffer are gone and cannot be recovered.</summary>
    public string? GetAllText()
    {
        var buf = _emulator?.Buffer;
        if (buf is null) return null;
        lock (buf.SyncRoot)
            return buf.ExtractText(0, 0, buf.TotalLines - 1, int.MaxValue).TrimEnd('\n');
    }

    /// <summary>
    /// Bookmark capture: selected text anchored at the selection start, or a few
    /// preview lines anchored at the viewport's first visible line. The returned
    /// anchor is registered in <see cref="ScreenBuffer.Anchors"/> — a cancelled
    /// bookmark must remove it again (<c>buf.Anchors.Remove(anchor)</c>).
    /// </summary>
    public (string Text, BufferAnchor Anchor)? CaptureBookmark()
    {
        var buf = _emulator?.Buffer;
        if (buf is null) return null;
        lock (buf.SyncRoot)
        {
            SynchronizeCoordinates();
            int line, col;
            string text;
            if (_selAnchor is { } a && _selEnd is { } e && (a != e || _wordSelection))
            {
                var (sl, sc) = a.CompareTo(e) <= 0 ? a : e;
                var (el, ec) = a.CompareTo(e) <= 0 ? e : a;
                el = Math.Min(el, buf.TotalLines - 1);
                if (sl > el) return null;
                text = buf.ExtractText(sl, sc, el, ec);
                line = sl;
                col = sc;
            }
            else
            {
                line = Math.Clamp(buf.ScrollbackCount - EffectiveViewOffset(buf), 0, buf.TotalLines - 1);
                col = 0;
                var end = Math.Min(line + 3, buf.TotalLines - 1);
                text = buf.ExtractText(line, 0, end, int.MaxValue).TrimEnd('\n');
            }
            return (text, buf.CreateAnchor(line, col));
        }
    }
}
