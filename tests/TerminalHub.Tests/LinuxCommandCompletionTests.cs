using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>
/// The Linux counterpart of <see cref="WindowsCommandCompletionTests"/>:
/// interactive bash spawned with <see cref="ShellIntegration.BashArguments"/>
/// emits OSC 133 marks (A/B/C/D) and OSC 7 cwd reports, so the emulator gets
/// the same command-completion + cwd tracking PowerShell sessions have.
/// </summary>
public class LinuxCommandCompletionTests
{
    [Theory]
    [InlineData("bash", true)]
    [InlineData("/bin/bash", true)]
    [InlineData("/usr/local/bin/bash", true)]
    [InlineData("sh", false)]
    [InlineData("zsh", false)]
    [InlineData("pwsh", false)]
    public void IsBash_MatchesOnlyBash(string shell, bool expected)
        => Assert.Equal(expected, ShellIntegration.IsBash(shell));

    [Fact]
    public void BashArguments_PointsToExistingRcFile()
    {
        if (!OperatingSystem.IsLinux()) return;
        var args = ShellIntegration.BashArguments;
        var path = args["--rcfile \"".Length..^1];
        Assert.True(File.Exists(path), $"rc file missing: {path}");
        Assert.Contains("PROMPT_COMMAND", File.ReadAllText(path));
    }

    [Fact]
    public async Task Bash_ReportsSuccessFailureAndResetsPreviousExitCode()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var emulator = new TerminalEmulator(new LinuxPtySession());
        var cwdReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        emulator.CwdChanged += path => cwdReady.TrySetResult(path);
        var completions = System.Threading.Channels.Channel.CreateUnbounded<ShellCommandState>();
        emulator.CommandCompleted += state => completions.Writer.TryWrite(state);

        var dir = Path.Combine(Path.GetTempPath(), "th-bash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await emulator.StartAsync(new PtyOptions
            {
                Shell = "bash",
                Arguments = ShellIntegration.BashArguments,
                WorkingDirectory = dir
            });

            // The first prompt already reports the spawn directory via OSC 7.
            Assert.Equal(dir, await cwdReady.Task.WaitAsync(TimeSpan.FromSeconds(15)));
            await Task.Delay(300); // rc hookup settles after the first prompt.

            emulator.SendText("echo COMMAND_SUCCESS\r");
            Assert.Equal(0, (await Next()).ExitCode);
            emulator.SendText("sh -c 'exit 7'\r");
            Assert.Equal(7, (await Next()).ExitCode);
            emulator.SendText("echo SUCCESS_AFTER_FAILURE\r");
            Assert.Equal(0, (await Next()).ExitCode);
            emulator.SendText("false\r");
            Assert.Equal(1, (await Next()).ExitCode);

            // OSC 7 keeps tracking after cd — same data the toolbar/files panel use.
            var cdReport = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            emulator.CwdChanged += path => { if (path == "/") cdReport.TrySetResult(path); };
            emulator.SendText("cd /\r");
            Assert.Equal(0, (await Next()).ExitCode);
            Assert.Equal("/", await cdReport.Task.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }

        async Task<ShellCommandState> Next()
            => await completions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
    }

    /// <summary>Regression: \[ \] quoting in PS0 is wrong — bash expands the
    /// markers to raw SOH/STX bytes on stdout (no readline strip), which the
    /// terminal then paints as two box glyphs before every command's output.
    /// The C mark must arrive as a bare OSC with no \x01/\x02 around it.</summary>
    [Fact]
    public async Task Bash_CommandStartMark_LeavesNoControlBytesInOutput()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var emulator = new TerminalEmulator(new LinuxPtySession());
        var raw = System.Threading.Channels.Channel.CreateUnbounded<byte>();
        emulator.Pty.OutputReceived += (_, data) =>
        {
            foreach (var b in data.Span) raw.Writer.TryWrite(b);
        };

        await emulator.StartAsync(new PtyOptions
        {
            Shell = "bash", Arguments = ShellIntegration.BashArguments,
            WorkingDirectory = "/tmp"
        });
        emulator.SendText("echo RAWMARK\r");

        var window = new List<byte>(capacity: 512);
        var deadline = Environment.TickCount64 + 15_000;
        var sawC = false;
        while (Environment.TickCount64 < deadline)
        {
            window.Add(await raw.Reader.ReadAsync()
                .AsTask().WaitAsync(TimeSpan.FromSeconds(15)));
            if (window.Count > 1024) window.RemoveRange(0, 512);
            if (!sawC && EndsWith(window, "\x1b]133;C\x07"u8)) { sawC = true; continue; }
            if (sawC && EndsWith(window, "RAWMARK"u8)) break;
        }
        Assert.True(sawC, "no OSC 133;C mark within 15s");
        Assert.True(EndsWith(window, "RAWMARK"u8), "command output never arrived");

        // Everything between the C mark and the command's own output must be
        // the command output itself — never stray prompt-quoting bytes.
        var tail = window.Skip(Math.Max(0, window.Count - 32)).ToArray();
        Assert.DoesNotContain(tail, b => b is 1 or 2);
    }

    private static bool EndsWith(List<byte> window, ReadOnlySpan<byte> suffix)
    {
        if (window.Count < suffix.Length) return false;
        for (var i = 0; i < suffix.Length; i++)
            if (window[window.Count - suffix.Length + i] != suffix[i]) return false;
        return true;
    }

    /// <summary>OSC 7 regression: the rc script used to emit
    /// file://host/$PWD with the path unencoded, so a cwd containing '#'
    /// (URI fragment), '?' (query) or '%' (escape) was truncated/corrupted by
    /// TryParseOsc7. Bare-path emission keeps e.g. "C#proj" intact.</summary>
    [Fact]
    public async Task Bash_Osc7_SpecialCharsInPath_ReportedIntact()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var emulator = new TerminalEmulator(new LinuxPtySession());
        var cwdSeen = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        emulator.CwdChanged += path => cwdSeen.TrySetResult(path);

        var root = Path.Combine(Path.GetTempPath(), "th-osc7-" + Guid.NewGuid().ToString("N"));
        var specials = new[] { "C#proj", "a%20b", "q?mark", "space dir" }
            .Select(n => Path.Combine(root, n)).ToArray();
        Directory.CreateDirectory(root);
        foreach (var d in specials) Directory.CreateDirectory(d);
        try
        {
            await emulator.StartAsync(new PtyOptions
            {
                Shell = "bash",
                Arguments = ShellIntegration.BashArguments,
                WorkingDirectory = root
            });
            Assert.Equal(root, await cwdSeen.Task.WaitAsync(TimeSpan.FromSeconds(15)));
            await Task.Delay(300); // rc hookup settles after the first prompt.

            foreach (var target in specials)
            {
                var hit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnCwd(string path) { if (path == target) hit.TrySetResult(path); }
                emulator.CwdChanged += OnCwd;
                emulator.SendText($"cd {EscapeForBash(target)}\r");
                Assert.Equal(target, await hit.Task.WaitAsync(TimeSpan.FromSeconds(15)));
                emulator.CwdChanged -= OnCwd;
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }

        static string EscapeForBash(string path) => "'" + path.Replace("'", "'\\''") + "'";
    }

    /// <summary>Args persisted by older builds point bash --rcfile at our
    /// integration script; restore paths must recognize them and re-inject the
    /// current rc file (which also refreshes it if deleted). User rcfiles with
    /// other filenames are untouched.</summary>
    [Theory]
    [InlineData("--rcfile \"/home/u/.config/terminalhub/bash-integration.sh\"", true)]
    [InlineData("--rcfile /tmp/bash-integration.sh", true)]
    [InlineData("--rcfile ~/.bashrc", false)]
    [InlineData("--norc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBashRcArguments_MatchesOnlyOurRcfile(string? args, bool expected)
        => Assert.Equal(expected, ShellIntegration.IsBashRcArguments(args));

    /// <summary>Command journal parity: C/D marks produce finished records with
    /// exit codes, the same data the session card's exit hint uses.</summary>
    [Fact]
    public async Task Bash_CommandJournal_RecordsExitCodes()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var emulator = new TerminalEmulator(new LinuxPtySession());
        var completions = System.Threading.Channels.Channel.CreateUnbounded<ShellCommandState>();
        emulator.CommandCompleted += state => completions.Writer.TryWrite(state);

        await emulator.StartAsync(new PtyOptions
        {
            Shell = "bash", Arguments = ShellIntegration.BashArguments,
            WorkingDirectory = "/tmp"
        });
        await Task.Delay(300); // rc hookup settles after the first prompt.

        emulator.SendText("true\r");
        Assert.Equal(0, (await completions.Reader.ReadAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(15))).ExitCode);
        emulator.SendText("sh -c 'exit 3'\r");
        Assert.Equal(3, (await completions.Reader.ReadAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(15))).ExitCode);

        var done = emulator.Commands.Records.Where(r => r.CompletionKnown).ToList();
        Assert.Contains(done, r => r.ExitCode == 0);
        Assert.Contains(done, r => r.ExitCode == 3);
        Assert.Contains(done, r => r.Command.Contains("exit 3"));
    }
}
