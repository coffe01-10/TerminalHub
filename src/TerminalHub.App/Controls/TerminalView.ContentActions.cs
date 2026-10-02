using Avalonia;
using Avalonia.Input;
using Avalonia.Platform.Storage;
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
        { Avalonia.Controls.ToolTip.SetTip(this, $"无法打开：{ex.Message}"); }
        return true;
    }
}
