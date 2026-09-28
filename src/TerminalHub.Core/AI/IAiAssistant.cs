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

/// <summary>
/// AI assistant hook. MVP ships a mock; a real provider can implement this later.
/// No paid API calls are allowed in the default build.
/// </summary>
public interface IAiAssistant
{
    /// <summary>Short status line shown under the assistant header.</summary>
    string StatusText { get; }

    IReadOnlyList<ChecklistItem> Checklist { get; }
    IReadOnlyList<SuggestedTask> Suggestions { get; }

    /// <summary>0-100 progress for the center progress bar, or null when idle.</summary>
    int? ProgressPercent { get; }

    event Action? Changed;

    /// <summary>Submit natural-language task text ("描述你想做的任务…").</summary>
    Task SubmitTaskAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Accept a suggestion (starts mock execution).</summary>
    Task RunSuggestionAsync(SuggestedTask task, CancellationToken cancellationToken = default);
}
