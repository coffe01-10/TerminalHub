using TerminalHub.Core.Pty;
using Xunit;

namespace TerminalHub.Tests;

public class PtyEnvironmentTests
{
    [Fact]
    public void Build_SetsTerminalIdentity_AndLetsExplicitOverridesWin()
    {
        var env = PtyEnvironment.Build(
            new PtyOptions
            {
                Shell = "x",
                Environment = new Dictionary<string, string> { ["CUSTOM"] = "1", ["TERM"] = "override" }
            },
            new Dictionary<string, string> { ["USER"] = "u" });

        Assert.Equal("override", env["TERM"]);            // explicit override wins over the default
        Assert.Equal("truecolor", env["COLORTERM"]);
        Assert.Equal("TerminalHub", env["TERM_PROGRAM"]);
        Assert.Equal("u", env["USER"]);                   // inherited entries pass through
        Assert.Equal("1", env["CUSTOM"]);
    }

    /// <summary>A child spawned with no locale variables would get POSIX/C and
    /// degrade CJK/UTF-8 tools — non-Windows sessions fall back to C.UTF-8.</summary>
    [Fact]
    public void Build_WithoutLocale_FallsBackToUtf8_OnLinux()
    {
        if (OperatingSystem.IsWindows()) return;

        var env = PtyEnvironment.Build(new PtyOptions { Shell = "x" },
            new Dictionary<string, string>());
        Assert.Equal("C.UTF-8", env["LANG"]);

        env = PtyEnvironment.Build(new PtyOptions { Shell = "x" },
            new Dictionary<string, string> { ["LANG"] = "zh_CN.UTF-8" });
        Assert.Equal("zh_CN.UTF-8", env["LANG"]);

        env = PtyEnvironment.Build(new PtyOptions { Shell = "x" },
            new Dictionary<string, string> { ["LC_ALL"] = "ja_JP.UTF-8" });
        Assert.False(env.ContainsKey("LANG"));
    }
}
