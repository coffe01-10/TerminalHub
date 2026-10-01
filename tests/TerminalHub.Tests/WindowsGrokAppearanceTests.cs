using System.Runtime.InteropServices;
using System.Text.Json;
using TerminalHub.Core.Pty;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;
using Xunit;

namespace TerminalHub.Tests;

public sealed class LocalGrokFactAttribute : FactAttribute
{
    public LocalGrokFactAttribute()
    {
        // Windows uses ConPTY, Linux forkpty; the std-handle workaround below is Windows-only.
        if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERMINALHUB_GROK_PATH")))
            Skip = "Set TERMINALHUB_GROK_PATH for local PTY color capture; no trust or model requests are submitted.";
    }
}

[Collection("ProcessWide")]
public class WindowsGrokAppearanceTests
{
    [LocalGrokFact]
    public async Task GrokStartup_CapturesColorsWithoutSubmittingInput()
    {
        using var pty = OperatingSystem.IsWindows() ? new ConPtySession() : (IPtySession)new LinuxPtySession();
        using var terminal = new TerminalEmulator(pty, 100, 28);
        using var output = new MemoryStream();
        pty.OutputReceived += (_, data) => { lock (output) output.Write(data.Span); };
        terminal.Parser.DefaultColorQuery = foreground => foreground ? "3c3c/3535/2b2b" : "fcfc/f8f8/eeee";
        var ids = new[] { -10, -11, -12 };
        var handles = OperatingSystem.IsWindows() ? ids.Select(GetStdHandle).ToArray() : [];
        var noColor = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", null);
            if (OperatingSystem.IsWindows())
                foreach (var id in ids) SetStdHandle(id, IntPtr.Zero);
            await terminal.StartAsync(new PtyOptions
            {
                Shell = Environment.GetEnvironmentVariable("TERMINALHUB_GROK_PATH")!,
                WorkingDirectory = Environment.GetEnvironmentVariable("TERMINALHUB_CLI_CWD") ?? Environment.CurrentDirectory
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", noColor);
            if (OperatingSystem.IsWindows())
                for (var i = 0; i < ids.Length; i++) SetStdHandle(ids[i], handles[i]);
        }
        var deadline = Environment.TickCount64 + 10000;
        while (terminal.Buffer.Version == 0 && Environment.TickCount64 < deadline) await Task.Delay(100);
        await Task.Delay(3000);
        var frame = terminal.Buffer.CaptureFrame();
        var capture = Environment.GetEnvironmentVariable("TERMINALHUB_GROK_CAPTURE");
        if (!string.IsNullOrEmpty(capture))
        {
            Directory.CreateDirectory(capture);
            lock (output) File.WriteAllBytes(Path.Combine(capture, "startup.vt"), output.ToArray());
            File.WriteAllText(Path.Combine(capture, "colors.json"), JsonSerializer.Serialize(new
            {
                frame.Columns, frame.Rows,
                Screen = terminal.Buffer.TailText(frame.Rows),
                Backgrounds = frame.Cells.GroupBy(c => c.Bg).Select(g => new { g.Key.Kind, g.Key.Value, Cells = g.Count() }),
                Foregrounds = frame.Cells.GroupBy(c => c.Fg).Select(g => new { g.Key.Kind, g.Key.Value, Cells = g.Count() })
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Assert.Contains(frame.Cells, cell => cell.Char is not (' ' or '\0'));
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] private static extern bool SetStdHandle(int id, IntPtr handle);
}
