namespace TerminalHub.Core.AI;

/// <summary>
/// Deterministic assistant that walks a canned checklist on a timer.
/// Stands in for a real provider until one is wired up (local CLI or user-configured key).
/// </summary>
public sealed class MockAiAssistant : IAiAssistant, IDisposable
{
    private static readonly string[] Steps =
    [
        "读取项目文件",
        "分析依赖关系",
        "识别核心模块",
        "生成改进方案",
        "等待你的确认",
    ];

    private readonly Timer _timer;
    private int _stepIndex = 2; // two items already done at start, like the mockup
    private int _progress = 32;

    public MockAiAssistant()
    {
        _timer = new Timer(_ => Advance(), null, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
    }

    public string StatusText { get; private set; } =
        "正在分析 MangaFlow 项目结构…\n我将帮你梳理代码并生成改进建议。";

    public int? ProgressPercent => _progress;

    public IReadOnlyList<ChecklistItem> Checklist =>
        Steps.Select((s, i) => new ChecklistItem
        {
            Text = s,
            State = i < _stepIndex ? ChecklistState.Done
                : i == _stepIndex ? ChecklistState.Active
                : ChecklistState.Pending,
        }).ToList();

    public IReadOnlyList<SuggestedTask> Suggestions { get; } =
    [
        new SuggestedTask { Title = "梳理项目结构", Description = "分析代码组织并提供重构建议", Icon = "▦" },
        new SuggestedTask { Title = "实现新的漫画详情页", Description = "基于现有组件开发详情页面", Icon = "▣" },
        new SuggestedTask { Title = "优化图片加载性能", Description = "使用懒加载和缓存策略", Icon = "▤" },
        new SuggestedTask { Title = "添加单元测试", Description = "为核心模块生成测试用例", Icon = "◧" },
    ];

    public event Action? Changed;

    public Task SubmitTaskAsync(string text, CancellationToken cancellationToken = default)
    {
        StatusText = $"收到任务：{text}\n正在规划执行步骤…";
        _stepIndex = 0;
        _progress = 0;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task RunSuggestionAsync(SuggestedTask task, CancellationToken cancellationToken = default)
        => SubmitTaskAsync(task.Title, cancellationToken);

    private void Advance()
    {
        if (_stepIndex < Steps.Length - 1)
        {
            _stepIndex++;
            _progress = Math.Min(99, _progress + 17);
        }
        else
        {
            _progress = 100;
            StatusText = "分析完成。请选择建议任务或描述新任务。";
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        Changed?.Invoke();
    }

    public void Dispose() => _timer.Dispose();
}
