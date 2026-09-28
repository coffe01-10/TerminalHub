using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Files;

namespace TerminalHub.App.ViewModels;

/// <summary>Right-rail Files tab: browse local dirs, preview small text files.</summary>
public partial class FilesViewModel : ViewModelBase
{
    private readonly Func<string?> _sessionCwd;
    private readonly Action<string>? _openTerminalAt;
    private readonly Func<string, Task>? _copyTextAsync;
    private readonly Func<bool>? _hasActiveSession;
    private bool _initialized;

    /// <summary>One clickable breadcrumb segment.</summary>
    public sealed record Crumb(string Label, string Path);

    public ObservableCollection<FileEntry> Entries { get; } = [];
    public ObservableCollection<Crumb> Breadcrumbs { get; } = [];

    [ObservableProperty] private string _currentPath = "";
    [ObservableProperty] private FileEntry? _selectedEntry;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _previewTitle = "";
    [ObservableProperty] private string _previewMeta = "";
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private bool _canGoUp;

    /// <param name="sessionCwd">Returns the active session's working dir (may be null/empty).</param>
    /// <param name="openTerminalAt">Sends a real `cd` into the active terminal session.</param>
    /// <param name="copyTextAsync">Best-effort clipboard copy (owned by the shell VM).</param>
    /// <param name="hasActiveSession">Whether a live session exists to receive `cd`.</param>
    public FilesViewModel(Func<string?>? sessionCwd = null,
                          Action<string>? openTerminalAt = null,
                          Func<string, Task>? copyTextAsync = null,
                          Func<bool>? hasActiveSession = null)
    {
        _sessionCwd = sessionCwd ?? (() => null);
        _openTerminalAt = openTerminalAt;
        _copyTextAsync = copyTextAsync;
        _hasActiveSession = hasActiveSession;
    }

    /// <summary>Selection change arms/disarms the entry commands.</summary>
    partial void OnSelectedEntryChanged(FileEntry? value)
    {
        OpenInTerminalCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
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
            var entries = LocalFileBrowser.ListDirectory(path);
            Entries.Clear();
            foreach (var e in entries) Entries.Add(e);
            CurrentPath = Path.GetFullPath(path);
            RebuildCrumbs();
            StatusText = entries.Count == 0 ? "空目录" : "";
            CanGoUp = Directory.GetParent(CurrentPath) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusText = $"无法打开目录: {ex.Message}";
        }
    }

    private void RebuildCrumbs()
    {
        Breadcrumbs.Clear();
        var path = CurrentPath;
        var sep = Path.DirectorySeparatorChar;
        var parts = path.Split(sep, StringSplitOptions.RemoveEmptyEntries);

        // Root crumb: "/" on Unix, "C:\" on Windows.
        var root = Path.GetPathRoot(path) ?? sep.ToString();
        Breadcrumbs.Add(new Crumb(root.TrimEnd('\\'), root));
        var acc = root;
        foreach (var part in parts)
        {
            acc = Path.Combine(acc, part);
            Breadcrumbs.Add(new Crumb(part, acc));
        }
    }

    /// <summary>Open an entry (double-click / Enter): dir → navigate, file → preview.</summary>
    public void Open(FileEntry? entry)
    {
        if (entry is null) return;
        if (entry.IsDirectory) NavigateTo(entry.FullPath);
        else PreviewFile(entry.FullPath);
    }

    /// <summary>Open the currently selected entry (keyboard).</summary>
    public void OpenSelected() => Open(SelectedEntry);

    private void PreviewFile(string path)
    {
        try
        {
            var p = LocalFileBrowser.ReadPreview(path);
            PreviewTitle = Path.GetFileName(path);
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
            StatusText = $"无法读取文件: {ex.Message}";
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
        StatusText = $"已复制 {e.FullPath}";
    }

    private bool HasSelection(FileEntry? entry) => (entry ?? SelectedEntry) is not null;

    [RelayCommand]
    private void NavigateCrumb(Crumb? crumb)
    {
        if (crumb is not null) NavigateTo(crumb.Path);
    }
}
