using System.Buffers.Binary;
using System.Text;
using TerminalHub.Core.Ssh;
using Xunit;

namespace TerminalHub.Tests;

// DownloadAsync cleanup must not replace the transfer outcome (the same
// IOException guard UploadAsync already has): a completed download stays
// successful even if the final CloseAsync fails, and a failed download keeps
// its original error instead of the cleanup exception.
public class SftpTransferCleanupTests
{
    [Fact]
    public async Task CompletedDownloadStaysSuccessful_WhenCloseFailsAfterMove()
    {
        using var server = new CleanupServer { BreakOnClose = true };
        using var client = new SftpClient(server, server);
        await client.InitializeAsync();
        var local = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            await client.DownloadAsync(new("file", "/remote.txt", false, server.Content.Length), local, null, default);
            Assert.Equal(server.Content, await File.ReadAllBytesAsync(local));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(local)!, Path.GetFileName(local) + ".*.part"));
        }
        finally { File.Delete(local); }
    }

    [Fact]
    public async Task FailedDownloadKeepsOriginalError_WhenCloseAlsoFails()
    {
        using var server = new CleanupServer { FailReads = true, BreakOnClose = true };
        using var client = new SftpClient(server, server);
        await client.InitializeAsync();
        var local = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            var error = await Assert.ThrowsAsync<IOException>(
                () => client.DownloadAsync(new("file", "/remote.txt", false, 1), local, null, default));
            Assert.Contains("fixture failure", error.Message);
            Assert.False(File.Exists(local));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(local)!, Path.GetFileName(local) + ".*.part"));
        }
        finally { File.Delete(local); }
    }

    // In-process SFTP peer using the wire format (big-endian packets), like
    // SftpTests.FixtureServer but trimmed to the download path; no SSH
    // credentials or user configuration are involved.
    private sealed class CleanupServer : Stream
    {
        public byte[] Content { get; set; } = Encoding.UTF8.GetBytes("fixture 中文");
        public bool FailReads, BreakOnClose;
        private readonly MemoryStream _request = new();
        private MemoryStream _response = new();
        private readonly List<string> _handles = [];
        private bool _broken;
        public override void Flush()
        {
            var request = _request.ToArray(); _request.SetLength(0);
            var r = new WireReader(request[4..]); var type = r.Byte(); uint id = r.UInt(); var w = new WireWriter();
            void Status(uint code) { w.Byte(101); w.UInt(id); w.UInt(code); w.String(code == 0 ? "" : "fixture failure"); w.String(""); }
            switch (type)
            {
                case 1: w.Byte(2); w.UInt(3); w.String(""); w.String(""); break;
                case 3: r.String(); r.UInt(); r.UInt(); var handle = Guid.NewGuid().ToString("N"); _handles.Add(handle); w.Byte(102); w.UInt(id); w.String(handle); break;
                case 5:
                    r.String(); var offset = (int)r.ULong(); r.UInt();
                    if (FailReads) { Status(4); break; }
                    if (offset >= Content.Length) { Status(1); break; }
                    w.Byte(103); w.UInt(id); w.Bytes(Content.AsSpan(offset)); break;
                case 4: r.String(); if (BreakOnClose) _broken = true; Status(0); break;
                default: throw new InvalidOperationException("Unexpected SFTP request " + type);
            }
            _response.Dispose(); _response = new MemoryStream(w.Packet());
        }
        public override Task FlushAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Flush(); return Task.CompletedTask; }
        public override void Write(byte[] buffer, int offset, int count) => _request.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => _request.WriteAsync(buffer, ct);
        public override int Read(byte[] buffer, int offset, int count) => _response.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => _broken ? throw new EndOfStreamException() : _response.ReadAsync(buffer, ct);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    }
    private sealed class WireWriter
    {
        private readonly List<byte> _bytes = [];
        public void Byte(byte b) => _bytes.Add(b);
        public void UInt(uint n) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, n); _bytes.AddRange(b); }
        public void String(string s) => Bytes(Encoding.UTF8.GetBytes(s));
        public void Bytes(ReadOnlySpan<byte> b) { UInt((uint)b.Length); _bytes.AddRange(b.ToArray()); }
        public byte[] Packet() { var length = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length, (uint)_bytes.Count); return length.Concat(_bytes).ToArray(); }
    }
    private sealed class WireReader(byte[] data)
    {
        private int _position;
        public byte Byte() => data[_position++];
        public uint UInt() { var n = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(_position, 4)); _position += 4; return n; }
        public ulong ULong() { var n = BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(_position, 8)); _position += 8; return n; }
        public string String() { var n = (int)UInt(); var value = Encoding.UTF8.GetString(data.AsSpan(_position, n)); _position += n; return value; }
    }
}
