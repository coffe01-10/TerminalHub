using System.Text.Json;

namespace TerminalHub.Core.Settings;

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON under the per-user config dir.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    /// <summary>True when an existing file could not be read. Saves stay off for this process so a later write does not replace that file with defaults.</summary>
    public bool LoadFailed { get; private set; }

    public SettingsStore(string? path = null)
    {
        _path = path ?? DefaultPath();
    }

    public static string DefaultPath()
    {
        // Test isolation: when TERMINALHUB_SETTINGS_DIR is set, every store
        // gets its own throwaway file so windows opened in one test can't
        // leak persisted state (e.g. the saved workspace) into the next.
        var overrideDir = Environment.GetEnvironmentVariable("TERMINALHUB_SETTINGS_DIR");
        if (!string.IsNullOrEmpty(overrideDir))
            return Path.Combine(overrideDir, $"settings-{Guid.NewGuid():N}.json");

        var dir = OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TerminalHub")
            : Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
                "terminalhub");
        return Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new AppSettings();
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is null)
            {
                LoadFailed = true;
                return new AppSettings();
            }
            return loaded;
        }
        catch
        {
            // Corrupt or unreadable settings must not block startup, and must not
            // be overwritten by a later save of the in-memory defaults.
            LoadFailed = true;
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        if (LoadFailed) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            // Write-then-move: a crash mid-write must never leave a truncated
            // settings.json behind (WriteAllText truncates in place).
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Persistence must never break the app — a full/readonly disk or an
            // antivirus lock is survivable; losing the window on exit is not.
        }
    }
}
