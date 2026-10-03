using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.ViewModels;

public sealed partial class CommandRecordViewModel : ObservableObject
{
    public CommandRecord Record { get; }
    public string Title { get; }
    public string Detail { get; }
    public TerminalHub.Core.Localization.LocalizedText DisplayTitle => new(Title, !Record.HasCommandText || Record.Command.Length == 0);
    public TerminalHub.Core.Localization.LocalizedText DisplayDetail { get; }
    [ObservableProperty] private string _locateText = "";
    [ObservableProperty] private bool _canLocate;

    public CommandRecordViewModel(CommandRecord record, string sessionDirectory)
    {
        Record = record;
        Title = !record.HasCommandText ? "（未标记命令文本）"
            : record.Command.Length == 0 ? "（空命令）"
            : record.Command.Replace("\r", "").Split('\n')[0];
        var directory = record.WorkingDirectory.Length > 0 ? record.WorkingDirectory : sessionDirectory;
        var state = record.Running ? "运行中"
            : record.ExitCode is null ? "命令结束"
            : record.ExitCode == 0 ? "完成"
            : "失败 " + record.ExitCode;
        var duration = record.Duration is { } time ? " · " + time.TotalSeconds.ToString("0.0") + "s" : "";
        Detail = directory + " · " + state + duration;
        DisplayDetail = new(() => directory + " · " + TerminalHub.Core.Localization.Localizer.Current.Translate(state) + duration);
    }

    public void Refresh(ScreenBuffer buffer)
    {
        CanLocate = Record.LocateLine(buffer) is not null;
        LocateText = CanLocate ? "可定位"
            : Record.Start.Alive && Record.Start.Alternate != buffer.OnAlternateScreen ? "屏幕不同"
            : "输出已不在历史中";
    }
}
