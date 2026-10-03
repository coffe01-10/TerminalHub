namespace TerminalHub.App.ViewModels;

public sealed record PaletteEntry(string Label, string Detail, string Shortcut, Func<Task> Execute)
{
    public string LocalizedLabel => TerminalHub.Core.Localization.Localizer.Current.Translate(Label);
    public string LocalizedDetail => TerminalHub.Core.Localization.Localizer.Current.Translate(Detail);
    public bool Matches(string query) => query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .All(part => (LocalizedLabel + " " + LocalizedDetail).Contains(part, StringComparison.OrdinalIgnoreCase));
}
