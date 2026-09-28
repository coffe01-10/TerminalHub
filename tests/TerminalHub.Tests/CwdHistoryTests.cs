using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Terminal;
using Xunit;

namespace TerminalHub.Tests;

public class CwdHistoryTests
{
    [Fact]
    public void Push_Back_Forward_StackBehavesLikeBrowser()
    {
        var h = new CwdHistory();
        Assert.False(h.CanGoBack);
        Assert.False(h.CanGoForward);
        Assert.Null(h.Current);

        h.Push("/tmp/a");
        h.Push("/tmp/b");
        h.Push("/tmp/c");
        Assert.Equal(Path.GetFullPath("/tmp/c"), h.Current);
        Assert.True(h.CanGoBack);
        Assert.False(h.CanGoForward);

        Assert.Equal(Path.GetFullPath("/tmp/b"), h.Back());
        Assert.True(h.CanGoBack);
        Assert.True(h.CanGoForward);

        Assert.Equal(Path.GetFullPath("/tmp/a"), h.Back());
        Assert.False(h.CanGoBack);
        Assert.Null(h.Back()); // already at start

        Assert.Equal(Path.GetFullPath("/tmp/b"), h.Forward());
        // Navigate to a new path from the middle — clears forward.
        h.Push("/tmp/d");
        Assert.Equal(Path.GetFullPath("/tmp/d"), h.Current);
        Assert.False(h.CanGoForward);
        Assert.True(h.CanGoBack);
        Assert.Equal(Path.GetFullPath("/tmp/b"), h.Back());
    }

    [Fact]
    public void Push_SamePath_IsNoOp()
    {
        var h = new CwdHistory();
        h.Push("/tmp/x");
        h.Push("/tmp/x");
        h.Push("/tmp/x/");
        Assert.Equal(1, h.Count);
    }

    [Fact]
    public void Osc7_FileUri_SetsBufferCwd()
    {
        var buf = new ScreenBuffer(40, 10);
        var parser = new VtParser(buf);
        string? got = null;
        buf.CwdChanged += p => got = p;

        // OSC 7 ; file:///tmp/demo ST (BEL-terminated)
        parser.Feed("\u001b]7;file:///tmp/demo\u0007");
        Assert.Equal("/tmp/demo", buf.Cwd);
        Assert.Equal("/tmp/demo", got);
    }

    [Fact]
    public void Osc7_BareAbsolutePath_Accepted()
    {
        Assert.True(VtParser.TryParseOsc7("/home/user/proj", out var cwd));
        Assert.Equal("/home/user/proj", cwd);
        Assert.True(VtParser.TryParseOsc7("file://localhost/var/log", out cwd));
        Assert.Equal("/var/log", cwd.Replace('\\', '/'));
        Assert.True(VtParser.TryParseOsc7("file:///var/log", out cwd));
        Assert.Equal("/var/log", cwd);
        Assert.False(VtParser.TryParseOsc7("http://example.com/x", out _));
    }

    [Fact]
    public void ProcessCwd_InvalidPid_ReturnsNull()
    {
        Assert.Null(ProcessCwd.TryRead((int?)null));
        Assert.Null(ProcessCwd.TryRead(-1));
        Assert.Null(ProcessCwd.TryRead(0));
        // Extremely unlikely live pid; should not throw.
        Assert.Null(ProcessCwd.TryRead(int.MaxValue - 7));
    }
}
