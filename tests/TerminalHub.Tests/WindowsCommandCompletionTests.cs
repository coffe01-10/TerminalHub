using System.Runtime.InteropServices;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

[Collection("ProcessWide")]
public class WindowsCommandCompletionTests
{
    [LocalPwshFact]
    public async Task PowerShell_ReportsSuccessNativeFailureAndResetsPreviousExitCode()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var emulator = new TerminalEmulator(new ConPtySession());
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        emulator.CwdChanged += _ => ready.TrySetResult();
        var completions = System.Threading.Channels.Channel.CreateUnbounded<ShellCommandState>();
        emulator.CommandCompleted += state => completions.Writer.TryWrite(state);
        var ids = new[] { -10, -11, -12 };
        var handles = ids.Select(GetStdHandle).ToArray();
        try
        {
            foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
            await emulator.StartAsync(new PtyOptions
            {
                Shell = "pwsh", Arguments = "-NoLogo -NoProfile " + ShellIntegration.PowerShellArguments,
                WorkingDirectory = Environment.CurrentDirectory
            });
        }
        finally { for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]); }
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Task.Delay(300); // PSReadLine initializes after the first prompt.
        emulator.SendText("Write-Output 'COMMAND_SUCCESS'\r");
        Assert.Equal(0, (await Next()).ExitCode);
        emulator.SendText("cmd /c exit 7\r");
        Assert.Equal(7, (await Next()).ExitCode);
        emulator.SendText("Write-Output 'SUCCESS_AFTER_FAILURE'\r");
        Assert.Equal(0, (await Next()).ExitCode);
        emulator.SendText("Write-Error 'EXPECTED_TEST_ERROR'\r");
        Assert.Equal(1, (await Next()).ExitCode);
        async Task<ShellCommandState> Next() => await completions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}
