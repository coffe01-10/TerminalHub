using TerminalHub.Core.Settings;
using Xunit;

namespace TerminalHub.Tests;

// Exported workspace templates are shareable files: SSH connection details
// (user@host, port, key path) must never leave the machine in the JSON, while
// local duplication (Clone) and the preview (Describe) keep working as before.
public class WorkspaceTemplateExportPrivacyTests
{
    private static WorkspaceTemplate Template()
        => new()
        {
            Name = "带 SSH 的布局",
            Layout = new WorkspaceState
            {
                Sessions =
                [
                    new WorkspaceSession { Name = "prod", Tag = "SSH", Shell = "ssh",
                        Arguments = "-t -p 2222 user@prod.example -i C:\\keys\\id_ed25519" },
                    new WorkspaceSession { Name = "local", Tag = "Dev", Shell = "pwsh.exe", Arguments = "-NoLogo" },
                ]
            },
        };

    [Fact]
    public void ExportedJson_OmitsSshConnectionDetails_KeepsOtherSessions()
    {
        var json = WorkspaceTemplateTransfer.ToJson(Template());
        Assert.DoesNotContain("user@prod.example", json);
        Assert.DoesNotContain("-p 2222", json);
        Assert.DoesNotContain("id_ed25519", json);
        Assert.True(WorkspaceTemplateTransfer.TryParse(json, out var imported, out _));
        var ssh = Assert.Single(imported!.Layout.Sessions, s => s.Tag == "SSH");
        Assert.Equal("prod", ssh.Name);
        Assert.Equal("", ssh.Shell);
        Assert.Equal("", ssh.Arguments);
        var local = Assert.Single(imported.Layout.Sessions, s => s.Tag == "Dev");
        Assert.Equal("pwsh.exe", local.Shell);
        Assert.Equal("-NoLogo", local.Arguments);
    }

    [Fact]
    public void Clone_KeepsSshConnectionDetailsForLocalUse()
    {
        var copy = WorkspaceTemplateTransfer.Clone(Template());
        var ssh = Assert.Single(copy.Layout.Sessions, s => s.Tag == "SSH");
        Assert.Equal("ssh", ssh.Shell);
        Assert.Contains("user@prod.example", ssh.Arguments);
    }

    [Fact]
    public void Describe_PreviewNeverShowsSshArguments()
    {
        var text = WorkspaceTemplateTransfer.Describe(Template());
        Assert.DoesNotContain("user@prod.example", text);
        Assert.DoesNotContain("2222", text);
    }
}
