using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace TerminalHub.App;

/// <summary>Second launch asks the existing instance to restore its window or open a directory.</summary>
public sealed class SingleInstanceActivation : IDisposable
{
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();
    private Action? _activate;
    private Action<string?, string?>? _openLaunch;
    private bool _pending;
    private readonly List<(string? Path, string? Error)> _pendingLaunches = [];
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

    public void Attach(Action activate, Action<string?, string?>? openLaunch = null)
    {
        bool pending;
        (string? Path, string? Error)[] launches;
        lock (_sync)
        {
            _activate = activate;
            _openLaunch = openLaunch;
            pending = _pending;
            _pending = false;
            launches = _pendingLaunches.ToArray();
            _pendingLaunches.Clear();
        }
        if (pending) activate();
        if (openLaunch is not null)
            foreach (var launch in launches) openLaunch(launch.Path, launch.Error);
    }

    private void Signal(bool directory, string? path, string? error)
    {
        Action? activate;
        Action<string?, string?>? open;
        var deliver = false;
        lock (_sync)
        {
            activate = _activate;
            open = _openLaunch;
            if (activate is null) _pending = true;
            if (directory)
            {
                if (open is null) _pendingLaunches.Add((path, error));
                else deliver = true;
            }
        }
        activate?.Invoke();
        if (deliver) open?.Invoke(path, error);
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
                        if (await server.ReadAsync(request, _stop.Token) != 1) throw new EndOfStreamException();
                        if (request[0] == 1) Signal(directory: false, null, null);
                        else if (request[0] is 2 or 3)
                        {
                            var lengthBytes = new byte[2];
                            await server.ReadExactlyAsync(lengthBytes, _stop.Token);
                            var length = BitConverter.ToUInt16(lengthBytes);
                            var payload = new byte[length];
                            if (length > 0) await server.ReadExactlyAsync(payload, _stop.Token);
                            var text = Encoding.UTF8.GetString(payload);
                            if (request[0] == 2) Signal(directory: true, text, null);
                            else Signal(directory: true, null, text);
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

    public static Task<bool> RequestAsync(string name = "TerminalHub.Activate")
        => SendAsync(name, [1]);

    public static Task<bool> RequestDirectoryAsync(string name, string path)
        => SendAsync(name, Payload(2, path));

    public static Task<bool> RequestNoticeAsync(string name, string message)
        => SendAsync(name, Payload(3, message));

    private static byte[] Payload(byte kind, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > ushort.MaxValue) bytes = bytes[..ushort.MaxValue];
        var payload = new byte[1 + 2 + bytes.Length];
        payload[0] = kind;
        BitConverter.TryWriteBytes(payload.AsSpan(1, 2), (ushort)bytes.Length);
        bytes.CopyTo(payload, 3);
        return payload;
    }

    private static async Task<bool> SendAsync(string name, byte[] payload)
    {
        // Bound the second process lifetime when an older instance has no listener.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(timeout.Token);
                var process = new byte[4];
                await pipe.ReadExactlyAsync(process, timeout.Token);
                if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(BitConverter.ToInt32(process));
                await pipe.WriteAsync(payload, timeout.Token);
                return true;
            }
            // On Linux (unix sockets) a client that lands in the backlog while the
            // server recycles its instance gets ECONNRESET instead of queueing like
            // WaitNamedPipe — retry inside the same 2s budget.
            catch (IOException) when (!timeout.IsCancellationRequested)
            {
                try { await Task.Delay(50, timeout.Token); }
                catch (OperationCanceledException) { return false; }
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            { return false; }
        }
    }

    public void Dispose() { _stop.Cancel(); }
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
}
