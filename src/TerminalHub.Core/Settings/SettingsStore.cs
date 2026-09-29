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

    public SettingsStore(string? path = null)
    {
        _path = path ?? DefaultPath();
    }

    public static string DefaultPath()
    {
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
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt settings should never block startup; fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
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
