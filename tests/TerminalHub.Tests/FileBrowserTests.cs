using System.Text;
using TerminalHub.Core.Files;
using TerminalHub.Core.Logging;
using Xunit;

namespace TerminalHub.Tests;

public class FileBrowserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "th-files-" + Guid.NewGuid().ToString("N"));

    public FileBrowserTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "beta"));
        Directory.CreateDirectory(Path.Combine(_root, "alpha"));
        File.WriteAllText(Path.Combine(_root, "zeta.txt"), "zeta contents\nline2\n");
        File.WriteAllText(Path.Combine(_root, "apple.md"), "# hello\n");
        File.WriteAllBytes(Path.Combine(_root, "blob.bin"), [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ListDirectory_DirsFirst_ThenFiles_AlphaSorted()
    {
        var entries = LocalFileBrowser.ListDirectory(_root);
        Assert.Equal(5, entries.Count);
        Assert.True(entries[0].IsDirectory);
        Assert.Equal("alpha", entries[0].Name);
        Assert.Equal("beta", entries[1].Name);
        Assert.False(entries[2].IsDirectory);
        Assert.Equal("apple.md", entries[2].Name);
        Assert.Equal("zeta.txt", entries[4].Name);
    }

    [Fact]
    public void ListDirectory_Missing_Throws()
        => Assert.Throws<DirectoryNotFoundException>(
            () => LocalFileBrowser.ListDirectory(Path.Combine(_root, "nope")));

    [Fact]
    public void ReadPreview_Text_ReturnsContents()
    {
        var p = LocalFileBrowser.ReadPreview(Path.Combine(_root, "zeta.txt"));
        Assert.Equal(PreviewKind.Text, p.Kind);
        Assert.Contains("zeta contents", p.Text);
        Assert.False(p.Truncated);
        Assert.True(p.SizeBytes > 0);
    }

    [Fact]
    public void ReadPreview_Binary_Detected()
    {
        var p = LocalFileBrowser.ReadPreview(Path.Combine(_root, "blob.bin"));
        Assert.Equal(PreviewKind.Binary, p.Kind);
        Assert.Empty(p.Text);
    }

    [Fact]
    public void ReadPreview_TooLarge_Skipped()
    {
        var big = Path.Combine(_root, "big.dat");
        File.WriteAllBytes(big, new byte[LocalFileBrowser.MaxPreviewBytes + 1]);
        var p = LocalFileBrowser.ReadPreview(big);
        Assert.Equal(PreviewKind.TooLarge, p.Kind);
        Assert.Empty(p.Text);
    }
}

public class SessionOutputTests
{
    private static List<string> Collect(Utf8LineDecoder d, params string[] chunks)
    {
        var lines = new List<string>();
        d.LineReceived += lines.Add;
        foreach (var c in chunks) d.Feed(Encoding.UTF8.GetBytes(c));
        return lines;
    }

    [Fact]
    public void Decoder_SplitsCrlf_AcrossChunks()
    {
        var d = new Utf8LineDecoder();
        var lines = Collect(d, "abc\r", "\ndef", "\n");
        Assert.Equal(new[] { "abc", "def" }, lines);
    }

    [Fact]
    public void Decoder_LoneCr_RestartsLine_NotEmits()
    {
        // Progress-bar style redraw: "50%\r60%\r70%\n" → single line "70%".
        var d = new Utf8LineDecoder();
        var lines = Collect(d, "50%\r60%\r70%\n");
        Assert.Equal(new[] { "70%" }, lines);
    }

    [Fact]
    public void Decoder_EchoedInput_Survives()
    {
        // "prompt$ echo hi\r\n" — the user's echoed input is a real line.
        var d = new Utf8LineDecoder();
        var lines = Collect(d, "prompt$ echo hi\r\nhi\r\n");
        Assert.Equal(new[] { "prompt$ echo hi", "hi" }, lines);
    }

    [Fact]
    public void Ansi_StripsCsiOscCharset()
    {
        Assert.Equal("dev@host ~ pnpm", AnsiText.Strip("\u001b[36mdev@host\u001b[0m \u001b[34m~\u001b[0m pnpm"));
        Assert.Equal("x", AnsiText.Strip("\u001b]0;title\u0007x")); // OSC payload stripped, text after BEL kept
        Assert.Equal("plain", AnsiText.Strip("\u001b(Bplain\u001b[>4;2m"));
    }

    [Fact]
    public void Decoder_MultiByteUtf8_AcrossChunks()
    {
        var d = new Utf8LineDecoder();
        var bytes = Encoding.UTF8.GetBytes("目录列表\r\n");
        var lines = new List<string>();
        d.LineReceived += lines.Add;
        d.Feed(bytes.AsSpan(0, 3)); // split inside the first CJK char
        d.Feed(bytes.AsSpan(3));
        Assert.Equal(new[] { "目录列表" }, lines);
    }

    [Fact]
    public void Decoder_OversizedLine_ForceEmits()
    {
        // A stream with no \r or \n (cat of a huge binary) must not accumulate
        // in _line forever — the pending line is force-emitted at the cap.
        var d = new Utf8LineDecoder();
        var lines = new List<string>();
        d.LineReceived += lines.Add;
        var chunk = Encoding.UTF8.GetBytes(new string('a', 256 * 1024));
        for (var i = 0; i < 5; i++) d.Feed(chunk); // 1.25M chars > 1Mi cap
        Assert.Single(lines);
        Assert.Equal(1 << 20, lines[0].Length);
        d.Feed(Encoding.UTF8.GetBytes("\n"));
        Assert.Equal(2, lines.Count);
        // The char that tipped the cap is appended to the fresh pending line.
        Assert.Equal(256 * 1024, lines[1].Length);
    }
}
