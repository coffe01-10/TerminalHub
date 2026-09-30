using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [ObservableProperty] private bool _isOpeningTemplate;

    partial void OnSelectedTemplateChanged(WorkspaceTemplate? value)
    {
        TemplateSessions.Clear();
        TemplateName = value?.Name ?? WorkspaceName;
        if (value is not null)
            foreach (var session in value.Layout.Sessions) TemplateSessions.Add(new(session));
        TemplateMessage = "";
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

    private void PersistTemplates()
    {
        _settings.WorkspaceTemplates = WorkspaceTemplates.ToList();
        SaveSettingsInternal();
    }
}
