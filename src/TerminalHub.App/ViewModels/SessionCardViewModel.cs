using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.ViewModels;

/// <summary>One colored line in a session-card mini preview.</summary>
public sealed record PreviewLineView(string Text, IBrush Foreground);

/// <summary>Left-rail session card: name, tag pill, status dot, mini preview.</summary>
public partial class SessionCardViewModel : ViewModelBase
{
    private static readonly IBrush DefaultPreviewBrush = new SolidColorBrush(Color.Parse("#7C8AA5"));

    public TerminalSessionModel Model { get; }

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private int _stageDistance;
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private List<PreviewLineView> _previewLines = [];
    [ObservableProperty] private bool _hasUnreadOutput;
    private bool _isDisplayed;
    private long _seenOutputVersion;

    public string Name => Model.Name;
    public string WorkingDirectory => Model.WorkingDirectory;
    public string DirectoryName => Path.GetFileName(WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : WorkingDirectory;
    public string TagText => Model.Tag.DisplayName();
    public bool HasTag => Model.Tag != SessionTag.None;
    public IBrush TagBrush => new SolidColorBrush(Color.Parse(Model.Tag.AccentColor()));
    /// <summary>Translucent tag-color fill for the header pill (mockup-style tinted chip).</summary>
    public IBrush TagPillBrush
    {
        get
        {
            var c = Color.Parse(Model.Tag.AccentColor());
            return new SolidColorBrush(new Color(0x3A, c.R, c.G, c.B));
        }
    }
    public string StatusText => !Model.IsRunning
        ? Model.Pty.ExitCode is { } code ? $"已退出 · {code}" : "未运行"
        : HasUnreadOutput ? "有新输出" : "运行中";
    public string CommandStatusText => Model.Emulator.CommandState is not { } command ? ""
        : command.Running ? "命令运行中"
        : $"{(command.ExitCode is null ? "命令结束" : command.ExitCode == 0 ? "命令完成" : $"命令失败 · {command.ExitCode}")} · {command.Duration.TotalSeconds:0.0}s";
    public IBrush StatusBrush => Controls.ThemeManager.Brush(!Model.IsRunning
        ? Model.Pty.ExitCode is { } code ? code == 0 ? "Good" : "Bad" : "Muted"
        : HasUnreadOutput ? "Accent" : "Good");

    partial void OnHasUnreadOutputChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CommandStatusText));
        OnPropertyChanged(nameof(StatusBrush));
    }

    public void SetDisplayed(bool displayed)
    {
        if (_isDisplayed == displayed) return;
        _isDisplayed = displayed;
        _seenOutputVersion = Model.Emulator.OutputVersion;
        if (displayed) HasUnreadOutput = false;
    }

    public SessionCardViewModel(TerminalSessionModel model)
    {
        Model = model;
        _seenOutputVersion = model.Emulator.OutputVersion;
    }

    public void Refresh()
    {
        var outputVersion = Model.Emulator.OutputVersion;
        if (outputVersion != _seenOutputVersion)
        {
            if (!_isDisplayed) HasUnreadOutput = true;
            _seenOutputVersion = outputVersion;
        }
        RefreshPreview();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(WorkingDirectory));
        OnPropertyChanged(nameof(CommandStatusText));
        OnPropertyChanged(nameof(DirectoryName));
    }

    private void RefreshPreview()
    {
        List<ScreenBuffer.PreviewLine> lines;
        lock (Model.Emulator.Buffer.SyncRoot) lines = Model.Emulator.Buffer.TailLines(12);
        PreviewLines = lines
            .Select(l => new PreviewLineView(l.Text,
                l.FgHex is null ? DefaultPreviewBrush : new SolidColorBrush(Color.Parse(l.FgHex))))
            .ToList();
        PreviewText = string.Join('\n', lines.Select(l => l.Text));
    }
}
