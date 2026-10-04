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

/// <summary>Real PSReadLine editing; all text is discarded without executing it.</summary>
[Collection("ProcessWide")]
public class WindowsShellEditingTests
{
    [LocalPwshFact]
    public async Task PowerShell_RealConPty_ChineseArrowsMultilineAndResize()
    {
        if (!OperatingSystem.IsWindows()) return;
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(WindowsShellEditingTests).Assembly)
            .Dispatch(async () =>
            {
                using var pty = new ConPtySession();
                using var terminal = new TerminalEmulator(pty, 80, 24);
                var view = new TerminalView { Emulator = terminal };
                var ids = new[] { -10, -11, -12 };
                var handles = ids.Select(GetStdHandle).ToArray();
                try
                {
                    foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
                    await terminal.StartAsync(new PtyOptions
                    {
                        // Short cwd keeps "PS <cwd>>" on one 80-column line —
                        // the prompt wait below reads the cursor row.
                        Shell = "pwsh", WorkingDirectory = Path.GetPathRoot(Environment.CurrentDirectory)!,
                        Arguments = "-NoLogo -NoProfile " + ShellIntegration.PowerShellArguments
                    });
                }
                finally { for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]); }
                // TailText trims line-end spaces, including the prompt's final space.
                await WaitFor(() => CursorLine(terminal).StartsWith("PS ") && CursorLine(terminal).TrimEnd().EndsWith('>'),
                    "PowerShell prompt", () => $"running={pty.IsRunning}, output={terminal.Buffer.TailText(24)}");
                terminal.SendText("ab中文cd");
                await WaitFor(() => CursorLine(terminal).Contains("ab中文cd"), "Chinese edit");
                var end = terminal.Buffer.CaptureFrame().CursorX;
                AssertIme(view, terminal);
                terminal.SendText("\x1b[D\x1b[D\x1b[D");
                await WaitFor(() => terminal.Buffer.CaptureFrame().CursorX == end - 4, "Left across 文");
                AssertIme(view, terminal);
                terminal.SendText("\x1b[C");
                await WaitFor(() => terminal.Buffer.CaptureFrame().CursorX == end - 2, "Right across 文");
                AssertIme(view, terminal);
                terminal.SendText("\x1b[F");
                await WaitFor(() => terminal.Buffer.CaptureFrame().CursorX == end, "End");
                terminal.PasteText("\n第二行");
                await WaitFor(() => CursorLine(terminal).Contains("第二行"), "Multiline paste");
                AssertIme(view, terminal);
                terminal.Resize(40, 24);
                await Task.Delay(150);
                // PSReadLine repaints on the next editing key after a ConPTY resize.
                terminal.SendText("x");
                await WaitFor(() => CursorLine(terminal).Contains("第二行x"), "Resize edit");
                AssertIme(view, terminal);
                return true;
            }, CancellationToken.None);
    }
    private static string CursorLine(TerminalEmulator terminal)
    {
        var frame = terminal.Buffer.CaptureFrame();
        return ScreenBuffer.FlattenRow(frame.Cells.AsSpan(frame.CursorY * frame.Columns, frame.Columns)).Text;
    }
    private static void AssertIme(TerminalView view, TerminalEmulator terminal)
    {
        var frame = terminal.Buffer.CaptureFrame();
        var client = (TextInputMethodClient)typeof(TerminalView).GetField("_imeClient", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        var width = (double)typeof(TerminalView).GetField("_cellW", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        var rect = client.CursorRectangle;
        Assert.Equal(frame.CursorX * width, rect.X, 6);
        Assert.Equal(frame.CursorY * rect.Height, rect.Y, 6);
    }
    private static async Task WaitFor(Func<bool> condition, string scenario, Func<string>? diagnostic = null)
    {
        var deadline = Environment.TickCount64 + 15000;
        while (!condition() && Environment.TickCount64 < deadline) await Task.Delay(50);
        Assert.True(condition(), scenario + (diagnostic is null ? "" : ": " + diagnostic()));
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}
