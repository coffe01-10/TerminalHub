namespace TerminalHub.App.Views;
public partial class MainWindow
{
    private void UpdateSplitLayout() => RenderPaneTree();
    // Legacy splitter hooks stay inert; each tree branch saves its own proportion.
    private void SavePaneRatios() { }
}
