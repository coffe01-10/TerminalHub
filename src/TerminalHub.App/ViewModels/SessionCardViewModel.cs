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
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private List<PreviewLineView> _previewLines = [];

    public string Name => Model.Name;
    public string TagText => Model.Tag.DisplayName();
    public bool HasTag => Model.Tag != SessionTag.None;
    public IBrush TagBrush => new SolidColorBrush(Color.Parse(Model.Tag.AccentColor()));
    public IBrush StatusBrush => new SolidColorBrush(
        Color.Parse(Model.IsRunning ? "#34D399" : "#F87171"));

    public SessionCardViewModel(TerminalSessionModel model)
    {
        Model = model;
        model.Emulator.Changed += OnBufferChanged;
    }

    private int _tick;
    private void OnBufferChanged()
    {
        // Throttle preview refresh (mock/idle output is sparse; keep it snappy).
        if (++_tick % 5 != 0) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshPreview);
    }

    public void Refresh()
    {
        RefreshPreview();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(StatusBrush));
    }

    private void RefreshPreview()
    {
        var lines = Model.Emulator.Buffer.TailLines(10);
        PreviewLines = lines
            .Select(l => new PreviewLineView(l.Text,
                l.FgHex is null ? DefaultPreviewBrush : new SolidColorBrush(Color.Parse(l.FgHex))))
            .ToList();
        PreviewText = string.Join('\n', lines.Select(l => l.Text));
    }
}
