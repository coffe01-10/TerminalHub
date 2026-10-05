using Avalonia.Controls;
using Avalonia.Layout;
using TerminalHub.Extensibility;

namespace TerminalHub.Official.Snippets;

/// <summary>Saved one-liners pasted into the active session on click —
/// the answer to "what was that kubectl incantation again". Pasted without
/// Enter so the user reviews before running; deletion is one click.</summary>
public sealed class SnippetsPlugin : IWorkbenchPlugin
{
    public sealed record Entry(string Name, string Command);
    public sealed record Options(List<Entry> Items);
    private IPluginContext? _context;
    private PluginUi _ui = null!;
    private Options _options = new([]);
    private TextBox? _name, _command;
    private StackPanel? _list;
    private TextBlock? _status;

    public void Initialize(IPluginContext context)
    {
        _context = context; _ui = new(context);
        _options = context.ReadConfiguration<Options>() ?? new([]);
        _ui.Languages(("snippets", "命令片段", "Command snippets"),
            ("detail", "常用命令一键粘贴进当前会话；不自动回车。", "Paste saved commands into the active session; Enter is never pressed for you."),
            ("name", "名称", "Name"), ("command", "命令", "Command"),
            ("add", "保存片段", "Save snippet"), ("apply", "粘贴", "Paste"), ("remove", "删除", "Remove"),
            ("need", "名称和命令都要填", "Name and command are both required"),
            ("noactive", "没有活动会话", "No active session"),
            ("added", "已保存", "Saved"), ("none", "还没有片段", "No snippets yet"));
        context.RegisterView(new("snippets", "命令片段", Order: 230,
            IconSvg: "<svg viewBox='0 0 24 24'><path d='M4 4H20V6H4Z M4 10H16V12H4Z M4 16H18V18H4Z'/></svg>"), CreateView);
    }

    private Control CreateView()
    {
        _name = new TextBox { Name = "SnippetName", Padding = new(10, 8), CornerRadius = new(7) };
        _command = new TextBox { Name = "SnippetCommand", Padding = new(10, 8), CornerRadius = new(7) };
        var namePh = _name; var cmdPh = _command;
        void UpdateWatermarks() { namePh.Watermark = _ui.T("name", "名称"); cmdPh.Watermark = _ui.T("command", "命令"); }
        UpdateWatermarks();
        _status = _ui.Label(brush: "UiMuted", size: 11);
        _list = new StackPanel { Spacing = 6 };
        var editors = new Grid { ColumnDefinitions = new("*,2*,Auto"), ColumnSpacing = 8 };
        editors.Children.Add(_name); Grid.SetColumn(_command, 1); editors.Children.Add(_command);
        var add = _ui.Button("SnippetAdd", "add", "保存片段", Add, primary: true);
        Grid.SetColumn(add, 2); editors.Children.Add(add);
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(editors); body.Children.Add(_list); body.Children.Add(_status);
        RefreshList();
        var page = _ui.Page("snippets", "命令片段", "detail", "常用命令一键粘贴进当前会话；不自动回车。", body);
        _context!.Subscribe(e => { if (e.Kind == WorkbenchEventKind.LanguageChanged) { _ui.Translate(); UpdateWatermarks(); RefreshList(); } });
        return page;
    }

    private void Add()
    {
        if (_context is null) return;
        var name = (_name?.Text ?? "").Trim();
        var command = (_command?.Text ?? "").Trim();
        if (name.Length == 0 || command.Length == 0) { _status!.Text = _ui.T("need", "名称和命令都要填"); return; }
        _options.Items.RemoveAll(i => i.Name == name);
        _options.Items.Add(new(name, command));
        _context.SaveConfiguration(_options);
        _name!.Text = ""; _command!.Text = "";
        _status!.Text = _ui.T("added", "已保存");
        RefreshList();
    }

    private void Apply(Entry entry)
    {
        if (_context is null) return;
        if (_context.Host.ActiveSessionId is not { } id) { _status!.Text = _ui.T("noactive", "没有活动会话"); return; }
        _context.Host.SendInput(id, entry.Command, submit: false);
        _context.Host.ActivateSession(id);
    }

    private void Remove(Entry entry)
    {
        _options.Items.RemoveAll(i => i.Name == entry.Name);
        _context?.SaveConfiguration(_options);
        RefreshList();
    }

    private void RefreshList()
    {
        if (_list is null || _context is null) return;
        _list.Children.Clear();
        if (_options.Items.Count == 0) { _list.Children.Add(_ui.LocalLabel("none", "还没有片段", "UiMuted", 12)); return; }
        foreach (var entry in _options.Items.ToArray())
        {
            var captured = entry;
            var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
            var label = _ui.Label(entry.Name + "  " + entry.Command, "UiInk", 12);
            label.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(label);
            var apply = _ui.Button("SnippetApply", "apply", "粘贴", () => Apply(captured));
            Grid.SetColumn(apply, 1); row.Children.Add(apply);
            var remove = _ui.Button("SnippetRemove", "remove", "删除", () => Remove(captured));
            Grid.SetColumn(remove, 2); row.Children.Add(remove);
            _list.Children.Add(row);
        }
    }

    public void Deactivate() { _context = null; }
}
