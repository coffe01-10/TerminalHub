using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using TerminalHub.App.Plugins;
using TerminalHub.Core.Settings;
using TerminalHub.Extensibility;

namespace TerminalHub.App.Views;

public partial class MainWindow
{
    private readonly List<ListBoxItem> _builtinInspectorTabs = [];
    private readonly List<Button> _pluginDockButtons = [];
    private bool _updatingWorkbench, _closingWorkbench;
    private string? _inspectorWorkbenchId;
    private int _lastBuiltinInspectorTab;

    private void InitializeWorkbenchDock()
    {
        _builtinInspectorTabs.AddRange(InspectorTabs.Items.Cast<ListBoxItem>());
        _lastBuiltinInspectorTab = Vm.SelectedRightTab;
        Vm.PropertyChanged += OnWorkbenchInspectorStateChanged;
        SizeChanged += (_, _) => ActionDock.MaxWidth = Math.Max(280, Bounds.Width - 48);
    }
    private void MarkToolOpen(ModuleRegistration selected)
    {
        var standalone = _plugins.Settings(selected).Placement == ToolPlacement.Window;
        // Bottom shortcuts and right tabs share the existing inspector; it displays one page.
        foreach (var module in _plugins.Modules.Where(m => m.Definition.Surface == ExtensionSurface.WorkspaceTools
            && (_plugins.Settings(m).Placement == ToolPlacement.Window) == standalone))
            _plugins.Settings(module).WasOpen = module.Id == selected.Id;
    }
    internal void RememberWindowTool(string id)
    {
        if (_projectToolsWindow is not null && _plugins.Modules.FirstOrDefault(m => m.Id == id) is { } module)
        { MarkToolOpen(module); Vm.SavePluginPreferences(); }
    }
    public bool IsWorkbenchModuleOpen(ModuleRegistration module)
        => _projectToolsWindow is not null && _projectToolsView?.SelectedModuleId == module.Id
            || Vm.InspectorVisible && _inspectorWorkbenchId == module.Id && Vm.SelectedRightTab >= _builtinInspectorTabs.Count;
    public void MoveWorkbenchModule(ModuleRegistration module, ToolPlacement placement)
    {
        var open = IsWorkbenchModuleOpen(module);
        _plugins.Settings(module).Placement = placement; _plugins.SaveModules();
        if (open) OpenWorkbenchModule(module);
    }
    public void OpenWorkbenchModule(ModuleRegistration module)
    {
        EnsureToolModules();
        var state = _plugins.Settings(module);
        state.Visible = true; state.Workspaces[Vm.ActiveWorkspace.Id] = true; MarkToolOpen(module);
        if (state.Placement != ToolPlacement.Window)
        { _inspectorWorkbenchId = module.Id; Vm.InspectorVisible = true; }
        _plugins.SaveModules();
        if (state.Placement == ToolPlacement.Window) { ToggleProjectTools(); _projectToolsView!.SelectModule(module.Id); }
    }
    public void RestoreWorkbenchTools()
    {
        EnsureToolModules();
        var requested = _plugins.Visible(ExtensionSurface.WorkspaceTools).Where(module =>
        {
            var state = _plugins.Settings(module);
            return state.Startup == ToolStartup.OnLaunch || state.Startup == ToolStartup.RestoreLast && state.WasOpen;
        }).ToArray();
        foreach (var module in requested) OpenWorkbenchModule(module);
    }
    private void OnWorkbenchInspectorClose(object? sender, RoutedEventArgs e)
    { CloseWorkbenchInspector(); ActiveTerminal()?.Focus(); }
    private void OnWorkbenchInspectorPopout(object? sender, RoutedEventArgs e)
    {
        if (_plugins.Modules.FirstOrDefault(m => m.Id == _inspectorWorkbenchId) is { } module)
            MoveWorkbenchModule(module, ToolPlacement.Window);
    }
    private void CloseWorkbenchInspector()
    {
        if (_inspectorWorkbenchId is { } id && Vm.ModulePreferences.TryGetValue(id, out var state)) state.WasOpen = false;
        _inspectorWorkbenchId = null;
        RefreshWorkbenchDock(); Vm.SavePluginPreferences();
    }
    private void OnWorkbenchInspectorStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingWorkbench || _closingWorkbench) return;
        if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.SelectedRightTab))
        {
            if (Vm.SelectedRightTab >= 0 && Vm.SelectedRightTab < _builtinInspectorTabs.Count)
            {
                _lastBuiltinInspectorTab = Vm.SelectedRightTab;
                if (_inspectorWorkbenchId is not null) CloseWorkbenchInspector();
                else UpdateWorkbenchHighlight();
            }
            else if ((InspectorTabs.SelectedItem as ListBoxItem)?.Tag is string id
                && _plugins.Modules.FirstOrDefault(m => m.Id == id) is { } module)
            {
                _inspectorWorkbenchId = id; MarkToolOpen(module);
                InspectorWorkbenchContent.Content = module.GetView(); InspectorWorkbenchContent.IsVisible = true;
                WorkbenchInspectorActions.IsVisible = true; Vm.SavePluginPreferences(); UpdateWorkbenchHighlight();
            }
        }
        if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.InspectorVisible) && !Vm.InspectorVisible && _inspectorWorkbenchId is not null)
            CloseWorkbenchInspector();
        if (e.PropertyName == nameof(ViewModels.MainWindowViewModel.SettingsOpen)) UpdateWorkbenchHighlight();
    }
    private void UpdateWorkbenchHighlight()
    {
        foreach (var button in _pluginDockButtons)
            button.Classes.Set("dock-active", Vm.InspectorVisible && !Vm.SettingsOpen && Equals(button.Tag, _inspectorWorkbenchId));
    }
    private Button WorkbenchDockButton(ModuleRegistration module)
    {
        var button = new Button { Name = "Launch-" + module.Id, Tag = module.Id, Classes = { "dock" } };
        var content = new StackPanel(); Control icon;
        if (module.Definition.IconSvg is { } svg)
        {
            try
            {
                var vector = svg.TrimStart().StartsWith('<') ? new SvgIcon(svg)
                    : SvgIcon.FromFile(Path.Combine(_plugins.Plugins.First(p => p.Manifest.Id == module.Owner).Directory, svg));
                vector.Width = vector.Height = 20; vector.Bind(SvgIcon.ForegroundProperty, new Binding(nameof(Button.Foreground)) { Source = button });
                icon = vector;
            }
            catch { icon = new TextBlock { Text = "◇", FontSize = 20 }; }
        }
        else icon = new TextBlock { Text = "◇", FontSize = 20 };
        icon.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(icon);
        content.Children.Add(new TextBlock { Text = module.ToString(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 84, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0,5,0,0) });
        button.Content = content; ToolTip.SetTip(button, module.ToString());
        button.Click += (_, _) => OpenWorkbenchModule(module); return button;
    }
    private void RefreshWorkbenchDock()
    {
        var modules = _plugins.Visible(ExtensionSurface.WorkspaceTools).ToArray();
        var active = modules.FirstOrDefault(m => m.Id == _inspectorWorkbenchId && _plugins.Settings(m).Placement != ToolPlacement.Window);
        _updatingWorkbench = true;
        try
        {
            foreach (var button in _pluginDockButtons) ActionDockItems.Children.Remove(button);
            _pluginDockButtons.Clear();
            foreach (var module in modules.Where(m => _plugins.Settings(m).Placement == ToolPlacement.Bottom && _plugins.Settings(m).ShowLauncher))
            {
                var button = WorkbenchDockButton(module); _pluginDockButtons.Add(button); ActionDockItems.Children.Add(button);
            }
            if (active is null)
            {
                if (_inspectorWorkbenchId is { } id && Vm.ModulePreferences.TryGetValue(id, out var state)) state.WasOpen = false;
                _inspectorWorkbenchId = null;
            }
            // Keep the original containers in place: resetting them inside a selection
            // notification can re-add a ListBoxItem before Avalonia has removed its visual parent.
            while (InspectorTabs.Items.Count > _builtinInspectorTabs.Count) InspectorTabs.Items.RemoveAt(InspectorTabs.Items.Count - 1);
            foreach (var module in modules.Where(m => _plugins.Settings(m).Placement == ToolPlacement.Right && _plugins.Settings(m).ShowLauncher || m == active))
            {
                var tab = new ListBoxItem { Name = "InspectorTab-" + module.Id, Tag = module.Id,
                    Content = new TextBlock { Text = module.ToString(), MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis } };
                ToolTip.SetTip(tab, module.ToString()); InspectorTabs.Items.Add(tab);
            }
            var selected = active is null ? _lastBuiltinInspectorTab
                : InspectorTabs.Items.Cast<ListBoxItem>().ToList().FindIndex(tab => Equals(tab.Tag, active.Id));
            InspectorTabs.SelectedIndex = selected; Vm.SelectedRightTab = selected;
            InspectorWorkbenchContent.Content = active?.GetView(); InspectorWorkbenchContent.IsVisible = active is not null;
            WorkbenchInspectorActions.IsVisible = active is not null; UpdateWorkbenchHighlight();
        }
        finally { _updatingWorkbench = false; }
    }
}
