using TerminalHub.Core.Deploy;
using Xunit;

namespace TerminalHub.Tests;

public class ArtifactLocatorTests
{
    [Fact]
    public void Find_PublishDirsWithFiles_Found()
    {
        var root = Path.Combine(Path.GetTempPath(), $"th-art-{Guid.NewGuid():N}");
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "artifacts", "publish", "linux-x64"));
            File.WriteAllText(Path.Combine(dir.FullName, "TerminalHub"), "bin");
            var found = ArtifactLocator.Find(root);
            Assert.Single(found);
            Assert.Single(found[0].Files);
            Assert.EndsWith("TerminalHub", found[0].Files[0]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Find_FromSubdir_WalksUp()
    {
        var root = Path.Combine(Path.GetTempPath(), $"th-art-{Guid.NewGuid():N}");
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "artifacts", "publish", "win-x64"));
            File.WriteAllText(Path.Combine(dir.FullName, "Setup.exe"), "x");
            var deep = Directory.CreateDirectory(Path.Combine(root, "src", "App", "bin"));
            var found = ArtifactLocator.Find(deep.FullName);
            Assert.Single(found);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Find_NoArtifacts_Empty()
    {
        var root = Path.Combine(Path.GetTempPath(), $"th-art-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            Assert.Empty(ArtifactLocator.Find(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Find_EmptyPublishDir_Empty()
    {
        var root = Path.Combine(Path.GetTempPath(), $"th-art-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "artifacts", "publish", "linux-x64"));
            Assert.Empty(ArtifactLocator.Find(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
