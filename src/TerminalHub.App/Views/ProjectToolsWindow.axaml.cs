using Avalonia.Controls;
using Avalonia.Data.Converters;
using TerminalHub.App.Localization;
using TerminalHub.App.ViewModels;
namespace TerminalHub.App.Views;
public partial class ProjectToolsWindow : Window
{
    public static readonly FuncValueConverter<int, bool> IsEmpty = ProjectToolsView.IsEmpty;
    public ProjectToolsWindow() { InitializeComponent(); }
    public ProjectToolsWindow(MainWindow owner, MainWindowViewModel vm)
        : this(owner, vm, new ProjectToolsView(owner, vm)) { }
    public ProjectToolsWindow(MainWindow owner, MainWindowViewModel vm, ProjectToolsView view) : this()
    {
        DataContext = vm; Content = view;
        Closed += (_, _) => { vm.SaveProjectToolsCommand.Execute(null); Content = null; };
    }
}
