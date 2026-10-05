using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace TerminalHub.Core.Ssh;

public sealed record RemoteFile(string Name, string Path, bool IsDirectory, long Size);

/// <summary>SFTP v3 over the installed OpenSSH client; no shell interpolation for file paths.</summary>
public sealed class SftpClient : IDisposable
{
    private readonly Stream _input, _output;
    private readonly Process? _process;
    private readonly SemaphoreSlim _serial = new(1);
    private uint _id;
    private bool _posixRename;
    private bool _disposed;
    private Task<string>? _error;
    public string? IncompleteRemotePath { get; private set; }
    public SftpClient(Stream input, Stream output) { _input = input; _output = output; }
    public static async Task<SftpClient> ConnectAsync(SshHost host, CancellationToken ct)
    {
        var start = new ProcessStartInfo("ssh") { RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-p", host.Port.ToString(), "-o", "BatchMode=yes", "-s", "--", host.Target, "sftp" }) start.ArgumentList.Add(arg);
        var process = Process.Start(start) ?? throw new IOException("无法启动 OpenSSH。");
        var client = new SftpClient(process.StandardInput.BaseStream, process.StandardOutput.BaseStream, process);
        try { await client.InitializeAsync(ct); return client; }
        catch (Exception ex)
        {
            client.Dispose();
            var error = "";
            try { error = await client._error!; }
            catch (Exception) { } // a failing stderr reader must not replace the original connect error
            throw new IOException($"SFTP 连接失败。请先在 SSH 终端完成主机确认，并配置密钥或 ssh-agent。{error.Trim()}", ex);
        }
    }
    private SftpClient(Stream input, Stream output, Process process) : this(input, output)
    { _process = process; _error = process.StandardError.ReadToEndAsync(); }
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var packet = new Packet(1); packet.UInt(3); await WriteAsync(packet, ct);
        var reply = await ReadAsync(ct);
        if (reply.Byte() != 2 || reply.UInt() < 3) throw new IOException("服务器不支持 SFTP v3。");
        while (reply.Remaining > 0) { var name = reply.String(); reply.String(); if (name == "posix-rename@openssh.com") _posixRename = true; }
    }
    public async Task<string> RealPathAsync(string path, CancellationToken ct = default)
    {
        var p = new Packet(16); p.String(path); var r = await RequestAsync(p, ct);
        Expect(r, 104); r.UInt(); return r.String();
    }
    public async Task<IReadOnlyList<RemoteFile>> ListAsync(string path, CancellationToken ct = default)
    {
        var p = new Packet(11); p.String(path); var r = await RequestAsync(p, ct); Expect(r, 102); var handle = r.Bytes();
        var files = new List<RemoteFile>();
        try
        {
            while (true)
            {
                p = new Packet(12); p.Bytes(handle); r = await RequestAsync(p, ct);
                if (r.Type == 101 && r.Status == 1) break;
                Expect(r, 104); var count = r.UInt();
                for (var i = 0; i < count; i++)
                {
                    var name = r.String(); r.String(); var flags = r.UInt(); long size = 0; uint mode = 0;
                    if ((flags & 1) != 0) size = checked((long)r.ULong());
                    if ((flags & 2) != 0) { r.UInt(); r.UInt(); }
                    if ((flags & 4) != 0) mode = r.UInt();
                    if ((flags & 8) != 0) { r.UInt(); r.UInt(); }
                    if ((flags & 0x80000000) != 0) { var n = r.UInt(); for (var j = 0; j < n; j++) { r.String(); r.String(); } }
                    if (name is not "." and not "..") files.Add(new(name, Join(path, name), (mode & 0xF000) == 0x4000, size));
                }
            }
        }
        finally { await CloseAsync(handle); }
        return files.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static string Join(string path, string name) => path.TrimEnd('/') + "/" + name;
    /// <summary>SSH_FXP_MKDIR (14): creates a remote directory with default attrs.</summary>
    public async Task MkdirAsync(string path, CancellationToken ct = default)
    { var p = new Packet(14); p.String(path); p.UInt(0); Expect(await RequestAsync(p, ct), 101); }
    /// <summary>SSH_FXP_RENAME refuses an existing target. Replacement is
    /// opt-in and uses posix-rename when the server advertises it.</summary>
    public async Task RenameAsync(string source, string target, CancellationToken ct = default, bool replace = false)
    {
        var usePosixRename = replace && _posixRename;
        var p = new Packet(usePosixRename ? (byte)200 : (byte)18);
        if (usePosixRename) p.String("posix-rename@openssh.com");
        p.String(source); p.String(target); Expect(await RequestAsync(p, ct), 101);
    }
    /// <summary>SSH_FXP_REMOVE (13) for files / SSH_FXP_RMDIR (15) for empty
    /// directories, dispatched on <see cref="RemoteFile.IsDirectory"/>.</summary>
    public async Task RemoveAsync(RemoteFile file, CancellationToken ct = default)
    { var p = new Packet(file.IsDirectory ? (byte)15 : (byte)13); p.String(file.Path); Expect(await RequestAsync(p, ct), 101); }
    /// <summary>SSH_FXP_STAT (7): remote attrs reduced to (isDirectory, size).</summary>
    public async Task<(bool IsDirectory, long Size)> StatAsync(string path, CancellationToken ct = default)
    {
        var p = new Packet(7); p.String(path); var r = await RequestAsync(p, ct); Expect(r, 105);
        var flags = r.UInt(); long size = 0; uint mode = 0;
        if ((flags & 1) != 0) size = checked((long)r.ULong());
        if ((flags & 2) != 0) { r.UInt(); r.UInt(); }
        if ((flags & 4) != 0) mode = r.UInt();
        if ((flags & 8) != 0) { r.UInt(); r.UInt(); }
        if ((flags & 0x80000000) != 0) { var n = r.UInt(); for (var j = 0; j < n; j++) { r.String(); r.String(); } }
        return ((mode & 0xF000) == 0x4000, size);
    }
    private async Task<byte[]> OpenAsync(string path, uint flags, CancellationToken ct)
    { var p = new Packet(3); p.String(path); p.UInt(flags); p.UInt(0); var r = await RequestAsync(p, ct); Expect(r, 102); return r.Bytes(); }
    private async Task CloseAsync(byte[] handle)
    { var p = new Packet(4); p.Bytes(handle); Expect(await RequestAsync(p, CancellationToken.None), 101); }
    public async Task DownloadAsync(RemoteFile file, string local, IProgress<long>? progress, CancellationToken ct)
    {
        var temporary = local + "." + Guid.NewGuid().ToString("N") + ".part";
        var handle = await OpenAsync(file.Path, 1, ct);
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 32768, true))
            {
                ulong offset = 0;
                while (true)
                {
                    var p = new Packet(5); p.Bytes(handle); p.ULong(offset); p.UInt(32768); var r = await RequestAsync(p, ct);
                    if (r.Type == 101 && r.Status == 1) break;
                    Expect(r, 103); var data = r.Bytes(); if (data.Length == 0) throw new IOException("服务器返回空数据，传输未完成。");
                    await stream.WriteAsync(data, ct); offset += (uint)data.Length; progress?.Report((long)offset);
                }
            }
            ct.ThrowIfCancellationRequested(); File.Move(temporary, local, true);
        }
        finally
        {
            // Cleanup must not replace the transfer outcome (original error or a
            // completed download) — same IOException guard as UploadAsync.
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            try { if (!_disposed) await CloseAsync(handle); }
            catch (IOException) { Dispose(); }
        }
    }
    public async Task UploadAsync(string local, string remote, IProgress<long>? progress, CancellationToken ct)
    {
        var temporary = remote + "." + Guid.NewGuid().ToString("N") + ".part";
        IncompleteRemotePath = temporary;
        var handle = await OpenAsync(temporary, 2 | 8 | 16 | 32, ct); bool complete = false, closed = false;
        try
        {
            await using var stream = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, true);
            var data = new byte[32768]; ulong offset = 0; int count;
            while ((count = await stream.ReadAsync(data, ct)) > 0)
            {
                var p = new Packet(6); p.Bytes(handle); p.ULong(offset); p.Bytes(data.AsSpan(0, count));
                Expect(await RequestAsync(p, ct), 101); offset += (uint)count; progress?.Report((long)offset);
            }
            await CloseAsync(handle); closed = true;
            await RenameAsync(temporary, remote, ct, replace: true); complete = true; IncompleteRemotePath = null;
        }
        finally
        {
            try
            {
                if (!closed && !_disposed) await CloseAsync(handle);
                if (!complete && !_disposed) { var remove = new Packet(13); remove.String(temporary); Expect(await RequestAsync(remove, CancellationToken.None), 101); IncompleteRemotePath = null; }
            }
            catch (IOException) { Dispose(); } // Keep the transfer error and the known partial path for the UI.
        }
    }
    private async Task<Reply> RequestAsync(Packet packet, CancellationToken ct)
    {
        await _serial.WaitAsync(ct);
        try
        {
            packet.InsertId(++_id); await WriteAsync(packet, ct); var reply = await ReadAsync(ct);
            reply.Type = reply.Byte(); if (reply.UInt() != _id) throw new IOException("SFTP 响应序号不匹配。");
            if (reply.Type == 101) { reply.Status = reply.UInt(); reply.Message = reply.String(); }
            return reply;
        }
        catch (OperationCanceledException) { Dispose(); throw; } // A cancelled partial packet cannot be reused as the next reply.
        finally { _serial.Release(); }
    }
    private static void Expect(Reply r, byte type)
    { if (r.Type != type || (r.Type == 101 && r.Status != 0)) throw new IOException(r.Message.Length > 0 ? r.Message : $"SFTP 响应错误：{r.Type}/{r.Status}"); }
    private async Task WriteAsync(Packet p, CancellationToken ct)
    { var data = p.Data.ToArray(); var length = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length); await _input.WriteAsync(length, ct); await _input.WriteAsync(data, ct); await _input.FlushAsync(ct); }
    private async Task<Reply> ReadAsync(CancellationToken ct)
    {
        var length = new byte[4]; await _output.ReadExactlyAsync(length, ct); var n = BinaryPrimitives.ReadUInt32BigEndian(length);
        // SFTP packets are bounded to prevent a corrupt length from allocating gigabytes.
        if (n > 16 * 1024 * 1024) throw new IOException("SFTP 数据包过大。");
        var data = new byte[n]; await _output.ReadExactlyAsync(data, ct); return new Reply(data);
    }
    public void Dispose()
    { if (_disposed) return; _disposed = true; _input.Dispose(); _output.Dispose(); if (_process is { } p) { try { if (!p.HasExited) p.Kill(); } catch (InvalidOperationException) { } p.Dispose(); } }
    private sealed class Packet(byte type)
    {
        public List<byte> Data { get; } = [type];
        public void UInt(uint n) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, n); Data.AddRange(b); }
        public void ULong(ulong n) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, n); Data.AddRange(b); }
        public void String(string s) => Bytes(Encoding.UTF8.GetBytes(s));
        public void Bytes(ReadOnlySpan<byte> b) { UInt((uint)b.Length); Data.AddRange(b.ToArray()); }
        public void InsertId(uint n) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, n); Data.InsertRange(1, b); }
    }
    private sealed class Reply(byte[] data)
    {
        private int _offset;
        public byte Type; public uint Status; public string Message = "";
        public int Remaining => data.Length - _offset;
        public byte Byte() => data[_offset++];
        public uint UInt() { var n = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(_offset, 4)); _offset += 4; return n; }
        public ulong ULong() { var n = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(_offset, 8)); _offset += 8; return n; }
        public byte[] Bytes() { var n = checked((int)UInt()); var b = data.AsSpan(_offset, n).ToArray(); _offset += n; return b; }
        public string String() => Encoding.UTF8.GetString(Bytes());
    }
}
