using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using TerminalHub.App.ViewModels;

namespace TerminalHub.App.Views;

public partial class MainWindow
{
    private ProjectToolsWindow? _projectTools;
    private void OnProjectTools(object? sender, RoutedEventArgs e)
    {
        if (_projectTools is not null) { _projectTools.Activate(); return; }
        _projectTools = new(this, Vm); _projectTools.Closed += (_, _) => _projectTools = null; _projectTools.Show(this);
    }
    private void OnSwitchWorkspace(object? sender, RoutedEventArgs e)
    { if ((sender as Button)?.Tag is LiveWorkspace workspace) Vm.SwitchProjectWorkspace(workspace); }
    private void OnCloseWorkspace(object? sender, RoutedEventArgs e)
    { if ((sender as Button)?.Tag is LiveWorkspace workspace) Vm.CloseProjectWorkspaceCommand.Execute(workspace); }
    private async void OnRenameWorkspace(object? sender, TappedEventArgs e)
    {
        if ((sender as Button)?.Tag is not LiveWorkspace workspace) return;
        var input = new TextBox { Text = workspace.Name, Margin = new(20) };
        var save = new Button { Content = "保存", HorizontalAlignment = HorizontalAlignment.Right, Margin = new(20,0,20,20) };
        var dialog = new Window { Title = "重命名工作区", Width = 380, Height = 180, CanResize = false,
            Content = new StackPanel { Children = { input, save } } };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) { workspace.Name = input.Text.Trim(); Vm.SaveProjectToolsCommand.Execute(null); dialog.Close(); } };
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); }; await dialog.ShowDialog(this);
    }
}
