namespace TerminalHub.Core.Settings;

public enum SessionShortcutAction { Next, Previous, Select, CommandPalette, Recent, UndoLayout, RedoLayout }

public sealed class SessionShortcutBinding
{
    public SessionShortcutAction Action { get; set; }
    public int SessionIndex { get; set; }
    public string Gesture { get; set; } = "";

    public static List<SessionShortcutBinding> Defaults() =>
    [
        new() { Action = SessionShortcutAction.Next, Gesture = "Ctrl+Tab" },
        new() { Action = SessionShortcutAction.Previous, Gesture = "Ctrl+Shift+Tab" },
        .. Enumerable.Range(0, 9).Select(i => new SessionShortcutBinding
            { Action = SessionShortcutAction.Select, SessionIndex = i, Gesture = $"Alt+{i + 1}" }),
        new() { Action = SessionShortcutAction.CommandPalette, Gesture = "Ctrl+Shift+P" },
        new() { Action = SessionShortcutAction.Recent, Gesture = "Ctrl+F6" },
        new() { Action = SessionShortcutAction.UndoLayout, Gesture = "Ctrl+Alt+Z" },
        new() { Action = SessionShortcutAction.RedoLayout, Gesture = "Ctrl+Alt+Y" }
    ];
}
