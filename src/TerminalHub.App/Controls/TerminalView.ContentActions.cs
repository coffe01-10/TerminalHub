using Avalonia;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Controls;

public partial class TerminalView
{
    public static readonly StyledProperty<string> WorkingDirectoryProperty = AvaloniaProperty.Register<TerminalView, string>(nameof(WorkingDirectory), "");
    public static readonly StyledProperty<string> ShellCommandProperty = AvaloniaProperty.Register<TerminalView, string>(nameof(ShellCommand), "");
    public static readonly StyledProperty<bool> IsRemoteProperty = AvaloniaProperty.Register<TerminalView, bool>(nameof(IsRemote));
    public string WorkingDirectory { get => GetValue(WorkingDirectoryProperty); set => SetValue(WorkingDirectoryProperty, value); }
    public string ShellCommand { get => GetValue(ShellCommandProperty); set => SetValue(ShellCommandProperty, value); }
    public bool IsRemote { get => GetValue(IsRemoteProperty); set => SetValue(IsRemoteProperty, value); }

    private void InitializeContentActions()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            // Remote shells cannot use local file paths from Explorer.
            e.DragEffects = !IsPreview && !IsRemote && e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        });
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var paths = e.Data.GetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
            e.Handled = InsertDroppedPaths(paths);
        });
        // The open-failure hint is transient: drop it with the pointer, do not
        // replay the stale error on every later hover.
        AddHandler(PointerExitedEvent, (_, _) => ClearLinkFailureTip());
    }

    public bool InsertDroppedPaths(IEnumerable<string> paths)
    {
        if (IsPreview || IsRemote || _emulator is null) return false;
        var quoted = ShellPathInput.Format(paths, ShellCommand);
        if (quoted.Length == 0) return false;
        Focus();
        SendUserInput(new([], quoted));
        return true;
    }

    private bool OpenContentAt(Point point)
    {
        if (_emulator is null) return false;
        ClearLinkFailureTip();   // a new attempt supersedes the previous error
        var (line, col) = PointToCell(point);
        var buffer = _emulator.Buffer;
        TerminalContentLink? link;
        lock (buffer.SyncRoot)
        {
            var row = buffer.GetLine(Math.Min(line, buffer.TotalLines - 1));
            var (text, cols) = ScreenBuffer.FlattenRow(row);
            var index = TextIndexAtCol(cols, col);
            link = TerminalContentLinks.Resolve(text, index, WorkingDirectory, IsRemote, row[col].Hyperlink);
        }
        if (link is null) return false;
        try { TerminalLinkOpener.Open(link); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { ShowLinkFailureTip($"无法打开：{ex.Message}"); }
        return true;
    }

    private DispatcherTimer? _linkFailureTipTimer;

    /// <summary>Show the open-failure hint right at the failed click, then drop
    /// it (timeout or pointer leave). Leaving the Tip attached would replay the
    /// expired error on every later hover.</summary>
    private void ShowLinkFailureTip(string message)
    {
        Avalonia.Controls.ToolTip.SetTip(this, message);
        Avalonia.Controls.ToolTip.SetIsOpen(this, true);
        _linkFailureTipTimer?.Stop();
        _linkFailureTipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _linkFailureTipTimer.Tick += (_, _) => ClearLinkFailureTip();
        _linkFailureTipTimer.Start();
    }

    private void ClearLinkFailureTip()
    {
        _linkFailureTipTimer?.Stop();
        _linkFailureTipTimer = null;
        if (Avalonia.Controls.ToolTip.GetTip(this) is null) return;
        if (Avalonia.Controls.ToolTip.GetIsOpen(this)) Avalonia.Controls.ToolTip.SetIsOpen(this, false);
        Avalonia.Controls.ToolTip.SetTip(this, null);
    }
}
