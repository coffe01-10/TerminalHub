namespace TerminalHub.Core.Deploy;

/// <summary>
/// Named publish settings for the Deploy dock.
/// Empty <see cref="RepoRoot"/> keeps the walk-up-from-CWD behavior.
/// Empty <see cref="Rid"/> keeps the host platform script.
/// </summary>
public sealed class PublishProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Absolute or stored repo root. Empty = resolve from the process working directory.</summary>
    public string RepoRoot { get; set; } = "";
    /// <summary><c>linux-x64</c>, <c>win-x64</c>, or empty (host).</summary>
    public string Rid { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}

/// <summary>Values the save-profile dialog edits before <see cref="PublishProfiles.Save"/>.</summary>
public sealed record PublishProfileDraft(string Name, string RepoRoot, string Rid, string Note);

/// <summary>
/// Save / switch / delete named publish profiles on <see cref="Settings.AppSettings"/>.
/// The dock applies the active profile on the next Deploy when the caller does not pass
/// an explicit start directory: <see cref="RepoRoot"/> is the walk-up start (empty = CWD),
/// and <see cref="Rid"/> selects the existing script
/// (<c>linux-x64</c> → <c>scripts/publish-linux.sh</c>, <c>win-x64</c> → <c>scripts/publish-windows.ps1</c>).
/// Those scripts still publish their built-in RID; this does not add a new packaging target.
/// </summary>
public static class PublishProfiles
{
    public const string LinuxRid = "linux-x64";
    public const string WindowsRid = "win-x64";

    public static List<PublishProfile> Ensure(Settings.AppSettings settings)
    {
        settings.PublishProfiles ??= [];
        return settings.PublishProfiles;
    }

    /// <summary>Newest <see cref="PublishProfile.LastUsedAt"/> first, then name.</summary>
    public static IReadOnlyList<PublishProfile> List(Settings.AppSettings settings) =>
        Ensure(settings)
            .OrderByDescending(p => p.LastUsedAt)
            .ThenBy(p => p.Name ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static PublishProfile? Active(Settings.AppSettings settings)
    {
        var id = settings.ActivePublishProfileId;
        if (string.IsNullOrWhiteSpace(id)) return null;
        return Ensure(settings).FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.Ordinal));
    }

    /// <summary>Match a profile id (ordinal) or display name (ignore case).</summary>
    public static PublishProfile? Find(Settings.AppSettings settings, string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        var key = idOrName.Trim();
        var profiles = Ensure(settings);
        return profiles.FirstOrDefault(p => string.Equals(p.Id, key, StringComparison.Ordinal))
               ?? profiles.FirstOrDefault(p =>
                   string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Insert or update by name. Empty name is rejected (<c>null</c>).
    /// The saved profile becomes active and <see cref="PublishProfile.LastUsedAt"/> moves to <paramref name="now"/>.
    /// </summary>
    public static PublishProfile? Save(
        Settings.AppSettings settings,
        string? name,
        string? repoRoot,
        string? rid,
        string? note,
        DateTimeOffset? now = null)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return null;

        var clock = now ?? DateTimeOffset.UtcNow;
        var profiles = Ensure(settings);
        var existing = profiles.FirstOrDefault(p =>
            string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new PublishProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAt = clock,
            };
            profiles.Add(existing);
        }

        if (string.IsNullOrWhiteSpace(existing.Id))
            existing.Id = Guid.NewGuid().ToString("N");
        if (existing.CreatedAt == default)
            existing.CreatedAt = clock;

        existing.Name = trimmed;
        existing.RepoRoot = NormalizeRoot(repoRoot);
        existing.Rid = NormalizeRid(rid);
        existing.Note = (note ?? "").Trim();
        existing.LastUsedAt = clock;
        settings.ActivePublishProfileId = existing.Id;
        return existing;
    }

    /// <summary>Make <paramref name="idOrName"/> active. Updates <see cref="PublishProfile.LastUsedAt"/>.</summary>
    public static bool Activate(Settings.AppSettings settings, string? idOrName, DateTimeOffset? now = null)
    {
        var profile = Find(settings, idOrName);
        if (profile is null) return false;
        if (string.IsNullOrWhiteSpace(profile.Id))
            profile.Id = Guid.NewGuid().ToString("N");
        profile.LastUsedAt = now ?? DateTimeOffset.UtcNow;
        settings.ActivePublishProfileId = profile.Id;
        return true;
    }

    /// <summary>Remove a profile. Clears the active id when it pointed at the removed row.</summary>
    public static bool Delete(Settings.AppSettings settings, string? idOrName)
    {
        var profile = Find(settings, idOrName);
        if (profile is null) return false;
        Ensure(settings).Remove(profile);
        if (string.Equals(settings.ActivePublishProfileId, profile.Id, StringComparison.Ordinal))
            settings.ActivePublishProfileId = "";
        return true;
    }

    /// <summary>Stamp the active profile as just used. No-op when none is active.</summary>
    public static PublishProfile? TouchActive(Settings.AppSettings settings, DateTimeOffset? now = null)
    {
        var active = Active(settings);
        if (active is null) return null;
        active.LastUsedAt = now ?? DateTimeOffset.UtcNow;
        return active;
    }

    /// <summary>
    /// A non-empty <paramref name="explicitStart"/> wins (programmatic callers / tests).
    /// Otherwise the active profile root is used when set, else <paramref name="currentDirectory"/>.
    /// </summary>
    public static string ResolveStartDirectory(
        Settings.AppSettings settings, string? explicitStart, string currentDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitStart))
            return explicitStart;
        var root = Active(settings)?.RepoRoot;
        if (!string.IsNullOrWhiteSpace(root))
            return root;
        return currentDirectory ?? "";
    }

    /// <summary>Active profile RID when it names a known script, otherwise <paramref name="hostPlatform"/>.</summary>
    public static PublishPlatform ResolvePlatform(Settings.AppSettings settings, PublishPlatform hostPlatform) =>
        TryParseRid(Active(settings)?.Rid, out var platform) ? platform : hostPlatform;

    public static string HostRid(PublishPlatform platform) =>
        platform == PublishPlatform.Windows ? WindowsRid : LinuxRid;


    /// <summary>
    /// Short Deploy-dock line for the active profile: <c>Name</c>, or <c>Name · rid</c>
    /// when RID is set. Empty when there is no meaningful name (hide the line).
    /// </summary>
    public static string FormatDockLabel(PublishProfile? profile)
    {
        if (profile is null) return "";
        var name = (profile.Name ?? "").Trim();
        if (name.Length == 0) return "";
        var rid = (profile.Rid ?? "").Trim();
        return rid.Length == 0 ? name : $"{name} · {rid}";
    }


    /// <summary>Unique display name, preferring <paramref name="rid"/> then <c>profile</c>, then <c>-2</c>, <c>-3</c>, …</summary>
    public static string SuggestName(Settings.AppSettings settings, string? rid)
    {
        var normalized = NormalizeRid(rid);
        var baseName = normalized.Length == 0 ? "profile" : normalized;
        if (Find(settings, baseName) is null) return baseName;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName}-{i}";
            if (Find(settings, candidate) is null) return candidate;
        }
        return baseName + "-" + Guid.NewGuid().ToString("N")[..6];
    }

    /// <summary>
    /// <c>linux</c>/<c>linux-x64</c> and <c>win</c>/<c>windows</c>/<c>win-x64</c>.
    /// Anything else (including an empty string) is "use the host".
    /// </summary>
    public static bool TryParseRid(string? rid, out PublishPlatform platform)
    {
        platform = default;
        if (string.IsNullOrWhiteSpace(rid)) return false;
        switch (rid.Trim().ToLowerInvariant())
        {
            case "linux":
            case "linux-x64":
                platform = PublishPlatform.Linux;
                return true;
            case "win":
            case "windows":
            case "win-x64":
                platform = PublishPlatform.Windows;
                return true;
            default:
                return false;
        }
    }

    public static string NormalizeRid(string? rid) =>
        TryParseRid(rid, out var platform) ? HostRid(platform) : "";

    private static string NormalizeRoot(string? repoRoot)
    {
        var root = (repoRoot ?? "").Trim();
        if (root.Length == 0) return "";
        try { return Path.GetFullPath(root); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return root;
        }
    }
}
