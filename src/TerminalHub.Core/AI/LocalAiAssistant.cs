namespace TerminalHub.Core.AI;

/// <summary>
/// Deterministic local assistant — no network, no paid APIs. The checklist is a
/// real mutable list driven by user input/suggestions; progress is derived from
/// the checklist itself (done/total), not a timer.
/// </summary>
public sealed class LocalAiAssistant : IAiAssistant
{
    private readonly List<ChecklistItem> _checklist =
    [
        new ChecklistItem { Text = "读取项目结构", State = ChecklistState.Done },
        new ChecklistItem { Text = "分析会话输出", State = ChecklistState.Done },
        new ChecklistItem { Text = "等待任务输入", State = ChecklistState.Active },
    ];

    private readonly List<ChatMessage> _messages = [];

    public string StatusText { get; private set; } =
        "本地助手已就绪（规则模式，不调用外部 API)。\n勾选清单项、点击建议任务，或在下方描述任务。";

    public IReadOnlyList<ChecklistItem> Checklist => _checklist;

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>Progress follows the real checklist: done/total, null when empty.</summary>
    public int? ProgressPercent =>
        _checklist.Count == 0 ? null
        : (int)Math.Round(100.0 * _checklist.Count(i => i.State == ChecklistState.Done) / _checklist.Count);

    public IReadOnlyList<SuggestedTask> Suggestions { get; } =
    [
        new SuggestedTask { Title = "梳理项目结构", Description = "列出工作区顶层目录并给出模块划分建议", Icon = "▦" },
        new SuggestedTask { Title = "检查日志中的错误", Description = "扫一遍会话输出里的 error/exception", Icon = "◈" },
        new SuggestedTask { Title = "补单元测试", Description = "为最近改动生成测试清单", Icon = "◧" },
        new SuggestedTask { Title = "准备发布产物", Description = "运行 publish 脚本并核对输出", Icon = "☁" },
    ];

    public event Action? Changed;

    /// <summary>Flip a checklist row Done ↔ Pending; Active stays unchanged.</summary>
    public void ToggleItem(int index)
    {
        if (index < 0 || index >= _checklist.Count) return;
        var item = _checklist[index];
        _checklist[index] = item with
        {
            State = item.State == ChecklistState.Done ? ChecklistState.Pending : ChecklistState.Done,
        };
        Changed?.Invoke();
    }

    /// <summary>Add a user task to the checklist and return a local template reply.</summary>
    public Task<string> SubmitTaskAsync(string text, CancellationToken cancellationToken = default)
    {
        var steps = Decompose(text.Trim());
        foreach (var s in steps)
            _checklist.Add(new ChecklistItem { Text = s, State = ChecklistState.Pending });
        ActivateNextPending();

        var reply = ReplyFor(text);
        _messages.Add(new ChatMessage("你", text));
        _messages.Add(new ChatMessage("Codex", reply));
        StatusText = reply;
        Changed?.Invoke();
        return Task.FromResult(reply);
    }

    /// <summary>Accept a suggestion → checklist item + reply.</summary>
    public Task<string> RunSuggestionAsync(SuggestedTask task, CancellationToken cancellationToken = default)
    {
        _checklist.Add(new ChecklistItem { Text = task.Title, State = ChecklistState.Pending });
        ActivateNextPending();
        var reply = $"已将「{task.Title}」加入任务清单。";
        _messages.Add(new ChatMessage("Codex", reply));
        StatusText = reply;
        Changed?.Invoke();
        return Task.FromResult(reply);
    }

    private void ActivateNextPending()
    {
        if (_checklist.Any(i => i.State == ChecklistState.Active)) return;
        var idx = _checklist.FindIndex(i => i.State == ChecklistState.Pending);
        if (idx >= 0)
            _checklist[idx] = _checklist[idx] with { State = ChecklistState.Active };
    }

    /// <summary>Split a request into 1-4 concrete steps (by punctuation/keywords).</summary>
    private static List<string> Decompose(string text)
    {
        var parts = text.Split(['，', '、', ';', '；', ',', '.', '。', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var steps = parts.Take(4).ToList();
        if (steps.Count == 0) steps.Add(text);
        return steps;
    }

    /// <summary>Keyword-matched template reply; deterministic and offline.</summary>
    private static string ReplyFor(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("test") || text.Contains("测试"))
            return "已加入清单：编写并运行测试。建议先跑 dotnet test 看当前基线，再针对改动补断言。";
        if (t.Contains("deploy") || t.Contains("publish") || text.Contains("部署") || text.Contains("发布"))
            return "已加入清单：构建发布产物。对应脚本 scripts/publish-linux.sh（Windows 用 publish-windows.ps1），完成后可用底栏 Deploy 打开产物目录。";
        if (t.Contains("ssh") || text.Contains("远程") || text.Contains("连接"))
            return "已加入清单：配置 SSH 连接。右侧 SSH 面板可保存主机并一键起会话，密钥建议走 ssh-agent。";
        if (text.Contains("日志") || t.Contains("log"))
            return "已加入清单：梳理日志。Logs 面板支持文本/正则过滤，勾「保留历史」可不被 Output 清空影响。";
        if (text.Contains("文件") || t.Contains("file"))
            return "已加入清单：处理文件。Files 面板从当前会话目录起步，双击预览小文件。";
        if (text.Contains("重构") || t.Contains("refactor") || text.Contains("优化"))
            return "已加入清单：重构/优化。建议小步提交，每步跑测试保持绿。";
        return "已拆解并加入任务清单。完成后可直接勾选清单项更新进度。";
    }
}
