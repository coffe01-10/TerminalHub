using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Settings;

namespace TerminalHub.App.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<WorkspaceTemplate> WorkspaceTemplates { get; } = [];
    public ObservableCollection<WorkspaceSessionEditor> TemplateSessions { get; } = [];
    public event Action? PaletteRequested;
    [ObservableProperty] private WorkspaceTemplate? _selectedTemplate;
    [ObservableProperty] private string _templateName = "";
    [ObservableProperty] private string _templateMessage = "";
    [ObservableProperty] private string _templatePreview = "";
    [ObservableProperty] private bool _isOpeningTemplate;

    private bool _loadingTemplate;
    private DispatcherTimer? _previewTimer;

    partial void OnTemplateNameChanged(string value)
    {
        if (!_loadingTemplate) ScheduleTemplatePreview();
    }

    partial void OnSelectedTemplateChanged(WorkspaceTemplate? value)
    {
        _loadingTemplate = true;
        try
        {
            TemplateSessions.Clear();
            TemplateName = value?.Name ?? WorkspaceName;
            if (value is not null)
                foreach (var session in value.Layout.Sessions)
                {
                    var editor = new WorkspaceSessionEditor(session);
                    editor.PropertyChanged += (_, _) => ScheduleTemplatePreview();
                    TemplateSessions.Add(editor);
                }
            TemplateMessage = "";
        }
        finally { _loadingTemplate = false; }
        RefreshTemplatePreview();
    }

    private void ScheduleTemplatePreview()
    {
        if (_disposed) return;
        _previewTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _previewTimer.Tick -= OnPreviewTick;
        _previewTimer.Tick += OnPreviewTick;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void OnPreviewTick(object? sender, EventArgs e)
    {
        _previewTimer?.Stop();
        if (!_disposed) RefreshTemplatePreview();
    }

    private void StopTemplatePreview()
    {
        if (_previewTimer is null) return;
        _previewTimer.Stop();
        _previewTimer.Tick -= OnPreviewTick;
    }

    [RelayCommand]
    private void RefreshTemplatePreview()
    {
        if (SelectedTemplate is null) { TemplatePreview = ""; return; }
        var draft = new WorkspaceTemplate
        {
            Name = string.IsNullOrWhiteSpace(TemplateName) ? SelectedTemplate.Name : TemplateName.Trim(),
            Layout = new WorkspaceState { Sessions = TemplateSessions.Select(session => session.Snapshot()).ToList() }
        };
        TemplatePreview = WorkspaceTemplateTransfer.Describe(draft, Directory.Exists, command =>
            string.IsNullOrWhiteSpace(command) || ShellDiscovery.Exists(command));
    }

    [RelayCommand]
    public void SaveCurrentAsTemplate()
    {
        var name = TemplateName.Trim();
        if (name.Length == 0) { TemplateMessage = "请填写模板名称。"; return; }
        var layout = CaptureWorkspace();
        if (layout.Sessions.Count == 0) { TemplateMessage = "先打开一个终端，再保存模板。"; return; }
        var template = new WorkspaceTemplate { Name = name, Layout = layout };
        WorkspaceTemplates.Add(template);
        SelectedTemplate = template;
        PersistTemplates();
        TemplateMessage = "已保存当前布局为新模板。";
    }

    [RelayCommand]
    public void SaveTemplateChanges()
    {
        if (SelectedTemplate is not { } template) return;
        if (string.IsNullOrWhiteSpace(TemplateName)) { TemplateMessage = "请填写模板名称。"; return; }
        template.Name = TemplateName.Trim();
        template.Layout.Sessions = TemplateSessions.Select(s => s.Snapshot()).ToList();
        PersistTemplates();
        RefreshTemplatePreview();
        TemplateMessage = "已保存名称与终端配置。";
    }

    [RelayCommand]
    public void UpdateTemplateLayout()
    {
        if (SelectedTemplate is not { } template) return;
        var layout = CaptureWorkspace();
        if (layout.Sessions.Count == 0) { TemplateMessage = "当前没有可保存的终端。"; return; }
        template.Layout = layout;
        OnSelectedTemplateChanged(template);
        PersistTemplates();
        TemplateMessage = "已用当前终端及分屏更新模板。";
    }

    [RelayCommand]
    public void DeleteTemplate()
    {
        if (SelectedTemplate is not { } template) return;
        WorkspaceTemplates.Remove(template);
        SelectedTemplate = WorkspaceTemplates.FirstOrDefault();
        PersistTemplates();
        TemplateMessage = "已删除模板，运行中的终端继续保留。";
    }

    [RelayCommand]
    public async Task OpenTemplateAsync(WorkspaceTemplate? template)
    {
        template ??= SelectedTemplate;
        if (template is null || IsOpeningTemplate) return;
        var live = WorkspaceTemplates.FirstOrDefault(item => item.Id == template.Id);
        if (live is not null)
        {
            if (ReferenceEquals(live, SelectedTemplate)
                && TemplateSessions.Count > 0
                && TemplateSessions.Count == live.Layout.Sessions.Count)
            {
                if (!string.IsNullOrWhiteSpace(TemplateName)) live.Name = TemplateName.Trim();
                live.Layout.Sessions = TemplateSessions.Select(session => session.Snapshot()).ToList();
            }
            live.LastUsed = DateTimeOffset.Now;
            var index = WorkspaceTemplates.IndexOf(live);
            if (index > 0) WorkspaceTemplates.Move(index, 0);
            template = live;
        }
        IsOpeningTemplate = true;
        try
        {
            await RestoreWorkspaceAsync(template.Layout, runStartupCommands: true);
            WorkspaceNameLive = template.Name;
            // Session cards are posted from SessionAdded. Saving here would
            // snapshot an empty shelf if those posts have not run yet.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed) PersistSettings();
            });
            TemplateMessage = $"已打开 {template.Name}；原有终端继续运行。";
        }
        finally { IsOpeningTemplate = false; }
    }

    [RelayCommand]
    public void DuplicateTemplate()
    {
        if (SelectedTemplate is null) { TemplateMessage = "先选择一个模板。"; return; }
        var copy = WorkspaceTemplateTransfer.Clone(SelectedTemplate);
        var index = Math.Min(WorkspaceTemplates.IndexOf(SelectedTemplate) + 1, WorkspaceTemplates.Count);
        WorkspaceTemplates.Insert(index, copy);
        SelectedTemplate = copy;
        PersistTemplates();
        TemplateMessage = "已复制模板。启动命令不会执行，可以改目录后再打开。";
    }

    public string ExportSelectedTemplateJson()
    {
        if (SelectedTemplate is null) return "";
        return WorkspaceTemplateTransfer.ToJson(SelectedTemplate);
    }

    public void ImportTemplateJson(string json)
    {
        if (!WorkspaceTemplateTransfer.TryParse(json, out var template, out var error) || template is null)
        {
            TemplateMessage = error;
            return;
        }
        WorkspaceTemplates.Insert(0, template);
        SelectedTemplate = template;
        PersistTemplates();
        TemplateMessage = "已导入模板，没有执行启动命令。请检查目录和 Shell，再按预览打开。";
    }

    private void PersistTemplates()
    {
        _settings.WorkspaceTemplates = WorkspaceTemplates.ToList();
        SaveSettingsInternal();
    }
}
