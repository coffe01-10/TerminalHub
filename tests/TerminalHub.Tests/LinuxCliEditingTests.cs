using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

/// <summary>
/// Real bash on a real Linux PTY: readline editing after the prompt
/// (arrows/Home/End across CJK), bracketed multiline paste, right-edge wrap,
/// and cursor survival across a mid-edit resize. These are the Linux halves of
/// the CLI-editing checks that Windows exercises against ConPTY.
/// </summary>
public class LinuxCliEditingTests
{
    private static async Task<TerminalEmulator> StartBash(int columns = 80, int rows = 24)
    {
        var emulator = new TerminalEmulator(new LinuxPtySession(), columns, rows);
        await emulator.StartAsync(new PtyOptions
        {
            Shell = "bash",
            Arguments = ShellIntegration.BashArguments,
            WorkingDirectory = "/tmp"
        });
        await WaitFor(() => InputRow(emulator.Buffer.CaptureFrame(), "$ ") >= 0,
            "bash prompt never appeared");
        return emulator;
    }

    /// <summary>Readline cursor = the real VT cursor; assert it lands on the
    /// edited character, not on the text end, and Home/End keep working.</summary>
    [Fact]
    public async Task ArrowKeys_EditAfterPrompt_IncludingCjk()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var emulator = await StartBash();

        emulator.SendText("echo ab中文cd");
        var (row, firstCol) = await WaitInput(emulator, "echo ab中文cd");
        // echo ab中 文 c d — input starts at firstCol; 中 and 文 take 2 columns.
        var endCol = firstCol + "echo ab".Length + 2 + 2 + 2;
        var wenCol = firstCol + "echo ab".Length + 2;
        await WaitFor(() => CursorIs(emulator, row, endCol), "cursor not at input end after typing");

        emulator.SendText("\x1b[D\x1b[D\x1b[D"); // d → c → 文 (wide cell start)
        await WaitFor(() => CursorIs(emulator, row, wenCol), $"Left x3 did not land on 文: {Cursor(emulator)} want ({row},{wenCol})");

        emulator.SendText("\x1b[H");
        await WaitFor(() => CursorIs(emulator, row, firstCol), $"Home did not reach input start: {Cursor(emulator)} want ({row},{firstCol})");
        emulator.SendText("\x1b[F");
        await WaitFor(() => CursorIs(emulator, row, endCol), $"End did not reach input end: {Cursor(emulator)} want ({row},{endCol})");

        emulator.SendText("\x1b[D\x1b[D\x1b[D" + "X");
        await WaitFor(() => CursorIs(emulator, row, wenCol + 1), $"insertion did not advance past X: {Cursor(emulator)} want ({row},{wenCol + 1})");
        emulator.SendText("\r");
        await WaitFor(() => FrameHas(emulator, "ab中X文cd"), "edited line did not execute verbatim");
    }

    /// <summary>Bracketed paste must not execute: both pasted lines sit in the
    /// editing buffer (evaluatable text unexpanded); only Enter runs them.</summary>
    [Fact]
    public async Task MultilinePaste_StaysInBuffer_UntilEnter()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var emulator = await StartBash();

        emulator.PasteText("echo PRE_$((3+4))\necho OUT_$((10-1))");
        await WaitFor(() => FrameHas(emulator, "echo OUT_$((10-1))"),
            "second pasted line never entered the buffer");
        var frame = emulator.Buffer.CaptureFrame();
        Assert.True(FrameText(frame, "echo PRE_$((3+4))"), "first pasted line missing");
        Assert.False(FrameText(frame, "PRE_7"), "paste executed before Enter");
        Assert.False(FrameText(frame, "OUT_9"), "second line executed before Enter");

        emulator.SendText("\r");
        await WaitFor(() => FrameHas(emulator, "PRE_7") && FrameHas(emulator, "OUT_9"),
            "Enter did not run both pasted lines");
    }

    /// <summary>A line longer than the terminal wraps at the right edge; the VT
    /// cursor follows readline onto the continuation row and back.</summary>
    [Fact]
    public async Task RightEdgeWrap_CursorFollowsInputAcrossWrap()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var emulator = await StartBash(columns: 40);

        // printf echoes a run of W's only when executed — the typed input
        // itself never contains a 40-W run, so its absence proves "not yet run".
        const string command = "printf 'W%.0s' {1..90}";
        var fullRow = new string('W', 40);
        var (row, firstCol) = await WaitPrompt(emulator); // anchor before typing
        emulator.SendText(command);
        var end = firstCol + command.Length;
        var wantRow = row + end / 40;
        var wantCol = end % 40;
        await WaitFor(() => CursorIs(emulator, wantRow, wantCol),
            $"cursor not at wrapped input end ({wantRow},{wantCol}): {Cursor(emulator)}");

        emulator.SendText("\x1b[D\x1b[D");
        await WaitFor(() => CursorIs(emulator, wantRow, wantCol - 2),
            $"Left did not move back two columns on the wrapped row: {Cursor(emulator)}");

        Assert.False(FrameHas(emulator, fullRow), "command output appeared before Enter");
        emulator.SendText("\r");
        await WaitFor(() => FrameHas(emulator, fullRow),
            "wrapped command did not execute correctly");
    }

    /// <summary>Layout switch regression: widening the grid mid-edit unwraps the
    /// input line; readline redraws and the cursor must land on the logical end
    /// of the text, not on a stale cell from the narrower layout.</summary>
    [Fact]
    public async Task ResizeMidEdit_KeepsInputAndCursorAtEnd()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var emulator = await StartBash(columns: 40);

        const string command = "echo RESIZE_$((20+2))";
        var (row, firstCol) = await WaitPrompt(emulator); // anchor before typing
        emulator.SendText(command);
        var end = firstCol + command.Length;
        await WaitFor(() => emulator.Buffer.CaptureFrame().CursorY > row,
            "input did not wrap onto a continuation row at 40 cols");

        emulator.Resize(80, 24); // unwrap: readline repaints prompt+input on one row
        await WaitFor(() =>
        {
            var f = emulator.Buffer.CaptureFrame();
            var (r, c) = PromptAnchor(f); // prompt row may shift under reflow — find it fresh
            return r >= 0 && f.CursorY == r && f.CursorX == c + command.Length;
        }, "after unwrap the cursor is not at the input's logical end: " +
           $"{Cursor(emulator)}");

        emulator.SendText("\r");
        await WaitFor(() => FrameHas(emulator, "RESIZE_22"), "resized edit did not execute");
    }

    /// <summary>Buffer-level reflow: the hardware cursor must follow the logical
    /// end of its text when a narrower grid unwraps — the headless half of the
    /// layout-switch regression.</summary>
    [Fact]
    public void ResizeReflow_KeepsCursorAtLogicalTextEnd()
    {
        using var terminal = new TerminalEmulator(columns: 4, rows: 6);
        terminal.Parser.Feed("abcdefgh");
        terminal.Resize(8, 6);
        var frame = terminal.Buffer.CaptureFrame();
        var row = RowOf(frame, "abcdefgh");
        Assert.True(row >= 0, "text lost during unwrap");
        Assert.Equal(row, frame.CursorY);
        Assert.Equal(7, frame.CursorX); // on 'h' with deferred wrap — the logical end
    }

    private static string Cursor(TerminalEmulator emulator)
    {
        var f = emulator.Buffer.CaptureFrame();
        return $"cursor=({f.CursorY},{f.CursorX})";
    }

    private static bool CursorIs(TerminalEmulator emulator, int row, int col)
    {
        var f = emulator.Buffer.CaptureFrame();
        return f.CursorY == row && f.CursorX == col;
    }

    private static bool FrameHas(TerminalEmulator emulator, string text)
        => FrameText(emulator.Buffer.CaptureFrame(), text);

    private static bool FrameText(TerminalFrame frame, string needle)
        => RowOf(frame, needle) >= 0;

    private static int RowOf(TerminalFrame frame, string needle)
    {
        for (var r = 0; r < frame.Rows; r++)
            if (ScreenBuffer.FlattenRow(
                    frame.Cells.AsSpan(r * frame.Columns, frame.Columns)).Text.Contains(needle))
                return r;
        return -1;
    }

    private static int InputRow(TerminalFrame frame, string marker) => RowOf(frame, marker);

    /// <summary>The LAST row ending a bash prompt — "$ " followed by the input
    /// area. Returns that row and the column where typed input begins.</summary>
    private static (int row, int inputCol) PromptAnchor(TerminalFrame frame)
    {
        for (var r = frame.Rows - 1; r >= 0; r--)
        {
            var span = frame.Cells.AsSpan(r * frame.Columns, frame.Columns);
            var (text, cols) = ScreenBuffer.FlattenRow(span);
            var i = text.LastIndexOf("$ ", StringComparison.Ordinal);
            if (i < 0) continue;
            return i + 2 < cols.Length ? (r, cols[i + 2]) : (r + 1, 0);
        }
        return (-1, -1);
    }

    private static async Task<(int row, int inputCol)> WaitPrompt(TerminalEmulator emulator)
    {
        (int row, int inputCol) found = (-1, -1);
        await WaitFor(() => (found = PromptAnchor(emulator.Buffer.CaptureFrame())).row >= 0,
            "bash prompt anchor never appeared");
        return found;
    }

    /// <summary>Locate the row of `needle` and the column of its first char.</summary>
    private static (int row, int firstCol) Find(TerminalFrame frame, string needle)
    {
        for (var r = 0; r < frame.Rows; r++)
        {
            var span = frame.Cells.AsSpan(r * frame.Columns, frame.Columns);
            var hit = ScreenBuffer.FlattenRow(span).Text.IndexOf(needle, StringComparison.Ordinal);
            if (hit < 0) continue;
            // FlattenRow text indexes glyph cells; wide chars occupy one index,
            // so the firstChars column map is needed to translate to a grid col.
            var (_, hitCols) = ScreenBuffer.FlattenRow(span);
            return (r, hitCols[hit]);
        }
        return (-1, -1);
    }

    private static async Task<(int row, int firstCol)> WaitInput(TerminalEmulator emulator, string needle)
    {
        (int row, int firstCol) found = (-1, -1);
        await WaitFor(() => (found = Find(emulator.Buffer.CaptureFrame(), needle)).row >= 0,
            $"'{needle}' never appeared");
        return found;
    }

    private static async Task WaitFor(Func<bool> predicate, string failure)
    {
        var until = Environment.TickCount64 + 15000;
        while (Environment.TickCount64 < until)
        {
            if (predicate()) return;
            await Task.Delay(60);
        }
        Assert.Fail(failure);
    }
}
