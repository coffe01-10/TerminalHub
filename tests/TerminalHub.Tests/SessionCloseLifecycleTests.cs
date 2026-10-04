using System.Diagnostics;
using System.Runtime.InteropServices;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Session close lifecycle: SessionManager.Close must dispose off the
/// caller's thread (UI) while still completing, and a ConPTY exit code committed
/// by either the Exited callback or Dispose must survive the other side racing
/// in late (no -1 overwrite).</summary>
public class SessionCloseLifecycleTests
{
    [Fact]
    public async Task Close_DisposesOffTheCallerThread_AndStillRunsToCompletion()
    {
        var pty = new SlowDisposePty();
        var manager = new SessionManager();
        var removed = new ManualResetEventSlim(false);
        manager.SessionRemoved += _ => removed.Set();
        var session = await manager.CreateAsync(() => pty, new PtyOptions { Shell = "slow" });

        var watch = Stopwatch.StartNew();
        manager.Close(session);
        watch.Stop();

        Assert.True(removed.IsSet, "SessionRemoved must still fire synchronously from Close");
        Assert.True(pty.KillCalled, "Close must kill the PTY before returning");
        Assert.True(watch.ElapsedMilliseconds < 200,
            $"Close blocked the caller for {watch.ElapsedMilliseconds}ms — dispose must run off the caller's thread");
        Assert.True(pty.Disposed.Wait(TimeSpan.FromSeconds(5)), "dispose must still run to completion");
        Assert.NotEqual(Environment.CurrentManagedThreadId, pty.DisposeThreadId);
    }

    /// <summary>A PTY whose Dispose stalls — stands in for the real ConPTY
    /// WaitForExit + read-loop join that used to freeze the UI thread on close.</summary>
    private sealed class SlowDisposePty : IPtySession
    {
        private int _disposedCount;
        public bool KillCalled;
        public int DisposeThreadId;
        public ManualResetEventSlim Disposed { get; } = new(false);

        public Guid Id { get; } = Guid.NewGuid();
        public bool IsRunning => Volatile.Read(ref _disposedCount) == 0;
        public int? ExitCode => Volatile.Read(ref _disposedCount) > 0 ? 0 : null;
        public int? ProcessId => null;
        public event Action<IPtySession, ReadOnlyMemory<byte>>? OutputReceived { add { } remove { } }
        public event Action<IPtySession, int>? Exited { add { } remove { } }

        public Task StartAsync(PtyOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Write(ReadOnlySpan<byte> data) { }
        public void Resize(int columns, int rows) { }
        public void Kill() => KillCalled = true;

        public void Dispose()
        {
            if (Interlocked.Increment(ref _disposedCount) != 1) return;
            DisposeThreadId = Environment.CurrentManagedThreadId;
            Thread.Sleep(250);
            Disposed.Set();
        }
    }
}

/// <summary>Real ConPTY exit-code tests — Windows only, and mutates process-global
/// std handles like the other real-ConPTY suites, so join the ProcessWide collection.</summary>
[Collection("ProcessWide")]
public class ConPtyCloseExitCodeTests
{
    [Fact]
    public async Task RealConPty_NaturalExit_ExitCodeSurvivesDispose()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var pty = new ConPtySession();
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        pty.Exited += (_, code) => exited.TrySetResult(code);
        await StartCmdExit42Async(pty);

        Assert.Equal(42, await exited.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        pty.Dispose();
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(42, pty.ExitCode);
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task RealConPty_ImmediateDispose_KeepsRealExitCodeNotMinusOne()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var pty = new ConPtySession();
        await StartCmdExit42Async(pty);

        // Wait only for the child to die, NOT for the Exited event, then dispose
        // right away: Dispose's WaitForExit and the queued Process.Exited callback
        // race to commit — whichever wins, a late -1 guess must never overwrite.
        var died = Environment.TickCount64 + 10000;
        while (pty.IsRunning && Environment.TickCount64 < died) await Task.Delay(20);
        Assert.False(pty.IsRunning, "cmd did not exit in time");

        pty.Dispose();
        var deadline = Environment.TickCount64 + 1000;
        while (Environment.TickCount64 < deadline)
        {
            Assert.Equal(42, pty.ExitCode);
            await Task.Delay(50);
        }
    }

    private static async Task StartCmdExit42Async(ConPtySession pty)
    {
        var ids = new[] { -10, -11, -12 };
        var handles = ids.Select(GetStdHandle).ToArray();
        try
        {
            foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
            await pty.StartAsync(new PtyOptions
            {
                Shell = "cmd.exe",
                Arguments = "/c exit 42",
                Columns = 80,
                Rows = 24,
            });
        }
        finally
        {
            for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]);
        }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern void SetStdHandle(int id, IntPtr handle);
}
