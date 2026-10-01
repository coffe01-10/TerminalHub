using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Non-interactive bash keeps script children in its process group.
    /// Kill must signal that group; killing only the shell pid leaves them running.
    /// </summary>
    [Fact]
    public async Task Kill_SignalsProcessGroup_SoScriptChildDies()
    {
        if (!OperatingSystem.IsLinux()) return;

        var dir = Path.Combine(Path.GetTempPath(), "th-pgid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "hold.sh");
        File.WriteAllText(script, "#!/bin/bash\nset +m\nsleep 120 &\necho CHILD:$!\nwait\n");
        var child = 0;
        using var pty = new LinuxPtySession();
        try
        {
            var output = new List<byte>();
            var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            pty.OutputReceived += (_, d) => { lock (output) output.AddRange(d.ToArray()); };
            pty.Exited += (_, code) => exited.TrySetResult(code);

            await pty.StartAsync(new PtyOptions
            {
                Shell = "/bin/bash",
                Arguments = script,
                WorkingDirectory = dir,
                Columns = 80,
                Rows = 24,
            });

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            while (true)
            {
                string text;
                lock (output) text = Encoding.UTF8.GetString(output.ToArray());
                var match = Regex.Match(text, @"CHILD:(\d+)");
                if (match.Success)
                {
                    child = int.Parse(match.Groups[1].Value);
                    break;
                }
                Assert.True(DateTime.UtcNow < deadline, "timed out waiting for child pid: " + text);
                await Task.Delay(50);
            }

            var leader = pty.ProcessId ?? 0;
            Assert.True(leader > 1);
            Assert.Equal(leader, ProcessGroupOf(child));

            pty.Kill();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(8));

            var gone = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (DateTime.UtcNow < gone && ProcessAlive(child))
                await Task.Delay(40);
            Assert.False(ProcessAlive(child), $"child {child} still running after process-group kill");
        }
        finally
        {
            if (child > 0) Native.kill(child, 9);
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task Arguments_PreserveEmptyValuesBackslashesAndSpacedShellPath()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Path.Combine(Path.GetTempPath(), "th-args-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var shell = Path.Combine(dir, "shell with 中文 spaces");
        File.CreateSymbolicLink(shell, "/bin/bash");
        using var pty = new LinuxPtySession();
        var output = new StringBuilder();
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        pty.OutputReceived += (_, data) => { lock (output) output.Append(Encoding.UTF8.GetString(data.Span)); };
        pty.Exited += (_, code) => exited.TrySetResult(code);
        try
        {
            await pty.StartAsync(new PtyOptions
            {
                Shell = shell,
                Arguments = "-c 'printf \"COUNT:%s|\" \"$#\"; printf \"<%s>\" \"$@\"' marker \"\" \"C:\\tools\" '中文 space'"
            });
            Assert.Equal(0, await exited.Task.WaitAsync(TimeSpan.FromSeconds(8)));
            var deadline = Environment.TickCount64 + 3000;
            while (Environment.TickCount64 < deadline)
            {
                lock (output) if (output.ToString().Contains("COUNT:3|<><C:\\tools><中文 space>")) return;
                await Task.Delay(20);
            }
            lock (output) Assert.Contains("COUNT:3|<><C:\\tools><中文 space>", output.ToString());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Kill_StopsBackgroundJobsInSeparateProcessGroups()
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = Path.Combine(Path.GetTempPath(), "th-jobs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "jobs.sh");
        File.WriteAllText(script, "set -m\n(trap '' HUP; exec sleep 120) &\necho JOB:$!\nwait\n");
        var child = 0;
        using var pty = new LinuxPtySession();
        var output = new StringBuilder();
        pty.OutputReceived += (_, data) => { lock (output) output.Append(Encoding.UTF8.GetString(data.Span)); };
        try
        {
            await pty.StartAsync(new PtyOptions { Shell = "/bin/bash", Arguments = script });
            var deadline = Environment.TickCount64 + 8000;
            while (child == 0 && Environment.TickCount64 < deadline)
            {
                lock (output)
                {
                    var match = Regex.Match(output.ToString(), @"JOB:(\d+)");
                    if (match.Success) child = int.Parse(match.Groups[1].Value);
                }
                if (child == 0) await Task.Delay(20);
            }
            Assert.True(child > 0, "Background job did not start.");
            Assert.NotEqual(pty.ProcessId, ProcessGroupOf(child));
            pty.Kill();
            deadline = Environment.TickCount64 + 3000;
            while (ProcessAlive(child) && Environment.TickCount64 < deadline) await Task.Delay(20);
            Assert.False(ProcessAlive(child), "Closing a terminal must stop its background job too.");
        }
        finally
        {
            if (child > 0) Native.kill(child, 9);
            Directory.Delete(dir, true);
        }
    }

    private static bool ProcessAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var rp = stat.LastIndexOf(')');
            if (rp < 0 || rp + 2 >= stat.Length) return true;
            var state = stat[rp + 2];
            return state is not ('Z' or 'X');
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static int ProcessGroupOf(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var rp = stat.LastIndexOf(')');
        var rest = stat[(rp + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return int.Parse(rest[2]);
    }

    private static class Native
    {
        [DllImport("libc")]
        public static extern int kill(int pid, int sig);
    }
}
