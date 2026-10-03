using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
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
            var list = new ListBox { Name = "CompactSessionList", Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(4), HorizontalAlignment = HorizontalAlignment.Stretch };
            list.Styles.Add(new Style(x => x.OfType<ListBoxItem>()) { Setters = { new Setter(TemplatedControl.PaddingProperty,new Thickness(12,10)), new Setter(TemplatedControl.MarginProperty,new Thickness(0,3)), new Setter(TemplatedControl.CornerRadiusProperty,new CornerRadius(7)), new Setter(ContentControl.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch) } });
            list.ItemTemplate = new FuncDataTemplate<SessionRow>((row, _) =>
            {
                var label = new TextBlock { Text = row?.Name, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
                label.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiInk")); return label;
            });
            var refreshing = false;
            void Refresh()
            {
                refreshing = true;
                try
                {
                    var rows = context.Host.Sessions.Where(s => s.WorkspaceId == context.Host.ActiveWorkspaceId).Select(s => new SessionRow(s.Id, s.Name)).ToArray();
                    list.ItemsSource = rows; list.SelectedItem = rows.FirstOrDefault(s => s.Id == context.Host.ActiveSessionId);
                }
                finally { refreshing = false; }
            }
            list.SelectionChanged += (_, _) => { if (!refreshing && list.SelectedItem is SessionRow row) context.Host.ActivateSession(row.Id); };
            context.Subscribe(e => { if (e.Kind is WorkbenchEventKind.SessionCreated or WorkbenchEventKind.SessionClosed or WorkbenchEventKind.WorkspaceChanged or WorkbenchEventKind.ActiveSessionChanged) Refresh(); });
            Refresh(); return list;
        });
    }
    private sealed record SessionRow(Guid Id,string Name) { public override string ToString() => Name; }
    public void Deactivate() { }
}
