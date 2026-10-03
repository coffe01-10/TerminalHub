using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using TerminalHub.App.Plugins;
using TerminalHub.Core.Pty;
using TerminalHub.Extensibility;
using Xunit;

namespace TerminalHub.Tests;

public class PluginTutorialTests
{
    [AvaloniaFact]
    public async Task TutorialDll_PastesWithoutReturnIntoCurrentSession_AndRestoresSavedDraft()
    {
        using var f = new StageLayoutTests.StageFixture(); await Task.Delay(650);
        var manager = (PluginManager)f.Window.GetType().GetField("_plugins", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Window)!;
        try
        {
            var folder = Path.GetDirectoryName(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "AcceptancePlugins", "CommandDraft"), "plugin.json", SearchOption.AllDirectories).Single())!;
            manager.Import(folder); var plugin = manager.Plugins.Single(); Assert.True(plugin.Enabled, plugin.Error);
            var page = manager.Modules.Single(m => m.Owner == plugin.Manifest.Id).GetView();
            var editor = page.GetLogicalDescendants().OfType<TextBox>().Single(); editor.Text = "echo tutorial";
            var first = f.Vm.ActiveSession!; var firstPty = Assert.IsType<MockPtySession>(first.Pty); firstPty.RawInput.Clear();
            await manager.Commands.Single(c => c.Owner == plugin.Manifest.Id).Command.Execute();
            Assert.Equal("echo tutorial", firstPty.RawInput.ToString());
            var second = f.Vm.SessionCards.First(c => c.Model.Id != first.Id).Model;
            ((IWorkbenchHost)f.Vm).ActivateSession(second.Id);
            await Task.Delay(60); // SessionManager posts the host's active-session notification to the UI thread.
            Assert.Contains(page.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.Contains(second.Name) == true);
            var secondPty = Assert.IsType<MockPtySession>(second.Pty); secondPty.RawInput.Clear();
            page.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "粘贴到活动终端")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("echo tutorial", secondPty.RawInput.ToString()); Assert.Equal("echo tutorial", firstPty.RawInput.ToString());
            page.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "保存草稿")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.Text = "unsaved edit"; manager.Disable(plugin); Assert.True(first.IsRunning); Assert.True(second.IsRunning);
            manager.Enable(plugin); Assert.True(plugin.Enabled, plugin.Error);
            Assert.Equal("echo tutorial", manager.Modules.Single(m => m.Owner == plugin.Manifest.Id).GetView().GetLogicalDescendants().OfType<TextBox>().Single().Text);
            Assert.Empty(manager.LastError);
        }
        finally { foreach (var plugin in manager.Plugins.ToArray()) manager.Remove(plugin); }
    }
}
