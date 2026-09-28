using TerminalHub.Core.Deploy;
using Xunit;

namespace TerminalHub.Tests;

public class RecentArtifactTests
{
    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), $"th-recent-{Guid.NewGuid():N}");

    [Fact]
    public void List_EmptyWhenNone_AndWhenPublishDirHasNoFiles()
    {
        var root = TempRoot();
        try
        {
            Directory.CreateDirectory(root);
            Assert.Empty(RecentArtifactList.List(root));
            Directory.CreateDirectory(Path.Combine(root, "artifacts", "publish", "linux-x64"));
            Assert.Empty(RecentArtifactList.List(root));
            Assert.Empty(RecentArtifactList.List(null));
            Assert.Empty(RecentArtifactList.List(""));
            Assert.Empty(RecentArtifactList.List(root, limit: 0));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void List_OrdersByFileMtimeDesc_WithRidSizeAndWalkUp()
    {
        var root = TempRoot();
        try
        {
            var olderDir = Directory.CreateDirectory(Path.Combine(root, "artifacts", "publish", "linux-x64"));
            var newerDir = Directory.CreateDirectory(Path.Combine(root, "artifacts", "publish", "win-x64"));
            var olderFile = Path.Combine(olderDir.FullName, "TerminalHub");
            var olderPdb = Path.Combine(olderDir.FullName, "TerminalHub.pdb");
            var newerFile = Path.Combine(newerDir.FullName, "Setup.exe");
            File.WriteAllText(olderFile, "abc");
            File.WriteAllText(olderPdb, "de");
            File.WriteAllText(newerFile, "w");
            var older = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
            var newer = older.AddHours(5);
            File.SetLastWriteTimeUtc(olderFile, older);
            File.SetLastWriteTimeUtc(olderPdb, older.AddMinutes(-10));
            File.SetLastWriteTimeUtc(newerFile, newer);

            var deep = Directory.CreateDirectory(Path.Combine(root, "src", "App"));
            var listed = RecentArtifactList.List(deep.FullName);
            Assert.Equal(2, listed.Count);
            Assert.Equal("win-x64", listed[0].Rid);
            Assert.Equal(newerDir.FullName, listed[0].Path);
            Assert.Equal(1, listed[0].SizeBytes);
            Assert.Equal(1, listed[0].FileCount);
            Assert.Equal(newer, listed[0].Modified.UtcDateTime);

            Assert.Equal("linux-x64", listed[1].Rid);
            Assert.Equal(5, listed[1].SizeBytes);
            Assert.Equal(2, listed[1].FileCount);
            Assert.Equal(older, listed[1].Modified.UtcDateTime);
            Assert.True(listed[0].Modified > listed[1].Modified);

            var top = Assert.Single(RecentArtifactList.List(root, limit: 1));
            Assert.Equal("win-x64", top.Rid);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
