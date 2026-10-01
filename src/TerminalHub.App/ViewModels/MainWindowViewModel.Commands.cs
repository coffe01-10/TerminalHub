using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<CommandRecordViewModel> CommandRecords { get; } = [];
    [ObservableProperty] private CommandRecordViewModel? _selectedCommand;
    [ObservableProperty] private string _commandNotice = "";
    public event Action<CommandRecord>? RevealCommandRequested;
    private TerminalSessionModel? _commandSession;

    public void BindCommandSession(TerminalSessionModel? session)
    {
        if (ReferenceEquals(_commandSession, session))
        {
            ReloadCommandRecords();
            return;
        }
        if (_commandSession is not null) _commandSession.Emulator.CommandsChanged -= OnCommandsChanged;
        _commandSession = session;
        if (session is not null) session.Emulator.CommandsChanged += OnCommandsChanged;
        ReloadCommandRecords();
    }

    private void OnCommandsChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (!_disposed) ReloadCommandRecords();
    });

    private void ReloadCommandRecords()
    {
        var session = _commandSession;
        if (session is null)
        {
            CommandRecords.Clear();
            SelectedCommand = null;
            return;
        }
        CommandRecord[] records;
        var buffer = session.Emulator.Buffer;
        lock (buffer.SyncRoot) records = session.Emulator.Commands.Records.ToArray();
        var selected = SelectedCommand?.Record.Id;
        CommandRecords.Clear();
        foreach (var record in records)
        {
            var item = new CommandRecordViewModel(record, session.WorkingDirectory);
            lock (buffer.SyncRoot) item.Refresh(buffer);
            CommandRecords.Add(item);
        }
        SelectedCommand = CommandRecords.FirstOrDefault(item => item.Record.Id == selected) ?? CommandRecords.LastOrDefault();
    }

    [RelayCommand]
    private void ShowCommandHistory()
    {
        InspectorVisible = true;
        SelectedRightTab = 4;
        ReloadCommandRecords();
    }

    [RelayCommand]
    private void LocateCommand(CommandRecordViewModel? item)
    {
        item ??= SelectedCommand;
        if (item is null || ActiveSession is null) { CommandNotice = "没有选中的命令。"; return; }
        SelectedCommand = item;
        bool found;
        lock (ActiveSession.Emulator.Buffer.SyncRoot) found = item.Record.LocateLine(ActiveSession.Emulator.Buffer) is not null;
        item.CanLocate = found;
        item.LocateText = found ? "可定位" : UnavailableLocateText(item.Record);
        if (!found) { CommandNotice = UnavailableNotice(item.Record, locate: true); return; }
        CommandNotice = "";
        RevealCommandRequested?.Invoke(item.Record);
    }

    [RelayCommand] private void PreviousCommand() => StepCommand(-1);
    [RelayCommand] private void NextCommand() => StepCommand(1);

    private void StepCommand(int direction)
    {
        if (CommandRecords.Count == 0) { CommandNotice = "这个终端还没有命令记录。"; return; }
        var index = SelectedCommand is null
            ? (direction > 0 ? -1 : CommandRecords.Count)
            : CommandRecords.IndexOf(SelectedCommand);
        var next = index + direction;
        if (next < 0 || next >= CommandRecords.Count)
        {
            CommandNotice = direction > 0 ? "后面没有命令。" : "前面没有命令。";
            return;
        }
        LocateCommand(CommandRecords[next]);
    }

    /// <summary>Command text + output of a record, or null (notice set). Shared by
    /// the copy button and the save-as-text export.</summary>
    public string? GetCommandRecordText(CommandRecordViewModel? item)
    {
        item ??= SelectedCommand;
        if (item is null || ActiveSession is null) { CommandNotice = "没有选中的命令。"; return null; }
        string? text;
        lock (ActiveSession.Emulator.Buffer.SyncRoot)
            text = item.Record.CopyText(ActiveSession.Emulator.Buffer);
        if (text is null) { CommandNotice = UnavailableNotice(item.Record, locate: false); return null; }
        return text;
    }

    [RelayCommand]
    private async Task CopyCommandRecord(CommandRecordViewModel? item)
    {
        if (GetCommandRecordText(item) is not { } text) return;
        CommandNotice = await CopyTextToClipboardAsync(text)
            ? "已复制命令和输出。"
            : "复制失败：剪贴板暂时不可用，请重试。";
    }

    private bool OnOtherScreen(CommandRecord record)
        => ActiveSession is not null && record.Start.Alive
            && record.Start.Alternate != ActiveSession.Emulator.Buffer.OnAlternateScreen;

    private string UnavailableLocateText(CommandRecord record)
        => OnOtherScreen(record) ? "屏幕不同" : "输出已不在历史中";

    private string UnavailableNotice(CommandRecord record, bool locate)
    {
        if (OnOtherScreen(record))
            return locate
                ? "当前屏幕和记录命令时不同，无法定位，也没有改到其他内容。"
                : "当前屏幕和记录命令时不同，没有复制其他内容。";
        return locate
            ? "这段输出已被历史缓冲裁掉，无法定位。"
            : "这段输出已不在历史中，没有复制其他命令的内容。";
    }
}
