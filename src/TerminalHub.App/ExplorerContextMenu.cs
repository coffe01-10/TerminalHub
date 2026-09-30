using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TerminalHub.App;

public static class ExplorerContextMenu
{
    public const string Verb = "TerminalHub";

    public static string DirectoryCommand(string exe) => $"\"{exe}\" --cwd \"%1\"";
    public static string BackgroundCommand(string exe) => $"\"{exe}\" --cwd \"%V\"";

    [SupportedOSPlatform("windows")]
    public static bool IsInstalled(string verb = Verb)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return HasCommand($@"Software\Classes\Directory\shell\{verb}\command")
            && HasCommand($@"Software\Classes\Directory\Background\shell\{verb}\command");
    }

    [SupportedOSPlatform("windows")]
    public static void Install(string exe, string verb = Verb)
    {
        if (!OperatingSystem.IsWindows()) return;
        Write($@"Software\Classes\Directory\shell\{verb}", "在 Terminal Hub 中打开", DirectoryCommand(exe), exe);
        Write($@"Software\Classes\Directory\Background\shell\{verb}", "在 Terminal Hub 中打开", BackgroundCommand(exe), exe);
    }

    [SupportedOSPlatform("windows")]
    public static void Remove(string verb = Verb)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Directory\shell\{verb}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Directory\Background\shell\{verb}", throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            System.Diagnostics.Trace.WriteLine($"Explorer menu: {ex.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool HasCommand(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(null) is string { Length: > 0 };
    }

    [SupportedOSPlatform("windows")]
    private static void Write(string path, string label, string command, string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue(null, label);
        key.SetValue("Icon", exe);
        using var commandKey = key.CreateSubKey("command");
        commandKey.SetValue(null, command);
    }
}
