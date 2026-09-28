using TerminalHub.Core.Deploy;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Ssh;
using Xunit;

namespace TerminalHub.Tests;

public class PublishProfileTests
{
    private static string TempSettingsPath() =>
        Path.Combine(Path.GetTempPath(), $"th-profiles-{Guid.NewGuid():N}.json");

    [Fact]
    public void Save_Switch_Delete_AndRoundTrip()
    {
        var path = TempSettingsPath();
        var t0 = new DateTimeOffset(2026, 9, 28, 1, 2, 3, TimeSpan.Zero);
        var t1 = t0.AddHours(1);
        var t2 = t0.AddHours(2);
        try
        {
            var store = new SettingsStore(path);
            var settings = new AppSettings
            {
                WorkspaceName = "KeepMe",
                SshHosts = [new SshHost { Name = "local", Host = "127.0.0.1", User = "tester", Port = 2222 }],
            };

            Assert.Null(PublishProfiles.Save(settings, "  ", "/tmp/nope", "linux-x64", "", t0));
            Assert.Empty(settings.PublishProfiles);

            var linux = PublishProfiles.Save(settings, "  Linux box  ", "/tmp/linux", "LINUX", "nightly", t0);
            Assert.NotNull(linux);
            Assert.Equal("Linux box", linux!.Name);
            Assert.Equal(Path.GetFullPath("/tmp/linux"), linux.RepoRoot);
            Assert.Equal(PublishProfiles.LinuxRid, linux.Rid);
            Assert.Equal("nightly", linux.Note);
            Assert.Equal(t0, linux.CreatedAt);
            Assert.Equal(t0, linux.LastUsedAt);
            Assert.Equal(linux.Id, settings.ActivePublishProfileId);

            var same = PublishProfiles.Save(settings, "linux box", "/tmp/linux-2", "win-x64", "updated", t1);
            Assert.Same(linux, same);
            Assert.Equal(t0, linux.CreatedAt);
            Assert.Equal(t1, linux.LastUsedAt);
            Assert.Equal(PublishProfiles.WindowsRid, linux.Rid);
            Assert.Single(settings.PublishProfiles);

            var win = PublishProfiles.Save(settings, "Windows", "", "windows", "", t1.AddMinutes(30));
            Assert.NotNull(win);
            Assert.Equal("", win!.RepoRoot);
            Assert.Equal(PublishProfiles.WindowsRid, win.Rid);
            Assert.Equal(win.Id, settings.ActivePublishProfileId);
            Assert.Equal(2, settings.PublishProfiles.Count);

            Assert.Equal(win, PublishProfiles.List(settings)[0]);
            Assert.True(PublishProfiles.Activate(settings, linux.Name, t2));
            Assert.Equal(linux.Id, settings.ActivePublishProfileId);
            Assert.Equal(t2, linux.LastUsedAt);
            Assert.Equal(linux, PublishProfiles.List(settings)[0]);
            Assert.False(PublishProfiles.Activate(settings, "missing", t2));
            Assert.Equal(linux.Id, settings.ActivePublishProfileId);

            store.Save(settings);
            var loaded = new SettingsStore(path).Load();
            Assert.Equal("KeepMe", loaded.WorkspaceName);
            Assert.Equal("local", Assert.Single(loaded.SshHosts).Name);
            Assert.Equal(2, loaded.PublishProfiles.Count);
            var loadedActive = PublishProfiles.Active(loaded);
            Assert.NotNull(loadedActive);
            Assert.Equal("linux box", loadedActive!.Name);
            Assert.Equal(Path.GetFullPath("/tmp/linux-2"), loadedActive.RepoRoot);
            Assert.Equal(PublishProfiles.WindowsRid, loadedActive.Rid);
            Assert.Equal("updated", loadedActive.Note);
            Assert.Equal(t0, loadedActive.CreatedAt);
            Assert.Equal(t2, loadedActive.LastUsedAt);
            Assert.Equal(loadedActive.Id, loaded.ActivePublishProfileId);

            Assert.True(PublishProfiles.Delete(loaded, "Windows"));
            Assert.Equal(loadedActive.Id, loaded.ActivePublishProfileId);
            Assert.Single(loaded.PublishProfiles);
            Assert.True(PublishProfiles.Delete(loaded, loadedActive.Id));
            Assert.Equal("", loaded.ActivePublishProfileId);
            Assert.Empty(loaded.PublishProfiles);
            Assert.False(PublishProfiles.Delete(loaded, loadedActive.Id));

            store.Save(loaded);
            var empty = store.Load();
            Assert.Empty(empty.PublishProfiles);
            Assert.Equal("", empty.ActivePublishProfileId);
            Assert.Equal("KeepMe", empty.WorkspaceName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacySettings_MissingProfileKeys_StayEmpty()
    {
        var path = TempSettingsPath();
        try
        {
            File.WriteAllText(path, """{"FontSize": 18, "WorkspaceName": "Old"}""");
            var loaded = new SettingsStore(path).Load();
            Assert.NotNull(loaded.PublishProfiles);
            Assert.Empty(loaded.PublishProfiles);
            Assert.Equal("", loaded.ActivePublishProfileId);
            Assert.Equal(18, loaded.FontSize);
            Assert.Equal("Old", loaded.WorkspaceName);
            Assert.Null(PublishProfiles.Active(loaded));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Resolve_ExplicitStartWins_EmptyRootUsesCwd_RidSelectsScript()
    {
        var settings = new AppSettings();
        var cwd = Path.Combine(Path.GetTempPath(), "cwd-fallback");
        Assert.Equal(cwd, PublishProfiles.ResolveStartDirectory(settings, null, cwd));
        Assert.Equal(PublishPlanner.CurrentPlatform,
            PublishProfiles.ResolvePlatform(settings, PublishPlanner.CurrentPlatform));

        PublishProfiles.Save(settings, "pinned", "/opt/repo", "win-x64", "");
        Assert.Equal(Path.GetFullPath("/opt/repo"),
            PublishProfiles.ResolveStartDirectory(settings, null, cwd));
        Assert.Equal("/elsewhere",
            PublishProfiles.ResolveStartDirectory(settings, "/elsewhere", cwd));
        Assert.Equal(PublishPlatform.Windows,
            PublishProfiles.ResolvePlatform(settings, PublishPlatform.Linux));

        PublishProfiles.Save(settings, "host", "", "nope", "");
        Assert.Equal(cwd, PublishProfiles.ResolveStartDirectory(settings, "  ", cwd));
        Assert.Equal(PublishPlatform.Linux,
            PublishProfiles.ResolvePlatform(settings, PublishPlatform.Linux));
        Assert.False(PublishProfiles.TryParseRid("", out _));
        Assert.False(PublishProfiles.TryParseRid("osx-arm64", out _));
        Assert.True(PublishProfiles.TryParseRid("linux", out var linux));
        Assert.Equal(PublishPlatform.Linux, linux);
    }

    [Fact]
    public void SuggestName_SkipsTaken()
    {
        var settings = new AppSettings();
        Assert.Equal("linux-x64", PublishProfiles.SuggestName(settings, "linux"));
        PublishProfiles.Save(settings, "linux-x64", "", "linux-x64", "");
        Assert.Equal("linux-x64-2", PublishProfiles.SuggestName(settings, "linux-x64"));
        PublishProfiles.Save(settings, "linux-x64-2", "", "", "");
        Assert.Equal("linux-x64-3", PublishProfiles.SuggestName(settings, "Linux-X64"));
        Assert.Equal("profile", PublishProfiles.SuggestName(settings, ""));
    }

    [Fact]
    public void TouchActive_UpdatesLastUsed_OnlyWhenActive()
    {
        var settings = new AppSettings();
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Null(PublishProfiles.TouchActive(settings, t0));

        var saved = PublishProfiles.Save(settings, "a", "", "", "", t0);
        var t1 = t0.AddMinutes(5);
        var touched = PublishProfiles.TouchActive(settings, t1);
        Assert.Same(saved, touched);
        Assert.Equal(t1, saved!.LastUsedAt);
        Assert.Equal(t0, saved.CreatedAt);
    }

    [Fact]
    public void FormatDockLabel_NameAndOptionalRid()
    {
        Assert.Equal("", PublishProfiles.FormatDockLabel(null));
        Assert.Equal("", PublishProfiles.FormatDockLabel(new PublishProfile { Name = "  " }));
        Assert.Equal("", PublishProfiles.FormatDockLabel(new PublishProfile { Name = "", Rid = "linux-x64" }));
        Assert.Equal("默认", PublishProfiles.FormatDockLabel(new PublishProfile { Name = "默认" }));
        Assert.Equal("默认 · linux-x64",
            PublishProfiles.FormatDockLabel(new PublishProfile { Name = " 默认 ", Rid = " linux-x64 " }));
        Assert.Equal("win box · win-x64",
            PublishProfiles.FormatDockLabel(new PublishProfile { Name = "win box", Rid = PublishProfiles.WindowsRid }));
    }
}
