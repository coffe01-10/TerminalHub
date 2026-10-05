using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Avalonia.Threading;
using TerminalHub.App.Controls;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<BroadcastTarget> BroadcastTargets { get; } = [];
    public ObservableCollection<string> ProjectTerminalNames { get; } = [];
    [ObservableProperty] private bool _broadcastEnabled;
    [ObservableProperty] private string _broadcastStatus = "选择目标终端后开启广播；Esc 可退出。";
    public ObservableCollection<OutputRuleViewModel> OutputRules { get; } = [];
    public ObservableCollection<OutputRuleResult> OutputRuleResults { get; } = [];
    [ObservableProperty] private OutputRuleViewModel? _selectedOutputRule;
    private IReadOnlyList<OutputRuleMatcher> _ruleMatchers = [];
    private readonly List<ProjectTaskViewModel> _allProjectTasks = [];
    public ObservableCollection<ProjectTaskViewModel> ProjectTasks { get; } = [];
    [ObservableProperty] private ProjectTaskViewModel? _selectedProjectTask;
    public RemoteFilesViewModel RemoteFiles { get; private set; } = null!;

    private void InitializeProjectTools()
    {
        foreach (var rule in _settings.OutputRules) AddRule(rule);
        SelectedOutputRule = OutputRules.FirstOrDefault();
        foreach (var task in _settings.ProjectTasks) _allProjectTasks.Add(new(task));
        RemoteFiles = new RemoteFilesViewModel(OpenRemoteTerminalAsync);
        CompileOutputRules();
        RefreshProjectTools();
    }

    private void RefreshProjectTools()
    {
        if (RemoteFiles is null) return;
        var selectedTask = SelectedProjectTask;
        SelectedProjectTask = null;
        var selected = BroadcastTargets.Where(t => t.Selected).Select(t => t.Card.Model.Id).ToHashSet();
        BroadcastTargets.Clear();
        ProjectTerminalNames.Clear();
        foreach (var card in SessionCards)
        {
            ProjectTerminalNames.Add(card.Name);
            var target = new BroadcastTarget(card) { Selected = selected.Contains(card.Model.Id) };
            target.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(target.Selected) && BroadcastEnabled) UpdateBroadcastStatus(); };
            BroadcastTargets.Add(target);
        }
        if (BroadcastEnabled) UpdateBroadcastStatus();
        ProjectTasks.Clear();
        foreach (var task in _allProjectTasks.Where(t => t.WorkspaceId == ActiveWorkspace.Id)) ProjectTasks.Add(task);
        SelectedProjectTask = selectedTask is not null && ProjectTasks.Contains(selectedTask) ? selectedTask : ProjectTasks.FirstOrDefault();
    }

    [RelayCommand]
    private void StartBroadcast()
    {
        var targets = BroadcastTargets.Where(t => t.Selected && t.Card.Model.IsRunning).ToArray();
        if (targets.Length < 2) { BroadcastStatus = "至少选择两个运行中的终端。"; return; }
        BroadcastEnabled = true;
        UpdateBroadcastStatus();
    }
    private void UpdateBroadcastStatus() => BroadcastStatus = "广播输入 → " + string.Join("、", BroadcastTargets.Where(t => t.Selected).Select(t => t.Name));
    [RelayCommand]
    public void StopBroadcast() { BroadcastEnabled = false; BroadcastStatus = "广播已停止；输入只发送到当前终端。"; }
    public void SendTerminalInput(TerminalEmulator source, TerminalInput input)
    {
        if (!BroadcastEnabled) { input.SendTo(source); return; }
        if (!_sessionWorkspaces.TryGetValue(source.Pty.Id, out var owner) || owner != ActiveWorkspace) { input.SendTo(source); return; }
        var targets = BroadcastTargets.Where(t => t.Selected && t.Card.Model.IsRunning).Select(t => t.Card.Model.Emulator).ToArray();
        if (targets.Length < 2) { StopBroadcast(); input.SendTo(source); return; }
        UpdateBroadcastStatus();
        foreach (var target in targets) input.SendTo(target);
    }

    private void AddRule(OutputRule rule)
    {
        var vm = new OutputRuleViewModel(rule);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(vm.Name) or nameof(vm.Pattern) or nameof(vm.Enabled)
                or nameof(vm.Notify) or nameof(vm.IsRegex) or nameof(vm.Color)) CompileOutputRules();
        };
        OutputRules.Add(vm);
    }
    [RelayCommand] private void NewOutputRule() { AddRule(new()); SelectedOutputRule = OutputRules.Last(); }
    [RelayCommand] private void DeleteOutputRule()
    {
        if (SelectedOutputRule is { } rule) OutputRules.Remove(rule);
        SelectedOutputRule = OutputRules.FirstOrDefault(); CompileOutputRules();
    }
    [RelayCommand] private void SaveProjectTools() => SaveSettingsInternal();
    private void CompileOutputRules()
    {
        var matchers = OutputRules.Select(rule => new OutputRuleMatcher(rule.Snapshot())).ToArray();
        foreach (var rule in OutputRules)
        {
            var matcher = matchers.First(m => m.Rule.Id == rule.Id);
            rule.Error = Color.TryParse(rule.Color, out _) ? matcher.Error : "颜色需使用 #RRGGBB 格式。";
        }
        _ruleMatchers = matchers.Where(m => OutputRules.First(r => r.Id == m.Rule.Id).Error.Length == 0).ToArray();
        foreach (var session in _sessions.Sessions.Concat(DetachedSessions))
        { session.Emulator.OutputRules = _ruleMatchers; session.Emulator.RefreshDisplay(); }
    }

    private void EvaluateOutputRules(Guid sessionId, string line)
    {
        foreach (var matcher in _ruleMatchers)
        {
            var count = matcher.Find(line).Count;
            if (matcher.Error.Length > 0) RunOnUi(() => { if (OutputRules.FirstOrDefault(r => r.Id == matcher.Rule.Id) is { } rule) rule.Error = matcher.Error; });
            if (count == 0) continue;
            RunOnUi(() =>
            {
                if (_disposed) return;
                var rule = OutputRules.FirstOrDefault(r => r.Id == matcher.Rule.Id);
                if (rule is null) return;
                rule.MatchCount += count;
                if (matcher.Rule.Notify && _sessions.Sessions.Concat(DetachedSessions).FirstOrDefault(s => s.Id == sessionId) is { } session)
                    ShowNotification(session, $"{session.Name} · {matcher.Rule.Name} · {line}");
            });
        }
    }

    [RelayCommand]
    public void ScanOutputRules()
    {
        foreach (var result in OutputRuleResults) lock (result.Result.Buffer.SyncRoot) result.Result.Buffer.Anchors.Remove(result.Result.Anchor);
        OutputRuleResults.Clear();
        foreach (var rule in OutputRules) rule.MatchCount = 0;
        foreach (var card in SessionCards)
        {
            var buffer = card.Model.Emulator.Buffer;
            lock (buffer.SyncRoot)
            {
                for (var row = 0; row < buffer.TotalLines; row++)
                {
                    var (text, columns) = ScreenBuffer.FlattenRow(buffer.GetLine(row));
                    foreach (var matcher in _ruleMatchers)
                        foreach (var match in matcher.Find(text))
                        {
                            OutputRules.First(r => r.Id == matcher.Rule.Id).MatchCount++;
                            if (OutputRuleResults.Count >= 200) continue;
                            var query = text.Substring(match.Index, match.Length);
                            OutputRuleResults.Add(new(matcher.Rule.Name, new(card.Model, buffer,
                                new ScreenBuffer.SearchHit(row, text, buffer.RemovedLineCount, buffer.LayoutVersion),
                                buffer.CreateAnchor(row, columns[match.Index]), query)));
                        }
                }
            }
        }
    }

    [RelayCommand]
    private void NewProjectTask()
    {
        var task = new ProjectTaskViewModel(new() { WorkspaceId = ActiveWorkspace.Id, SessionName = ActiveCard?.Name ?? "" });
        _allProjectTasks.Add(task); ProjectTasks.Add(task); SelectedProjectTask = task;
    }
    [RelayCommand] private void DeleteProjectTask()
    {
        if (SelectedProjectTask is not { Running: false } task) return;
        _allProjectTasks.Remove(task); ProjectTasks.Remove(task); SelectedProjectTask = ProjectTasks.FirstOrDefault();
    }
    [RelayCommand]
    public void RunProjectTask(ProjectTaskViewModel? task)
    {
        task ??= SelectedProjectTask;
        if (task is null || task.Running) return;
        var session = ActiveWorkspace.Cards.FirstOrDefault(c => c.Name == task.SessionName)?.Model;
        if (session is null || !session.IsRunning) { task.Status = "目标终端未运行，请重新选择。"; return; }
        if (!ShellIntegration.IsPowerShell(session.Shell) && !ShellIntegration.IsBash(session.Shell))
        { task.Status = "请选择 PowerShell 或 Bash Shell 终端，以获取命令完成状态。"; return; }
        if (string.IsNullOrWhiteSpace(task.Command)) { task.Status = "请填写任务命令。"; return; }
        if (_allProjectTasks.Any(t => t.Running && t.RunningSession == session) || session.Emulator.CommandState?.Running == true)
        { task.Status = "目标终端已有命令运行，请等待或停止后再运行。"; return; }
        task.RunningSession = session; task.Started = DateTimeOffset.Now;
        task.Running = true; task.Status = "运行中";
        session.Emulator.PasteText(ShellIntegration.PrepareTaskCommand(session.Shell, task.Command)); session.Emulator.SendText("\r");
    }
    [RelayCommand]
    public void StopProjectTask(ProjectTaskViewModel? task)
    {
        task ??= SelectedProjectTask;
        if (task is not { Running: true, RunningSession: { } session }) return;
        session.Emulator.SendBytes([3]); task.Status = "已请求停止，等待 Shell 返回";
    }
    [RelayCommand] private void ShowProjectTaskTerminal(ProjectTaskViewModel? task)
    { if ((task ?? SelectedProjectTask)?.RunningSession is { } session) _sessions.Activate(session); }
    private void OnProjectTaskCompleted(TerminalSessionModel session, int? code)
    {
        foreach (var task in _allProjectTasks.Where(t => t.Running && t.RunningSession == session))
        {
            task.Running = false;
            task.Status = $"{(code == 0 ? "完成" : code is null ? "命令结束" : $"失败（{code}）")} · {(DateTimeOffset.Now - task.Started).TotalSeconds:0.0}s";
        }
    }
    private void SnapshotProjectTools()
    {
        _settings.OutputRules = OutputRules.Select(r => r.Snapshot()).ToList();
        _settings.ProjectTasks = _allProjectTasks.Select(t => t.Snapshot()).ToList();
    }
    private void DisposeProjectTools()
    {
        foreach (var result in OutputRuleResults) lock (result.Result.Buffer.SyncRoot) result.Result.Buffer.Anchors.Remove(result.Result.Anchor);
        RemoteFiles?.Dispose();
        AiPanel.Dispose();
        StopAllRecordings();
    }
}
