using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Win32;
using TerminalHub.App;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class ProductV04Tests
{
    [Fact]
    public void CommandJournal_MarkersKeepTheirScreenWithinOneChunk()
    {
        using var emulator = new TerminalEmulator(columns: 40, rows: 6);
        emulator.Parser.Feed("\u001b]133;E;echo primary\u0007\u001b]133;C\u0007PRIMARY\r\n\u001b]133;D;0\u0007\u001b[?1049hOTHER-SCREEN");
        var primary = Assert.Single(emulator.Commands.Records);
        Assert.False(primary.Start.Alternate);
        Assert.False(primary.End!.Alternate);
        Assert.Null(primary.LocateLine(emulator.Buffer));
        Assert.Null(primary.CopyText(emulator.Buffer));
        emulator.Parser.Feed("\r\n\u001b]133;E;echo alternate\u0007\u001b]133;C\u0007ALTERNATE\r\n\u001b]133;D;0\u0007\u001b[?1049l");
        var alternate = emulator.Commands.Records[^1];
        Assert.True(alternate.Start.Alternate);
        Assert.True(alternate.End!.Alternate);
        Assert.Null(alternate.CopyText(emulator.Buffer));
        Assert.Equal("echo primary\nPRIMARY", primary.CopyText(emulator.Buffer));
    }

    [Fact]
    public void CommandJournal_DirectoryIsCapturedBeforeLaterBytesChangeIt()
    {
        using var emulator = new TerminalEmulator(columns: 40, rows: 6);
        emulator.Parser.Feed("\u001b]9;9;C:\\before\u0007");
        emulator.Parser.Feed("\u001b]133;E;cd C:\\after\u0007\u001b]133;C\u0007\r\n\u001b]133;D;0\u0007\u001b]9;9;C:\\after\u0007\u001b]133;E;pwd\u0007\u001b]133;C\u0007\r\nC:\\after\r\n\u001b]133;D;0\u0007");
        Assert.Equal(2, emulator.Commands.Records.Count);
        Assert.Equal(@"C:\before", emulator.Commands.Records[0].WorkingDirectory);
        Assert.Equal(@"C:\after", emulator.Commands.Records[1].WorkingDirectory);
    }

    [Fact]
    public async Task Activation_AbandonedHandshakeDoesNotDisableLaterRequests()
    {
        var name = "TerminalHub.Review." + Guid.NewGuid().ToString("N");
        using var activation = new SingleInstanceActivation(name);
        var calls = 0;
        activation.Attach(() => Interlocked.Increment(ref calls));
        using (var client = new System.IO.Pipes.NamedPipeClientStream(".", name,
            System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous))
        {
            await client.ConnectAsync(2000);
            await client.ReadExactlyAsync(new byte[4]);
        }
        Assert.True(await SingleInstanceActivation.RequestAsync(name));
        await Until(() => calls == 1);
        Assert.True(await SingleInstanceActivation.RequestAsync(name));
        await Until(() => calls == 2);
    }

    private static async Task Until(Func<bool> condition)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5))
                throw new TimeoutException("条件未在 5 秒内成立。");
            await Task.Delay(20);
        }
    }

    [Fact]
    public void CommandJournal_RecordsMarkedCommands_AndRefusesTrimmedOrUnmarkedText()
    {
        using var emulator = new TerminalEmulator(columns: 10, rows: 6);
        var buffer = emulator.Buffer;
        emulator.Parser.Feed("plain error: failed\r\n");
        Assert.Empty(emulator.Commands.Records);

        emulator.Parser.Feed("\x1b]9;9;C:\\项目\\work\x07");
        emulator.Parser.Feed("\x1b]133;E;echo a;b\x07\x1b]133;C\x07\r\n0123456789WRAP\r\n\x1b]133;D;3;ignored\x07");
        var marked = emulator.Commands.Records[^1];
        Assert.Equal("echo a;b", marked.Command);
        Assert.True(marked.HasCommandText);
        Assert.Equal(@"C:\项目\work", marked.WorkingDirectory);
        Assert.Equal(3, marked.ExitCode);
        Assert.False(marked.Running);
        Assert.NotNull(marked.Duration);
        string? copy;
        lock (buffer.SyncRoot) copy = marked.CopyText(buffer);
        Assert.Equal("echo a;b\n0123456789WRAP", copy);
        int? line;
        lock (buffer.SyncRoot) line = marked.LocateLine(buffer);
        Assert.NotNull(line);

        emulator.Parser.Feed("\x1b]133;B\x07git status\x1b]133;C\x07\r\nok\r\n\x1b]133;D;1\x07");
        var fromScreen = emulator.Commands.Records[^1];
        Assert.Equal("git status", fromScreen.Command);
        lock (buffer.SyncRoot) Assert.Equal("git status\nok", fromScreen.CopyText(buffer));

        emulator.Parser.Feed("\x1b]133;C\x07visible output\r\n\x1b]133;D;0\x07");
        var unmarked = emulator.Commands.Records[^1];
        Assert.False(unmarked.HasCommandText);
        lock (buffer.SyncRoot)
        {
            var text = unmarked.CopyText(buffer);
            Assert.NotNull(text);
            Assert.Contains("visible output", text);
            Assert.DoesNotContain("git status", text);
        }

        emulator.Resize(18, 8);
        lock (buffer.SyncRoot)
        {
            Assert.NotNull(marked.LocateLine(buffer));
            Assert.Contains("echo a;b", marked.CopyText(buffer));
            Assert.Contains("0123456789WRAP", marked.CopyText(buffer));
        }

        for (var i = 0; i < 2300; i++) emulator.Parser.Feed("\r\n");
        lock (buffer.SyncRoot)
        {
            Assert.Null(marked.LocateLine(buffer));
            Assert.Null(marked.CopyText(buffer));
            Assert.Null(fromScreen.CopyText(buffer));
        }
        emulator.Parser.Feed("\u001b]133;E;second\u0007\u001b]133;C\u0007BETA\r\n\u001b]133;D;0\u0007");
        var second = emulator.Commands.Records[^1];
        lock (buffer.SyncRoot)
        {
            Assert.Null(marked.CopyText(buffer));
            var secondText = second.CopyText(buffer);
            Assert.Contains("BETA", secondText);
            Assert.DoesNotContain("echo a;b", secondText);
        }
    }

    [Fact]
    public void CommandJournal_DoesNotCopyTheOtherScreen()
    {
        using var emulator = new TerminalEmulator(columns: 20, rows: 6);
        emulator.Parser.Feed("\u001b]133;E;echo hi\u0007\u001b]133;C\u0007hello\r\n\u001b]133;D;0\u0007");
        var primary = emulator.Commands.Records[^1];
        emulator.Parser.Feed("\u001b[?1049hTUI-TEXT\u001b]133;E;alt\u0007\u001b]133;C\u0007ALTDATA\u001b]133;D;0\u0007");
        var alternate = emulator.Commands.Records[^1];
        lock (emulator.Buffer.SyncRoot)
        {
            Assert.Null(primary.CopyText(emulator.Buffer));
            Assert.Null(primary.LocateLine(emulator.Buffer));
            Assert.Contains("ALTDATA", alternate.CopyText(emulator.Buffer));
            Assert.DoesNotContain("hello", alternate.CopyText(emulator.Buffer));
        }
        emulator.Parser.Feed("\u001b[?1049l");
        lock (emulator.Buffer.SyncRoot)
        {
            var text = primary.CopyText(emulator.Buffer);
            Assert.Contains("hello", text);
            Assert.DoesNotContain("TUI-TEXT", text);
            Assert.DoesNotContain("ALTDATA", text);
            Assert.Null(alternate.CopyText(emulator.Buffer));
        }
    }

    [Fact]
    public void PowerShellIntegration_MarksCommandTextWithoutEmbeddingItInThePrompt()
    {
        Assert.InRange(ShellIntegration.PowerShellArguments.Length, 1, 7000);
        Assert.Contains("133;E;", ShellIntegration.PowerShellScript);
        Assert.Contains("133;C", ShellIntegration.PowerShellScript);
        Assert.Contains("133;D;", ShellIntegration.PowerShellScript);
        Assert.DoesNotContain("133;B", ShellIntegration.PowerShellScript);
    }

    [Fact]
    public void LaunchRequest_AcceptsSpacedAndChinesePaths_AndNamesTheProblem()
    {
        Assert.False(LaunchRequest.TryGetDirectory(["--mock"], out _, out var none));
        Assert.Null(none);
        Assert.False(LaunchRequest.TryGetDirectory(["--cwd"], out _, out var missing));
        Assert.Equal("请在 --cwd 后面写目录。", missing);
        Assert.False(LaunchRequest.TryGetDirectory(["--cwd="], out _, out var empty));
        Assert.Equal("请在 --cwd 后面写目录。", empty);
        Assert.True(LaunchRequest.TryGetDirectory(["--cwd", @"C:\项目 files\终端"], out var path, out var error));
        Assert.Null(error);
        Assert.Equal(@"C:\项目 files\终端", path);
        Assert.True(LaunchRequest.TryGetDirectory([@"--cwd=D:\ai tool\中文"], out path, out error));
        Assert.Equal(@"D:\ai tool\中文", path);
        Assert.Contains("目录不存在", LaunchRequest.CheckDirectory(@"D:\不存在的目录\终端 hub"));
        Assert.Equal("没有提供目录。", LaunchRequest.CheckDirectory("  "));
    }

    [Fact]
    public async Task Activation_CarriesDirectoryAndNoticeBeforeTheWindowAttaches()
    {
        var name = "TerminalHub.Test." + Guid.NewGuid().ToString("N");
        using var activation = new SingleInstanceActivation(name);
        var path = @"D:\项目 files\终端";
        Assert.True(await SingleInstanceActivation.RequestDirectoryAsync(name, path));
        await Task.Delay(40);
        var calls = 0;
        string? received = null;
        string? problem = null;
        activation.Attach(() => calls++, (directory, error) => { received = directory; problem = error; });
        Assert.Equal(1, calls);
        Assert.Equal(path, received);
        Assert.Null(problem);

        Assert.True(await SingleInstanceActivation.RequestNoticeAsync(name, "目录不存在：x"));
        await Task.Delay(40);
        Assert.Equal(2, calls);
        Assert.Equal("目录不存在：x", problem);
    }

    [Fact]
    public void ExplorerMenu_InstallsQuotedCommands_AndRemovesOnlyThatVerb()
    {
        if (!OperatingSystem.IsWindows()) return;
        var verb = "TerminalHubTest" + Guid.NewGuid().ToString("N");
        var kept = ExplorerContextMenu.IsInstalled();
        var exe = @"C:\Program Files\Terminal Hub\TerminalHub.exe";
        try
        {
            ExplorerContextMenu.Install(exe, verb);
            Assert.True(ExplorerContextMenu.IsInstalled(verb));
            using var directory = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Directory\shell\{verb}\command");
            using var background = Registry.CurrentUser.OpenSubKey($@"Software\Classes\Directory\Background\shell\{verb}\command");
            Assert.Equal(ExplorerContextMenu.DirectoryCommand(exe), directory?.GetValue(null));
            Assert.Equal(ExplorerContextMenu.BackgroundCommand(exe), background?.GetValue(null));
            Assert.Contains("%1", ExplorerContextMenu.DirectoryCommand(exe));
            Assert.Contains("%V", ExplorerContextMenu.BackgroundCommand(exe));
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Directory\Background\shell\{verb}", false);
            Assert.False(ExplorerContextMenu.IsInstalled(verb));
        }
        finally
        {
            ExplorerContextMenu.Remove(verb);
            Assert.False(ExplorerContextMenu.IsInstalled(verb));
            Assert.Equal(kept, ExplorerContextMenu.IsInstalled());
        }
    }

    [Fact]
    public void TemplateTransfer_DescribesMissingPaths_AndRejectsAnEmptyImport()
    {
        var template = new WorkspaceTemplate
        {
            Name = "前端项目",
            Layout = new WorkspaceState
            {
                Sessions =
                [
                    new WorkspaceSession
                    {
                        Name = "界面", WorkingDirectory = @"D:\不存在的目录\前端",
                        Shell = @"D:\missing\shell.exe", StartupCommand = "npm run dev", RunStartupCommand = true,
                        Pinned = true, GroupName = "前端"
                    }
                ]
            }
        };
        var preview = WorkspaceTemplateTransfer.Describe(template, _ => false, _ => false);
        Assert.Contains("界面", preview);
        Assert.Contains("目录不存在，可修改", preview);
        Assert.Contains("未找到 Shell，可修改", preview);
        Assert.Contains("npm run dev", preview);
        Assert.Contains("置顶", preview);
        Assert.Contains("前端", preview);
        var copy = WorkspaceTemplateTransfer.Clone(template);
        Assert.Equal("前端项目 副本", copy.Name);
        Assert.NotEqual(template.Id, copy.Id);
        Assert.False(WorkspaceTemplateTransfer.TryParse("{}", out _, out var error));
        Assert.Equal("模板里没有终端。", error);
        Assert.False(WorkspaceTemplateTransfer.TryParse("""{"Name":"空","Layout":{"Sessions":[]}}""", out _, out error));
        Assert.Equal("模板里没有终端。", error);
    }

    [AvaloniaFact]
    public async Task GroupsAndPins_KeepShortcutOrder_WhenAGroupCollapses()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Until(() => fixture.Vm.SessionCards.Count == 5);
        var vm = fixture.Vm;
        var first = vm.SessionCards[0];
        var third = vm.SessionCards[2];
        var pinned = vm.SessionCards[4];
        var height = fixture.Window.ThumbnailHeight;
        vm.CreateGroup("后端", third);
        Assert.Equal("后端", vm.SessionGroups.Single().Name);
        Assert.Contains("后端", vm.DescribeSession(third));
        Assert.Equal(0, vm.SessionCards.IndexOf(third));
        Assert.Same(first, vm.SessionCards[1]);
        var header = vm.ShelfItems.OfType<SessionGroupHeader>().Single(item => item.Group?.Name == "后端");
        Assert.Contains(vm.ShelfItems, item => ReferenceEquals(item, third));
        vm.ToggleGroup(header);
        Assert.True(header.Group!.Collapsed);
        Assert.DoesNotContain(vm.ShelfItems, item => ReferenceEquals(item, third));
        Assert.Equal(0, vm.SessionCards.IndexOf(third));
        Assert.Same(first, vm.SessionCards[1]);
        fixture.Window.UpdateLayout();
        Assert.Equal(4, fixture.Window.GetVisualDescendants().OfType<StageCard>().Count());
        Assert.Equal(height, fixture.Window.ThumbnailHeight);
        third.Model.Emulator.SendText("build\r");
        typeof(MainWindowViewModel).GetMethod("RefreshCounts",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(vm, null);
        Assert.Equal("有新输出", header.Activity);

        fixture.Window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Alt);
        Assert.Same(first, vm.ActiveCard);
        Assert.True(header.Group.Collapsed);
        fixture.Window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);
        Assert.Same(third, vm.ActiveCard);
        Assert.False(header.Group.Collapsed);
        Assert.Contains(vm.ShelfItems, item => ReferenceEquals(item, third));
        vm.ToggleActiveGroupCommand.Execute(null);
        Assert.True(header.Group.Collapsed);
        vm.ToggleActiveGroupCommand.Execute(null);
        Assert.False(header.Group.Collapsed);

        vm.RenameGroup(header.Group, "服务");
        Assert.Contains("服务", header.Title);
        fixture.Window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        var grouped = fixture.Window.FindControl<ListBox>("PaletteResults")!.Items.Cast<PaletteEntry>().ToArray();
        Assert.Contains(grouped, entry => entry.Detail.Contains("服务"));
        fixture.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        vm.MoveCardToGroup(third, null);
        Assert.Equal("", third.Model.GroupId);
        vm.SetPinned(pinned, true);
        Assert.Same(pinned, vm.SessionCards[0]);
        Assert.Contains(vm.ShelfItems, item => item is SessionGroupHeader pin && pin.IsPinSection);
        fixture.Window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);
        Assert.Same(pinned, vm.ActiveCard);
        Assert.StartsWith("置顶", vm.DescribeSession(pinned));

        vm.PersistSettings();
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-v04-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(vm.Settings);
            var loaded = store.Load();
            Assert.Contains(loaded.SessionGroups, group => group.Name == "服务");
            Assert.Contains(loaded.Workspace.Sessions, session => session.Pinned);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    }

    [AvaloniaFact]
    public async Task Favorites_InsertWithoutEnter_AndStayOutOfTextBoxes()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Until(() => fixture.Vm.SessionCards.Count == 5);
        var vm = fixture.Vm;
        vm.AddFavoriteCommand.Execute(null);
        var favorite = vm.FavoriteCommands[^1];
        favorite.Name = "启动开发服务";
        favorite.Command = "echo 第一行\necho 第二行";
        favorite.Shell = "cmd.exe";
        favorite.Shortcut = "A";
        Assert.NotEmpty(favorite.Error);
        favorite.Shortcut = "Alt+1";
        Assert.NotEmpty(favorite.Error);
        Assert.Empty(vm.SessionShortcuts.First(item => item.Gesture == "Alt+1").Error);
        favorite.Shortcut = "Ctrl+C";
        Assert.Contains("冲突", favorite.Error);
        favorite.Shortcut = "Ctrl+Alt+F";
        Assert.Empty(favorite.Error);

        var pty = (MockPtySession)vm.ActiveSession!.Pty;
        pty.RawInput.Clear();
        Assert.False(vm.HandleSessionShortcut(new KeyEventArgs
        {
            Key = Key.F, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt, Source = new TextBox()
        }));
        Assert.Equal("", pty.RawInput.ToString());
        favorite.Command = "echo only";
        Assert.True(vm.HandleSessionShortcut(new KeyEventArgs
        {
            Key = Key.F, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt
        }));
        Assert.Equal("echo only", pty.RawInput.ToString());
        pty.RawInput.Clear();
        favorite.Command = "echo 第一行\necho 第二行";
        Assert.True(vm.HandleSessionShortcut(new KeyEventArgs
        {
            Key = Key.F, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt
        }));
        Assert.Equal("", pty.RawInput.ToString());
        Assert.Contains("没有填入", vm.FavoriteNotice);
        vm.ActiveSession.Emulator.Buffer.BracketedPaste = true;
        Assert.True(vm.HandleSessionShortcut(new KeyEventArgs
        {
            Key = Key.F, KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt
        }));
        Assert.Equal("\u001b[200~echo 第一行\necho 第二行\u001b[201~", pty.RawInput.ToString());
        Assert.Contains("当前 Shell", vm.FavoriteNotice);

        fixture.Window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        var search = fixture.Window.FindControl<TextBox>("PaletteSearch")!;
        search.Text = "启动开发";
        await Task.Delay(40);
        var results = fixture.Window.FindControl<ListBox>("PaletteResults")!;
        Assert.Contains(results.Items.Cast<PaletteEntry>(), entry => entry.Label.Contains("启动开发服务"));
        pty.RawInput.Clear();
        fixture.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Task.Delay(40);
        Assert.Equal("\u001b[200~echo 第一行\necho 第二行\u001b[201~", pty.RawInput.ToString());

        vm.PersistSettings();
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-fav-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(vm.Settings);
            var loaded = store.Load().FavoriteCommands.Single();
            Assert.Equal("echo 第一行\necho 第二行", loaded.Command);
            Assert.Equal("cmd.exe", loaded.Shell);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task Templates_RecentOrderDuplicateAndImport_DoNotExecuteCommands()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Until(() => fixture.Vm.SessionCards.Count == 5);
        var vm = fixture.Vm;
        var tabs = fixture.Window.FindControl<TabControl>("SettingsTabs")!;
        Assert.Equal("关于", ((TabItem)tabs.Items[4]!).Header);
        Assert.Equal("命令", ((TabItem)tabs.Items[5]!).Header);

        vm.TemplateName = "前端项目";
        vm.SaveCurrentAsTemplate();
        vm.TemplateSessions[0].StartupCommand = "echo template-command";
        vm.TemplateSessions[0].RunStartupCommand = true;
        vm.TemplateSessions[0].WorkingDirectory = @"D:\不存在的目录\前端";
        vm.TemplateSessions[0].Shell = @"D:\missing\no-such-shell.exe";
        vm.RefreshTemplatePreviewCommand.Execute(null);
        Assert.Contains("echo template-command", vm.TemplatePreview);
        Assert.Contains("目录不存在，可修改", vm.TemplatePreview);
        Assert.Contains("未找到 Shell，可修改", vm.TemplatePreview);
        vm.SaveTemplateChanges();

        vm.TemplateName = "后端项目";
        vm.SaveCurrentAsTemplate();
        var front = vm.WorkspaceTemplates.Single(item => item.Name == "前端项目");
        await vm.OpenTemplateAsync(front);
        await Until(() => vm.SessionCards.Count == 10);
        Assert.Equal("前端项目", vm.WorkspaceTemplates[0].Name);
        Assert.Contains("echo template-command\r", ((MockPtySession)vm.SessionCards[5].Model.Pty).RawInput.ToString());

        var running = vm.SessionCards.Count;
        vm.DuplicateTemplate();
        Assert.Contains("副本", vm.SelectedTemplate!.Name);
        Assert.Equal(running, vm.SessionCards.Count);
        var json = vm.ExportSelectedTemplateJson();
        vm.ImportTemplateJson(json);
        Assert.Contains("没有执行启动命令", vm.TemplateMessage);
        Assert.Equal(running, vm.SessionCards.Count);
        vm.ImportTemplateJson("{");
        Assert.Equal("无法读取这个模板文件。", vm.TemplateMessage);
    }

    [AvaloniaFact]
    public async Task LaunchDirectory_AddsOneSession_AndReportsAMissingFolder()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Until(() => fixture.Vm.SessionCards.Count == 5);
        var vm = fixture.Vm;
        var original = vm.SessionCards.Select(card => card.Model).ToArray();
        var missing = Path.Combine(Path.GetTempPath(), "terminalhub-missing-" + Guid.NewGuid().ToString("N"));
        await vm.OpenLaunchDirectoryAsync(missing);
        Assert.Equal(5, vm.SessionCards.Count);
        Assert.Contains("目录不存在", vm.LaunchNotice);
        Assert.All(original, session => Assert.True(session.IsRunning));

        var directory = Path.Combine(Path.GetTempPath(), "th 项目 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await vm.OpenLaunchDirectoryAsync(directory);
            await Until(() => vm.SessionCards.Count == 6);
            var created = vm.SessionCards[^1];
            Assert.Equal(new DirectoryInfo(directory).Name, created.Name);
            Assert.Equal(Path.GetFullPath(directory), created.Model.WorkingDirectory);
            Assert.All(original, session => Assert.True(session.IsRunning));
            Assert.Equal("", vm.LaunchNotice);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task CommandPanel_LocatesMarkedOutput_AndStopsWhenHistoryIsGone()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Until(() => fixture.Vm.SessionCards.Count == 5);
        var vm = fixture.Vm;
        var session = vm.ActiveSession!;
        session.Emulator.Parser.Feed("\x1b]133;E;echo hi\x07\x1b]133;C\x07\r\nhello from command\r\n\x1b]133;D;0\x07");
        vm.ShowCommandHistoryCommand.Execute(null);
        Assert.Equal(4, vm.SelectedRightTab);
        Assert.True(vm.InspectorVisible);
        var record = vm.CommandRecords[^1];
        Assert.Equal("echo hi", record.Title);
        Assert.Contains("完成", record.Detail);
        vm.LocateCommandCommand.Execute(record);
        Assert.Equal("", vm.CommandNotice);
        Assert.True(record.CanLocate);
        vm.CopyCommandRecordCommand.Execute(record);
        // Headless has no clipboard — the copy path must run to a notice, and
        // that notice reports failure rather than a fake success.
        await Until(() => vm.CommandNotice.Length > 0);
        Assert.Contains("复制失败", vm.CommandNotice);

        for (var i = 0; i < 40; i++) session.Emulator.Parser.Feed("\r\n");
        lock (session.Emulator.Buffer.SyncRoot) session.Emulator.Buffer.ClearScrollback();
        vm.ShowCommandHistoryCommand.Execute(null);
        record = vm.CommandRecords.Single(item => item.Title == "echo hi");
        vm.LocateCommandCommand.Execute(record);
        Assert.Contains("无法定位", vm.CommandNotice);
        vm.CopyCommandRecordCommand.Execute(record);
        Assert.Contains("没有复制", vm.CommandNotice);
        session.Emulator.Parser.Feed("\x1b]133;E;other\x07\x1b]133;C\x07\r\nOTHER\r\n\x1b]133;D;0\x07");
        vm.ShowCommandHistoryCommand.Execute(null);
        vm.CopyCommandRecordCommand.Execute(vm.CommandRecords.Single(item => item.Title == "echo hi"));
        Assert.Contains("没有复制", vm.CommandNotice);
        vm.CopyCommandRecordCommand.Execute(vm.CommandRecords.Single(item => item.Title == "other"));
        // Reachable record but headless has no clipboard: the failure notice,
        // distinct from「没有复制」which marks unusable records.
        Assert.Contains("复制失败", vm.CommandNotice);
    }
}
