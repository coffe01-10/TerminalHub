namespace TerminalHub.App.ViewModels;

public sealed record PaletteEntry(string Label, string Detail, string Shortcut, Func<Task> Execute)
{
    public bool Matches(string query) => query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .All(part => (Label + " " + Detail).Contains(part, StringComparison.OrdinalIgnoreCase));
}
