using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.AI;

namespace TerminalHub.App.ViewModels;

/// <summary>Codex/AI assistant right-panel: checklist + suggestions + NL input.</summary>
public partial class AiPanelViewModel : ViewModelBase
{
    private readonly IAiAssistant _assistant;

    public ObservableCollection<ChecklistItem> Checklist { get; } = [];
    public ObservableCollection<SuggestedTask> Suggestions { get; } = [];

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int? _progressPercent;
    [ObservableProperty] private bool _hasProgress;
    [ObservableProperty] private string _taskInput = "";

    public AiPanelViewModel(IAiAssistant assistant)
    {
        _assistant = assistant;
        _assistant.Changed += OnChanged;
        Refresh();
    }

    private void OnChanged()
        => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        StatusText = _assistant.StatusText;
        ProgressPercent = _assistant.ProgressPercent;
        HasProgress = _assistant.ProgressPercent is not null;
        Checklist.Clear();
        foreach (var c in _assistant.Checklist) Checklist.Add(c);
        Suggestions.Clear();
        foreach (var s in _assistant.Suggestions) Suggestions.Add(s);
    }

    [RelayCommand]
    private async Task SubmitTask()
    {
        if (string.IsNullOrWhiteSpace(TaskInput)) return;
        var text = TaskInput;
        TaskInput = "";
        await _assistant.SubmitTaskAsync(text);
    }

    [RelayCommand]
    private async Task RunSuggestion(SuggestedTask? task)
    {
        if (task is null) return;
        await _assistant.RunSuggestionAsync(task);
    }
}
