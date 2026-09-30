using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TerminalHub.App.ViewModels;

namespace TerminalHub.App.Views;

public partial class MainWindow
{
    private IInputElement? _palettePreviousFocus;
    private List<PaletteEntry> _paletteEntries = [];

    private void OnOpenPalette(object? sender, RoutedEventArgs e) => OpenPalette();
    private void OpenPalette()
    {
        if (PalettePanel.IsVisible) { ClosePalette(); return; }
        _palettePreviousFocus = FocusManager?.GetFocusedElement();
        _paletteEntries = Vm.SessionCards.Select((card, index) => new PaletteEntry("切换 · " + card.Name,
            card.WorkingDirectory + " · " + card.Model.Shell,
            Vm.SessionShortcuts.FirstOrDefault(s => s.Binding.Action == TerminalHub.Core.Settings.SessionShortcutAction.Select
                && s.Binding.SessionIndex == index && s.Error.Length == 0 && s.ParsedGesture is not null)?.Gesture ?? "",
            () => { Vm.ActiveCard = card; return Task.CompletedTask; })).ToList();
        Add("新建终端", "打开新的 Shell", "Ctrl+Shift+N", () => Vm.NewSessionCommand.ExecuteAsync(null));
        Add("切换分屏", "并排查看终端", "", () => { Vm.ToggleSplitCommand.Execute(null); return Task.CompletedTask; });
        Add("重命名当前终端", "修改终端名称", "F2", async () =>
        { if (Vm.ActiveCard is { } card) await RenameSessionAsync(card); });
        Add("打开设置", "主题、字体、快捷键", "", () => { Vm.SettingsOpen = true; return Task.CompletedTask; });
        Add("管理工作区模板", "保存、配置、打开工作区", "", () =>
        { Vm.SettingsOpen = true; SettingsTabs.SelectedIndex = 1; return Task.CompletedTask; });
        Add("保存当前工作区为模板", "保存会话及分屏", "", () =>
        { Vm.SettingsOpen = true; SettingsTabs.SelectedIndex = 1; Vm.TemplateName = Vm.WorkspaceName; return Task.CompletedTask; });
        foreach (var template in Vm.WorkspaceTemplates)
            Add("打开工作区 · " + template.Name, $"{template.Layout.Sessions.Count} 个终端 · 保留当前会话", "",
                () => Vm.OpenTemplateAsync(template));
        PaletteSearch.Text = "";
        PalettePanel.IsVisible = true;
        FilterPalette();
        PaletteSearch.Focus();
        void Add(string label, string detail, string shortcut, Func<Task> action) =>
            _paletteEntries.Add(new(label, detail, shortcut, action));
    }

    private void ClosePalette(bool restoreFocus = true)
    {
        PalettePanel.IsVisible = false;
        if (restoreFocus) (_palettePreviousFocus ?? ActiveTerminal())?.Focus();
    }
    private void OnPaletteBackdrop(object? sender, PointerPressedEventArgs e)
    { ClosePalette(); e.Handled = true; }

    private void OnPaletteTextChanged(object? sender, TextChangedEventArgs e) => FilterPalette();
    private void FilterPalette()
    {
        if (PaletteResults is null) return; // XAML TextChanged can fire during initialization.
        var items = _paletteEntries.Where(entry => entry.Matches(PaletteSearch.Text ?? "")).ToArray();
        PaletteResults.ItemsSource = items;
        PaletteResults.SelectedIndex = items.Length > 0 ? 0 : -1;
        PaletteEmpty.IsVisible = items.Length == 0;
    }

    private async void OnExecutePalette(object? sender, TappedEventArgs e) => await ExecutePaletteAsync();
    private async Task ExecutePaletteAsync()
    {
        if (PaletteResults.SelectedItem is not PaletteEntry entry) return;
        ClosePalette(restoreFocus: false);
        await entry.Execute();
        if (!Vm.SettingsOpen) ActiveTerminal()?.Focus();
    }

    private void HandlePaletteKey(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) ClosePalette();
        else if (e.Key == Key.Tab) PaletteSearch.Focus();
        else if (e.Key == Key.Enter) _ = ExecutePaletteAsync();
        else if (e.Key is Key.Down or Key.Up)
        {
            var count = PaletteResults.ItemCount;
            if (count > 0)
            {
                PaletteResults.SelectedIndex = (PaletteResults.SelectedIndex + (e.Key == Key.Down ? 1 : -1) + count) % count;
                PaletteResults.ScrollIntoView(PaletteResults.SelectedItem!);
            }
        }
        else return;
        e.Handled = true;
    }
}
