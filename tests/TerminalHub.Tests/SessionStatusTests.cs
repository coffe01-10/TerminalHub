using Avalonia.Headless.XUnit;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class SessionStatusTests
{
    [AvaloniaFact]
    public async Task BackgroundOutput_IsUnread_VisiblePaneAndResizeAreNot()
    {
        using var terminal = new TerminalEmulator(columns: 20, rows: 4);
        await terminal.StartAsync(new PtyOptions { Shell = "mock" });
        var card = new SessionCardViewModel(new TerminalSessionModel { Name = "test", Emulator = terminal });
        terminal.Resize(10, 4);
        card.Refresh();
        Assert.False(card.HasUnreadOutput);
        terminal.SendText("background");
        card.Refresh();
        Assert.True(card.HasUnreadOutput);
        Assert.Equal("有新输出", card.StatusText);
        card.SetDisplayed(true);
        Assert.False(card.HasUnreadOutput);
        terminal.SendText("visible");
        card.Refresh();
        Assert.False(card.HasUnreadOutput);
        card.SetDisplayed(false);
        card.Refresh();
        Assert.False(card.HasUnreadOutput);
        terminal.SendText("later");
        card.Refresh();
        Assert.True(card.HasUnreadOutput);
    }

    [AvaloniaFact]
    public async Task ProcessExit_ShowsActualCode_ZeroExitIsSuccessful()
    {
        ThemeManager.Apply("White");
        using var terminal = new TerminalEmulator();
        await terminal.StartAsync(new PtyOptions { Shell = "mock" });
        var card = new SessionCardViewModel(new TerminalSessionModel { Name = "test", Emulator = terminal });
        terminal.SendText("exit\r");
        card.Refresh();
        Assert.Equal("已退出 · 0", card.StatusText);
        Assert.Same(ThemeManager.Brush("Good"), card.StatusBrush);
        using var failed = new TerminalEmulator();
        await failed.StartAsync(new PtyOptions { Shell = "mock" });
        var other = new SessionCardViewModel(new TerminalSessionModel { Name = "failed", Emulator = failed });
        failed.Pty.Kill();
        other.Refresh();
        Assert.Equal("已退出 · -1", other.StatusText);
        Assert.Same(ThemeManager.Brush("Bad"), other.StatusBrush);
    }
}
