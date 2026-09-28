namespace TerminalHub.Core.Deploy;

/// <summary>
/// One publish output directory under <c>artifacts/publish</c>.
/// <see cref="Modified"/> is the newest immediate file write time (UTC).
/// <see cref="SizeBytes"/> is the sum of those immediate files — not a recursive walk.
/// </summary>
public sealed record RecentArtifact(
    string Path,
    string Rid,
    DateTimeOffset Modified,
    long SizeBytes,
    int FileCount);

/// <summary>
/// Recent publish outputs for the Deploy dock menu.
/// Ordering is <see cref="RecentArtifact.Modified"/> descending, then path.
/// Empty when <see cref="ArtifactLocator"/> finds nothing.
/// </summary>
public static class RecentArtifactList
{
    public const int DefaultLimit = 12;

    public static IReadOnlyList<RecentArtifact> List(string? startDir, int limit = DefaultLimit)
    {
        if (string.IsNullOrWhiteSpace(startDir) || limit <= 0) return [];

        List<ArtifactLocator.ArtifactDir> found;
        try
        {
            found = ArtifactLocator.Find(startDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return [];
        }

        var rows = new List<RecentArtifact>(found.Count);
        foreach (var dir in found)
        {
            long size = 0;
            var counted = 0;
            DateTimeOffset? newest = null;
            foreach (var file in dir.Files)
            {
                try
                {
                    var fi = new FileInfo(file);
                    if (!fi.Exists) continue;
                    size += fi.Length;
                    counted++;
                    var modified = new DateTimeOffset(DateTime.SpecifyKind(fi.LastWriteTimeUtc, DateTimeKind.Utc));
                    if (newest is null || modified > newest)
                        newest = modified;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file can disappear between the locate and the stat.
                }
            }
            if (counted == 0 || newest is null) continue;

            rows.Add(new RecentArtifact(
                dir.Path,
                RidOf(dir.Path),
                newest.Value,
                size,
                counted));
        }

        return rows
            .OrderByDescending(r => r.Modified)
            .ThenBy(r => r.Path, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    private static string RidOf(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed);
    }
}
