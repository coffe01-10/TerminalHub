namespace TerminalHub.Core.Settings;

/// <summary>Persisted output bookmark: name + source session name + text snapshot.
/// The buffer anchor is runtime-only — after a restart the text stays viewable
/// and copyable, but the bookmark can no longer jump to live output.</summary>
public sealed class OutputBookmark
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SessionName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Text { get; set; } = "";
}
