using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.AI;

namespace TerminalHub.App.ViewModels;

/// <summary>Codex/AI assistant right-panel: checklist + suggestions + NL input + replies.</summary>
public partial class AiPanelViewModel : ViewModelBase
{
    private readonly IAiAssistant _assistant;
    private readonly Action<string>? _onOutput;

    public ObservableCollection<ChecklistItem> Checklist { get; } = [];
    public ObservableCollection<SuggestedTask> Suggestions { get; } = [];
    public ObservableCollection<ChatMessage> Messages { get; } = [];

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private int? _progressPercent;
    [ObservableProperty] private bool _hasProgress;
    [ObservableProperty] private bool _hasMessages;
    [ObservableProperty] private string _taskInput = "";

    public AiPanelViewModel(IAiAssistant assistant, Action<string>? onOutput = null)
    {
        _assistant = assistant;
        _onOutput = onOutput;
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
        Messages.Clear();
        foreach (var m in _assistant.Messages) Messages.Add(m);
        HasMessages = Messages.Count > 0;
    }

    [RelayCommand]
    private async Task SubmitTask() => await SubmitQueryAsync(TaskInput, () => TaskInput = "");

    /// <summary>Shared NL submit path — right-panel input and the middle "?"-prefixed prompt.</summary>
    public async Task SubmitQueryAsync(string text, Action? clear = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        clear?.Invoke();
        var reply = await _assistant.SubmitTaskAsync(text.Trim());
        _onOutput?.Invoke($"Codex 收到任务: {text.Trim()}");
        _onOutput?.Invoke($"Codex: {reply}");
    }

    [RelayCommand]
    private async Task RunSuggestion(SuggestedTask? task)
    {
        if (task is null) return;
        var reply = await _assistant.RunSuggestionAsync(task);
        _onOutput?.Invoke($"Codex: {reply}");
    }

    [RelayCommand]
    private void ToggleItem(ChecklistItem? item)
    {
        if (item is null) return;
        _assistant.ToggleItem(Checklist.IndexOf(item));
    }
}
