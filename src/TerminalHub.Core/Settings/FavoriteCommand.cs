namespace TerminalHub.Core.Settings;

public sealed class FavoriteCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    /// <summary>Empty means any shell. A value is informational and does not block insertion.</summary>
    public string Shell { get; set; } = "";
    public string Shortcut { get; set; } = "";

    public static bool AppliesTo(string favoriteShell, string sessionShell)
    {
        if (string.IsNullOrWhiteSpace(favoriteShell)) return true;
        var favorite = Path.GetFileNameWithoutExtension(favoriteShell);
        var session = Path.GetFileNameWithoutExtension(sessionShell);
        return favorite.Equals(session, StringComparison.OrdinalIgnoreCase);
    }
}
