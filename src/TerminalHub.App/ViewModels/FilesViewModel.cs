using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Files;

namespace TerminalHub.App.ViewModels;

/// <summary>Right-rail Files tab: browse local dirs, preview small text files.</summary>
public partial class FilesViewModel : ViewModelBase, IDisposable
{
    private FileSystemWatcher? _watcher;
    private int _refreshPending;
    private bool _disposed;
    private readonly Func<string?> _sessionCwd;
    private readonly Action<string>? _openTerminalAt;
    private readonly Func<string, Task>? _copyTextAsync;
    private readonly Func<bool>? _hasActiveSession;
    private readonly Action<string> _revealInFileManager;
    private readonly Action<string> _openExternal;
    private bool _initialized;

    /// <summary>One clickable breadcrumb segment.</summary>
    public sealed record Crumb(string Label, string Path);

    public ObservableCollection<FileEntry> Entries { get; } = [];
    public ObservableCollection<Crumb> Breadcrumbs { get; } = [];

    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private FileEntry? _selectedEntry;
    [ObservableProperty] private string _statusText = "";
    /// <summary>true → status line paints as an error (UiBad); false → info (UiMuted).</summary>
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _previewTitle = "";
    /// <summary>Absolute path of the previewed file (title tooltip).</summary>
    [ObservableProperty] private string _previewPath = "";
    [ObservableProperty] private string _previewMeta = "";
    [ObservableProperty] private string _previewText = "";
    /// <summary>Decoded image for raster previews; null for text/binary.</summary>
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _previewImage;
    [ObservableProperty] private bool _previewIsImage;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private bool _canGoUp;
    [ObservableProperty] private bool _showHidden;

    /// <param name="sessionCwd">Returns the active session's working dir (may be null/empty).</param>
    /// <param name="openTerminalAt">Sends a real `cd` into the active terminal session.</param>
    /// <param name="copyTextAsync">Best-effort clipboard copy (owned by the shell VM).</param>
    /// <param name="hasActiveSession">Whether a live session exists to receive `cd`.</param>
    /// <param name="revealInFileManager">Opens the OS file manager at a path
    /// (tests inject a capture; null → platform default).</param>
    /// <param name="showHidden">Include dot-prefixed/OS-hidden entries; off by
    /// default like Explorer/Finder.</param>
    /// <param name="openExternal">Launches a file with the OS default app
    /// (tests inject a capture; null → platform default).</param>
    public FilesViewModel(Func<string?>? sessionCwd = null,
                          Action<string>? openTerminalAt = null,
                          Func<string, Task>? copyTextAsync = null,
                          Func<bool>? hasActiveSession = null,
                          Action<string>? revealInFileManager = null,
                          Action<string>? openExternal = null,
                          bool showHidden = false)
    {
        _sessionCwd = sessionCwd ?? (() => null);
        _openTerminalAt = openTerminalAt;
        _copyTextAsync = copyTextAsync;
        _hasActiveSession = hasActiveSession;
        _revealInFileManager = revealInFileManager ?? RevealDefault;
        _openExternal = openExternal ?? OpenDefault;
        _showHidden = showHidden;
    }

    /// <summary>Toggling dotfile visibility refilters the current directory.</summary>
    partial void OnShowHiddenChanged(bool value) => NavigateTo(CurrentPath);

    /// <summary>Selection change arms/disarms the entry commands and keeps the
    /// preview pane tracking the selected file (single-click preview); a dir or
    /// no selection clears it.</summary>
    partial void OnSelectedEntryChanged(FileEntry? value)
    {
        OpenInTerminalCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        RevealInFileManagerCommand.NotifyCanExecuteChanged();
        OpenExternallyCommand.NotifyCanExecuteChanged();
        if (value is { IsDirectory: false }) PreviewFile(value.FullPath);
        else HasPreview = false;
    }

    /// <summary>Active-session churn also gates 「在此打开终端」 — the shell VM
    /// calls this whenever ActiveSession changes.</summary>
    public void NotifySessionAvailability() => OpenInTerminalCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// First visit to the Files tab lands in the active session's cwd (else ~).
    /// Called when the right rail switches to Files — deferred so startup
    /// sessions have spawned by then.
    /// </summary>
    public void EnsureSessionDir()
    {
        if (_initialized) return;
        _initialized = true;
        NavigateTo(NonEmpty(_sessionCwd()) ?? Home());
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string Home() =>
        OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Environment.GetEnvironmentVariable("HOME")
              ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Navigate the browser to <paramref name="path"/> (dirs only).</summary>
    public void NavigateTo(string? path)
    {
        _initialized = true;
        if (string.IsNullOrWhiteSpace(path)) path = Home();
        try
        {
            var entries = LocalFileBrowser.ListDirectory(path, ShowHidden);
            var selection = SelectedEntry?.FullPath;
            Entries.Clear();
            foreach (var e in entries) Entries.Add(e);
            SelectedEntry = Entries.FirstOrDefault(e => e.FullPath == selection);
            CurrentPath = Path.GetFullPath(path);
            WatchDirectory();
            RebuildCrumbs();
            StatusIsError = false;
            StatusText = entries.Count == 0 ? "空目录" : $"{entries.Count} 项";
            CanGoUp = Directory.GetParent(CurrentPath) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusIsError = true;
            StatusText = $"无法打开目录: {ex.Message}";
        }
    }

    private void WatchDirectory()
    {
        if (_watcher?.Path == CurrentPath || _disposed) return;
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(CurrentPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
        };
        _watcher.Changed += OnDirectoryChanged;
        _watcher.Created += OnDirectoryChanged;
        _watcher.Deleted += OnDirectoryChanged;
        _watcher.Renamed += OnDirectoryChanged;
        // Buffer overflow (npm install / build output) makes the watcher stop
        // raising events entirely — rebuild it and refresh once when that happens.
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _watcher?.Dispose();
            _watcher = null;
            // WatchDirectory() → new FileSystemWatcher(CurrentPath) throws if the
            // watched dir was deleted between the error and this rebuild — that
            // would be an unhandled exception on the dispatcher. NavigateTo has
            // its own guard; only rebuild the watcher when the dir still exists.
            if (Directory.Exists(CurrentPath)) WatchDirectory();
            NavigateTo(CurrentPath);
        });
    }

    private void OnDirectoryChanged(object sender, FileSystemEventArgs e)
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            if (_disposed) return;
            NavigateTo(CurrentPath);
            if (HasPreview && SelectedEntry is { IsDirectory: false } entry) PreviewFile(entry.FullPath);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher?.Dispose();
    }

    private void RebuildCrumbs()
    {
        Breadcrumbs.Clear();
        var path = CurrentPath;
        var sep = Path.DirectorySeparatorChar;

        // Root crumb: "/" on Unix, "C:\" on Windows, "\\server\share\" for UNC.
        var root = Path.GetPathRoot(path) ?? sep.ToString();
        Breadcrumbs.Add(new Crumb(root.TrimEnd('\\'), root));
        // Split only the part *below* the root — the raw split would re-yield the
        // drive ("C:") or server/share segments and produce "C:\C:"-style paths.
        var rel = path[root.Length..];
        var acc = root;
        foreach (var part in rel.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            acc = Path.Combine(acc, part);
            Breadcrumbs.Add(new Crumb(part, acc));
        }
    }

    /// <summary>Open an entry (double-click / Enter): dir → navigate,
    /// file → default app (single-click already previews).</summary>
    public void Open(FileEntry? entry)
    {
        if (entry is null) return;
        if (entry.IsDirectory) NavigateTo(entry.FullPath);
        else OpenExternally(entry);
    }

    /// <summary>Open the currently selected entry (keyboard).</summary>
    public void OpenSelected() => Open(SelectedEntry);

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico" };

    /// <summary>Bitmaps beyond this size fall through to the binary notice.</summary>
    public const long MaxImagePreviewBytes = 16 * 1024 * 1024;

    private void PreviewFile(string path)
    {
        // Release the previous bitmap before replacing: keeping it alive would
        // hold the source file open (a real file lock on Windows).
        PreviewImage?.Dispose();
        PreviewImage = null;
        PreviewIsImage = false;
        try
        {
            // Images bypass ReadPreview: its 2 MB text cap would classify even
            // a modest PNG as TooLarge before the bitmap decode is attempted.
            if (ImageExtensions.Contains(Path.GetExtension(path)))
            {
                var info = new FileInfo(path);
                var imeta = $"{MainWindowViewModel.FmtBytes(info.Length)} · {info.LastWriteTime:yyyy-MM-dd HH:mm}";
                PreviewTitle = Path.GetFileName(path);
                PreviewPath = Path.GetFullPath(path);
                if (info.Length <= MaxImagePreviewBytes && TryLoadImage(path))
                {
                    PreviewMeta = $"{imeta} · 图片";
                    PreviewText = "";
                    PreviewIsImage = true;
                }
                else if (PreviewImage is null && info.Length > MaxImagePreviewBytes)
                {
                    PreviewMeta = $"{imeta} · 超过 {MainWindowViewModel.FmtBytes(MaxImagePreviewBytes)}";
                    PreviewText = "〔图片过大 — 不提供预览〕";
                }
                else
                {
                    // Decode failed (corrupt/mislabeled) — honest binary notice.
                    PreviewMeta = $"{imeta} · 二进制文件";
                    PreviewText = "〔二进制文件 — 不提供文本预览〕";
                }
                HasPreview = true;
                return;
            }

            var p = LocalFileBrowser.ReadPreview(path);
            PreviewTitle = Path.GetFileName(path);
            PreviewPath = Path.GetFullPath(path);
            var meta = $"{MainWindowViewModel.FmtBytes(p.SizeBytes)} · {p.Modified:yyyy-MM-dd HH:mm}";
            switch (p.Kind)
            {
                case PreviewKind.Binary:
                    PreviewMeta = $"{meta} · 二进制文件";
                    PreviewText = "〔二进制文件 — 不提供文本预览〕";
                    break;
                case PreviewKind.TooLarge:
                    PreviewMeta = $"{meta} · 超过 {MainWindowViewModel.FmtBytes(LocalFileBrowser.MaxPreviewBytes)}";
                    PreviewText = "〔文件过大 — 不提供文本预览〕";
                    break;
                default:
                    PreviewMeta = p.Truncated ? $"{meta} · 已截断" : meta;
                    PreviewText = p.Text;
                    break;
            }
            HasPreview = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Selection-tracked preview: a failed read must not leave the
            // previous file's preview on screen under the new selection.
            HasPreview = false;
            StatusIsError = true;
            StatusText = $"无法读取文件: {ex.Message}";
        }
    }

    private bool TryLoadImage(string path)
    {
        try
        {
            PreviewImage = new Avalonia.Media.Imaging.Bitmap(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                     or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    [RelayCommand]
    private void Up()
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null) NavigateTo(parent.FullName);
    }

    [RelayCommand] private void Refresh() => NavigateTo(CurrentPath);

    /// <summary>Jump to the active terminal session's working directory.</summary>
    [RelayCommand] private void GoToSessionDir() => NavigateTo(NonEmpty(_sessionCwd()) ?? Home());

    /// <summary>「在此打开终端」: cd the active terminal into the selected directory;
    /// for a file, cd to its parent dir (we don't launch editors). No-op without a
    /// selection or a live session.</summary>
    [RelayCommand(CanExecute = nameof(CanSendToTerminal))]
    private void OpenInTerminal(FileEntry? entry)
    {
        var e = entry ?? SelectedEntry;
        if (e is null || _openTerminalAt is null) return;
        var dir = e.IsDirectory ? e.FullPath : Path.GetDirectoryName(e.FullPath);
        if (string.IsNullOrEmpty(dir)) return;
        _openTerminalAt(dir);
        StatusIsError = false;
        StatusText = $"终端已 cd → {dir}";
    }

    private bool CanSendToTerminal(FileEntry? entry)
        => (entry ?? SelectedEntry) is not null && (_hasActiveSession?.Invoke() ?? true);

    /// <summary>「复制路径」: absolute path of the selection to the clipboard.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CopyPath(FileEntry? entry)
    {
        var e = entry ?? SelectedEntry;
        if (e is null) return;
        if (_copyTextAsync is not null) await _copyTextAsync(e.FullPath);
        StatusIsError = false;
        StatusText = $"已复制 {e.FullPath}";
    }

    /// <summary>「在文件管理器中显示」: Windows Explorer selects the file / opens
    /// the dir; elsewhere xdg-open on the dir (a file resolves to its parent).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RevealInFileManager(FileEntry? entry)
    {
        var e = entry ?? SelectedEntry;
        if (e is null) return;
        try
        {
            _revealInFileManager(e.FullPath);
            StatusIsError = false;
            StatusText = $"已在文件管理器中显示 {e.FullPath}";
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                     or System.ComponentModel.Win32Exception
                                     or IOException)
        {
            StatusIsError = true;
            StatusText = $"无法打开文件管理器: {ex.Message}";
        }
    }

    private static void RevealDefault(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var arg = Directory.Exists(path) ? $"\"{path}\"" : $"/select,\"{path}\"";
            System.Diagnostics.Process.Start("explorer.exe", arg);
        }
        else
        {
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (dir is null) return;
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("xdg-open", $"\"{dir}\"")
                { UseShellExecute = false });
        }
    }

    /// <summary>「用默认应用打开」: shell-associated handler for the file
    /// (preview stays the in-app double-click path). Files only.</summary>
    [RelayCommand(CanExecute = nameof(IsFileSelection))]
    private void OpenExternally(FileEntry? entry)
    {
        var e = entry ?? SelectedEntry;
        if (e is null || e.IsDirectory) return;
        try
        {
            _openExternal(e.FullPath);
            StatusIsError = false;
            StatusText = $"已打开 {e.Name}";
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                     or System.ComponentModel.Win32Exception
                                     or IOException)
        {
            StatusIsError = true;
            StatusText = $"无法打开文件: {ex.Message}";
        }
    }

    private static void OpenDefault(string path)
    {
        if (OperatingSystem.IsWindows())
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        else
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("xdg-open", $"\"{path}\"")
                { UseShellExecute = false });
    }

    private bool IsFileSelection(FileEntry? entry)
        => (entry ?? SelectedEntry) is { IsDirectory: false };

    /// <summary>Create a folder under CurrentPath. Null/blank name → auto
    /// 「新建文件夹」「新建文件夹 2」…; the new entry gets selected.</summary>
    public void NewFolder(string? name)
    {
        try
        {
            var final = string.IsNullOrWhiteSpace(name) ? UniqueName("新建文件夹") : name.Trim();
            var path = Path.Combine(CurrentPath, final);
            Directory.CreateDirectory(path);
            NavigateTo(CurrentPath);
            var full = Path.GetFullPath(path);
            SelectedEntry = Entries.FirstOrDefault(e => e.FullPath == full);
            StatusIsError = false;
            StatusText = $"已创建 {final}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                     or ArgumentException or NotSupportedException)
        {
            StatusIsError = true;
            StatusText = $"无法创建文件夹: {ex.Message}";
        }
    }

    /// <summary>Create an empty UTF-8 text file under CurrentPath; same
    /// unique-naming and selection rules as <see cref="NewFolder"/>.</summary>
    public void NewTextFile(string? name)
    {
        try
        {
            var baseName = string.IsNullOrWhiteSpace(name) ? "新建文本.txt" : name.Trim();
            if (!baseName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                && !baseName.Contains('.')) baseName += ".txt";
            var final = UniqueName(baseName);
            var path = Path.Combine(CurrentPath, final);
            File.WriteAllText(path, "");
            NavigateTo(CurrentPath);
            var full = Path.GetFullPath(path);
            SelectedEntry = Entries.FirstOrDefault(e => e.FullPath == full);
            StatusIsError = false;
            StatusText = $"已创建 {final}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                     or ArgumentException or NotSupportedException)
        {
            StatusIsError = true;
            StatusText = $"无法创建文件: {ex.Message}";
        }
    }

    private string UniqueName(string baseName)
    {
        var stem = Path.GetFileNameWithoutExtension(baseName);
        var ext = Path.GetExtension(baseName);
        var name = baseName;
        var i = 1;
        while (Directory.Exists(Path.Combine(CurrentPath, name))
               || File.Exists(Path.Combine(CurrentPath, name)))
            name = $"{stem} {++i}{ext}";
        return name;
    }

    private bool HasSelection(FileEntry? entry) => (entry ?? SelectedEntry) is not null;

    [RelayCommand]
    private void NavigateCrumb(Crumb? crumb)
    {
        if (crumb is not null) NavigateTo(crumb.Path);
    }
}
