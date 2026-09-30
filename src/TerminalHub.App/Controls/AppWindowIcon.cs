using Avalonia.Controls;
using Avalonia.Platform;

namespace TerminalHub.App.Controls;

internal static class AppWindowIcon
{
    public static void Refresh(Window window)
    {
        // Reapply after the native window opens. On Windows this also requests
        // a taskbar redraw, replacing a cached generic window icon.
        using var stream = AssetLoader.Open(new Uri("avares://TerminalHub/Assets/terminal-hub-icon.ico"));
        window.Icon = new WindowIcon(stream);
    }
}
