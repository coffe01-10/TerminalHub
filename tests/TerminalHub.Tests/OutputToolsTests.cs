using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class OutputToolsTests
{
    public class SavePickerProxy : DispatchProxy
    {
        public IStorageFile? File { get; set; }
        public FilePickerSaveOptions? Options { get; private set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IStorageProvider.SaveFilePickerAsync))
                throw new NotSupportedException(method?.Name);
            Options = (FilePickerSaveOptions)args![0]!;
            return Task.FromResult(File);
        }
    }

    private sealed class FakeMonitor : ISystemMonitor
    {
        public SystemSample Current { get; } = new();
        public IReadOnlyList<ProcessInfo> Processes { get; } = [];
#pragma warning disable CS0067
        public event Action<ISystemMonitor>? Sampled;
#pragma warning restore CS0067
        public void Start(TimeSpan interval) { }
        public void Stop() { }
        public void Dispose() { }
    }

    private static async Task Until(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(8));
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
    }

    private static TerminalView ViewFor(TerminalEmulator emulator) => new() { Emulator = emulator };

    /// <summary>Emulate a drag selection without pointer plumbing.</summary>
    private static void Select(TerminalView view, int sl, int sc, int el, int ec)
    {
        var t = typeof(TerminalView);
        t.GetField("_selAnchor", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(view, ((int line, int col)?)(sl, sc));
        t.GetField("_selEnd", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(view, ((int line, int col)?)(el, ec));
    }

    /// <summary>Simulate scroll drift accumulated by the PTY thread that the
    /// refresh tick has not applied yet (the view is not attached to the tree,
    /// so feeding output does not raise ScrollbackChanged on it).</summary>
    private static void SetDrift(TerminalView view, int lines)
        => typeof(TerminalView).GetField("_scrollDrift", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(view, lines);

    [AvaloniaFact]
    public void VisibleText_AtBottom_IsTheScreen_AndAllText_KeepsScrollback()
    {
        using var emulator = new TerminalEmulator(columns: 20, rows: 5);
        for (var i = 0; i < 12; i++) emulator.Parser.Feed($"L{i:D2}行{i}\r\n");
        emulator.Parser.Feed("tail尾");
        var view = ViewFor(emulator);

        var visible = view.GetVisibleText()!;
        Assert.Contains("tail尾", visible);
        Assert.DoesNotContain("L00行", visible);          // scrolled off the screen

        var all = view.GetAllText()!;
        Assert.Contains("L00行", all);                    // history is still retained
        Assert.Contains("tail尾", all);
    }

    [AvaloniaFact]
    public void VisibleText_ScrolledUp_ShowsTheHistoryViewport()
    {
        using var emulator = new TerminalEmulator(columns: 20, rows: 5);
        for (var i = 0; i < 12; i++) emulator.Parser.Feed($"row{i:D2}\r\n");
        var view = ViewFor(emulator);

        view.RevealLine(0);   // scroll back so absolute line 0 is visible
        var visible = view.GetVisibleText()!;
        Assert.Contains("row00", visible);
        Assert.DoesNotContain("row11", visible);
    }

    [AvaloniaFact]
    public void ExtractText_JoinsSoftWrap_AndKeepsCjk()
    {
        using var emulator = new TerminalEmulator(columns: 10, rows: 5);
        emulator.Parser.Feed("ab中文cdEFGH\r\nnext");     // first line soft-wraps at col 10
        var view = ViewFor(emulator);
        var all = view.GetAllText()!;
        Assert.Contains("ab中文cdEFGH\nnext", all);      // wrapped run joined, no stray newline
    }

    [AvaloniaFact]
    public void CaptureBookmark_NoSelection_AnchorsAtViewportTop()
    {
        using var emulator = new TerminalEmulator(columns: 20, rows: 5);
        for (var i = 0; i < 12; i++) emulator.Parser.Feed($"m{i:D2}\r\n");
        var view = ViewFor(emulator);
        view.RevealLine(0);

        var captured = view.CaptureBookmark()!.Value;
        Assert.Contains("m00", captured.Text);
        int? resolved;
        lock (emulator.Buffer.SyncRoot) resolved = emulator.Buffer.ResolveAnchor(captured.Anchor);
        Assert.Equal(0, resolved);
    }

    [AvaloniaFact]
    public void VisibleText_AndViewportBookmark_KeepScreenWindow_WithPendingDrift()
    {
        using var emulator = new TerminalEmulator(columns: 20, rows: 5);
        for (var i = 0; i < 12; i++) emulator.Parser.Feed($"row{i:D2}\r\n");
        var view = ViewFor(emulator);
        view.RevealLine(0);                              // screen shows row00..row04

        // Output bursts in on the PTY thread; until the next refresh tick only
        // the drift counter moves, and the renderer keeps the same window.
        for (var i = 0; i < 3; i++) emulator.Parser.Feed($"new{i}\r\n");
        SetDrift(view, 3);

        var visible = view.GetVisibleText()!;
        Assert.StartsWith("row00", visible);             // what the screen shows
        Assert.DoesNotContain("new0", visible);

        var captured = view.CaptureBookmark()!.Value;
        Assert.Contains("row00", captured.Text);
        Assert.DoesNotContain("new0", captured.Text);
        int? resolved;
        lock (emulator.Buffer.SyncRoot) resolved = emulator.Buffer.ResolveAnchor(captured.Anchor);
        Assert.Equal(0, resolved);                       // anchored on screen, not drifted down
    }

    [AvaloniaFact]
    public void AltScreenBookmark_DiesOnLeaving_PrimarySurvives_NeverResurrects()
    {
        using var emulator = new TerminalEmulator(columns: 20, rows: 5);
        for (var i = 0; i < 6; i++) emulator.Parser.Feed($"p{i}\r\n");
        var view = ViewFor(emulator);
        var primary = view.CaptureBookmark()!.Value;

        emulator.Parser.Feed("\x1b[?1049h");             // enter a TUI's alt screen
        emulator.Parser.Feed("alt-content");
        var alt = view.CaptureBookmark()!.Value;
        Assert.True(alt.Anchor.Alternate);

        emulator.Parser.Feed("\x1b[?1049l");             // leave: the alt grid is discarded
        lock (emulator.Buffer.SyncRoot)
        {
            Assert.False(alt.Anchor.Alive);
            Assert.Null(emulator.Buffer.ResolveAnchor(alt.Anchor));
            Assert.NotNull(emulator.Buffer.ResolveAnchor(primary.Anchor));
        }

        emulator.Parser.Feed("\x1b[?1049h");             // any later TUI must not revive it
        emulator.Parser.Feed("other-tui");
        lock (emulator.Buffer.SyncRoot)
        {
            Assert.Null(emulator.Buffer.ResolveAnchor(alt.Anchor));
            // Primary bookmark on the wrong screen: unresolvable but still alive.
            Assert.True(primary.Anchor.Alive);
            Assert.Null(emulator.Buffer.ResolveAnchor(primary.Anchor));
        }
    }

    [AvaloniaFact]
    public void CaptureBookmark_WithSelection_SavesSelectionAndStartAnchor()
    {
        using var emulator = new TerminalEmulator(columns: 20, rows: 5);
        for (var i = 0; i < 6; i++) emulator.Parser.Feed($"sel{i}\r\n");
        var view = ViewFor(emulator);
        Select(view, 2, 0, 3, 19);                        // absolute lines 2-3 → "sel2\nsel3"

        var captured = view.CaptureBookmark()!.Value;
        Assert.Equal("sel2\nsel3", captured.Text);
        int? resolved;
        lock (emulator.Buffer.SyncRoot) resolved = emulator.Buffer.ResolveAnchor(captured.Anchor);
        Assert.Equal(2, resolved);
        Assert.Equal(0, captured.Anchor.Column);
    }

    [AvaloniaFact]
    public void BookmarkAnchor_SurvivesReflow_AndDiesOnTrim()
    {
        using var emulator = new TerminalEmulator(columns: 20, rows: 5);
        for (var i = 0; i < 8; i++) emulator.Parser.Feed($"keep{i}\r\n");
        var view = ViewFor(emulator);
        var captured = view.CaptureBookmark()!.Value;

        emulator.Resize(10, 5);                          // reflow maps the anchor
        lock (emulator.Buffer.SyncRoot)
            Assert.NotNull(emulator.Buffer.ResolveAnchor(captured.Anchor));

        for (var i = 0; i < 2300; i++) emulator.Parser.Feed("\r\n");
        lock (emulator.Buffer.SyncRoot)
            Assert.Null(emulator.Buffer.ResolveAnchor(captured.Anchor));
    }

    [AvaloniaFact]
    public async Task Adding201stBookmark_PreservesOldestSnapshotAndAnchor_InSettingsAndAfterRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "terminalhub-bookmarks-" + Guid.NewGuid().ToString("N"));
        var previousMock = PtySessionFactory.UseMock;
        try
        {
            PtySessionFactory.UseMock = true;
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            using (var vm = new MainWindowViewModel(new FakeMonitor(), store))
            {
                await vm.NewSessionCommand.ExecuteAsync(null);
                await Until(() => vm.ActiveSession is not null);
                var session = vm.ActiveSession!;
                session.Emulator.Parser.Feed("important output");
                BookmarkViewModel? oldest = null;
                for (var i = 0; i < 201; i++)
                {
                    BufferAnchor anchor;
                    lock (session.Emulator.Buffer.SyncRoot)
                        anchor = session.Emulator.Buffer.CreateAnchor(0, 0);
                    var bookmark = vm.AddBookmark(session, anchor, $"书签 {i}", $"输出 {i}");
                    oldest ??= bookmark;
                }
                Assert.Equal(201, vm.Bookmarks.Count);
                Assert.Same(oldest, vm.Bookmarks.Last());
                lock (session.Emulator.Buffer.SyncRoot)
                    Assert.NotNull(session.Emulator.Buffer.ResolveAnchor(oldest!.Anchor!));
                Assert.Equal("输出 0", store.Load().OutputBookmarks.Last().Text);
            }
            using var restored = new MainWindowViewModel(new FakeMonitor(), store);
            Assert.Equal(201, restored.Bookmarks.Count);
            Assert.Equal("输出 0", restored.Bookmarks.Last().Text);
        }
        finally
        {
            PtySessionFactory.UseMock = previousMock;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(1100, 680)]
    public async Task RestoredBookmark_ViewAndSave_UseCompleteSnapshot(int width, int height)
    {
        var previousMock = PtySessionFactory.UseMock;
        var path = Path.Combine(Path.GetTempPath(), "terminalhub-bookmark-export-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            using var fixture = new StageLayoutTests.StageFixture(width, height);
            await Until(() => fixture.Vm.ActiveSession is not null);
            var window = fixture.Window;
            var vm = fixture.Vm;
            var text = string.Join("\n", Enumerable.Range(1, 25).Select(i => $"第 {i} 行：中文 emoji 👩‍💻 完整输出"));
            var bookmark = BookmarkViewModel.Restored(new OutputBookmark
            {
                Id = "restored-output", Name = "重启前的构建", SessionName = vm.ActiveSession!.Name,
                CreatedAt = DateTimeOffset.Now, Text = text
            });
            vm.Bookmarks.Add(bookmark);
            typeof(TerminalHub.App.Views.MainWindow).GetMethod("OpenBookmarks", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            await Task.Delay(150);
            var view = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "查看"));
            view.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var editor = window.FindControl<TextBox>("BookmarkSnapshotText")!;
            Assert.Same(bookmark, vm.SelectedBookmark);
            Assert.Equal(text, editor.Text);
            Assert.True(editor.IsReadOnly);
            Assert.True(window.FindControl<Border>("BookmarkPanel")!.IsVisible);
            editor.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Same(bookmark, vm.SelectedBookmark);
            Assert.True(editor.IsFocused);

            // Simulate only the native picker's result; use Avalonia's real local
            // file implementation and the actual export button for UTF-8 I/O.
            var picker = DispatchProxy.Create<IStorageProvider, SavePickerProxy>();
            var proxy = (SavePickerProxy)picker;
            var storageFileType = typeof(IStorageFile).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile")!;
            proxy.File = (IStorageFile)Activator.CreateInstance(storageFileType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [new FileInfo(path)], null)!;
            typeof(TopLevel).GetField("_storageProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, picker);
            window.FindControl<Button>("SaveBookmarkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => window.FindControl<TextBlock>("ToastText")!.Text?.StartsWith("已保存：") == true);
            Assert.Equal(text, await File.ReadAllTextAsync(path));
            Assert.Contains("重启前的构建", proxy.Options!.SuggestedFileName);

            proxy.File = null; // cancelled picker: the existing export stays intact
            window.FindControl<Button>("SaveBookmarkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(text, await File.ReadAllTextAsync(path));
            var captures = Environment.GetEnvironmentVariable("TERMINALHUB_OUTPUT_CAPTURES");
            if (captures is not null)
                window.CaptureRenderedFrame()!.Save(Path.Combine(captures, $"bookmarks-{width}.png"));
        }
        finally
        {
            PtySessionFactory.UseMock = previousMock;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task Bookmarks_Add_Filter_Persist_AndOrphanOnRestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "terminalhub-bookmarks-" + Guid.NewGuid().ToString("N"));
        var previousMock = PtySessionFactory.UseMock;
        try
        {
            PtySessionFactory.UseMock = true;
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            var vm = new MainWindowViewModel(new FakeMonitor(), store);
            await vm.NewSessionCommand.ExecuteAsync(null);
            await Until(() => vm.ActiveSession is not null);
            var session = vm.ActiveSession!;
            session.Emulator.Parser.Feed("build ok\r\n部署完成\r\n");

            BufferAnchor anchor;
            lock (session.Emulator.Buffer.SyncRoot)
                anchor = session.Emulator.Buffer.CreateAnchor(0, 0);
            var bm = vm.AddBookmark(session, anchor, "构建输出", "build ok\n部署完成");

            Assert.Same(bm, vm.VisibleBookmarks[0]);      // 当前会话 scope by default
            Assert.Equal("可定位", bm.LocateText);

            vm.BookmarkFilter = "部署";                    // filter hits the saved text
            Assert.Single(vm.VisibleBookmarks);
            vm.BookmarkFilter = "不存在的内容";
            Assert.Empty(vm.VisibleBookmarks);
            vm.BookmarkFilter = "";

            vm.RenameBookmark(bm, "改名后的书签");
            Assert.Equal("改名后的书签", bm.Name);
            Assert.Equal("改名后的书签", store.Load().OutputBookmarks[0].Name);

            vm.Dispose();                                // persists; session dies
            var vm2 = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(Path.Combine(dir, "settings.json")));
            await Until(() => vm2.Bookmarks.Count == 1, TimeSpan.FromSeconds(3));
            var restored = vm2.Bookmarks[0];
            Assert.Equal("改名后的书签", restored.Name);
            Assert.Equal("build ok\n部署完成", restored.Text);   // snapshot survives restart
            Assert.Null(restored.Session);
            Assert.Null(restored.Anchor);
            vm2.RefreshBookmarkList();
            Assert.Equal("重启前保存", restored.LocateText);
            vm2.LocateBookmarkCommand.Execute(restored);          // must not locate, only notice
            Assert.Contains("重启前", vm2.BookmarkNotice);
            vm2.Dispose();
        }
        finally
        {
            PtySessionFactory.UseMock = previousMock;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public async Task Bookmark_Delete_DropsAnchor_AndSessionClose_OrphansIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "terminalhub-bookmarks-" + Guid.NewGuid().ToString("N"));
        var previousMock = PtySessionFactory.UseMock;
        try
        {
            PtySessionFactory.UseMock = true;
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            var vm = new MainWindowViewModel(new FakeMonitor(), store);
            await vm.NewSessionCommand.ExecuteAsync(null);
            await Until(() => vm.ActiveSession is not null);
            var session = vm.ActiveSession!;
            var buf = session.Emulator.Buffer;
            session.Emulator.Parser.Feed("one\r\ntwo\r\n");

            BufferAnchor a1, a2;
            lock (buf.SyncRoot) { a1 = buf.CreateAnchor(0, 0); a2 = buf.CreateAnchor(1, 0); }
            var anchorCount = buf.Anchors.Count;
            var bm1 = vm.AddBookmark(session, a1, "第一条", "one");
            var bm2 = vm.AddBookmark(session, a2, "第二条", "two");

            vm.DeleteBookmarkCommand.Execute(bm1);
            Assert.Equal(anchorCount - 1, buf.Anchors.Count);   // a1's anchor was removed
            Assert.Single(vm.Bookmarks);

            // Closing the session orphans the remaining bookmark — text stays copyable.
            vm.CloseSessionCommand.Execute(vm.SessionCards.First(c => ReferenceEquals(c.Model, session)));
            await Until(() => bm2.SessionClosed, TimeSpan.FromSeconds(3));
            Assert.True(bm2.SessionClosed);
            Assert.Null(bm2.Session);
            vm.RefreshBookmarkList();
            Assert.Equal("原会话已关闭", bm2.LocateText);
            Assert.DoesNotContain(store.Load().OutputBookmarks, b => b.Name == "第一条");
            vm.Dispose();
        }
        finally
        {
            PtySessionFactory.UseMock = previousMock;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public async Task ClosedSessionBookmarks_NotReattachedToSameNamedNewSession()
    {
        var dir = Path.Combine(Path.GetTempPath(), "terminalhub-bookmarks-" + Guid.NewGuid().ToString("N"));
        var previousMock = PtySessionFactory.UseMock;
        try
        {
            PtySessionFactory.UseMock = true;
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            var vm = new MainWindowViewModel(new FakeMonitor(), store);
            await vm.NewSessionCommand.ExecuteAsync(null);
            await Until(() => vm.ActiveSession is not null);
            var session = vm.ActiveSession!;
            var name = session.Name;                     // auto name, e.g. "Terminal 01"
            session.Emulator.Parser.Feed("important output\r\n");

            BufferAnchor anchor;
            lock (session.Emulator.Buffer.SyncRoot)
                anchor = session.Emulator.Buffer.CreateAnchor(0, 0);
            var bm = vm.AddBookmark(session, anchor, "重要输出", "important output");
            Assert.Same(bm, vm.VisibleBookmarks[0]);     // 当前会话 scope while alive

            vm.CloseSessionCommand.Execute(vm.SessionCards.First(c => ReferenceEquals(c.Model, session)));
            await Until(() => bm.SessionClosed, TimeSpan.FromSeconds(3));

            await vm.NewSessionCommand.ExecuteAsync(null);   // recycles the freed auto name
            await Until(() => vm.ActiveSession is not null && !ReferenceEquals(vm.ActiveSession, session));
            Assert.Equal(name, vm.ActiveSession!.Name);  // same name, different session

            vm.RefreshBookmarkList();
            Assert.Empty(vm.VisibleBookmarks);           // closed-run bookmarks: 「全部」only
            vm.BookmarksAllSessions = true;
            Assert.Same(bm, vm.VisibleBookmarks.SingleOrDefault());
            vm.Dispose();
        }
        finally
        {
            PtySessionFactory.UseMock = previousMock;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public async Task CommandRecord_Text_ExtractsForCopyAndSave()
    {
        var previousMock = PtySessionFactory.UseMock;
        var dir = Path.Combine(Path.GetTempPath(), "terminalhub-bookmarks-" + Guid.NewGuid().ToString("N"));
        try
        {
            PtySessionFactory.UseMock = true;
            var vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(Path.Combine(dir, "settings.json")));
            await vm.NewSessionCommand.ExecuteAsync(null);
            await Until(() => vm.ActiveSession is not null);
            var session = vm.ActiveSession!;
            session.Emulator.Parser.Feed("\x1b]133;E;echo hi\x07\x1b]133;C\x07hello out\r\n\x1b]133;D;0\x07");
            vm.BindCommandSession(session);
            await Until(() => vm.CommandRecords.Count == 1, TimeSpan.FromSeconds(3));

            var text = vm.GetCommandRecordText(vm.CommandRecords[0]);
            Assert.Contains("hello out", text);
            Assert.Contains("echo hi", text);
            vm.Dispose();
        }
        finally
        {
            PtySessionFactory.UseMock = previousMock;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [AvaloniaFact]
    public async Task CopyCommandRecord_WithoutClipboard_NoticesFailure_NotSuccess()
    {
        var previousMock = PtySessionFactory.UseMock;
        var dir = Path.Combine(Path.GetTempPath(), "terminalhub-bookmarks-" + Guid.NewGuid().ToString("N"));
        try
        {
            PtySessionFactory.UseMock = true;
            var vm = new MainWindowViewModel(new FakeMonitor(), new SettingsStore(Path.Combine(dir, "settings.json")));
            await vm.NewSessionCommand.ExecuteAsync(null);
            await Until(() => vm.ActiveSession is not null);
            var session = vm.ActiveSession!;
            session.Emulator.Parser.Feed("\x1b]133;E;echo hi\x07\x1b]133;C\x07hello out\r\n\x1b]133;D;0\x07");
            vm.BindCommandSession(session);
            await Until(() => vm.CommandRecords.Count == 1, TimeSpan.FromSeconds(3));

            // Headless: no MainWindow → the clipboard helper returns false; the
            // notice must report failure, never a fake「已复制」.
            vm.CopyCommandRecordCommand.Execute(vm.CommandRecords[0]);
            await Until(() => vm.CommandNotice.Length > 0, TimeSpan.FromSeconds(3));
            Assert.Contains("复制失败", vm.CommandNotice);
            Assert.DoesNotContain("已复制", vm.CommandNotice);
            vm.Dispose();
        }
        finally
        {
            PtySessionFactory.UseMock = previousMock;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
