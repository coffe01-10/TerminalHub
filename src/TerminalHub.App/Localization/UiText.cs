using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using TerminalHub.Core.Localization;

namespace TerminalHub.App.Localization;

public sealed class TranslateExtension : MarkupExtension
{
    public IBinding Binding { get; set; }
    public TranslateExtension(IBinding binding) => Binding = binding;
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Binding(Binding);
}

public sealed class TextExtension : MarkupExtension
{
    public string Key { get; set; }
    public string? ModuleId { get; set; }
    public string Fallback { get; set; } = "";
    public TextExtension(string key) => Key = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => new MultiBinding
    {
        Bindings = { new Binding(nameof(Localizer.Revision)) { Source = Localizer.Current } },
        Converter = new FuncMultiValueConverter<object, string>(_ => ModuleId is null ? Localizer.Current.Get(Key)
            : Localizer.Current.GetModuleText(ModuleId, Key, Fallback))
    };
}

public static class UiText
{
    public static Localizer Locale => Localizer.Current;
    private static readonly IMultiValueConverter Converter = new FuncMultiValueConverter<object, string>(values =>
        values.FirstOrDefault() is string source ? Localizer.Current.Translate(source) : values.FirstOrDefault()?.ToString() ?? "");
    public static MultiBinding Binding(string source) => Binding(new Binding { Source = source });
    public static MultiBinding Binding(IBinding source) => new()
    {
        Bindings = { source, new Binding(nameof(Localizer.Revision)) { Source = Localizer.Current } },
        Converter = Converter
    };
    public static void Set(AvaloniaObject target, AvaloniaProperty property, string source) => target.Bind(property, Binding(source));
}
