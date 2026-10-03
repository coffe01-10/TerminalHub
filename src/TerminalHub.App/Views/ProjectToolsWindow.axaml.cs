using Avalonia.Controls;
using Avalonia.Data.Converters;
using TerminalHub.App.Localization;
using TerminalHub.App.ViewModels;
namespace TerminalHub.App.Views;
public partial class ProjectToolsWindow : Window
{
    public static readonly FuncValueConverter<int, bool> IsEmpty = ProjectToolsView.IsEmpty;
    public ProjectToolsWindow() { InitializeComponent(); }
    public ProjectToolsWindow(MainWindow owner, MainWindowViewModel vm) : this()
    { DataContext = vm; Content = new ProjectToolsView(owner, vm); Closed += (_, _) => vm.SaveProjectToolsCommand.Execute(null); }
}
