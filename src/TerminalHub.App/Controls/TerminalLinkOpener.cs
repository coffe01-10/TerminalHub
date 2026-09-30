using System.Diagnostics;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Controls;

public static class TerminalLinkOpener
{
    public static string EditorPath { get; set; } = "";
    public static void Open(TerminalContentLink link)
    {
        if (!link.IsUrl && File.Exists(link.Target) && File.Exists(EditorPath))
        {
            var editor = new ProcessStartInfo(EditorPath) { UseShellExecute = true };
            // The editor setting explicitly selects VS Code's --goto interface.
            editor.ArgumentList.Add("--goto");
            editor.ArgumentList.Add($"{link.Target}:{link.Line ?? 1}:{link.Column ?? 1}");
            Process.Start(editor);
        }
        else if (!link.IsUrl && File.Exists(link.Target) && OperatingSystem.IsWindows())
        {
            // Opening a traceback's .py/.cmd/.exe path must not execute the file.
            var editor = new ProcessStartInfo("notepad.exe") { UseShellExecute = true };
            editor.ArgumentList.Add(link.Target);
            Process.Start(editor);
        }
        else Process.Start(new ProcessStartInfo(link.Target) { UseShellExecute = true });
    }
}
