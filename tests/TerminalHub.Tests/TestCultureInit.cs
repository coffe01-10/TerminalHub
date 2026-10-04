using System.Globalization;
using System.Runtime.CompilerServices;

namespace TerminalHub.Tests;

// ~35 tests assert zh-CN UI strings. Localizer's "system" selection resolves via
// CultureInfo.CurrentUICulture, so an en-US machine would see English and those
// tests fail purely on locale. Pin the test process to a zh-CN UI culture so
// "system" resolves the same way everywhere (the author's dev box is zh-CN).
// Tests that exercise English still do so explicitly via LanguageIndex /
// SetLanguage("en").
internal static class TestCultureInit
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var culture = CultureInfo.GetCultureInfo("zh-CN");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
