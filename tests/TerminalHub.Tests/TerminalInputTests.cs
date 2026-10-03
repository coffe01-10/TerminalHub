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
        var vm = (TerminalHub.App.ViewModels.MainWindowViewModel)window.DataContext!;
        var deadline = Environment.TickCount64 + 5000;
        while (vm.SessionCards.Count < 3 && Environment.TickCount64 < deadline)
            await Task.Delay(20);
        Assert.True(vm.SessionCards.Count >= 3, "startup sessions never spawned");
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
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        string text = "";
        while (DateTime.UtcNow < deadline)
        {
            text = vm.ActiveSession!.Emulator.Buffer.TailText(10);
            if (text.Contains("mock: dir")) break;
            await Task.Delay(25);
        }
        Assert.Contains("mock: dir", text);
        window.Close();
    }

    /// <summary>AltGr = Ctrl+Alt on many layouts: the key must NOT turn into a
    /// control byte. Before the fix, AltGr+E wrote \x05 (ENQ) to the PTY and
    /// swallowed the character the user's IME would have produced.</summary>
    [AvaloniaFact]
    public async Task CtrlAltLetter_DoesNotSendControlByte()
    {
        var (window, vm) = await Boot();
        var view = window.GetVisualDescendants().OfType<TerminalView>().First(v => !v.IsPreview && v.IsEffectivelyVisible);
        view.Focus();
        await Task.Delay(100);
        Assert.True(view.IsFocused);

        var pty = (TerminalHub.Core.Pty.MockPtySession)vm.ActiveSession!.Emulator.Pty;
        var before = pty.RawInput.Length;

        window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control | RawInputModifiers.Alt);
        await Task.Delay(150);

        Assert.Equal(before, pty.RawInput.Length); // AltGr+E produced nothing at the PTY
        window.Close();
    }

    /// <summary>Plain Ctrl+letter still sends its control byte — AltGr exclusion
    /// must not break normal control keys.</summary>
    [AvaloniaFact]
    public async Task CtrlLetter_StillSendsControlByte()
    {
        var (window, vm) = await Boot();
        var view = window.GetVisualDescendants().OfType<TerminalView>().First(v => !v.IsPreview && v.IsEffectivelyVisible);
        view.Focus();
        await Task.Delay(100);
        Assert.True(view.IsFocused);

        var pty = (TerminalHub.Core.Pty.MockPtySession)vm.ActiveSession!.Emulator.Pty;

        window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var raw = "";
        while (DateTime.UtcNow < deadline)
        {
            raw = pty.RawInput.ToString();
            if (raw.Contains("\x05")) break;
            await Task.Delay(25);
        }
        Assert.Contains("\x05", raw);
        window.Close();
    }
}
