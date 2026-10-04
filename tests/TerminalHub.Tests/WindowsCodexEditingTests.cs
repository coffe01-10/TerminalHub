using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Headless;
using Avalonia.Input.TextInput;
using TerminalHub.App.Controls;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public sealed class LocalCodexFactAttribute : FactAttribute
{
    public LocalCodexFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERMINALHUB_CODEX_PATH")))
            Skip = "Set TERMINALHUB_CODEX_PATH to the local codex.cmd; use an already trusted directory. No prompts are submitted.";
    }
}

[Collection("ProcessWide")]
public class WindowsCodexEditingTests
{
    [LocalCodexFact]
    public async Task Codex_RealConPty_ChineseArrowsAndResize_WithoutSubmitting()
    {
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(WindowsCodexEditingTests).Assembly)
            .Dispatch(async () =>
            {
                using var pty = new ConPtySession();
                using var terminal = new TerminalEmulator(pty, 100, 28);
                var view = new TerminalView { Emulator = terminal };
                var ids = new[] { -10, -11, -12 };
                var handles = ids.Select(GetStdHandle).ToArray();
                try
                {
                    foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
                    await terminal.StartAsync(new PtyOptions
                    {
                        Shell = "cmd.exe",
                        Arguments = "/d /s /c \"\"" + Environment.GetEnvironmentVariable("TERMINALHUB_CODEX_PATH") + "\" --no-alt-screen\"",
                        WorkingDirectory = Environment.GetEnvironmentVariable("TERMINALHUB_CLI_CWD") ?? Environment.CurrentDirectory
                    });
                }
                finally { for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]); }
                await WaitFor(() =>
                {
                    var screen = terminal.Buffer.TailText(28);
                    Assert.False(screen.Contains("Sign in with ChatGPT", StringComparison.OrdinalIgnoreCase),
                        "Codex requires login. Chinese editing was not verified; no login choice is confirmed by this test.");
                    Assert.False(screen.Contains("trust this", StringComparison.OrdinalIgnoreCase),
                        "Codex requires workspace trust. Chinese editing was not verified; this test does not accept trust.");
                    return HasEditingPrompt(terminal.Buffer.CaptureFrame());
                }, "Codex editing prompt unavailable", terminal);
                terminal.SendText("ab中文cd");
                await WaitFor(() => AtInputOffset(terminal.Buffer.CaptureFrame(), "ab中文cd", 6), "Codex Chinese input caret", terminal);
                AssertIme(view, terminal);
                terminal.SendText("\x1b[D\x1b[D\x1b[D");
                await WaitFor(() => AtInputOffset(terminal.Buffer.CaptureFrame(), "ab中文cd", 3), "Codex left across 文", terminal);
                AssertIme(view, terminal);
                terminal.SendText("\x1b[C");
                await WaitFor(() => AtInputOffset(terminal.Buffer.CaptureFrame(), "ab中文cd", 4), "Codex right across 文", terminal);
                terminal.Resize(60, 28);
                terminal.SendText("x");
                await WaitFor(() => AtInputOffset(terminal.Buffer.CaptureFrame(), "ab中文xcd", 5), "Codex resized editing", terminal);
                AssertIme(view, terminal);
                return true;
            }, CancellationToken.None);
    }
    /// <summary>The editing composer is up when the screen shows the Codex
    /// header, carries no login/trust blocker, and the visible VT cursor sits on
    /// the '› ' composer row in the cell right after the arrow — with the row
    /// still empty or holding only the "Ask Codex to do anything" placeholder.
    /// Works for both the old 'model:'-era UI and the 0.158.x layout that no
    /// longer prints a model line.</summary>
    internal static bool HasEditingPrompt(TerminalFrame frame)
    {
        var screen = ScreenText(frame);
        if (!screen.Contains("OpenAI Codex", StringComparison.Ordinal)) return false;
        if (screen.Contains("Sign in with ChatGPT", StringComparison.OrdinalIgnoreCase)) return false;
        if (screen.Contains("trust this", StringComparison.OrdinalIgnoreCase)) return false;
        if (!frame.CursorVisible) return false;
        var row = ScreenBuffer.FlattenRow(frame.Cells.AsSpan(frame.CursorY * frame.Columns, frame.Columns)).Text;
        var arrow = row.IndexOf('›');
        if (arrow < 0) return false;
        if (frame.CursorX != arrow + 2) return false;
        var tail = row[(arrow + 1)..].Trim();
        return tail.Length == 0 || tail == "Ask Codex to do anything";
    }
    private static string ScreenText(TerminalFrame frame)
    {
        var all = new System.Text.StringBuilder();
        for (var r = 0; r < frame.Rows; r++)
            all.Append(ScreenBuffer.FlattenRow(frame.Cells.AsSpan(r * frame.Columns, frame.Columns)).Text).Append('\n');
        return all.ToString();
    }
    private static bool AtInputOffset(TerminalFrame frame, string input, int offset)
    {
        for (var row = 0; row < frame.Rows; row++)
        {
            var flat = ScreenBuffer.FlattenRow(frame.Cells.AsSpan(row * frame.Columns, frame.Columns));
            var index = flat.Text.IndexOf(input, StringComparison.Ordinal);
            if (index >= 0 && index + offset < flat.Cols.Length
                && frame.CursorVisible && frame.CursorY == row && frame.CursorX == flat.Cols[index + offset]) return true;
        }
        return false;
    }
    private static void AssertIme(TerminalView view, TerminalEmulator terminal)
    {
        lock (terminal.Buffer.SyncRoot)
        {
            var frame = terminal.Buffer.CaptureFrame();
            var client = (TextInputMethodClient)typeof(TerminalView).GetField("_imeClient", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            var width = (double)typeof(TerminalView).GetField("_cellW", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            var rect = client.CursorRectangle;
            Assert.Equal(frame.CursorX * width, rect.X, 6);
            Assert.Equal(frame.CursorY * rect.Height, rect.Y, 6);
        }
    }
    private static async Task WaitFor(Func<bool> condition, string scenario, TerminalEmulator? terminal = null)
    {
        var deadline = Environment.TickCount64 + 15000;
        while (!condition() && Environment.TickCount64 < deadline) await Task.Delay(100);
        if (!condition() && terminal is not null)
        {
            var frame = terminal.Buffer.CaptureFrame();
            scenario += $"; cursor ({frame.CursorX},{frame.CursorY}), visible {frame.CursorVisible}; screen: {terminal.Buffer.TailText(frame.Rows)}";
        }
        Assert.True(condition(), scenario);
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}
