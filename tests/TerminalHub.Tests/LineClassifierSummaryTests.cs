using TerminalHub.Core.Logging;
using Xunit;

namespace TerminalHub.Tests;

public class LineClassifierSummaryTests
{
    // 2026-09 code-review fix: successful build summaries ("0 个错误") must
    // not feed the Problems panel; real error counts still must.
    [Theory]
    [InlineData("已成功生成。 0 个警告 0 个错误")]
    [InlineData("生成成功。错误: 0")]
    [InlineData("Build succeeded. 0 errors")]
    [InlineData("no errors were found")]
    [InlineData("0 Errors")]
    public void ZeroErrorSummaries_AreInfo(string line)
        => Assert.Equal("info", LineClassifier.Classify(line));

    [Theory]
    [InlineData("3 个错误")]
    [InlineData("错误: 2")]
    [InlineData("10 errors")]
    [InlineData("compiler error CS1002")]
    [InlineData("生成失败。")]
    [InlineData("Process exited with code 1")]
    public void RealErrors_AreError(string line)
        => Assert.Equal("error", LineClassifier.Classify(line));
}
