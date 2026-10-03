using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Skia;

namespace TerminalHub.Tests;

// Deterministic fix for intermittent full-suite failures:
// System.InvalidOperationException "Unable to locate 'Avalonia.Platform.IFontManagerImpl'"
// thrown from FontManager.Current under TextBlock.Measure inside
// HeadlessUnitTestSession.EnsureApplication (Avalonia.Headless 11.3.2).
//
// For every dispatched action EnsureApplication first swaps AvaloniaLocator.Current
// to an empty scope locator, then calls Dispatcher.ResetForUnitTests, which drains
// jobs left pending by earlier tests (Dispatcher.Queue.cs), and only afterwards runs
// SetupUnsafe, where SkiaPlatform.Initialize binds IFontManagerImpl/ITextShaperImpl.
// A drained job that touches text layout therefore resolves FontManager.Current
// against a still-empty locator chain and throws. Filtered runs start with no
// pending jobs, so only full-suite runs reproduce it.
//
// Binding the Skia font services and FontManager on the root locator up front makes
// that chain resolvable for every thread and every scope (scope locators fall back
// to the root through their parent chain), closing the window deterministically.
internal static class HeadlessFontWarmStart
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SkiaPlatform.Initialize();
        _ = FontManager.Current;
    }
}
