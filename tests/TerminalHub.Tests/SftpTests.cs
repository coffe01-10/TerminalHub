using System.Buffers.Binary;
using System.Text;
using TerminalHub.Core.Ssh;
using Xunit;

namespace TerminalHub.Tests;

public class SftpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rename_RejectsExistingTarget_WithoutReplacingEitherFile(bool advertisesPosixRename)
    {
        using var server = new FixtureServer { AdvertisesPosixRename = advertisesPosixRename };
        using var client = new SftpClient(server, server);
        await client.InitializeAsync();
        const string source = "/项目 文件/中文 ' 文件.txt";
        const string target = "/项目 文件/已有文件.txt";
        var originalSource = server.Files[source];
        var originalTarget = Encoding.UTF8.GetBytes("保留目标内容");
        server.Files[target] = originalTarget;

        await Assert.ThrowsAsync<IOException>(() => client.RenameAsync(source, target));
        Assert.Equal(originalSource, server.Files[source]);
        Assert.Equal(originalTarget, server.Files[target]);
        Assert.Empty(server.Extensions);

        const string available = "/项目 文件/新名称.txt";
        await client.RenameAsync(source, available);
        Assert.False(server.Files.ContainsKey(source));
        Assert.Equal(originalSource, server.Files[available]);
    }

    [Fact]
    public async Task BinaryProtocolListsAndTransfersChineseSpaceNames_WithProgressAndAtomicReplacement()
    {
        using var server = new FixtureServer(); using var client = new SftpClient(server, server); await client.InitializeAsync();
        var directory = await client.RealPathAsync("."); Assert.Equal("/项目 文件", directory);
        var files = await client.ListAsync(directory); Assert.Contains(files, f => f.IsDirectory && f.Name == "子目录");
        var file = Assert.Single(files, f => !f.IsDirectory); Assert.Equal("中文 ' 文件.txt", file.Name);
        var local = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            long downloaded = 0; await client.DownloadAsync(file, local, new InlineProgress(n => downloaded = n), default);
            Assert.Equal("fixture 中文", await File.ReadAllTextAsync(local)); Assert.Equal(file.Size, downloaded);
            var bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("传输内容", 10000))); await File.WriteAllBytesAsync(local, bytes);
            long uploaded = 0; await client.UploadAsync(local, file.Path, new InlineProgress(n => uploaded = n), default);
            Assert.Equal(bytes.Length, uploaded); Assert.Equal(bytes, server.Files[file.Path]);
            Assert.DoesNotContain(server.Files.Keys, p => p.EndsWith(".part"));
            Assert.Contains("posix-rename@openssh.com", server.Extensions);
        }
        finally { File.Delete(local); }
    }
    [Fact]
    public async Task CancelledTransfersKeepExistingFilesAndCleanTemporaryFiles()
    {
        using var server = new FixtureServer(); using var client = new SftpClient(server, server); await client.InitializeAsync();
        var local = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"); var remote = "/项目 文件/中文 ' 文件.txt";
        var original = server.Files[remote];
        try
        {
            await File.WriteAllBytesAsync(local, new byte[70000]); using var uploadCancel = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.UploadAsync(local, remote, new InlineProgress(_ => uploadCancel.Cancel()), uploadCancel.Token));
            Assert.Equal(original, server.Files[remote]); Assert.DoesNotContain(server.Files.Keys, p => p.EndsWith(".part"));
            await File.WriteAllTextAsync(local, "keep local"); server.Files[remote] = new byte[70000]; using var downloadCancel = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DownloadAsync(new("file", remote, false, 70000), local, new InlineProgress(_ => downloadCancel.Cancel()), downloadCancel.Token));
            Assert.Equal("keep local", await File.ReadAllTextAsync(local));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(local)!, Path.GetFileName(local) + ".*.part"));
        }
        finally { File.Delete(local); }
    }
    [Fact]
    public async Task FailedUploadCleansRemotePartialAndPreservesOriginal()
    {
        using var server = new FixtureServer { FailWrites = true }; using var client = new SftpClient(server, server); await client.InitializeAsync();
        var local = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt"); var remote = "/项目 文件/中文 ' 文件.txt"; var original = server.Files[remote];
        try
        {
            await File.WriteAllTextAsync(local, "new data");
            await Assert.ThrowsAsync<IOException>(() => client.UploadAsync(local, remote, null, default));
            Assert.Equal(original, server.Files[remote]); Assert.DoesNotContain(server.Files.Keys, p => p.EndsWith(".part"));
        }
        finally { File.Delete(local); }
    }
    private sealed class InlineProgress(Action<long> report) : IProgress<long> { public void Report(long value) => report(value); }

    // In-process SFTP peer uses the wire format (big-endian packets), no SSH credentials or user configuration.
    private sealed class FixtureServer : Stream
    {
        public Dictionary<string, byte[]> Files { get; } = new() { ["/项目 文件/中文 ' 文件.txt"] = Encoding.UTF8.GetBytes("fixture 中文") };
        public List<string> Extensions { get; } = [];
        public bool FailWrites;
        public bool AdvertisesPosixRename = true;
        private readonly MemoryStream _request = new();
        private MemoryStream _response = new();
        private readonly Dictionary<string, string> _handles = [];
        private bool _listed;
        public override void Flush()
        {
            var request = _request.ToArray(); _request.SetLength(0);
            Assert.Equal(request.Length - 4, (int)BinaryPrimitives.ReadUInt32BigEndian(request));
            var r = new WireReader(request[4..]); var type = r.Byte(); uint id = r.UInt(); var reply = new WireWriter();
            void Status(uint code) { reply.Byte(101); reply.UInt(id); reply.UInt(code); reply.String(code == 0 ? "" : "fixture failure"); reply.String(""); }
            void Handle(string path) { var handle = Guid.NewGuid().ToString("N"); _handles[handle] = path; reply.Byte(102); reply.UInt(id); reply.String(handle); }
            switch (type)
            {
                case 1:
                    Assert.Equal(3u, id); reply.Byte(2); reply.UInt(3);
                    if (AdvertisesPosixRename) { reply.String("posix-rename@openssh.com"); reply.String("1"); }
                    break;
                case 16: r.String(); reply.Byte(104); reply.UInt(id); reply.UInt(1); reply.String("/项目 文件"); reply.String(""); reply.UInt(0); break;
                case 11: Handle(r.String()); _listed = false; break;
                case 12:
                    r.String(); if (_listed) { Status(1); break; } _listed = true; reply.Byte(104); reply.UInt(id); reply.UInt(2);
                    reply.String("子目录"); reply.String(""); reply.UInt(4); reply.UInt(0x4000);
                    reply.String("中文 ' 文件.txt"); reply.String(""); reply.UInt(5); reply.ULong((ulong)Files.Values.First().Length); reply.UInt(0x8000); break;
                case 3:
                    var path = r.String(); var flags = r.UInt(); r.UInt();
                    if ((flags & 2) != 0) { Assert.True((flags & 32) != 0); Files[path] = []; }
                    Handle(path); break;
                case 5:
                    var data = Files[_handles[r.String()]]; var offset = (int)r.ULong(); var length = (int)r.UInt();
                    if (offset >= data.Length) { Status(1); break; }
                    reply.Byte(103); reply.UInt(id); reply.Bytes(data.AsSpan(offset, Math.Min(length, data.Length - offset))); break;
                case 6:
                    var target = _handles[r.String()]; var start = (int)r.ULong(); var content = r.Bytes();
                    if (FailWrites) { Status(4); break; }
                    var current = Files[target]; Array.Resize(ref current, Math.Max(current.Length, start + content.Length)); content.CopyTo(current.AsSpan(start)); Files[target] = current; Status(0); break;
                case 4: _handles.Remove(r.String()); Status(0); break;
                case 13: Files.Remove(r.String()); Status(0); break;
                case 18:
                    var source = r.String(); var destination = r.String();
                    if (Files.ContainsKey(destination)) { Status(4); break; }
                    Files[destination] = Files[source]; Files.Remove(source); Status(0); break;
                case 200:
                    Extensions.Add(r.String()); var from = r.String(); var to = r.String(); Files[to] = Files[from]; Files.Remove(from); Status(0); break;
                default: throw new InvalidOperationException("Unexpected SFTP request " + type);
            }
            _response.Dispose(); _response = new MemoryStream(reply.Packet());
        }
        public override Task FlushAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Flush(); return Task.CompletedTask; }
        public override void Write(byte[] buffer, int offset, int count) => _request.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => _request.WriteAsync(buffer, ct);
        public override int Read(byte[] buffer, int offset, int count) => _response.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _response.ReadAsync(buffer, ct);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { _request.Dispose(); _response.Dispose(); base.Dispose(disposing); }
    }
    private sealed class WireWriter
    {
        private readonly List<byte> _bytes = [];
        public void Byte(byte b) => _bytes.Add(b);
        public void UInt(uint n) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, n); _bytes.AddRange(b); }
        public void ULong(ulong n) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, n); _bytes.AddRange(b); }
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
        public byte[] Bytes() { var n = (int)UInt(); var b = data.AsSpan(_position, n).ToArray(); _position += n; return b; }
        public string String() => Encoding.UTF8.GetString(Bytes());
    }
}
