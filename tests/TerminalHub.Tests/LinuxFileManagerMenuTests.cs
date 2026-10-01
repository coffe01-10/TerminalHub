using System.Xml.Linq;
using TerminalHub.App;
using Xunit;

namespace TerminalHub.Tests;

public class LinuxFileManagerMenuTests
{
    [Fact]
    public void InstallAndRemove_PreserveOtherActions_AndQuotedProgramPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-menu-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "Thunar", "uca.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var existing = new XElement("action", new XElement("unique-id", "user-action"),
            new XElement("name", "My tool"), new XElement("command", "my-tool %f"));
        new XDocument(new XElement("actions", existing)).Save(path);
        try
        {
            var exe = "/opt/终端 tools/TerminalHub";
            LinuxFileManagerMenu.Install(exe, dir);
            LinuxFileManagerMenu.Install(exe, dir);
            Assert.True(LinuxFileManagerMenu.IsInstalled(dir));
            var actions = XDocument.Load(path).Root!.Elements("action").ToList();
            Assert.Equal(2, actions.Count);
            var ours = Assert.Single(actions, a => (string?)a.Element("unique-id") == "terminalhub-open-directory");
            Assert.Equal("\"/opt/终端 tools/TerminalHub\" --cwd %f", (string?)ours.Element("command"));
            Assert.NotNull(ours.Element("directories"));
            LinuxFileManagerMenu.Remove(dir);
            Assert.False(LinuxFileManagerMenu.IsInstalled(dir));
            Assert.True(XNode.DeepEquals(existing, Assert.Single(XDocument.Load(path).Root!.Elements("action"))));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MalformedExistingActions_AreNotOverwritten()
    {
        var dir = Path.Combine(Path.GetTempPath(), "th-menu-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "Thunar", "uca.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<actions>unfinished");
        try
        {
            Assert.False(LinuxFileManagerMenu.IsInstalled(dir));
            Assert.Throws<System.Xml.XmlException>(() => LinuxFileManagerMenu.Install("/opt/TerminalHub", dir));
            Assert.Equal("<actions>unfinished", File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}
