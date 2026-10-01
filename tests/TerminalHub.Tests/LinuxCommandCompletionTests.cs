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
