using TerminalHub.Core.Deploy;
using Xunit;

namespace TerminalHub.Tests;

public class PublishPlannerTests
{
    [Theory]
    [InlineData(false, false, DeployAction.StartPublish)]
    [InlineData(false, true, DeployAction.StartPublish)]
    [InlineData(true, false, DeployAction.OpenArtifacts)]
    [InlineData(true, true, DeployAction.StartPublish)]
    public void Decide_MissingPublishes_ExistingOpens_ForceAlwaysPublishes(
        bool hasArtifacts, bool force, DeployAction expected)
    {
        Assert.Equal(expected, PublishPlanner.Decide(hasArtifacts, force));
    }

    [Fact]
    public void FindRepoRoot_WalksUpToScripts()
    {
        var root = TempRepo(linux: true, windows: true);
        try
        {
            var deep = Directory.CreateDirectory(Path.Combine(root, "src", "App", "bin"));
            Assert.Equal(Path.GetFullPath(root), PublishPlanner.FindRepoRoot(deep.FullName));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void FindRepoRoot_NoScript_Null()
    {
        var root = Path.Combine(Path.GetTempPath(), $"th-pub-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(PublishPlanner.FindRepoRoot(root));
            Assert.Null(PublishPlanner.FindRepoRoot(null));
            Assert.Null(PublishPlanner.FindRepoRoot(""));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TryPlan_Linux_BashRunsScriptFromRepoRoot()
    {
        var root = TempRepo(linux: true, windows: false);
        try
        {
            var from = Path.Combine(root, "src");
            Directory.CreateDirectory(from);
            var plan = PublishPlanner.TryPlan(from, PublishPlatform.Linux);
            Assert.NotNull(plan);
            Assert.Equal(Path.GetFullPath(root), plan!.RepoRoot);
            Assert.Equal(plan.RepoRoot, plan.WorkingDirectory);
            Assert.Equal("bash", plan.Shell);
            Assert.Equal("./scripts/publish-linux.sh", plan.Arguments);
            Assert.Equal("./scripts/publish-linux.sh", plan.DisplayCommand);
            Assert.DoesNotContain(' ', plan.Arguments);

            Assert.Null(PublishPlanner.TryPlan(from, PublishPlatform.Windows));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TryPlan_Windows_PwshOrGivenShell_FileArgument()
    {
        var root = TempRepo(linux: false, windows: true);
        try
        {
            var plan = PublishPlanner.TryPlan(root, PublishPlatform.Windows);
            Assert.NotNull(plan);
            Assert.Equal("pwsh", plan!.Shell);
            Assert.Equal("-NoProfile -ExecutionPolicy Bypass -File scripts\\publish-windows.ps1", plan.Arguments);
            Assert.Contains("scripts\\publish-windows.ps1", plan.DisplayCommand);
            Assert.StartsWith("pwsh ", plan.DisplayCommand);

            var ps = PublishPlanner.TryPlan(root, PublishPlatform.Windows, "powershell");
            Assert.Equal("powershell", ps!.Shell);
            Assert.StartsWith("powershell ", ps.DisplayCommand);

            Assert.Null(PublishPlanner.TryPlan(root, PublishPlatform.Linux));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ResolveWindowsShell_PrefersPwshThenPowerShell()
    {
        Assert.Equal("pwsh", PublishPlanner.ResolveWindowsShell(n => n is "pwsh" or "powershell"));
        Assert.Equal("powershell", PublishPlanner.ResolveWindowsShell(n => n == "powershell"));
        Assert.Equal("pwsh", PublishPlanner.ResolveWindowsShell(_ => false));
    }

    [Fact]
    public void NameOnPath_FindsBareNameAndWindowsExe()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"th-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "pwsh"), "");
            File.WriteAllText(Path.Combine(dir, "powershell.exe"), "");
            var other = Path.Combine(Path.GetTempPath(), $"th-path-{Guid.NewGuid():N}");
            Directory.CreateDirectory(other);
            try
            {
                Assert.True(PublishPlanner.NameOnPath("pwsh", $"{other}:{dir}", windows: false));
                Assert.True(PublishPlanner.NameOnPath("powershell", $"{other};{dir}", windows: true));
                Assert.False(PublishPlanner.NameOnPath("missing", dir, windows: false));
                Assert.False(PublishPlanner.NameOnPath("pwsh", null, windows: false));
            }
            finally { Directory.Delete(other, true); }
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string TempRepo(bool linux, bool windows)
    {
        var root = Path.Combine(Path.GetTempPath(), $"th-pub-{Guid.NewGuid():N}");
        var scripts = Directory.CreateDirectory(Path.Combine(root, "scripts"));
        if (linux)
            File.WriteAllText(Path.Combine(scripts.FullName, "publish-linux.sh"), "#!/bin/sh\nexit 0\n");
        if (windows)
            File.WriteAllText(Path.Combine(scripts.FullName, "publish-windows.ps1"), "exit 0\n");
        return root;
    }
}
