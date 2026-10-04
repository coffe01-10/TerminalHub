using System.Reflection;
using System.Text;
using System.Runtime.InteropServices;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;
using Xunit.Sdk;

namespace TerminalHub.Tests;

/// <summary>Inline data carrying the shell name as the theory argument; skips its
/// test case when that shell is not on PATH (e.g. PowerShell 7 not installed)
/// instead of failing at process start. Gated machines still exercise shells
/// they do have.</summary>
public sealed class InlineDataIfShellInstalledAttribute : DataAttribute
{
    private readonly object[] _data;

    public InlineDataIfShellInstalledAttribute(string shell)
    {
        _data = new object[] { shell };
        if (!OnPath(shell))
            Skip = $"{shell} is not installed on this machine; skipping this shell case.";
    }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        yield return _data;
    }

    private static bool OnPath(string program)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (File.Exists(Path.Combine(dir, program)) ||
                File.Exists(Path.Combine(dir, program + ".exe"))) return true;
        }
        return false;
    }
}

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

    [Fact]
    public async Task RealPowerShell_ReadLine_DeletesChineseAtEditingCursor()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var pty = new ConPtySession();
        using var terminal = new TerminalEmulator(pty);
        var options = new PtyOptions
        {
            Shell = "powershell.exe",
            Arguments = "-NoLogo -NoProfile -NoExit -Command \"Import-Module PSReadLine; function prompt { 'TH-EDIT> ' }\""
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
        async Task WaitForText(string expected)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                lock (terminal.Buffer.SyncRoot)
                    if (terminal.Buffer.TailText(10).Contains(expected)) return;
                await Task.Delay(30);
            }
            lock (terminal.Buffer.SyncRoot)
                Assert.Fail($"Expected {expected} in real PowerShell output: {terminal.Buffer.TailText(10)}");
        }
        await WaitForText("TH-EDIT>");
        pty.Write(Encoding.UTF8.GetBytes("ab中文cd"));
        await WaitForText("ab中文cd");
        // No Enter: only edit the command line, without executing a command.
        pty.Write("\x1b[D\x1b[D\x7f"u8);
        await WaitForText("ab中cd");
        await Task.Delay(100);
        lock (terminal.Buffer.SyncRoot)
            Assert.Equal("TH-EDIT> ".Length + 4, terminal.Buffer.CursorX);
    }

    [Theory]
    [InlineDataIfShellInstalled("pwsh")]
    [InlineDataIfShellInstalled("powershell.exe")]
    public async Task RealPowerShell_TaskCompletionAndRecordingReplayUseActualOutput(string shell)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var pty = new ConPtySession(); using var terminal = new TerminalEmulator(pty);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<ShellCommandState>(TaskCreationOptions.RunContinuationsAsynchronously);
        terminal.CwdChanged += _ => ready.TrySetResult(); terminal.CommandCompleted += command => completed.TrySetResult(command);
        var handles = new[] { -10, -11, -12 }.Select(GetStdHandle).ToArray();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".threc");
        try
        {
            try
            {
                foreach (var id in new[] { -10, -11, -12 }) SetStdHandle(id, IntPtr.Zero);
                await terminal.StartAsync(new PtyOptions { Shell = shell, Arguments = "-NoLogo -NoProfile " + ShellIntegration.PowerShellArguments, WorkingDirectory = Environment.CurrentDirectory });
            }
            finally { for (var i = 0; i < handles.Length; i++) SetStdHandle(-10 - i, handles[i]); }
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15)); await Task.Delay(400);
            await using (var recorder = new TerminalRecorder(terminal, path, "actual PowerShell"))
            {
                terminal.PasteText(ShellIntegration.PrepareTaskCommand(shell, "Write-Output 'project-task-output 中文'; cmd /c exit 7")); terminal.SendText("\r");
                ShellCommandState command;
                try { command = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (TimeoutException) { Assert.Fail("Shell completion missing: " + terminal.Buffer.TailText(15)); throw; }
                Assert.Equal(7, command.ExitCode);
                await Task.Delay(100);
            }
            using var playback = await TerminalPlayback.LoadAsync(path); playback.Seek(playback.DurationMs);
            Assert.NotEmpty(playback.Emulator.Buffer.SearchLines("project-task-output 中文"));
            Assert.Equal(7, Assert.Single(playback.Emulator.Commands.Records).ExitCode);
        }
        finally { File.Delete(path); }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}
