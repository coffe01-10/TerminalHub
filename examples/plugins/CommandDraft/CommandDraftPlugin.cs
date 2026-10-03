using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TerminalHub.Extensibility;

namespace TerminalHub.Example.CommandDraft;

public sealed class CommandDraftPlugin : IWorkbenchPlugin
{
    public sealed record Options(string Draft = "echo hello");

    private IPluginContext _context = null!;
    private Options _options = new();
    private TextBlock? _sessionLabel;
    private TextBox? _editor;
    private Button? _pasteButton, _saveButton;

    public void Initialize(IPluginContext context)
    {
        _context = context;
        _options = context.ReadConfiguration<Options>() ?? new();

        context.RegisterView(new("draft", "命令草稿", ExtensionSurface.WorkspaceTools), CreateView);
        context.RegisterCommand(new("paste-draft", "粘贴命令草稿", () =>
        {
            PasteDraft();
            return Task.CompletedTask;
        }));
        context.Subscribe(e =>
        {
            if (e.Kind is WorkbenchEventKind.ActiveSessionChanged
                or WorkbenchEventKind.WorkspaceChanged
                or WorkbenchEventKind.SessionCreated
                or WorkbenchEventKind.SessionClosed)
                RefreshSession();
        });
    }

    private Control CreateView()
    {
        var panel = new StackPanel { Margin = new(20), Spacing = 12 };
        var title = new TextBlock { Text = "命令草稿", FontSize = 22 };
        title.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiInk"));
        panel.Children.Add(title);

        _sessionLabel = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _sessionLabel.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiMuted"));
        panel.Children.Add(_sessionLabel);

        _editor = new TextBox { Text = _options.Draft, Watermark = "例如：echo hello" };
        _editor.Bind(TextBox.ForegroundProperty, new DynamicResourceExtension("UiInk"));
        _editor.Bind(TextBox.BackgroundProperty, new DynamicResourceExtension("UiInset"));
        _editor.Bind(TextBox.BorderBrushProperty, new DynamicResourceExtension("UiBorder"));
        panel.Children.Add(_editor);

        var actions = new WrapPanel();
        _pasteButton = ActionButton("粘贴到活动终端", PasteDraft);
        _saveButton = ActionButton("保存草稿", SaveDraft);
        actions.Children.Add(_pasteButton); actions.Children.Add(_saveButton);
        panel.Children.Add(actions);

        var hint = new TextBlock { Text = "粘贴不会发送回车。保存后，重新启用插件可恢复草稿。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        hint.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("UiMuted"));
        panel.Children.Add(hint);
        RefreshSession();
        return panel;
    }

    private Button ActionButton(string title, Action action)
    {
        var button = new Button { Content = title, Padding = new(12, 8), Margin = new(0, 0, 8, 8), CornerRadius = new(6) };
        button.Bind(Button.ForegroundProperty, new DynamicResourceExtension("UiInk"));
        button.Bind(Button.BackgroundProperty, new DynamicResourceExtension("UiRaised"));
        button.Bind(Button.BorderBrushProperty, new DynamicResourceExtension("UiBorder"));
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { _context.ReportError(ex); }
        };
        return button;
    }

    private void RefreshSession()
    {
        var session = _context.Host.Sessions.FirstOrDefault(s => s.Id == _context.Host.ActiveSessionId);
        if (_sessionLabel is not null)
            _sessionLabel.Text = session is null ? "没有活动会话" : $"目标：{session.Name}\n{session.WorkingDirectory}";
        if (_pasteButton is not null) _pasteButton.IsEnabled = session?.Running == true;
    }

    private void PasteDraft()
    {
        var session = _context.Host.Sessions.FirstOrDefault(s => s.Id == _context.Host.ActiveSessionId);
        if (session?.Running != true) return;
        _context.Host.SendInput(session.Id, _editor?.Text ?? _options.Draft, submit: false);
    }

    private void SaveDraft()
    {
        _options = new(_editor?.Text ?? _options.Draft);
        _context.SaveConfiguration(_options);
    }

    public void Deactivate() { }
}
