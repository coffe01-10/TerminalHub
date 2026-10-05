using System.Diagnostics;
using System.Runtime.InteropServices;
using TerminalHub.App.ViewModels;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Ssh;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Gate: a working passwordless `ssh localhost` (key or agent auth,
/// host key already known or accept-new). Skips like the real-CLI tests when
/// no local sshd is configured — the probe doubles as the host-key accept step
/// so the product-path sessions below run prompt-free.</summary>
public sealed class LocalSshdFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> _available = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    public LocalSshdFactAttribute()
    {
        if (!InlineDataIfShellInstalledAttribute.OnPath("ssh"))
            Skip = "ssh is not installed on this machine; skipping real SSH tests.";
        else if (!_available.Value)
            Skip = "No passwordless ssh localhost (no key/agent auth); skipping real SSH tests.";
    }

    private static bool Probe()
    {
        try
        {
            var start = new ProcessStartInfo("ssh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=accept-new", "-o", "ConnectTimeout=5", "localhost", "exit" })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null) return false;
            return process.WaitForExit(15000) && process.ExitCode == 0;
        }
        catch { return false; }
    }
}

/// <summary>Real-sshd end-to-end: SFTP protocol and RemoteFilesViewModel against
/// localhost (remote FS is the real local FS), plus the ssh ConPTY terminal path.</summary>
public class LocalSshRealTests
{
    private static readonly SshHost Localhost = new() { Host = "localhost", Port = 22 };

    /// <summary>OpenSSH-for-Windows reports absolute remote paths as `/C:/dir/…`;
    /// strip the leading slash so the same file can be checked in the local FS.
    /// (On a Unix remote the path stays POSIX and verification against the local
    /// FS is meaningless — this suite only runs against localhost sshd.)</summary>
    private static string ToLocalPath(string remotePath)
        => remotePath.Length > 2 && remotePath[0] == '/' && char.IsLetter(remotePath[1]) && remotePath[2] == ':'
            ? remotePath[1..] : remotePath;

    private static async Task WaitFor(Func<bool> condition, string scenario, Func<string>? diagnostic = null)
    {
        var deadline = Environment.TickCount64 + 20000;
        while (!condition() && Environment.TickCount64 < deadline) await Task.Delay(50);
        Assert.True(condition(), scenario + (diagnostic is null ? "" : ": " + diagnostic()));
    }

    [LocalSshdFact]
    public async Task SftpClient_RealServer_RoundtripsUploadDownloadAndList()
    {
        using var client = await SftpClient.ConnectAsync(Localhost, CancellationToken.None);
        var home = await client.RealPathAsync(".", CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(home));

        var fileName = $"terminalhub-sftp-{Guid.NewGuid():N}.txt";
        var remotePath = SftpClient.Join(home, fileName);
        var local = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        var roundtrip = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        var payload = System.Text.Encoding.UTF8.GetBytes("真实 SFTP 往返 " + Guid.NewGuid());
        try
        {
            await File.WriteAllBytesAsync(local, payload);
            var uploaded = 0L;
            await client.UploadAsync(local, remotePath, new InlineProgress(n => uploaded = n), CancellationToken.None);
            Assert.Equal(payload.Length, uploaded);

            var files = await client.ListAsync(home, CancellationToken.None);
            var remote = Assert.Single(files, f => !f.IsDirectory && f.Name == fileName);
            Assert.Equal(payload.Length, remote.Size);
            Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(ToLocalPath(home), fileName)));

            var downloaded = 0L;
            await client.DownloadAsync(remote, roundtrip, new InlineProgress(n => downloaded = n), CancellationToken.None);
            Assert.Equal(payload.Length, downloaded);
            Assert.Equal(payload, await File.ReadAllBytesAsync(roundtrip));
        }
        finally
        {
            File.Delete(local); File.Delete(roundtrip);
            try { File.Delete(Path.Combine(ToLocalPath(home), fileName)); } catch { }
        }
    }

    [LocalSshdFact]
    public async Task RemoteFilesViewModel_RealServer_BrowseUploadDownload()
    {
        var opened = new List<string>();
        using var vm = new RemoteFilesViewModel((host, dir) => { opened.Add($"{host.Target}:{dir}"); return Task.CompletedTask; });
        vm.Host = Localhost;
        await vm.Refresh();
        Assert.True(vm.Entries.Count > 0, $"expected entries in home dir; status={vm.Status}");

        // The remote name comes from the LOCAL file's basename — upload a file
        // whose name we can find again in the listing.
        var fileName = $"terminalhub-vm-{Guid.NewGuid():N}.txt";
        var workDir = Path.Combine(Path.GetTempPath(), "terminalhub-sftp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var local = Path.Combine(workDir, fileName);
        var roundtrip = Path.Combine(workDir, "downloaded-" + fileName);
        try
        {
            await File.WriteAllTextAsync(local, "viewmodel 真实上传");
            await vm.UploadAsync(local);
            Assert.Contains("上传完成", vm.Status);
            Assert.True(File.Exists(Path.Combine(ToLocalPath(vm.Path), fileName)),
                $"uploaded file should exist; path={vm.Path}");
            var remote = vm.Entries.FirstOrDefault(e => e.Name == fileName);
            Assert.True(remote is not null,
                $"uploaded file should appear in listing; path={vm.Path}; entries={string.Join(", ", vm.Entries.Select(e => e.Name))}");

            vm.Selected = remote;
            await vm.DownloadAsync(roundtrip);
            Assert.Contains("下载完成", vm.Status);
            Assert.Equal("viewmodel 真实上传", await File.ReadAllTextAsync(roundtrip));
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch { }
            try { File.Delete(Path.Combine(ToLocalPath(vm.Path), fileName)); } catch { }
        }
    }

    [LocalSshdFact]
    public async Task SftpClient_RealServer_MkdirStatRenameRemove()
    {
        using var client = await SftpClient.ConnectAsync(Localhost, CancellationToken.None);
        var home = await client.RealPathAsync(".", CancellationToken.None);
        var dir = SftpClient.Join(home, $"terminalhub-mkdir-{Guid.NewGuid():N}");
        var renamed = dir + "-renamed";
        try
        {
            await client.MkdirAsync(dir, CancellationToken.None);
            var stat = await client.StatAsync(dir, CancellationToken.None);
            Assert.True(stat.IsDirectory, "mkdir'd path should stat as a directory");
            Assert.True(Directory.Exists(ToLocalPath(dir)));

            await client.RenameAsync(dir, renamed, CancellationToken.None);
            Assert.False(Directory.Exists(ToLocalPath(dir)));
            Assert.True(Directory.Exists(ToLocalPath(renamed)));

            var fileName = "inside.txt";
            var local = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
            await File.WriteAllTextAsync(local, "payload");
            await client.UploadAsync(local, SftpClient.Join(renamed, fileName), null, CancellationToken.None);
            var fileStat = await client.StatAsync(SftpClient.Join(renamed, fileName), CancellationToken.None);
            Assert.False(fileStat.IsDirectory);
            Assert.Equal(7, fileStat.Size);
            File.Delete(local);

            await client.RemoveAsync(new RemoteFile(fileName, SftpClient.Join(renamed, fileName), false, 7), CancellationToken.None);
            Assert.False(File.Exists(Path.Combine(ToLocalPath(renamed), fileName)));
            await client.RemoveAsync(new RemoteFile("x", renamed, true, 0), CancellationToken.None);
            Assert.False(Directory.Exists(ToLocalPath(renamed)));

            var gone = await Assert.ThrowsAnyAsync<IOException>(() => client.StatAsync(renamed, CancellationToken.None));
            Assert.False(string.IsNullOrWhiteSpace(gone.Message));
        }
        finally
        {
            try { Directory.Delete(ToLocalPath(renamed), true); } catch { }
            try { Directory.Delete(ToLocalPath(dir), true); } catch { }
        }
    }

    [LocalSshdFact]
    public async Task RemoteFilesViewModel_RealServer_NewFolderRenameDelete()
    {
        using var vm = new RemoteFilesViewModel((host, dir) => Task.CompletedTask);
        vm.Host = Localhost;
        await vm.Refresh();
        var home = vm.Path;
        var folderName = $"terminalhub-vmfld-{Guid.NewGuid():N}";
        try
        {
            vm.NameDraft = folderName;
            await vm.NewFolderCommand.ExecuteAsync(null);
            Assert.Contains("已创建", vm.Status);
            var folder = vm.Entries.FirstOrDefault(e => e.IsDirectory && e.Name == folderName);
            Assert.True(folder is not null,
                $"new folder should be listed; status={vm.Status}; entries={string.Join(", ", vm.Entries.Select(e => e.Name))}");

            vm.Selected = folder;
            vm.NameDraft = folderName + "-2";
            await vm.RenameCommand.ExecuteAsync(null);
            Assert.Contains("已重命名", vm.Status);
            var renamed = vm.Entries.FirstOrDefault(e => e.IsDirectory && e.Name == folderName + "-2");
            Assert.True(renamed is not null, $"renamed folder should be listed; status={vm.Status}");

            // Delete is armed by a first click and committed by a second.
            vm.Selected = renamed;
            await vm.DeleteCommand.ExecuteAsync(null);
            Assert.Contains("确认删除", vm.Status);
            Assert.True(Directory.Exists(Path.Combine(ToLocalPath(home), folderName + "-2")),
                "first delete click must not delete");
            await vm.DeleteCommand.ExecuteAsync(null);
            Assert.Contains("已删除", vm.Status);
            Assert.False(Directory.Exists(Path.Combine(ToLocalPath(home), folderName + "-2")));
        }
        finally
        {
            try { Directory.Delete(Path.Combine(ToLocalPath(home), folderName), true); } catch { }
            try { Directory.Delete(Path.Combine(ToLocalPath(home), folderName + "-2"), true); } catch { }
        }
    }

    private sealed class InlineProgress(Action<long> report) : IProgress<long> { public void Report(long value) => report(value); }
}

/// <summary>Real ssh.exe under ConPTY — same spawn path as the SSH panel's
/// Connect (`ssh -p port target`) and RemoteFiles' open-terminal (`ssh -t …`).</summary>
[Collection("ProcessWide")]
public class LocalSshTerminalTests
{
    private static async Task WaitFor(Func<bool> condition, string scenario, Func<string>? diagnostic = null)
    {
        var deadline = Environment.TickCount64 + 20000;
        while (!condition() && Environment.TickCount64 < deadline) await Task.Delay(50);
        Assert.True(condition(), scenario + (diagnostic is null ? "" : ": " + diagnostic()));
    }

    [LocalSshdFact]
    public async Task SshSession_RealConPty_RemoteShellEchoAndExit()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var pty = new ConPtySession();
        using var terminal = new TerminalHub.Core.Terminal.TerminalEmulator(pty, 80, 24);
        var ids = new[] { -10, -11, -12 };
        var handles = ids.Select(GetStdHandle).ToArray();
        try
        {
            // Same handle juggling as the pwsh ConPTY tests: testhost's redirected
            // std handles must not leak into the pseudoconsole spawn.
            foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
            await terminal.StartAsync(new PtyOptions
            {
                // The SSH panel's spawn path: plain `ssh -p 22 target`, no BatchMode —
                // key auth + known host key (accepted by the gate probe) mean no prompt.
                Shell = "ssh", Arguments = "-p 22 localhost",
                WorkingDirectory = Path.GetPathRoot(Environment.CurrentDirectory)!,
            });
        }
        finally { for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]); }
        var marker = "SSH_E2E_" + Guid.NewGuid().ToString("N")[..8];
        await WaitFor(() => pty.IsRunning, "ssh session running");
        // Remote default shell (cmd.exe on stock Windows OpenSSH) echoes our marker.
        terminal.SendText($"echo {marker}\r");
        await WaitFor(() => terminal.Buffer.TailText(24).Contains(marker), "remote shell echo",
            () => $"running={pty.IsRunning}, exit={pty.ExitCode}, output={terminal.Buffer.TailText(24)}");
        terminal.SendText("exit\r");
        await WaitFor(() => !pty.IsRunning, "remote exit closes session",
            () => $"output={terminal.Buffer.TailText(24)}");
    }

    [LocalSshdFact]
    public async Task SshSession_RemoteCommand_ThenExit()
    {
        if (!OperatingSystem.IsWindows()) return;
        // RemoteFiles → 在终端打开 runs `ssh -t <args> <quoted remote command>`.
        var host = new SshHost { Host = "localhost", Port = 22 };
        var marker = "REMOTE_CMD_" + Guid.NewGuid().ToString("N")[..8];
        using var pty = new ConPtySession();
        using var terminal = new TerminalHub.Core.Terminal.TerminalEmulator(pty, 80, 24);
        // An interactive remote shell over -t (like production's `exec $SHELL -l`):
        // the remote command must keep the channel open — a one-shot remote
        // command's output is erased by the remote console teardown screen-clear.
        var ids = new[] { -10, -11, -12 };
        var handles = ids.Select(GetStdHandle).ToArray();
        try
        {
            foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
            await terminal.StartAsync(new PtyOptions
            {
                Shell = "ssh",
                Arguments = $"-t {host.SshArguments} {SshHost.QuoteArgument("cmd")}",
                WorkingDirectory = Path.GetPathRoot(Environment.CurrentDirectory)!,
            });
        }
        finally { for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]); }
        await WaitFor(() => pty.IsRunning, "ssh -t session running");
        terminal.SendText($"echo {marker}\r");
        await WaitFor(() => terminal.Buffer.TailText(24).Contains(marker), "remote -t shell echo",
            () => $"running={pty.IsRunning}, exit={pty.ExitCode}, output={terminal.Buffer.TailText(24)}");
        terminal.SendText("exit\r");
        await WaitFor(() => !pty.IsRunning, "ssh exits after remote command",
            () => $"output={terminal.Buffer.TailText(24)}");
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}
