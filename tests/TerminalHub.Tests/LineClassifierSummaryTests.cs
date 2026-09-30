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
    [InlineData("10 个错误")]
    [InlineData("100 个错误")]
    [InlineData("错误: 2")]
    [InlineData("10 errors")]
    [InlineData("compiler error CS1002")]
    [InlineData("生成失败。")]
    [InlineData("Process exited with code 1")]
    public void RealErrors_AreError(string line)
        => Assert.Equal("error", LineClassifier.Classify(line));
}

/// <summary>Heuristic classify cases — moved from the deleted AiAssistantTests.cs
/// (the AI assistant it tested is gone; the classifier coverage stays).</summary>
public class LineClassifierTests
{
    [Theory]
    [InlineData("ls: cannot access 'x': No such file or directory", "error")]
    [InlineData("Unhandled exception: boom", "error")]
    [InlineData("FAIL: build failed", "error")]
    [InlineData("exit code 2", "error")]
    [InlineData("exit status 127", "error")]
    [InlineData("exit_code_1", "error")]
    [InlineData("process exited with code 3", "error")]
    [InlineData("bash: foo: command not found", "error")]
    [InlineData("Permission denied", "error")]
    [InlineData("执行失败", "error")]
    [InlineData("npm warn deprecated something", "warn")]
    [InlineData("warnings in the build", "warn")]
    [InlineData("warns once", "warn")]
    [InlineData("10 个警告", "warn")]
    [InlineData("警告: 配置缺失", "warn")]
    [InlineData("0 个警告", "info")]
    [InlineData("0 warnings", "info")]
    [InlineData("no warnings", "info")]
    [InlineData("10 warnings", "warn")]
    [InlineData("hello world", "info")]
    [InlineData("exit code 0", "info")]
    [InlineData("all tests passed", "info")]
    public void Classify_Heuristics(string line, string expected)
        => Assert.Equal(expected, LineClassifier.Classify(line));
}
