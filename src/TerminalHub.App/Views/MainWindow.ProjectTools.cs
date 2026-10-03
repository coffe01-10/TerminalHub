using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using TerminalHub.App.ViewModels;

namespace TerminalHub.App.Views;

public partial class MainWindow
{
    private ProjectToolsView? _projectToolsView;
    private ProjectToolsWindow? _projectToolsWindow;
    private void OnProjectTools(object? sender, RoutedEventArgs e) => ToggleProjectTools();
    public void CollapseProjectTools()
    {
        Vm.SaveProjectToolsCommand.Execute(null);
        _projectToolsWindow?.Close();
        ActiveTerminal()?.Focus();
    }
    private void ToggleProjectTools()
    {
        EnsureToolModules();
        if (_projectToolsWindow is not null) { _projectToolsWindow.Activate(); return; }
        var window = new ProjectToolsWindow(this, Vm, _projectToolsView!);
        window.Closed += (_, _) => _projectToolsWindow = null;
        _projectToolsWindow = window;
        window.Show(this);
    }
    private void OnSwitchWorkspace(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not LiveWorkspace workspace) return;
        Vm.SwitchProjectWorkspace(workspace);
        ActiveTerminal()?.Focus();
    }
    private void OnCloseWorkspace(object? sender, RoutedEventArgs e)
    { if ((sender as Button)?.Tag is LiveWorkspace workspace) Vm.CloseProjectWorkspaceCommand.Execute(workspace); }
    private async void OnRenameWorkspace(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not LiveWorkspace workspace) return;
        var input = new TextBox { Text = workspace.Name, Margin = new(20) };
        var save = new Button { [!Avalonia.Controls.ContentControl.ContentProperty] = TerminalHub.App.Localization.UiText.Binding("保存"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new(20,0,20,20) };
        var dialog = new Window { [!Avalonia.Controls.Window.TitleProperty] = TerminalHub.App.Localization.UiText.Binding("重命名工作区"), Width = 380, Height = 180, CanResize = false,
            Content = new StackPanel { Children = { input, save } } };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) { workspace.Name = input.Text.Trim(); Vm.SaveProjectToolsCommand.Execute(null); dialog.Close(); } };
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); }; await dialog.ShowDialog(this);
    }
}
