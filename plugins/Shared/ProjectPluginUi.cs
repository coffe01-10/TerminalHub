using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using TerminalHub.Extensibility;

namespace TerminalHub.Official;

internal static class ProjectPluginUi
{
    public static WrapPanel Actions(params Control[] controls)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }
    public static void OpenPath(string path, bool folder)
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open")
                { UseShellExecute = false, ArgumentList = { path } });
    }
    public static async Task<string?> PickFolder(Control view, CancellationToken ct)
    {
        var storage = TopLevel.GetTopLevel(view)?.StorageProvider;
        if (storage is null) return null;
        var files = await storage.OpenFolderPickerAsync(new() { AllowMultiple = false });
        return ct.IsCancellationRequested ? null : files.FirstOrDefault()?.TryGetLocalPath();
    }
    public static async Task<string?> PickFile(Control view, bool save, CancellationToken ct)
    {
        var storage = TopLevel.GetTopLevel(view)?.StorageProvider;
        if (storage is null) return null;
        if (save)
        {
            var file = await storage.SaveFilePickerAsync(new() { SuggestedFileName = "projects.json", DefaultExtension = "json" });
            return ct.IsCancellationRequested ? null : file?.TryGetLocalPath();
        }
        var files = await storage.OpenFilePickerAsync(new() { AllowMultiple = false });
        return ct.IsCancellationRequested ? null : files.FirstOrDefault()?.TryGetLocalPath();
    }
    public static SessionInfo? Active(IPluginContext context)
        => context.Host.Sessions.FirstOrDefault(s => s.Id == context.Host.ActiveSessionId);
}
