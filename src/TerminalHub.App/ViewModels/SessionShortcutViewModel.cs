using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public sealed partial class SessionShortcutViewModel : ObservableObject
{
    public SessionShortcutBinding Binding { get; }
    public string Label => Binding.Action switch
    {
        SessionShortcutAction.Next => "下一个终端",
        SessionShortcutAction.Previous => "上一个终端",
        _ => $"第 {Binding.SessionIndex + 1} 个终端"
    };
    private readonly Action _changed;
    public SessionShortcutViewModel(SessionShortcutBinding binding, Action changed)
    {
        Binding = binding;
        _changed = changed;
    }
    public string Gesture
    {
        get => Binding.Gesture;
        set { Binding.Gesture = value ?? ""; OnPropertyChanged(); _changed(); }
    }
    [ObservableProperty] private string _error = "";
    public KeyGesture? ParsedGesture { get; set; }
}
