using TerminalHub.Core.Deploy;
using TerminalHub.Core.Settings;
using Xunit;

namespace TerminalHub.Tests;

public class LastPublishResultTests
{
    [Fact]
    public void SettingsStore_RoundTripsLastPublishResult()
    {
        var path = Path.Combine(Path.GetTempPath(), $"th-last-{Guid.NewGuid():N}.json");
        try
        {
            var finished = new DateTimeOffset(2026, 9, 28, 3, 4, 5, TimeSpan.Zero);
            var settings = new AppSettings
            {
                FontSize = 15,
                WorkspaceName = "DockWS",
            };
            PublishProfiles.Save(settings, "linux box", "/opt/repo", "linux-x64", "nightly", finished);
            LastPublishResults.Record(
                settings,
                LastPublishResults.Success,
                0,
                finished,
                12_000,
                "/opt/repo",
                "/opt/repo/artifacts/publish/linux-x64");

            new SettingsStore(path).Save(settings);
            var loaded = new SettingsStore(path).Load();

            Assert.Equal(15, loaded.FontSize);
            Assert.Equal("DockWS", loaded.WorkspaceName);
            Assert.Equal("linux box", PublishProfiles.Active(loaded)!.Name);

            var last = loaded.LastPublishResult;
            Assert.NotNull(last);
            Assert.Equal(LastPublishResults.Success, last!.Outcome);
            Assert.Equal(0, last.ExitCode);
            Assert.Equal(finished, last.FinishedAt);
            Assert.Equal(12_000, last.DurationMs);
            Assert.Equal("/opt/repo", last.RepoRoot);
            Assert.Equal("/opt/repo/artifacts/publish/linux-x64", last.ArtifactPath);
            Assert.Equal("成功 · 12s", LastPublishResults.FormatBadge(last));
            Assert.Contains("exit 0", LastPublishResults.FormatTooltip(last));
            Assert.Contains("12s", LastPublishResults.FormatTooltip(last));
            Assert.Contains(finished.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                LastPublishResults.FormatTooltip(last));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SettingsStore_MissingOrNullLastPublishResult_DoesNotCrash()
    {
        var path = Path.Combine(Path.GetTempPath(), $"th-last-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "FontSize": 14,
                  "WorkspaceName": "Keep"
                }
                """);
            var missing = new SettingsStore(path).Load();
            Assert.Null(missing.LastPublishResult);
            Assert.Equal(14, missing.FontSize);
            Assert.Equal("Keep", missing.WorkspaceName);
            Assert.Equal("", LastPublishResults.FormatBadge(missing.LastPublishResult));
            Assert.Equal("尚未打包", LastPublishResults.FormatTooltip(missing.LastPublishResult));
            Assert.False(LastPublishResults.CanOpen(missing.LastPublishResult));

            File.WriteAllText(path, """
                {
                  "FontSize": 16,
                  "LastPublishResult": null
                }
                """);
            var nully = new SettingsStore(path).Load();
            Assert.Null(nully.LastPublishResult);
            Assert.Equal(16, nully.FontSize);
            Assert.Equal("", LastPublishResults.FormatBadge(nully.LastPublishResult));

            File.WriteAllText(path, """
                {
                  "LastPublishResult": {
                    "Outcome": null,
                    "RepoRoot": null,
                    "ArtifactPath": null
                  }
                }
                """);
            var partial = new SettingsStore(path).Load();
            Assert.NotNull(partial.LastPublishResult);
            Assert.Equal("", partial.LastPublishResult!.Outcome);
            Assert.Equal("", partial.LastPublishResult.RepoRoot);
            Assert.Equal("", partial.LastPublishResult.ArtifactPath);
            Assert.Equal(0, partial.LastPublishResult.ExitCode);
            Assert.Equal(0, partial.LastPublishResult.DurationMs);
            Assert.Equal("", LastPublishResults.FormatBadge(partial.LastPublishResult));
            Assert.False(LastPublishResults.CanOpen(partial.LastPublishResult));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Record_PreservesLastSuccessPath_AndFormatsOutcomes()
    {
        var settings = new AppSettings();
        var t0 = new DateTimeOffset(2026, 9, 28, 1, 0, 0, TimeSpan.Zero);
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"th-art-{Guid.NewGuid():N}"));
        try
        {
            var success = LastPublishResults.Record(
                settings, "success", 0, t0, 12_000, "/repo", dir.FullName);
            Assert.Equal("成功 · 12s", LastPublishResults.FormatBadge(success));
            Assert.True(LastPublishResults.CanOpen(success));

            var failed = LastPublishResults.Record(
                settings, "fail", 1, t0.AddMinutes(1), 3_000, "/repo", "/should-not-replace");
            Assert.Equal(LastPublishResults.Fail, failed.Outcome);
            Assert.Equal(1, failed.ExitCode);
            Assert.Equal(dir.FullName, failed.ArtifactPath);
            Assert.Equal("失败 exit 1 · 3s", LastPublishResults.FormatBadge(failed));
            Assert.Contains("exit 1", LastPublishResults.FormatTooltip(failed));
            Assert.True(LastPublishResults.CanOpen(settings.LastPublishResult));

            var cancelled = LastPublishResults.Record(
                settings, "cancelled", -1, t0.AddMinutes(2), 1_000, "/repo", null);
            Assert.Equal(LastPublishResults.Cancelled, cancelled.Outcome);
            Assert.Equal(-1, cancelled.ExitCode);
            Assert.Equal(dir.FullName, cancelled.ArtifactPath);
            Assert.Equal("已取消 · 1s", LastPublishResults.FormatBadge(cancelled));

            var kept = LastPublishResults.Record(
                settings, "success", 0, t0.AddMinutes(3), 500, "  /repo  ", "  ");
            Assert.Equal(dir.FullName, kept.ArtifactPath);
            Assert.Equal("/repo", kept.RepoRoot);
            Assert.Equal("成功 · <1s", LastPublishResults.FormatBadge(kept));

            var replaced = LastPublishResults.Record(
                settings, " SUCCESS ", 0, t0, 65_000, "/other", Path.Combine(dir.FullName, "nope"));
            // Unknown / padded outcome that is not exactly success|fail|cancelled becomes fail,
            // and a fail does not replace the stored success path.
            Assert.Equal(LastPublishResults.Fail, replaced.Outcome);
            Assert.Equal(dir.FullName, replaced.ArtifactPath);

            var next = Directory.CreateDirectory(Path.Combine(dir.FullName, "linux-x64"));
            var again = LastPublishResults.Record(
                settings, LastPublishResults.Success, 0, t0, 65_000, "/other", next.FullName);
            Assert.Equal(next.FullName, again.ArtifactPath);
            Assert.Equal("成功 · 1m5s", LastPublishResults.FormatBadge(again));

            Directory.Delete(dir.FullName, true);
            Assert.False(LastPublishResults.CanOpen(settings.LastPublishResult));
            Assert.Equal(next.FullName, settings.LastPublishResult!.ArtifactPath);
        }
        finally
        {
            if (Directory.Exists(dir.FullName)) Directory.Delete(dir.FullName, true);
        }
    }

    [Fact]
    public void FormatDuration_Bounds()
    {
        Assert.Equal("<1s", LastPublishResults.FormatDuration(0));
        Assert.Equal("<1s", LastPublishResults.FormatDuration(999));
        Assert.Equal("1s", LastPublishResults.FormatDuration(1000));
        Assert.Equal("59s", LastPublishResults.FormatDuration(59_999));
        Assert.Equal("1m", LastPublishResults.FormatDuration(60_000));
        Assert.Equal("1m1s", LastPublishResults.FormatDuration(61_000));
        Assert.Equal("成功 · <1s", LastPublishResults.FormatBadge(new LastPublishResult
        {
            Outcome = LastPublishResults.Success,
            DurationMs = -20,
        }));
        Assert.Equal("", LastPublishResults.FormatBadge(new LastPublishResult { Outcome = "nope" }));
        Assert.Equal("尚未打包", LastPublishResults.FormatTooltip(null));
        Assert.False(LastPublishResults.CanOpen(new LastPublishResult
        {
            Outcome = LastPublishResults.Success,
            ArtifactPath = "",
        }));
    }

    [Fact]
    public void FormatLiveElapsed_MatchesDurationStyle()
    {
        Assert.Equal("0s", LastPublishResults.FormatLiveElapsed(0));
        Assert.Equal("0s", LastPublishResults.FormatLiveElapsed(-5));
        Assert.Equal("1s", LastPublishResults.FormatLiveElapsed(1200));
        Assert.Equal(LastPublishResults.FormatDuration(65_000),
            LastPublishResults.FormatLiveElapsed(65_000));
        Assert.Equal("打包中 · 0s", LastPublishResults.FormatLiveBadge(0));
        Assert.Equal("打包中 · 1s", LastPublishResults.FormatLiveBadge(1200));
        Assert.Equal($"打包中 · {LastPublishResults.FormatDuration(65_000)}",
            LastPublishResults.FormatLiveBadge(65_000));
    }

    [Fact]
    public void CanOpen_IsCopyGate()
    {
        var missing = new LastPublishResult
        {
            Outcome = LastPublishResults.Success,
            ArtifactPath = Path.Combine(Path.GetTempPath(), $"th-missing-{Guid.NewGuid():N}"),
        };
        Assert.False(LastPublishResults.CanOpen(missing));

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"th-copy-{Guid.NewGuid():N}"));
        try
        {
            var ok = new LastPublishResult
            {
                Outcome = LastPublishResults.Success,
                ArtifactPath = dir.FullName,
            };
            Assert.True(LastPublishResults.CanOpen(ok));
        }
        finally
        {
            if (Directory.Exists(dir.FullName)) Directory.Delete(dir.FullName, true);
        }
    }
}
