using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Sessions;

namespace TerminalHub.App.ViewModels;

/// <summary>Left-rail session card: name, tag pill, status dot, mini preview.</summary>
public partial class SessionCardViewModel : ViewModelBase
{
    public TerminalSessionModel Model { get; }

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _previewText = "";

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
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            PreviewText = Model.Emulator.Buffer.TailText(9));
    }

    public void Refresh()
    {
        PreviewText = Model.Emulator.Buffer.TailText(9);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(StatusBrush));
    }
}
