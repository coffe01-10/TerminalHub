using TerminalHub.Core.Monitoring;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Settings;
using Xunit;

namespace TerminalHub.Tests;

public class CoreSmokeTests
{
    [Fact]
    public void AppSettings_ResolveShell_ReturnsNonEmpty()
    {
        var s = new AppSettings();
        Assert.False(string.IsNullOrWhiteSpace(s.ResolveShellCommand()));
    }

    [Fact]
    public void ResolveShellCommand_HonorsOverride_AndCustomPath()
    {
        // Per-session kind overrides the configured default.
        var s = new AppSettings { Shell = ShellKind.Bash };
        Assert.Equal("bash", s.ResolveShellCommand());
        if (OperatingSystem.IsWindows())
            Assert.Equal("cmd.exe", s.ResolveShellCommand(ShellKind.Cmd));

        // Custom kind + configured path → the path itself; empty path falls back.
        var custom = new AppSettings { CustomShellPath = "/opt/zsh/bin/zsh" };
        Assert.Equal("/opt/zsh/bin/zsh", custom.ResolveShellCommand(ShellKind.Custom));
        Assert.False(string.IsNullOrWhiteSpace(
            new AppSettings().ResolveShellCommand(ShellKind.Custom)));
    }

    [Fact]
    public void SettingsStore_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"th-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            var settings = new AppSettings
            {
                FontSize = 15,
                WorkspaceName = "TestWS",
                Shell = ShellKind.Bash,
            };
            store.Save(settings);

            var loaded = store.Load();
            Assert.Equal(15, loaded.FontSize);
            Assert.Equal("TestWS", loaded.WorkspaceName);
            Assert.Equal(ShellKind.Bash, loaded.Shell);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SparklineBuffer_KeepsLatestCapacity()
    {
        var buf = new SparklineBuffer(capacity: 5);
        for (var i = 1; i <= 8; i++) buf.Add(i);
        Assert.Equal(5, buf.Count);
        Assert.Equal(8, buf.Latest);
        Assert.Equal(new double[] { 4, 5, 6, 7, 8 }, buf.ToArray());
    }

    [Fact]
    public async Task MockPty_EchoesAndExits()
    {
        var pty = new MockPtySession();
        var output = new List<byte>();
        var exited = new TaskCompletionSource<int>();
        pty.OutputReceived += (_, data) => output.AddRange(data.ToArray());
        pty.Exited += (_, code) => exited.TrySetResult(code);

        await pty.StartAsync(new PtyOptions { Shell = "mock" });
        pty.Write("echo hi\r"u8.ToArray());
        pty.Write("exit\r"u8.ToArray());

        Assert.Equal(0, await exited.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        var text = System.Text.Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("mock: echo hi", text);
        Assert.False(pty.IsRunning);
    }

    [Fact]
    public async Task SystemMonitor_Tick_ProducesSample()
    {
        using var monitor = new SystemMonitor();
        var tcs = new TaskCompletionSource();
        monitor.Sampled += _ => tcs.TrySetResult();
        monitor.Start(TimeSpan.FromMilliseconds(50));
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.InRange(monitor.Current.CpuPercent, 0, 100);
    }
}
