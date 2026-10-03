using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Views;

/// <summary>Output tools: copy/save terminal text and the bookmark popup.</summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private IInputElement? _bookmarkPreviousFocus;

    private void InitializeOutputTools()
    {
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastPanel.IsVisible = false; };
        Vm.RevealBookmarkRequested += OnRevealBookmark;
        Vm.VisibleBookmarks.CollectionChanged += (_, _) => UpdateBookmarkListState();
    }

    /// <summary>Transient status toast (saved path, copy summary…).</summary>
    private void ShowToast(string text)
    {
        TerminalHub.App.Localization.UiText.Set(ToastText, TextBlock.TextProperty, text);
        ToastPanel.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private async Task<bool> SetClipboardTextAsync(string text)
    {
        try
        {
            if (Clipboard is { } clip) { await clip.SetTextAsync(text); return true; }
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Clipboard copy failed: {ex.Message}"); }
        return false;
    }

    private static int CountLines(string text)
        => text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1;

    // ---------- copy ----------

    /// <summary>「复制当前屏幕」: the visible viewport — live screen at the bottom,
    /// the current scrollback window while browsing history.</summary>
    private async Task CopyVisibleScreenAsync()
    {
        var text = ActiveTerminal()?.GetVisibleText();
        if (string.IsNullOrEmpty(text)) { ShowToast("当前屏幕没有可复制的内容。"); return; }
        ShowToast(await SetClipboardTextAsync(text)
            ? $"已复制当前屏幕（{CountLines(text)} 行）。"
            : "复制失败：剪贴板暂时不可用，请重试。");
    }

    /// <summary>「复制全部输出」: scrollback history + screen still in the buffer;
    /// content already trimmed is not recoverable and is not claimed to be.</summary>
    private async Task CopyAllOutputAsync()
    {
        var text = ActiveTerminal()?.GetAllText();
        if (string.IsNullOrEmpty(text)) { ShowToast("这个终端还没有可复制的输出。"); return; }
        ShowToast(await SetClipboardTextAsync(text)
            ? $"已复制全部保留输出（{CountLines(text)} 行）。"
            : "复制失败：剪贴板暂时不可用，请重试。");
    }

    // ---------- save as text ----------

    /// <summary>Native save picker → UTF-8 .txt. The text snapshot is taken BEFORE
    /// the dialog opens, so no buffer lock is ever held across file I/O; a
    /// dismissed dialog just returns.</summary>
    private async Task SaveTextFileAsync(string title, string suggestedName, string? text)
    {
        if (string.IsNullOrEmpty(text)) { ShowToast("没有可保存的内容。"); return; }
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = TerminalHub.Core.Localization.Localizer.Current.Translate(title),
                SuggestedFileName = suggestedName,
                DefaultExtension = "txt",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(TerminalHub.Core.Localization.Localizer.Current.Translate("文本文件")) { Patterns = ["*.txt"] }],
            });
            if (file is null) return;
            await using (var stream = await file.OpenWriteAsync())
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await stream.WriteAsync(bytes);
            }
            ShowToast($"已保存：{file.TryGetLocalPath() ?? file.Name}");
        }
        catch (Exception ex)
        {
            ShowToast("保存失败：" + ex.Message);
        }
    }

    private async void OnDownloadUpdate(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedUpdateAsset is not { } asset) { Vm.UpdateMessage = "先检查更新并选择下载版本。"; return; }
        if (Vm.IsDownloadingUpdate) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = TerminalHub.Core.Localization.Localizer.Current.Translate("保存 " + asset.Label),
                SuggestedFileName = Path.GetFileName(asset.Name),
                ShowOverwritePrompt = true
            });
            if (file?.TryGetLocalPath() is { } path) await Vm.DownloadUpdateAsync(path);
        }
        catch (Exception ex) { Vm.UpdateMessage = "无法选择下载位置：" + ex.Message; }
    }

    private string DefaultOutputName(string kind)
    {
        var session = Vm.ActiveSession?.Name ?? "terminal";
        return $"{SafeFileName(session)}-{kind}-{DateTime.Now:yyyyMMdd-HHmmss}";
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }

    private void OnCopyScreen(object? sender, RoutedEventArgs e) => _ = CopyVisibleScreenAsync();
    private void OnCopyAllOutput(object? sender, RoutedEventArgs e) => _ = CopyAllOutputAsync();

    private void OnSaveSelection(object? sender, RoutedEventArgs e)
    {
        var text = ActiveTerminal()?.GetSelectedText();
        _ = SaveTextFileAsync("保存选区为文本", DefaultOutputName("选区"), text);
    }

    private void OnSaveAllOutput(object? sender, RoutedEventArgs e)
    {
        var text = ActiveTerminal()?.GetAllText();
        _ = SaveTextFileAsync("保存全部输出", DefaultOutputName("输出"), text);
    }

    /// <summary>命令记录页「保存为文本」— same extraction as 「复制命令和输出」.</summary>
    private void OnSaveCommandRecord(object? sender, RoutedEventArgs e)
    {
        var text = Vm.GetCommandRecordText(Vm.SelectedCommand);
        if (text is null) return; // GetCommandRecordText already set CommandNotice
        _ = SaveTextFileAsync("保存命令记录", DefaultOutputName("命令"), text);
    }

    // ---------- bookmarks ----------

    private void OnShowBookmarks(object? sender, RoutedEventArgs e) => OpenBookmarks();

    private void OpenBookmarks()
    {
        if (BookmarkPanel.IsVisible) { CloseBookmarks(); return; }
        _bookmarkPreviousFocus = FocusManager?.GetFocusedElement();
        Vm.BookmarkFilter = "";
        BookmarkSearch.Text = "";
        Vm.RefreshBookmarkList();
        BookmarkPanel.IsVisible = true;
        UpdateBookmarkListState();
        BookmarkSearch.Focus();
    }

    private void CloseBookmarks(bool restoreFocus = true)
    {
        BookmarkPanel.IsVisible = false;
        // The saved focus may already be detached (e.g. a closed palette box);
        // fall back to the active terminal so keys keep landing somewhere sane.
        if (restoreFocus && _bookmarkPreviousFocus?.Focus() != true)
            ActiveTerminal()?.Focus();
    }

    private void OnBookmarkBackdrop(object? sender, PointerPressedEventArgs e)
    { CloseBookmarks(); e.Handled = true; }

    private void OnBookmarkSearchChanged(object? sender, TextChangedEventArgs e)
        => Vm.BookmarkFilter = BookmarkSearch.Text ?? "";

    private void OnBookmarkScopeSession(object? sender, RoutedEventArgs e) => Vm.BookmarksAllSessions = false;
    private void OnBookmarkScopeAll(object? sender, RoutedEventArgs e) => Vm.BookmarksAllSessions = true;

    private void UpdateBookmarkListState()
    {
        var empty = Vm.VisibleBookmarks.Count == 0;
        BookmarkEmpty.IsVisible = empty;
        var emptyText = Vm.Bookmarks.Count == 0
            ? "还没有书签 — 选中终端文字后，用「添加书签」保存重要输出。"
            : Vm.BookmarksAllSessions ? "没有匹配的书签。" : "当前会话没有匹配的书签，可切换到「全部」。";
        TerminalHub.App.Localization.UiText.Set(BookmarkEmpty, TextBlock.TextProperty, emptyText);
        // Rebuilds (open/search/scope/delete) reset the ListBox selection; keep a
        // first row selected like the palette so Enter always has a target.
        if (!empty && BookmarkResults.SelectedIndex < 0)
            BookmarkResults.SelectedIndex = 0;
    }

    /// <summary>Row tap jumps to the source output; buttons inside the row keep
    /// their own actions (copy/rename/delete). The tap source is usually a
    /// TextBlock inside the button, so check visual ancestors, not the source.</summary>
    private void OnBookmarkTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
        if (BookmarkResults.SelectedItem is BookmarkViewModel bm)
            Vm.LocateBookmarkCommand.Execute(bm);
    }

    private async void OnRenameBookmark(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not BookmarkViewModel bm) return;
        var name = await PromptTextAsync("重命名书签", "书签名称", bm.Name);
        if (!string.IsNullOrWhiteSpace(name)) Vm.RenameBookmark(bm, name);
    }

    private void OnViewBookmark(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is BookmarkViewModel bm)
            Vm.SelectedBookmark = bm;
    }

    private async void OnSaveBookmark(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedBookmark is not { } bm) return;
        await SaveTextFileAsync("保存书签为文本",
            $"{SafeFileName(bm.SessionName)}-书签-{SafeFileName(bm.Name)}-{bm.CreatedAt:yyyyMMdd-HHmmss}", bm.Text);
    }

    /// <summary>「添加书签」: selection text anchored at its start, or a few
    /// preview lines anchored at the viewport's first line. A cancelled name
    /// prompt removes the just-registered anchor again.</summary>
    private async Task AddBookmarkAsync()
    {
        var view = ActiveTerminal();
        var session = Vm.ActiveSession;
        if (view is null || session is null) { ShowToast("没有活动终端。"); return; }
        if (view.CaptureBookmark() is not { } captured) { ShowToast("没有可标记的内容。"); return; }
        var name = await PromptTextAsync("添加书签", "书签名称", Vm.SuggestBookmarkName(captured.Text));
        if (name is null)
        {
            var buf = session.Emulator.Buffer;
            lock (buf.SyncRoot) buf.Anchors.Remove(captured.Anchor);
            return;
        }
        var bm = Vm.AddBookmark(session, captured.Anchor, name, captured.Text);
        ShowToast($"已添加书签「{bm.Name}」。");
    }

    private void OnAddBookmark(object? sender, RoutedEventArgs e) => _ = AddBookmarkAsync();

    /// <summary>Reveal happens after activation so the target pane's view is
    /// already bound to the bookmark's session.</summary>
    private void OnRevealBookmark(BookmarkViewModel bm)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (bm.Anchor is not null && ActiveTerminal()?.TryRevealAnchor(bm.Anchor) == true)
                CloseBookmarks();
            else
                Vm.BookmarkNotice = "原输出已被裁剪或不在当前屏幕，书签文字仍可复制。";
        });
    }

    private void HandleBookmarkKey(KeyEventArgs e)
    {
        // The full snapshot is a selectable text box: keep its navigation and
        // copying keys local, and let Tab reach the row and export buttons.
        if (e.Key == Key.Tab) return;
        if (e.Key != Key.Escape && e.Source is Visual source
            && (source == BookmarkSnapshotText || source.GetVisualAncestors().Contains(BookmarkSnapshotText)
                || source.GetSelfAndVisualAncestors().OfType<Button>().Any())) return;
        if (e.Key == Key.Escape) CloseBookmarks();
        else if (e.Key == Key.Enter)
        {
            if (BookmarkResults.SelectedItem is BookmarkViewModel bm)
                Vm.LocateBookmarkCommand.Execute(bm);
        }
        else if (e.Key is Key.Down or Key.Up)
        {
            var count = BookmarkResults.ItemCount;
            if (count > 0)
            {
                var current = BookmarkResults.SelectedIndex;
                // From no selection, Down/Up land on the first/last row — the
                // modular formula would send Up to count-2.
                BookmarkResults.SelectedIndex = current < 0
                    ? (e.Key == Key.Down ? 0 : count - 1)
                    : (current + (e.Key == Key.Down ? 1 : -1) + count) % count;
                BookmarkResults.ScrollIntoView(BookmarkResults.SelectedItem!);
            }
        }
        else return;
        e.Handled = true;
    }
}
