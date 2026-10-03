using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TerminalHub.Extensibility;
public sealed class SessionPanelPlugin : IWorkbenchPlugin
{
    public sealed record Options(bool ShowDirectory = true);
    public void Initialize(IPluginContext context)
    {
        context.RegisterLocalization("en", new Dictionary<string,IReadOnlyDictionary<string,string>>
        {
            ["en"] = new Dictionary<string,string> { ["sessions"] = "Session overview", ["settings"] = "Session overview settings", ["create"] = "New terminal", ["directories"] = "Show directories" },
            ["zh-CN"] = new Dictionary<string,string> { ["sessions"] = "会话概览", ["settings"] = "会话概览设置", ["create"] = "新建终端", ["directories"] = "显示目录" }
        });
        const string icon = "<svg viewBox='0 0 24 24'><path d='M2 3H22V21H2Z M4 5V19H20V5Z M6 8L10 12L6 16L7 17L12 12L7 7Z M13 15H18V17H13Z'/></svg>";
        var options = context.ReadConfiguration<Options>() ?? new();
        TextBlock? summary = null; Button? create = null; CheckBox? showDirectories = null;
        void Refresh()
        {
            if (summary is not null) summary.Text = string.Join("\n", context.Host.Sessions.Select(s => s.Name + " · " + s.WorkspaceId + (options.ShowDirectory ? "\n" + s.WorkingDirectory : "")));
            if (create is not null) create.Content = context.Text("create","New terminal");
            if (showDirectories is not null) showDirectories.Content = context.Text("directories","Show directories");
        }
        context.RegisterView(new("sessions","Session overview",IconSvg:icon), () =>
        {
            var panel = new StackPanel { Spacing = 12, Margin = new(16) };
            summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
            summary.Bind(TextBlock.ForegroundProperty,new DynamicResourceExtension("UiInk"));
            create = new Button(); create.Click += async (_, _) => { try { await context.Host.CreateSessionAsync(new()); } catch (Exception ex) { context.ReportError(ex); } };
            panel.Children.Add(create); panel.Children.Add(summary); Refresh(); return panel;
        });
        context.RegisterView(new("settings","Session overview settings",ExtensionSurface.Settings), () =>
        {
            showDirectories = new CheckBox { IsChecked = options.ShowDirectory };
            showDirectories.IsCheckedChanged += (_, _) => { options = new(showDirectories.IsChecked == true); context.SaveConfiguration(options); Refresh(); };
            Refresh(); return showDirectories;
        });
        context.RegisterCommand(new("create","Plugin: new terminal",async () => { await context.Host.CreateSessionAsync(new()); },"Ctrl+Shift+F8"));
        context.Subscribe(e => { if (e.Kind is not WorkbenchEventKind.OutputBatch) Refresh(); });
        // OutputBatch carries session IDs, at most once per 100 ms; request a frame only when needed.
        context.Subscribe(e => { if (e.Kind == WorkbenchEventKind.OutputBatch && e.Data is Guid[] ids && ids.Contains(context.Host.ActiveSessionId ?? Guid.Empty)) _ = context.Host.ReadFrame(ids[0]); });
        Refresh();
    }
    public void Deactivate() { }
}
