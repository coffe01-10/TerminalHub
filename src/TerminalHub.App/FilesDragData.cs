using Avalonia.Input;
using Avalonia.Platform.Storage;
using TerminalHub.Core.Files;

namespace TerminalHub.App;

/// <summary>Builds the OS-level drag payload (DataFormats.Files) used when a
/// Files-panel row is dragged into the terminal — the terminal then inserts the
/// shell-quoted path, same as a drop from Explorer.</summary>
public static class FilesDragData
{
    public static async Task<DataObject?> CreateAsync(IStorageProvider provider, FileEntry entry)
    {
        IStorageItem? item = entry.IsDirectory
            ? await provider.TryGetFolderFromPathAsync(entry.FullPath)
            : await provider.TryGetFileFromPathAsync(entry.FullPath);
        if (item is null) return null;
        var data = new DataObject();
        data.Set(DataFormats.Files, new[] { item });
        return data;
    }
}
