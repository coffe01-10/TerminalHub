using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public sealed partial class FavoriteCommandViewModel : ObservableObject
{
    public FavoriteCommand Model { get; }
    private readonly Action _changed;
    public KeyGesture? ParsedGesture { get; set; }
    [ObservableProperty] private string _error = "";

    public FavoriteCommandViewModel(FavoriteCommand model, Action changed)
    {
        Model = model;
        _changed = changed;
    }

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value ?? ""; OnPropertyChanged(); _changed(); }
    }
    public string Command
    {
        get => Model.Command;
        set { Model.Command = value ?? ""; OnPropertyChanged(); _changed(); }
    }
    public string Shell
    {
        get => Model.Shell;
        set { Model.Shell = value ?? ""; OnPropertyChanged(); _changed(); }
    }
    public string Shortcut
    {
        get => Model.Shortcut;
        set { Model.Shortcut = value ?? ""; OnPropertyChanged(); _changed(); }
    }
}
