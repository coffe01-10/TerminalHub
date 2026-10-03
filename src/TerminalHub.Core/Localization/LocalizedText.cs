namespace TerminalHub.Core.Localization;

/// <summary>An explicitly owned UI message, or raw content embedded in a localized view.</summary>
public sealed class LocalizedText
{
    private readonly Func<string> _render;
    public LocalizedText(string source, bool translate = true) => _render = () => translate ? Localizer.Current.Translate(source) : source;
    public LocalizedText(Func<string> render) => _render = render;
    public override string ToString() => _render();
}
