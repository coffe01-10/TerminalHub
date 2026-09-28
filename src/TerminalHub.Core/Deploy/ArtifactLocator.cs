namespace TerminalHub.Core.Deploy;

/// <summary>Locate publish outputs under the repo's artifacts/ directory (Deploy dock button).</summary>
public static class ArtifactLocator
{
    /// <summary>
    /// Returns publish directories (e.g. artifacts/publish/linux-x64) containing files,
    /// searched from <paramref name="startDir"/> upward to the filesystem root.
    /// </summary>
    public static List<ArtifactDir> Find(string startDir)
    {
        var found = new List<ArtifactDir>();
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            var publish = Path.Combine(dir.FullName, "artifacts", "publish");
            if (Directory.Exists(publish))
            {
                CollectLeafDirs(publish, found);
                if (found.Count == 0) return found; // publish exists but empty — report via callers
                return found;
            }
            dir = dir.Parent;
        }
        return found;
    }

    private static void CollectLeafDirs(string publish, List<ArtifactDir> into)
    {
        var subs = Directory.GetDirectories(publish);
        if (subs.Length == 0)
        {
            var files = Directory.GetFiles(publish);
            if (files.Length > 0) into.Add(new ArtifactDir(publish, files));
            return;
        }
        foreach (var s in subs)
        {
            var files = Directory.GetFiles(s);
            if (files.Length > 0) into.Add(new ArtifactDir(s, files));
        }
        var rootFiles = Directory.GetFiles(publish);
        if (rootFiles.Length > 0) into.Add(new ArtifactDir(publish, rootFiles));
    }

    /// <summary>A directory holding publish output files.</summary>
    public sealed record ArtifactDir(string Path, string[] Files);
}
