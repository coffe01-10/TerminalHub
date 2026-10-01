using System.Text;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>
/// Real forkpty() sessions — the Linux counterpart of <see cref="WindowsStreamingTests"/>.
/// No standard-handle juggling is needed: forkpty gives the child its own terminal
/// regardless of how the test runner wired our std handles.
/// </summary>
public class LinuxStreamingTests
{
    private static string Text(TerminalEmulator terminal)
        => new(terminal.Buffer.CaptureFrame().Cells.Select(c => c.Char).ToArray());

    [Fact]
    public async Task RealPty_StreamsBeforeExit_AndPassesColorEnvironment()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var pty = new LinuxPtySession();
        using var terminal = new TerminalEmulator(pty);
        var first = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var last = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.Changed += () =>
        {
            var text = Text(terminal);
            if (text.Contains("stream-first")) first.TrySetResult(Environment.TickCount64);
            if (text.Contains("stream-last:truecolor:custom")) last.TrySetResult(Environment.TickCount64);
        };
        // Newlines matter: stdout on a tty is line-buffered, so a bare printf
        // with no newline could sit in libc's buffer until exit.
        await terminal.StartAsync(new PtyOptions
        {
            Shell = "/bin/bash",
            Arguments = "-c 'echo stream-first; sleep 0.6; echo stream-last:$COLORTERM:$TERMINALHUB_TEST; sleep 1'",
            Environment = new Dictionary<string, string> { ["TERMINALHUB_TEST"] = "custom" }
        });
        var start = await first.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(pty.IsRunning);
        var end = await last.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(end - start >= 350, "First output must be delivered before the later token, not buffered until exit.");
    }

    [Fact]
    public async Task RealPty_SubscriberFailure_DoesNotStopLaterOutput()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var pty = new LinuxPtySession();
        var observed = new StringBuilder();
        var injected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pty.OutputReceived += (_, data) =>
        {
            if (injected.Task.IsCompleted) return;
            observed.Append(Encoding.UTF8.GetString(data.Span));
            if (!observed.ToString().Contains("break-reader")) return;
            injected.TrySetResult();
            throw new IOException("Simulated failing output subscriber");
        };
        using var terminal = new TerminalEmulator(pty);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.Changed += () =>
        {
            if (Text(terminal).Contains("reader-survived")) recovered.TrySetResult();
        };
        await terminal.StartAsync(new PtyOptions
        {
            Shell = "/bin/bash",
            Arguments = "-c 'echo break-reader; sleep 0.6; echo reader-survived; sleep 1'"
        });
        await injected.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    /// <summary>waitpid must surface the real exit code — the Windows session
    /// reports Process.ExitCode; -1 is reserved for signal death.</summary>
    [Fact]
    public async Task RealPty_ReportsChildExitCode()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var pty = new LinuxPtySession();
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        pty.Exited += (_, code) => exited.TrySetResult(code);

        await pty.StartAsync(new PtyOptions
        {
            Shell = "/bin/bash", Arguments = "-c 'exit 7'", Columns = 80, Rows = 24
        });

        Assert.Equal(7, await exited.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(7, pty.ExitCode);
        Assert.False(pty.IsRunning);
    }

    /// <summary>TIOCSWINSZ must reach the child — `stty size` reads the kernel's
    /// winsize directly, so this fails if Resize never issues the ioctl.</summary>
    [Fact]
    public async Task RealPty_Resize_UpdatesChildWinsize()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var pty = new LinuxPtySession();
        var output = new List<byte>();
        pty.OutputReceived += (_, d) => { lock (output) output.AddRange(d.ToArray()); };

        await pty.StartAsync(new PtyOptions { Shell = "/bin/bash", Columns = 80, Rows = 24 });
        pty.Write("stty size\n"u8.ToArray());
        await WaitForOutput(output, "24 80");

        pty.Resize(100, 40);
        pty.Write("stty size\n"u8.ToArray());
        await WaitForOutput(output, "40 100");
    }

    private static async Task WaitForOutput(List<byte> output, string token)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            string text;
            lock (output) text = Encoding.UTF8.GetString(output.ToArray());
            if (text.Contains(token)) return;
            Assert.True(DateTime.UtcNow < deadline, $"timed out waiting for {token}: {text}");
            await Task.Delay(60);
        }
    }
}
