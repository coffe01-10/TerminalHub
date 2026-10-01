using System.Xml.Linq;

namespace TerminalHub.App;

/// <summary>Thunar's per-user folder action; other custom actions are preserved.</summary>
public static class LinuxFileManagerMenu
{
    private const string ActionId = "terminalhub-open-directory";

    private static string ActionPath(string? configDirectory) => Path.Combine(
        configDirectory ?? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "Thunar", "uca.xml");

    public static bool IsInstalled(string? configDirectory = null)
    {
        var path = ActionPath(configDirectory);
        if (!File.Exists(path)) return false;
        try { return XDocument.Load(path).Root?.Elements("action").Any(IsOurAction) == true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        { return false; }
    }

    public static void Install(string executable, string? configDirectory = null)
    {
        var path = ActionPath(configDirectory);
        var document = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("actions"));
        var root = document.Root;
        if (root?.Name != "actions") throw new InvalidDataException("Thunar 自定义操作文件格式不正确。");
        root.Elements("action").Where(IsOurAction).Remove();
        root.Add(new XElement("action",
            new XElement("icon", "utilities-terminal"),
            new XElement("name", "在 Terminal Hub 中打开"),
            new XElement("unique-id", ActionId),
            new XElement("command", DirectoryCommand(executable)),
            new XElement("description", "在所选文件夹中打开终端"),
            new XElement("patterns", "*"),
            new XElement("directories")));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        document.Save(path);
    }

    public static void Remove(string? configDirectory = null)
    {
        var path = ActionPath(configDirectory);
        if (!File.Exists(path)) return;
        var document = XDocument.Load(path);
        document.Root?.Elements("action").Where(IsOurAction).Remove();
        document.Save(path);
    }

    public static string DirectoryCommand(string executable) =>
        "\"" + executable.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\" --cwd %f";

    private static bool IsOurAction(XElement action) => (string?)action.Element("unique-id") == ActionId;
}
