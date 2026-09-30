using System.Text;
using System.Runtime.InteropServices;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Mutates process-global std handles — must not overlap any other test.</summary>
[CollectionDefinition("ProcessWide", DisableParallelization = true)]
public sealed class ProcessWideCollection { }

[Collection("ProcessWide")]
public class WindowsStreamingTests
{
    [Fact]
    public async Task RealConPty_StreamsBeforeExit_AndPassesColorEnvironment()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var pty = new ConPtySession();
        using var terminal = new TerminalEmulator(pty);
        var first = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var last = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.Changed += () =>
        {
            var frame = terminal.Buffer.CaptureFrame();
            var text = new string(frame.Cells.Select(c => c.Char).ToArray());
            if (text.Contains("stream-first")) first.TrySetResult(Environment.TickCount64);
            if (text.Contains("stream-last:truecolor:custom")) last.TrySetResult(Environment.TickCount64);
        };
        var script = "[Console]::Write('stream-first'); Start-Sleep -Milliseconds 600; " +
                     "[Console]::Write('stream-last:' + $env:COLORTERM + ':' + $env:TERMINALHUB_TEST); Start-Sleep -Seconds 1";
        var options = new PtyOptions
        {
            Shell = "powershell.exe", Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
            Environment = new Dictionary<string, string> { ["TERMINALHUB_TEST"] = "custom" }
        };
        // Match the GUI's clean standard handles. VSTest redirects them to pipes,
        // which causes Windows PowerShell to skip its ConPTY console attachment.
        var ids = new[] { -10, -11, -12 };
        var handles = ids.Select(GetStdHandle).ToArray();
        try
        {
            foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
            await terminal.StartAsync(options);
        }
        finally
        {
            for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]);
        }
        var start = await first.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(pty.IsRunning);
        var end = await last.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(end - start >= 350, "First output must be delivered before the later token, not buffered until exit.");
    }

    [Fact]
    public async Task RealConPty_SubscriberFailure_DoesNotStopLaterOutput()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var pty = new ConPtySession();
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
            var text = new string(terminal.Buffer.CaptureFrame().Cells.Select(c => c.Char).ToArray());
            if (text.Contains("reader-survived")) recovered.TrySetResult();
        };
        var script = "[Console]::Write('break-reader'); Start-Sleep -Milliseconds 600; " +
                     "[Console]::Write('reader-survived'); Start-Sleep -Seconds 1";
        var options = new PtyOptions
        {
            Shell = "powershell.exe", Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand " +
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
        };
        var ids = new[] { -10, -11, -12 };
        var handles = ids.Select(GetStdHandle).ToArray();
        try
        {
            foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
            await terminal.StartAsync(options);
        }
        finally
        {
            for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]);
        }
        await injected.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}
