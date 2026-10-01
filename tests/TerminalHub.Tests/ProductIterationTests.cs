using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TerminalHub.App;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class ProductIterationTests
{
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.True(condition(), "UI session updates did not finish.");
    }
    [AvaloniaFact]
    public async Task Palette_SearchesBeyondNineSessions_AndConsumesKeysWithoutShellInput()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(500);
        for (var i = 0; i < 6; i++)
        {
            await fixture.Vm.NewSessionCommand.ExecuteAsync(null);
            await Task.Delay(40);
        }
        await Task.Delay(50);
        var target = fixture.Vm.SessionCards[10];
        fixture.Vm.RenameSession((target, "后端服务"));
        var terminal = fixture.Window.FindControl<TerminalView>("MainTerminal")!;
        terminal.Focus();
        var pty = (MockPtySession)fixture.Vm.ActiveSession!.Pty;
        var input = pty.RawInput.ToString();
        fixture.Window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.True(fixture.Window.FindControl<Border>("PalettePanel")!.IsVisible);
        var search = fixture.Window.FindControl<TextBox>("PaletteSearch")!;
        search.Text = "后端";
        await Task.Delay(40);
        Assert.Single(fixture.Window.FindControl<ListBox>("PaletteResults")!.Items);
        fixture.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Task.Delay(50);
        Assert.Same(target, fixture.Vm.ActiveCard);
        Assert.Equal(input, pty.RawInput.ToString());
        fixture.Window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        fixture.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.True(terminal.IsFocused);
    }

    [AvaloniaFact]
    public async Task Templates_RoundTripLayoutAndCommands_OpenFreshSessionsWithoutClosingExisting()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(500);
        var vm = fixture.Vm;
        await Until(() => vm.SessionCards.Count == 5);
        var original = vm.SessionCards.Select(c => c.Model).ToArray();
        vm.ToggleSplitCommand.Execute(null);
        vm.FocusPane(1);
        vm.TemplateName = "前端项目";
        vm.SaveCurrentAsTemplate();
        Assert.Single(vm.WorkspaceTemplates);
        vm.TemplateName = "项目开发";
        vm.TemplateSessions[0].StartupCommand = "echo template-command";
        vm.TemplateSessions[0].RunStartupCommand = true;
        vm.TemplateSessions[1].StartupCommand = "echo must-not-run";
        vm.SaveTemplateChanges();
        var template = vm.SelectedTemplate!;
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-template-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(vm.Settings);
            var saved = store.Load().WorkspaceTemplates.Single();
            Assert.Equal("项目开发", saved.Name);
            Assert.True(saved.Layout.IsSplit);
            Assert.Equal(1, saved.Layout.FocusedPane);
            await vm.OpenTemplateAsync(saved);
            await Until(() => vm.SessionCards.Count == 10);
            Assert.Equal(10, vm.SessionCards.Count);
            Assert.All(original, session => Assert.True(session.IsRunning));
            Assert.DoesNotContain(vm.LeftPane!, original);
            Assert.Same(vm.RightPane, vm.ActiveSession);
            var opened = vm.SessionCards.Skip(5).ToArray();
            Assert.Contains("echo template-command\r", ((MockPtySession)opened[0].Model.Pty).RawInput.ToString());
            Assert.DoesNotContain("must-not-run", ((MockPtySession)opened[1].Model.Pty).RawInput.ToString());
            vm.DeleteTemplate();
            Assert.Empty(vm.WorkspaceTemplates);
            Assert.All(original, session => Assert.True(session.IsRunning));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task BackgroundCompletion_CoalescesAndUsesProtocolRatherThanErrorWords()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Task.Delay(500);
        var vm = fixture.Vm;
        vm.Settings.NotifyCommandCompletion = true;
        var background = vm.SessionCards[0].Model;
        background.Emulator.Parser.Feed("error: harmless test text\r\n");
        await Task.Delay(25);
        Assert.False(vm.NotificationVisible);
        background.Emulator.Parser.Feed("\x1b]133;C\u0007build\x1b]133;D;0\u0007");
        await Task.Delay(25);
        Assert.True(vm.NotificationVisible);
        Assert.Contains("命令完成", vm.NotificationText);
        background.Emulator.Parser.Feed("\x1b]133;C\x07\x1b]133;D;2\x07");
        await Task.Delay(25);
        Assert.Contains("命令失败（2）", vm.NotificationText);
        vm.ShowNotificationSessionCommand.Execute(null);
        await Task.Delay(25);
        Assert.Same(background, vm.ActiveSession);
        Assert.False(vm.NotificationVisible);
    }

    [Fact]
    public void HyperlinksAndFileLocations_RetainUnicodeAndDoNotResolveRemotePathsLocally()
    {
        var buffer = new ScreenBuffer(80, 8);
        var parser = new VtParser(buffer);
        parser.Feed("\x1b]8;;https://example.com/文档\x1b\\中文链接\x1b]8;;\x1b\\ plain");
        Assert.Equal("https://example.com/文档", buffer.GetLine(0)[0].Hyperlink);
        Assert.Null(buffer.GetLine(0)[8].Hyperlink);
        var link = TerminalContentLinks.Resolve("中文链接", 1, "", true, buffer.GetLine(0)[0].Hyperlink);
        Assert.True(link!.IsUrl);
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-file-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "文件 with spaces.cs");
        try
        {
            File.WriteAllText(path, "line one\nline two");
            var text = $"{path}:2:3: error TEST";
            var file = TerminalContentLinks.Resolve(text, 4, directory, false);
            Assert.Equal(path, file!.Target);
            Assert.Equal(2, file.Line);
            Assert.Equal(3, file.Column);
            Assert.Null(TerminalContentLinks.Resolve(text, 4, directory, true));
            Directory.CreateDirectory(Path.Combine(directory, "src"));
            File.WriteAllText(Path.Combine(directory, "src", "App.cs"), "source");
            var relative = TerminalContentLinks.Resolve("src/App.cs(4,2): error", 3, directory, false);
            Assert.Equal(Path.Combine(directory, "src", "App.cs"), relative!.Target);
            Assert.Equal(4, relative.Line);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void DroppedPaths_QuoteEachShell_AndNeverAppendAnExecutionKey()
    {
        var paths = new[] { @"C:\项目 files\a.txt", @"C:\it's\b.txt" };
        Assert.Equal("'C:\\项目 files\\a.txt' 'C:\\it''s\\b.txt'", ShellPathInput.Format(paths, "pwsh"));
        Assert.Equal("\"C:\\项目 files\\a.txt\" \"C:\\it's\\b.txt\"", ShellPathInput.Format(paths, "cmd.exe"));
        Assert.Equal("'/mnt/c/项目 files/a.txt' '/mnt/c/it'\"'\"'s/b.txt'", ShellPathInput.Format(paths, "wsl.exe"));
        Assert.DoesNotContain('\r', ShellPathInput.Format(paths, "pwsh"));
    }

    [AvaloniaFact]
    public async Task FileDrop_InsertsWithoutEnter_AndRefusesRemoteOrPreviewInput()
    {
        using var fixture = new StageLayoutTests.StageFixture();
        await Until(() => fixture.Vm.SessionCards.Count == 5);
        var view = fixture.Window.FindControl<TerminalView>("MainTerminal")!;
        var pty = (MockPtySession)fixture.Vm.ActiveSession!.Pty;
        pty.RawInput.Clear();
        Assert.True(view.InsertDroppedPaths([@"C:\project files\source.cs"]));
        Assert.Equal("'C:\\project files\\source.cs'", pty.RawInput.ToString());
        var input = pty.RawInput.ToString();
        view.IsRemote = true;
        Assert.False(view.InsertDroppedPaths([@"C:\local-only.cs"]));
        Assert.Equal(input, pty.RawInput.ToString());
        view.IsRemote = false;
        view.IsPreview = true;
        Assert.False(view.InsertDroppedPaths([@"C:\local-only.cs"]));
        Assert.Equal(input, pty.RawInput.ToString());
    }

    [Fact]
    public async Task Activation_QueuesBeforeWindowReady_AndHandlesRepeatedLaunches()
    {
        var name = "TerminalHub.Test." + Guid.NewGuid().ToString("N");
        using var activation = new SingleInstanceActivation(name);
        using (var abandoned = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            await abandoned.ConnectAsync(2000);
        Assert.True(await SingleInstanceActivation.RequestAsync(name));
        await Task.Delay(40);
        var calls = 0;
        activation.Attach(() => Interlocked.Increment(ref calls));
        Assert.Equal(1, calls);
        Assert.True(await SingleInstanceActivation.RequestAsync(name));
        await Task.Delay(40);
        Assert.Equal(2, calls);
    }

    [AvaloniaTheory]
    [InlineData(1100, 680, 0)]
    [InlineData(1440, 900, 0)]
    [InlineData(1100, 680, 3)]
    [InlineData(1440, 900, 3)]
    public async Task PaletteAndTemplateSettings_FitWindowWithoutChangingShelf(int width, int height, int theme)
    {
        using var fixture = new StageLayoutTests.StageFixture(width, height);
        await Until(() => fixture.Vm.SessionCards.Count == 5);
        fixture.Vm.ThemeIndex = theme;
        fixture.Window.UpdateLayout();
        var labels = fixture.Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.Text is "查找" or "输出" or "工具")
            .Where(text => text.IsEffectivelyVisible) // collapsed DockHint reports (0,0)
            .Where(text => text.TranslatePoint(default, fixture.Window)!.Value.Y < 64).ToArray();
        Assert.Equal(3, labels.Length);
        var centers = labels.Select(text => text.TranslatePoint(
            new Avalonia.Point(0, text.Bounds.Height / 2), fixture.Window)!.Value.Y).ToArray();
        Assert.True(centers.Max() - centers.Min() <= 1, "顶部查找、输出、工具文字应垂直对齐。");
        var directory = Environment.GetEnvironmentVariable("TERMINALHUB_ITERATION_CAPTURES");
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(200);
            fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"main-{width}-{theme}.png"));
        }
        fixture.Vm.TemplateName = "前端 · 后端 · 测试";
        fixture.Vm.SaveCurrentAsTemplate();
        fixture.Vm.SettingsOpen = true;
        fixture.Window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 1;
        fixture.Window.UpdateLayout();
        var panel = fixture.Window.FindControl<Border>("SettingsPanel")!;
        Assert.InRange(panel.Bounds.Height, 200, height);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(200);
            fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"templates-{width}-{theme}.png"));
            fixture.Window.FindControl<TabControl>("SettingsTabs")!.SelectedIndex = 4;
            await Task.Delay(200);
            fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"about-{width}-{theme}.png"));
        }
        fixture.Vm.SettingsOpen = false;
        fixture.Window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Shift);
        fixture.Window.UpdateLayout();
        var palette = fixture.Window.FindControl<Border>("PalettePanel")!;
        Assert.True(palette.IsVisible);
        Assert.True(palette.Bounds.Width <= width);
        Assert.True(palette.Bounds.Bottom <= height);
        if (directory is not null)
        {
            await Task.Delay(200);
            fixture.Window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"palette-{width}-{theme}.png"));
        }
        fixture.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
    }

    private sealed class FakeReleaseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.NotEmpty(request.Headers.UserAgent);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"tag_name\":\"v0.4.0\",\"html_url\":\"https://example.com/untrusted\"}") });
        }
    }
    [Fact]
    public async Task UpdateLookup_UsesReleaseTagAndOwnedDownloadPage()
    {
        using var http = new HttpClient(new FakeReleaseHandler());
        var release = await new ReleaseUpdates(http).LatestAsync();
        Assert.Equal(new Version(0, 4, 0), release.Version);
        Assert.Equal("https://github.com/coffe01-10/TerminalHub/releases/tag/v0.4.0", release.DownloadPage.AbsoluteUri);
    }

    [AvaloniaFact]
    public async Task LegacyWorkspace_UpgradesOnlyOwnedPowerShellArguments()
    {
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-upgrade-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            store.Save(new AppSettings { Workspace = new WorkspaceState
            {
                Sessions = [new WorkspaceSession { Name = "旧会话", Shell = "powershell.exe", Arguments = ShellIntegration.LegacyPowerShellArguments },
                    new WorkspaceSession { Name = "自定义", Shell = "pwsh", Arguments = "-NoProfile" }]
            } });
            TerminalHub.Pty.PtySessionFactory.UseMock = true;
            using var vm = new MainWindowViewModel(settingsStore: store);
            await vm.SpawnStartupSessionsAsync();
            await Until(() => vm.SessionCards.Count == 2);
            Assert.Equal(ShellIntegration.PowerShellArguments, vm.SessionCards[0].Model.ShellArguments);
            Assert.Equal("-NoProfile", vm.SessionCards[1].Model.ShellArguments);
            Assert.Contains(vm.SessionShortcuts, shortcut => shortcut.Binding.Action == SessionShortcutAction.CommandPalette);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
