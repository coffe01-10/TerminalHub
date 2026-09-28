using TerminalHub.Core.AI;
using TerminalHub.Core.Logging;
using Xunit;

namespace TerminalHub.Tests;

public class AiAssistantTests
{
    [Fact]
    public async Task SubmitTask_ReturnsLocalReply_AndAddsChecklistItems()
    {
        var ai = new LocalAiAssistant();
        var before = ai.Checklist.Count;

        var reply = await ai.SubmitTaskAsync("帮我补单元测试");

        Assert.False(string.IsNullOrWhiteSpace(reply));
        Assert.Contains("测试", reply);
        Assert.True(ai.Checklist.Count > before);
        // user text + assistant reply both recorded
        Assert.Equal(2, ai.Messages.Count);
        Assert.Equal("你", ai.Messages[0].Sender);
        Assert.Equal("Codex", ai.Messages[1].Sender);
        Assert.Equal(reply, ai.Messages[1].Text);
    }

    [Fact]
    public async Task SubmitTask_MultiPartText_DecomposesIntoSteps()
    {
        var ai = new LocalAiAssistant();
        var before = ai.Checklist.Count;

        await ai.SubmitTaskAsync("改 UI、跑测试、提交");

        Assert.Equal(before + 3, ai.Checklist.Count);
    }

    [Fact]
    public async Task RunSuggestion_AddsPendingItem_AndReply()
    {
        var ai = new LocalAiAssistant();
        var before = ai.Checklist.Count;
        var suggestion = ai.Suggestions[0];

        var reply = await ai.RunSuggestionAsync(suggestion);

        Assert.Equal(before + 1, ai.Checklist.Count);
        Assert.Equal(suggestion.Title, ai.Checklist[^1].Text);
        Assert.Contains(suggestion.Title, reply);
        Assert.Single(ai.Messages);
    }

    [Fact]
    public async Task RunSuggestion_ActivatesItem_WhenNothingActive()
    {
        var ai = new LocalAiAssistant();
        // finish all seeded items first so nothing is active
        for (var i = 0; i < ai.Checklist.Count; i++)
            if (ai.Checklist[i].State != ChecklistState.Done)
                ai.ToggleItem(i);

        await ai.RunSuggestionAsync(ai.Suggestions[1]);

        Assert.Contains(ai.Checklist, i => i.State == ChecklistState.Active);
    }

    [Fact]
    public void ToggleItem_FlipsDoneAndPending_ProgressFollowsChecklist()
    {
        var ai = new LocalAiAssistant();
        var total = ai.Checklist.Count;
        var doneInitially = ai.Checklist.Count(i => i.State == ChecklistState.Done);
        Assert.Equal((int)Math.Round(100.0 * doneInitially / total), ai.ProgressPercent);

        // toggle a pending/active row to done
        var idx = ai.Checklist.Select((c, i) => (c, i))
            .First(x => x.c.State == ChecklistState.Pending || x.c.State == ChecklistState.Active).i;
        ai.ToggleItem(idx);
        Assert.Equal(ChecklistState.Done, ai.Checklist[idx].State);
        Assert.Equal((int)Math.Round(100.0 * (doneInitially + 1) / total), ai.ProgressPercent);

        // toggle back → pending, progress drops again
        ai.ToggleItem(idx);
        Assert.Equal(ChecklistState.Pending, ai.Checklist[idx].State);
        Assert.Equal((int)Math.Round(100.0 * doneInitially / total), ai.ProgressPercent);
    }
}

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
    [InlineData("警告: 配置缺失", "warn")]
    [InlineData("hello world", "info")]
    [InlineData("exit code 0", "info")]
    [InlineData("all tests passed", "info")]
    public void Classify_Heuristics(string line, string expected)
        => Assert.Equal(expected, LineClassifier.Classify(line));
}
