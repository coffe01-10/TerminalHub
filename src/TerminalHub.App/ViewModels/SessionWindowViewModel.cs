using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.ViewModels;

/// <summary>Popout window VM: a live view onto one detached session's emulator.
/// Owns nothing — lifetime belongs to whoever detached the session.</summary>
public partial class SessionWindowViewModel : ViewModelBase, IDisposable
{
    public TerminalSessionModel Model { get; }
    public TerminalEmulator Emulator => Model.Emulator;
    public double FontSize { get; }
    public FontFamily FontFamily { get; }

    [ObservableProperty] private string _title;

    public string Name => Model.Name;
    public string TagText => Model.Tag.DisplayName();
    public bool HasTag => Model.Tag != SessionTag.None;
    public IBrush TagBrush => new SolidColorBrush(Color.Parse(Model.Tag.AccentColor()));
    public IBrush TagPillBrush
    {
        get
        {
            var c = Color.Parse(Model.Tag.AccentColor());
            return new SolidColorBrush(new Color(0x3A, c.R, c.G, c.B));
        }
    }
    public IBrush StatusBrush => new SolidColorBrush(
        Color.Parse(Model.IsRunning ? "#34D399" : "#F87171"));

    public SessionWindowViewModel(TerminalSessionModel model, double fontSize, FontFamily? fontFamily = null)
    {
        Model = model;
        FontSize = fontSize;
        FontFamily = fontFamily
            ?? new FontFamily("Cascadia Code, Consolas, Menlo, DejaVu Sans Mono, monospace");
        _title = $"{model.Name} · 独立窗口 — Terminal Hub";
        // OSC title changes (e.g. vim / ssh hosts) flow into the window title.
        // Named handler so Dispose can unsubscribe — the emulator outlives this VM
        // and would otherwise pin it (plus fire stale UI posts) after every popout.
        model.Emulator.TitleChanged += OnEmulatorTitleChanged;
    }

    private void OnEmulatorTitleChanged(string title) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            Title = string.IsNullOrWhiteSpace(title)
                ? $"{Model.Name} · 独立窗口 — Terminal Hub"
                : $"{Model.Name} · {title} — Terminal Hub");

    public void Dispose() => Model.Emulator.TitleChanged -= OnEmulatorTitleChanged;
}
