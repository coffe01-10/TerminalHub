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

public sealed class LocalClaudeFactAttribute : FactAttribute
{
    public LocalClaudeFactAttribute()
    {
        // Windows uses ConPTY, Linux uses forkpty — the injected std-handle
        // workaround is Windows-only. TERMINALHUB_CLI_CWD must point at an
        // already-trusted directory or the run parks on the trust prompt.
        if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERMINALHUB_CLAUDE_PATH")))
            Skip = "Set TERMINALHUB_CLAUDE_PATH to run local PTY editing; no prompts are submitted.";
    }
}

/// <summary>Only edit text in an already trusted directory; never accept trust or submit an AI request.</summary>
[Collection("ProcessWide")]
public class WindowsCliEditingTests
{
    private static IPtySession CreatePty() =>
        OperatingSystem.IsWindows() ? new ConPtySession() : new LinuxPtySession();

    [LocalClaudeFact]
    public Task Claude_RealPty_CjkCursorAndResize() =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(WindowsCliEditingTests).Assembly)
            .Dispatch(async () => { await RunClaudeEditing(); return true; }, CancellationToken.None);

    private static async Task RunClaudeEditing()
    {
        using var pty = CreatePty();
        using var terminal = new TerminalEmulator(pty, 100, 28);
        var view = new TerminalView { Emulator = terminal };
        var ids = new[] { -10, -11, -12 };
        var handles = OperatingSystem.IsWindows() ? ids.Select(GetStdHandle).ToArray() : [];
        try
        {
            if (OperatingSystem.IsWindows())
                foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
            await terminal.StartAsync(new PtyOptions
            {
                Shell = Environment.GetEnvironmentVariable("TERMINALHUB_CLAUDE_PATH")!,
                WorkingDirectory = Environment.GetEnvironmentVariable("TERMINALHUB_CLI_CWD") ?? Environment.CurrentDirectory
            });
        }
        finally
        {
            if (OperatingSystem.IsWindows())
                for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]);
        }
        await WaitFor(() => HasPrompt(terminal.Buffer.CaptureFrame()), "Claude input prompt unavailable (trust/login/startup may be required).");
        terminal.SendText("ab中文cd");
        await WaitFor(() => SoftwareCaret(terminal.Buffer.CaptureFrame()) is { column: 10 }, "No end-of-input reverse-video caret.");
        AssertIme(view, terminal);
        terminal.SendText("\x1b[D\x1b[D\x1b[D");
        await WaitFor(() => SoftwareCaret(terminal.Buffer.CaptureFrame()) is { column: 6 }, "Left across Chinese did not reach 文.");
        AssertIme(view, terminal);
        terminal.SendText("\x1b[C");
        await WaitFor(() => SoftwareCaret(terminal.Buffer.CaptureFrame()) is { column: 8 }, "Right across Chinese did not reach c.");
        AssertIme(view, terminal);
        terminal.Resize(60, 28);
        await WaitFor(() => HasPrompt(terminal.Buffer.CaptureFrame()) && SoftwareCaret(terminal.Buffer.CaptureFrame()) is { column: 8 }, "Resize did not preserve editing caret.");
        AssertIme(view, terminal);
        terminal.SendText("\x1b[H");
        await WaitFor(() => SoftwareCaret(terminal.Buffer.CaptureFrame()) is { column: 2 }, "Home did not reach input start.");
        AssertIme(view, terminal);
        terminal.SendText("\x1b[F");
        await WaitFor(() => SoftwareCaret(terminal.Buffer.CaptureFrame()) is { column: 10 }, "End did not reach input end.");
        terminal.PasteText("\n第二行");
        await WaitFor(() => SoftwareCaret(terminal.Buffer.CaptureFrame()) is { column: 8 }, "Multiline paste did not preserve its Chinese caret.");
        AssertIme(view, terminal);
        // Disposal terminates only the process this test created, with unsent text.
    }

    private static bool HasPrompt(TerminalFrame frame)
    {
        for (var r = 1; r < frame.Rows - 1; r++)
        {
            var row = frame.Cells.AsSpan(r * frame.Columns, frame.Columns);
            if (!row.ToArray().Any(c => c.Char == '❯')) continue;
            var above = frame.Cells.AsSpan((r - 1) * frame.Columns, frame.Columns);
            if (above.ToArray().Count(c => c.Char == '─') >= frame.Columns / 2) return true;
        }
        return false;
    }

    private static (int column, int row)? SoftwareCaret(TerminalFrame frame)
    {
        var input = false;
        for (var r = 0; r < frame.Rows; r++)
        {
            var row = frame.Cells.AsSpan(r * frame.Columns, frame.Columns);
            var text = ScreenBuffer.FlattenRow(row).Text;
            if (text.Contains("ab中文cd")) input = true;
            if (!input) continue;
            if (text.Count(c => c == '─') >= frame.Columns / 2) return null;
            for (var c = 0; c < frame.Columns; c++)
                if (!row[c].IsWideContinuation && row[c].Attrs.HasFlag(CellAttrs.Inverse)) return (c, r);
        }
        return null;
    }

    private static void AssertIme(TerminalView view, TerminalEmulator terminal)
    {
        var caret = SoftwareCaret(terminal.Buffer.CaptureFrame())!.Value;
        var client = (TextInputMethodClient)typeof(TerminalView).GetField("_imeClient", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        var width = (double)typeof(TerminalView).GetField("_cellW", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        var rect = client.CursorRectangle;
        Assert.Equal(caret.column * width, rect.X, 6);
        Assert.Equal(caret.row * rect.Height, rect.Y, 6);
    }

    private static async Task WaitFor(Func<bool> predicate, string failure)
    {
        var until = Environment.TickCount64 + 15000;
        while (Environment.TickCount64 < until)
        {
            if (predicate()) return;
            await Task.Delay(100);
        }
        Assert.Fail(failure);
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}
