using System.Text;
using TerminalHub.Core.Pty;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Real PTY tests — run on Linux only (skipped elsewhere).</summary>
public class LinuxPtyTests
{
    [Fact]
    public async Task Forkpty_SpawnsShell_AndEchoes()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var pty = new LinuxPtySession();
        var output = new List<byte>();
        var exited = new TaskCompletionSource<int>();
        pty.OutputReceived += (_, d) => { lock (output) output.AddRange(d.ToArray()); };
        pty.Exited += (_, c) => exited.TrySetResult(c);

        await pty.StartAsync(new PtyOptions { Shell = "/bin/bash", Columns = 80, Rows = 24 });
        Assert.True(pty.IsRunning);

        pty.Write("echo PTY_$((6*7))\n"u8.ToArray());

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        string text;
        while (true)
        {
            lock (output) text = Encoding.UTF8.GetString(output.ToArray());
            if (text.Contains("PTY_42")) break;
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for echo output");
            await Task.Delay(60);
        }

        pty.Write("exit\n"u8.ToArray());
        var code = await exited.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Forkpty_RespectsWorkingDir()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var pty = new LinuxPtySession();
        var output = new List<byte>();
        pty.OutputReceived += (_, d) => { lock (output) output.AddRange(d.ToArray()); };

        await pty.StartAsync(new PtyOptions
        {
            Shell = "/bin/bash",
            WorkingDirectory = "/tmp",
            Columns = 80, Rows = 24,
        });
        pty.Write("pwd\n"u8.ToArray());

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (true)
        {
            string text;
            lock (output) text = Encoding.UTF8.GetString(output.ToArray());
            if (text.Contains("/tmp")) return;
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for pwd");
            await Task.Delay(60);
        }
    }
}
