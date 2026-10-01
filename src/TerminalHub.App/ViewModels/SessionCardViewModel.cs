using Avalonia;
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
    private int _previewVersion = -1;
    private bool _isFirstOnShelf;

    /// <summary>Pixels each card tucks under the previous one on the shelf
    /// (mockup stack look). Keep in sync with the shelf margin binding.</summary>
    public const double ShelfOverlap = 16;

    /// <summary>True for the topmost shelf item — it must not pull up, or its
    /// header would bleed past the list padding into the shelf title row.</summary>
    public bool IsFirstOnShelf
    {
        get => _isFirstOnShelf;
        set
        {
            if (_isFirstOnShelf == value) return;
            _isFirstOnShelf = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShelfTopMargin));
        }
    }

    /// <summary>Negative top margin producing the tucked stack; zero on the first card.</summary>
    public Thickness ShelfTopMargin => _isFirstOnShelf ? default : new Thickness(0, -ShelfOverlap, 0, 0);

    public string Name => Model.Name;
    public string WorkingDirectory => Model.WorkingDirectory;
    public string DirectoryName => Path.GetFileName(WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } name ? name : WorkingDirectory;
    private string _shelfCaption = "";
    public string ShelfCaption => _shelfCaption.Length > 0 ? _shelfCaption : DirectoryName;
    public void SetShelfCaption(string caption)
    {
        if (_shelfCaption == caption) return;
        _shelfCaption = caption;
        OnPropertyChanged(nameof(ShelfCaption));
    }
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
        OnPropertyChanged(nameof(ShelfCaption));
    }

    private void RefreshPreview()
    {
        List<ScreenBuffer.PreviewLine> lines;
        var buffer = Model.Emulator.Buffer;
        lock (buffer.SyncRoot)
        {
            if (_previewVersion == buffer.Version) return;
            lines = buffer.TailLines(12);
            _previewVersion = buffer.Version;
        }
        PreviewLines = lines
            .Select(l => new PreviewLineView(l.Text,
                l.FgHex is null ? DefaultPreviewBrush : new SolidColorBrush(Color.Parse(l.FgHex))))
            .ToList();
        PreviewText = string.Join('\n', lines.Select(l => l.Text));
    }
}
