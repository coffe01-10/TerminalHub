using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace TerminalHub.App;

/// <summary>Second launch asks the existing instance to restore its window.</summary>
public sealed class SingleInstanceActivation : IDisposable
{
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();
    private Action? _activate;
    private bool _pending;
    private readonly object _sync = new();
    public SingleInstanceActivation(string name = "TerminalHub.Activate")
    {
        _name = name;
        // Create the listener before UI initialization so early second launches queue.
        var server = CreateServer();
        _ = ListenAsync(server);
    }

    private NamedPipeServerStream CreateServer() => new(_name, PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    public void Attach(Action activate)
    {
        bool pending;
        lock (_sync) { _activate = activate; pending = _pending; _pending = false; }
        if (pending) activate();
    }

    private async Task ListenAsync(NamedPipeServerStream server)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using (server)
                {
                    await server.WaitForConnectionAsync(_stop.Token);
                    try
                    {
                        await server.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), _stop.Token);
                        var request = new byte[1];
                        if (await server.ReadAsync(request, _stop.Token) == 1)
                        {
                            Action? activate;
                            lock (_sync) { activate = _activate; if (activate is null) _pending = true; }
                            activate?.Invoke();
                        }
                    }
                    // A second process exiting mid-launch must not disable later activation.
                    catch (IOException) { }
                }
                if (!_stop.IsCancellationRequested) server = CreateServer();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        { System.Diagnostics.Trace.WriteLine($"Activation listener: {ex.Message}"); }
        finally { server.Dispose(); }
    }

    public static async Task<bool> RequestAsync(string name = "TerminalHub.Activate")
    {
        // Bound the second process lifetime when an older instance has no listener.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            var process = new byte[4];
            await pipe.ReadExactlyAsync(process, timeout.Token);
            if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(BitConverter.ToInt32(process));
            await pipe.WriteAsync(new byte[] { 1 }, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or TimeoutException)
        { return false; }
    }

    public void Dispose() { _stop.Cancel(); }
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
