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
            ["en"] = new Dictionary<string,string> { ["sessions"] = "Session overview", ["settings"] = "Session overview settings", ["create"] = "New terminal", ["directories"] = "Show directories", ["count"] = "{0} terminals", ["running"] = "Running", ["stopped"] = "Exited" },
            ["zh-CN"] = new Dictionary<string,string> { ["sessions"] = "会话概览", ["settings"] = "会话概览设置", ["create"] = "新建终端", ["directories"] = "显示目录", ["count"] = "{0} 个终端", ["running"] = "运行中", ["stopped"] = "已退出" }
        });
        const string icon = "<svg viewBox='0 0 24 24'><path d='M2 3H22V21H2Z M4 5V19H20V5Z M6 8L10 12L6 16L7 17L12 12L7 7Z M13 15H18V17H13Z'/></svg>";
        var options = context.ReadConfiguration<Options>() ?? new();
        TextBlock? heading = null; TextBlock? summary = null; StackPanel? sessions = null; Button? create = null; CheckBox? showDirectories = null;
        void Refresh()
        {
            var items = context.Host.Sessions;
            if (heading is not null) heading.Text = context.Text("sessions", "Session overview");
            if (summary is not null) summary.Text = string.Format(context.Text("count", "{0} terminals"), items.Count);
            if (create is not null) create.Content = context.Text("create", "New terminal");
            if (showDirectories is not null) showDirectories.Content = context.Text("directories", "Show directories");
            if (sessions is null) return;
            sessions.Children.Clear();
            var workspaces = context.Host.Workspaces.ToDictionary(w => w.Id, w => w.Name);
            foreach (var session in items)
            {
                var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 16 };
                var labels = new StackPanel { Spacing = 5 };
                var name = new TextBlock { Text = session.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                name.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiInk")); labels.Children.Add(name);
                var workspace = new TextBlock { Text = workspaces.GetValueOrDefault(session.WorkspaceId, ""), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
                workspace.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiMuted")); labels.Children.Add(workspace);
                if (options.ShowDirectory)
                {
                    var path = new TextBlock { Text = session.WorkingDirectory, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
                    path.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiFaint")); ToolTip.SetTip(path, session.WorkingDirectory); labels.Children.Add(path);
                }
                row.Children.Add(labels);
                var status = new TextBlock { Text = context.Text(session.Running ? "running" : "stopped", session.Running ? "Running" : "Exited"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                status.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension(session.Running ? "UiGood" : "UiMuted")); Grid.SetColumn(status, 1); row.Children.Add(status);
                var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(16,12), CornerRadius = new(8), BorderThickness = new(1) };
                button.Bind(Button.BackgroundProperty, new DynamicResourceExtension("UiSurface"));
                button.Bind(Button.BorderBrushProperty, new DynamicResourceExtension(context.Host.ActiveSessionId == session.Id ? "UiAccent" : "UiBorder"));
                button.Click += (_, _) => context.Host.ActivateSession(session.Id); sessions.Children.Add(button);
            }
        }
        context.RegisterView(new("sessions", "Session overview", IconSvg: icon), () =>
        {
            var panel = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 22, Margin = new(16) };
            var top = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 20 };
            var labels = new StackPanel { Spacing = 7 };
            heading = new TextBlock { FontSize = 23, FontWeight = FontWeight.SemiBold };
            heading.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiInk")); labels.Children.Add(heading);
            summary = new TextBlock { FontSize = 12 };
            summary.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiMuted")); labels.Children.Add(summary); top.Children.Add(labels);
            create = new Button { Classes = { "primary" }, VerticalAlignment = VerticalAlignment.Center, Padding = new(14,9), CornerRadius = new(7) };
            create.Click += async (_, _) => { try { await context.Host.CreateSessionAsync(new()); } catch (Exception ex) { context.ReportError(ex); } };
            Grid.SetColumn(create,1); top.Children.Add(create); panel.Children.Add(top);
            sessions = new StackPanel { Spacing = 8 };
            var scroll = new ScrollViewer { Content = sessions, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            Grid.SetRow(scroll,1); panel.Children.Add(scroll); Refresh(); return panel;
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
