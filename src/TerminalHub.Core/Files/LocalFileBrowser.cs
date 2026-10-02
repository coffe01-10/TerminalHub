using System.Text;

namespace TerminalHub.Core.Files;

/// <summary>One row in the Files panel (directory or file).</summary>
public sealed record FileEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsSymlink { get; init; }
    /// <summary>Link target as stored (may be relative); null for non-links.</summary>
    public string? LinkTarget { get; init; }
    /// <summary>Dot-prefixed name (Unix convention) or the OS hidden attribute.</summary>
    public bool IsHidden { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset Modified { get; init; }
}

public enum PreviewKind { Text, Binary, TooLarge }

/// <summary>Text preview of a small file.</summary>
public sealed record FilePreview
{
    public required string Path { get; init; }
    public required string Text { get; init; }
    public PreviewKind Kind { get; init; }
    public bool Truncated { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset Modified { get; init; }
}

/// <summary>
/// Local filesystem browsing for the right-rail Files panel.
/// Pure BCL — unit-testable, no Avalonia dependency.
/// </summary>
public static class LocalFileBrowser
{
    /// <summary>Files beyond this size are never previewed.</summary>
    public const long MaxPreviewBytes = 2 * 1024 * 1024;
    private const int SniffBytes = 8192;
    private const int MaxPreviewChars = 60_000;

    /// <summary>
    /// Directories first (alpha), then files (alpha). Entries that cannot be
    /// stat'ed (broken symlinks, denied) are skipped rather than failing the list.
    /// Throws <see cref="DirectoryNotFoundException"/> / <see cref="UnauthorizedAccessException"/>
    /// when the directory itself is unreadable.
    /// </summary>
    public static IReadOnlyList<FileEntry> ListDirectory(string path, bool includeHidden = true)
    {
        var dirs = new List<FileEntry>();
        var files = new List<FileEntry>();

        foreach (var d in Directory.EnumerateDirectories(path))
        {
            FileEntry? e = null;
            try { e = ToEntry(new DirectoryInfo(d), isDirectory: true); }
            catch { /* skip unreadable */ }
            if (e is not null && (includeHidden || !e.IsHidden)) dirs.Add(e);
        }
        foreach (var f in Directory.EnumerateFiles(path))
        {
            FileEntry? e = null;
            try { e = ToEntry(new FileInfo(f), isDirectory: false); }
            catch { /* skip unreadable */ }
            if (e is not null && (includeHidden || !e.IsHidden)) files.Add(e);
        }

        dirs.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        files.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        dirs.AddRange(files);
        return dirs;
    }

    private static FileEntry ToEntry(FileSystemInfo info, bool isDirectory)
        => new()
        {
            Name = info.Name,
            FullPath = info.FullName,
            IsDirectory = isDirectory,
            IsSymlink = info.LinkTarget is not null,
            LinkTarget = info.LinkTarget,
            IsHidden = info.Name.StartsWith('.')
                       || info.Attributes.HasFlag(FileAttributes.Hidden),
            SizeBytes = isDirectory ? 0 : ((FileInfo)info).Length,
            Modified = info.LastWriteTime,
        };

    /// <summary>
    /// Reads a text preview. Binary detection: a NUL byte within the first
    /// <see cref="SniffBytes"/> bytes. Files over <see cref="MaxPreviewBytes"/>
    /// come back as <see cref="PreviewKind.TooLarge"/>; very long text is
    /// truncated at <see cref="MaxPreviewChars"/>.
    /// </summary>
    public static FilePreview ReadPreview(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("file not found", path);

        if (info.Length > MaxPreviewBytes)
        {
            return new FilePreview
            {
                Path = path, Text = "", Kind = PreviewKind.TooLarge,
                SizeBytes = info.Length, Modified = info.LastWriteTime,
            };
        }

        var all = File.ReadAllBytes(path);
        var sniffLen = Math.Min(SniffBytes, all.Length);
        if (all.AsSpan(0, sniffLen).IndexOf((byte)0) >= 0)
        {
            return new FilePreview
            {
                Path = path, Text = "", Kind = PreviewKind.Binary,
                SizeBytes = info.Length, Modified = info.LastWriteTime,
            };
        }

        var text = Encoding.UTF8.GetString(all);
        var truncated = text.Length > MaxPreviewChars;
        if (truncated) text = text[..MaxPreviewChars];

        return new FilePreview
        {
            Path = path, Text = text, Kind = PreviewKind.Text, Truncated = truncated,
            SizeBytes = info.Length, Modified = info.LastWriteTime,
        };
    }
}
