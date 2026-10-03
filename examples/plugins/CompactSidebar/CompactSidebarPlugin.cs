using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using TerminalHub.Extensibility;
public sealed class CompactSidebarPlugin : IWorkbenchPlugin
{
    public void Initialize(IPluginContext context)
    {
        context.RegisterLocalization("en", new Dictionary<string, IReadOnlyDictionary<string,string>>
        {
            ["en"] = new Dictionary<string,string> { ["compact"] = "Compact sessions" },
            ["zh-CN"] = new Dictionary<string,string> { ["compact"] = "紧凑会话" }
        });
        context.RegisterStyles(new Style(x => x.OfType<Border>().Class("workspace-tab"))
            { Setters = { new Setter(Border.CornerRadiusProperty,new CornerRadius(14)), new Setter(Border.PaddingProperty,new Thickness(3)) } });
        context.RegisterStyles(new Style(x => x.OfType<ListBox>().Name("SessionShelf"))
            { Setters = { new Setter(TemplatedControl.PaddingProperty,new Thickness(4)) } });
        context.RegisterView(new("compact", "Compact sessions", ExtensionSurface.Sidebar, Replace:true), () =>
        {
            var list = new ListBox();
            void Refresh() => list.ItemsSource = context.Host.Sessions.Where(s => s.WorkspaceId == context.Host.ActiveWorkspaceId)
                .Select(s => new SessionRow(s.Id,s.Name)).ToArray();
            list.SelectionChanged += (_, _) => { if (list.SelectedItem is SessionRow row) context.Host.ActivateSession(row.Id); };
            context.Subscribe(e => { if (e.Kind is WorkbenchEventKind.SessionCreated or WorkbenchEventKind.SessionClosed or WorkbenchEventKind.WorkspaceChanged) Refresh(); });
            Refresh(); return list;
        });
    }
    private sealed record SessionRow(Guid Id,string Name) { public override string ToString() => Name; }
    public void Deactivate() { }
}
