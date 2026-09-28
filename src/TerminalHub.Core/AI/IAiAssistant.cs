namespace TerminalHub.Core.AI;

public enum ChecklistState
{
    Pending,
    Active,
    Done,
}

public sealed record ChecklistItem
{
    public required string Text { get; init; }
    public ChecklistState State { get; init; } = ChecklistState.Pending;
}

public sealed record SuggestedTask
{
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "📁";
}

/// <summary>A chat row in the Codex panel (sender label + text).</summary>
public sealed record ChatMessage(string Sender, string Text);

/// <summary>
/// AI assistant hook. The default build ships a deterministic local
/// implementation (<see cref="LocalAiAssistant"/>); a real provider can
/// implement this later. No paid API calls are allowed in the default build.
/// </summary>
public interface IAiAssistant
{
    /// <summary>Short status line shown under the assistant header.</summary>
    string StatusText { get; }

    IReadOnlyList<ChecklistItem> Checklist { get; }
    IReadOnlyList<SuggestedTask> Suggestions { get; }
    IReadOnlyList<ChatMessage> Messages { get; }

    /// <summary>0-100 progress for the center progress bar, or null when idle.</summary>
    int? ProgressPercent { get; }

    event Action? Changed;

    /// <summary>Submit natural-language task text ("描述你想做的任务…"). Returns the reply.</summary>
    Task<string> SubmitTaskAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Accept a suggestion → checklist item. Returns the reply.</summary>
    Task<string> RunSuggestionAsync(SuggestedTask task, CancellationToken cancellationToken = default);

    /// <summary>Toggle a checklist item's done state by index.</summary>
    void ToggleItem(int index);
}
