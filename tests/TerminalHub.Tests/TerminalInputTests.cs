using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.Views;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>Terminal keyboard input: typed text and Enter must reach the PTY.</summary>
public class TerminalInputTests
{
    private static async Task<(MainWindow Win, TerminalHub.App.ViewModels.MainWindowViewModel Vm)> Boot()
    {
        PtySessionFactory.UseMock = true;
        var window = new MainWindow { Width = 1200, Height = 800 };
        window.Show();
        await Task.Delay(500);
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
        Assert.True(vm.SessionCards.Count >= 3);
        return (window, vm);
    }

    [AvaloniaFact]
    public async Task Typing_ReachesPty_AndEnterExecutes()
    {
        var (window, vm) = await Boot();
        var view = window.GetVisualDescendants().OfType<TerminalView>().First(v => !v.IsPreview && v.IsEffectivelyVisible);
        view.Focus();
        await Task.Delay(100);
        Assert.True(view.IsFocused);

        foreach (var ch in "dir")
            window.KeyTextInput(ch.ToString());
        await Task.Delay(300);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await Task.Delay(400);

        var text = vm.ActiveSession!.Emulator.Buffer.TailText(10);
        Assert.Contains("mock: dir", text);
        window.Close();
    }
}
