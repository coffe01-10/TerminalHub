using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TerminalHub.Core.Localization;

/// <summary>UI messages only. Never pass terminal output, user names, commands or paths here.</summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public static Localizer Current { get; } = new();
    private sealed record Message(string Id, string Zh, string En);
    private readonly Message[] _messages;
    private readonly Dictionary<string, Message> _byId, _bySource;
    private readonly (Message Message, Regex Pattern)[] _templates;
    private readonly Dictionary<string, (string DefaultLanguage, Dictionary<string, Dictionary<string, string>> Resources)> _modules = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;
    public string Selection { get; private set; } = "system";
    public string Language { get; private set; } = "zh-CN";
    public int Revision { get; private set; }

    private Localizer()
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream("TerminalHub.Core.Localization.Messages.json")!;
        _messages = JsonSerializer.Deserialize<Message[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        _byId = _messages.ToDictionary(m => m.Id);
        _bySource = _messages.ToDictionary(m => m.Zh);
        _templates = _messages.Where(m => Regex.IsMatch(m.Zh, @"\{\d+\}"))
            .OrderByDescending(m => Regex.Replace(m.Zh, @"\{\d+\}", "").Length)
            .Select(m => (m, new Regex("^" + Regex.Replace(Regex.Escape(m.Zh), @"\\\{(\d+)}", "(?<p$1>.*?)") + "$", RegexOptions.Singleline))).ToArray();
    }

    public void SetLanguage(string selection, CultureInfo? systemCulture = null)
    {
        Selection = selection is "en" or "zh-CN" ? selection : "system";
        var next = Selection == "system" ? ((systemCulture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "zh" ? "zh-CN" : "en") : Selection;
        if (Language == next) return;
        Language = next; Revision++;
        PropertyChanged?.Invoke(this, new(nameof(Revision)));
        PropertyChanged?.Invoke(this, new(nameof(Language)));
        LanguageChanged?.Invoke();
    }

    public string Get(string id) => _byId.TryGetValue(id, out var message) ? Render(message) : id;
    public string Format(string source, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, Translate(source), arguments);
    private string Render(Message message) => Language == "en" ? message.En : message.Zh;

    public string Translate(string source)
    {
        if (Language != "en" || source.Length == 0) return source;
        if (_bySource.TryGetValue(source, out var exact)) return exact.En;
        foreach (var (message, pattern) in _templates)
        {
            var match = pattern.Match(source);
            if (!match.Success) continue;
            return Regex.Replace(message.En, @"\{(\d+)\}", placeholder => match.Groups["p" + placeholder.Groups[1].Value].Value);
        }
        // A few messages are built from a UI prefix and a raw name/path. Only the
        // declared prefix is translated; the suffix remains byte-for-byte unchanged.
        foreach (var message in _messages.Where(m => m.Zh.EndsWith(' ') || m.Zh.EndsWith('：') || m.Zh.EndsWith(':')).OrderByDescending(m => m.Zh.Length))
            if (source.StartsWith(message.Zh, StringComparison.Ordinal)) return message.En + source[message.Zh.Length..];
        return source;
    }

    /// <summary>Language resource contract for independently built extension modules.</summary>
    public void RegisterResources(string moduleId, string defaultLanguage, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources)
    {
        _modules[moduleId] = (defaultLanguage, resources.ToDictionary(r => r.Key, r => r.Value.ToDictionary(v => v.Key, v => v.Value)));
        RefreshResources();
    }
    public void UnregisterResources(string moduleId) { _modules.Remove(moduleId); RefreshResources(); }
    public string GetModuleText(string moduleId, string key, string fallback)
    {
        if (!_modules.TryGetValue(moduleId, out var module)) return fallback;
        if (module.Resources.TryGetValue(Language, out var current) && current.TryGetValue(key, out var translated)) return translated;
        return module.Resources.TryGetValue(module.DefaultLanguage, out var defaults) && defaults.TryGetValue(key, out var value) ? value : fallback;
    }
    private void RefreshResources()
    { Revision++; PropertyChanged?.Invoke(this, new(nameof(Revision))); LanguageChanged?.Invoke(); }
}
