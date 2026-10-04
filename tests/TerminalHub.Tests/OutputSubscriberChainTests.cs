using System.Runtime.InteropServices;
using System.Text;
using TerminalHub.Core.Logging;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

// Regression guard: an OutputReceived subscriber that throws skips every
// LATER subscriber for that chunk (a styled-property read on the parser thread
// did exactly this — "Call from invalid thread" → black screen + out=0 in the
// desktop measurement). This test keeps a second subscriber alive behind the
// emulator + a line decoder.
[Collection("ProcessWide")]
public class OutputSubscriberChainTests
{
    [Fact]
    public async Task SecondSubscriber_CountsBytes_WhileEmulatorAndDecoderFeed()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var pty = new ConPtySession();
        using var terminal = new TerminalEmulator(pty);
        // App-level handler slot: a line decoder like MainWindowViewModel's.
        var dec = new Utf8LineDecoder();
        var lines = 0;
        dec.LineReceived += _ => lines++;
        pty.OutputReceived += (_, data) => dec.Feed(data.Span);
        long bytes = 0;
        pty.OutputReceived += (_, data) => Interlocked.Add(ref bytes, data.Length);

        var script = "$i=0; while ($true) { [Console]::WriteLine(([char]27)+'[32mworker 0 '+$i+([char]27)+'[0m build output'); $i++; Start-Sleep -Milliseconds 50 }";
        var options = new PtyOptions
        {
            Shell = "powershell.exe",
            Arguments = "-NoLogo -NoProfile -NonInteractive -EncodedCommand "
                + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
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
        await Task.Delay(3000);
        Assert.True(pty.IsRunning, "pty exited early: " + pty.ExitCode);
        Assert.True(bytes > 0, $"no bytes reached the second subscriber (lines={lines})");
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}
