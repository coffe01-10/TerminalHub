using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.TextInput;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.App.ViewModels;
using TerminalHub.App.Views;
using TerminalHub.Core.Sessions;
using TerminalHub.Core.Settings;
using TerminalHub.Core.Terminal;
using TerminalHub.Pty;

namespace TerminalHub.DesktopMeasurements;

/// <summary>--ime-acceptance: production MainWindow, two real pwsh ConPTY
/// sessions with a fixed "IME&gt; " prompt, and REAL system input — SendInput
/// keystrokes through a thread-scoped zh-CN keyboard layout. Composition is
/// judged by the view's _preedit (set only via the OS text-input pipeline) and
/// by actual candidate windows; screenshots are real screen pixels of the test
/// HWND only, never RenderTargetBitmap and never the desktop. Internal
/// grid-coordinate assertions and system candidate-window observations are
/// recorded separately; every case is flagged NeedsVisualReview because the
/// real candidate position is judged by a human, not this report.
/// Exit code: 1 = failure/any fail case, 2 = partial/unverified/aborted, 0 = all pass.</summary>
public sealed partial class MeasurementApplication
{
    private static readonly FieldInfo PreeditField = typeof(TerminalView).GetField("_preedit", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo ImeClientField = typeof(TerminalView).GetField("_imeClient", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo CellWField = typeof(TerminalView).GetField("_cellW", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private sealed record CandidateRecord(string Class, string Rect);
    private sealed record ImeCase(string Name, string Status, bool NeedsVisualReview,
        bool? CandidatePositionAnchored, List<string> Facts, List<CandidateRecord> CandidateWindows,
        List<string> Screenshots);
    private sealed record ImeContext(MainWindow Window, MainWindowViewModel Vm, IntPtr Hwnd,
        string ReportDir, double SavedFont, TerminalSessionModel S1, bool GpuMode,
        List<(string Label, bool Anchored)> PositionChecks);

    private async Task AcceptImeAsync()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var reportDir = Path.GetDirectoryName(Program.Report)!;
        var directory = Path.Combine(Path.GetTempPath(), "terminalhub-ime-" + Guid.NewGuid().ToString("N"));
        var oldSettings = Environment.GetEnvironmentVariable("TERMINALHUB_SETTINGS_DIR");
        MainWindow? window = null; MainWindowViewModel? vm = null; string? failure = null;
        var cases = new List<ImeCase>(); var notes = new List<string>();
        IntPtr hwnd = IntPtr.Zero, oldLayout = IntPtr.Zero, activeHkl = IntPtr.Zero, loadedHkl = IntPtr.Zero;
        bool? oldOpen = null; double scaling = 0; var screens = 0; string? imeName = null;
        var gpuMode = Environment.GetCommandLineArgs().Contains("--ime-acceptance-gpu");
        List<string> adapters = [];
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("real IME acceptance is Windows-only");
            adapters = ImeNative.DisplayAdapters();
            notes.Add(gpuMode
                ? "--ime-acceptance-gpu: platform-default rendering (GPU if the driver stack offers one); screen-pixel captures are skipped — candidate position is verified by window geometry instead."
                : "software+RedirectionSurface rendering (forced); screenshots are BitBlt screen pixels.");
            Environment.SetEnvironmentVariable("TERMINALHUB_SETTINGS_DIR", directory);
            PtySessionFactory.UseMock = false;
            var store = new SettingsStore(Path.Combine(directory, "settings.json"));
            const string args = "-NoLogo -NoProfile -NoExit -Command \"if (Get-Module -ListAvailable PSReadLine) { Import-Module PSReadLine }; function global:prompt { 'IME> ' }\"";
            store.Save(new AppSettings
            {
                InspectorVisible = false, OutputVisible = false, Shell = ShellKind.PowerShell,
                Workspace = new()
                {
                    Sessions =
                    [
                        new() { Name = "ime-a", Shell = "pwsh", Arguments = args, WorkingDirectory = Environment.CurrentDirectory },
                        new() { Name = "ime-b", Shell = "pwsh", Arguments = args, WorkingDirectory = Environment.CurrentDirectory }
                    ]
                }
            });
            window = new MainWindow(store) { Width = 1440, Height = 900, Title = "Terminal Hub · IME acceptance",
                WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(40, 30) };
            desktop.MainWindow = window;
            window.Show();
            var area = window.Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1536, 864);
            if (area.Width < 1440 || area.Height < 900)
                (window.Width, window.Height) = (Math.Min(1100, area.Width - 60), Math.Min(700, area.Height - 60));
            window.Position = new PixelPoint(area.X + 40, area.Y + 30);
            vm = (MainWindowViewModel)window.DataContext!;
            await Until(() => vm.SessionCards.Count >= 2
                && vm.SessionCards.All(c => c.Model.Emulator.Buffer.TailText(30).Contains("IME>")));
            hwnd = Hwnd(window);
            window.Activate();
            if (!ImeNative.EnsureForeground(hwnd)) notes.Add("SetForegroundWindow could not own the foreground; real-key cases may abort.");
            oldLayout = ImeNative.GetKeyboardLayout(0);
            oldOpen = ImeNative.GetImeOpen(hwnd);
            activeHkl = ImeNative.ActivateChineseIme(hwnd, out var loaded);
            if (loaded) loadedHkl = activeHkl;
            // Keyboard layouts alone (hkl=0x8040804) cannot compose — a TSF
            // TIP must be selected instead. Win+Space is the supported
            // thread-local switch; the TSF COM activation stays as a
            // diagnostic fallback (it is unregistered on some systems).
            if (ImeNative.ImeDescription(activeHkl) is null)
            {
                ImeNative.CycleInputLanguage(hwnd);
                await Task.Delay(800); // let the profile switch settle
                activeHkl = ImeNative.GetKeyboardLayout(0);
                notes.Add($"no IMM-described IME on the layout list; sent Win+Space to cycle this thread's input profile → hkl=0x{(long)activeHkl:x}");
                if (ImeNative.ImeDescription(activeHkl) is null)
                {
                    var tipHr = ImeNative.ActivateMsPinyinForProcess();
                    notes.Add($"still no IMM description; TSF ActivateProfile(Microsoft Pinyin, process-scoped) hr=0x{tipHr:x8}");
                    if (tipHr == 0)
                    {
                        await Task.Delay(500); // let the TIP attach to our input thread
                        activeHkl = ImeNative.GetKeyboardLayout(0);
                    }
                }
            }
            if (activeHkl == IntPtr.Zero || ((long)activeHkl & 0xFFFF) != 0x0804)
                notes.Add($"no zh-CN input method active on the test thread (hkl=0x{(long)activeHkl:x}); composition cases may stay unverified.");
            imeName = ImeNative.ImeDescription(activeHkl);
            notes.Add($"active input method on test thread: hkl=0x{(long)activeHkl:x} immDescription={Q(imeName)} (HKL alone does not prove which TSF profile answered)");
            ImeNative.SetImeOpen(hwnd, true);
            scaling = window.RenderScaling; screens = window.Screens.All.Count;
            var ctx = new ImeContext(window, vm, hwnd, reportDir, vm.FontSize, vm.SessionCards[0].Model,
                gpuMode, new List<(string, bool)>());
            // Let activation + first frames settle, then take one window shot
            // as review material — pixels are judged by a human, not this tool.
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(static () => { }, Avalonia.Threading.DispatcherPriority.Render);
            await Task.Delay(400);
            if (!gpuMode)
            {
                var preflight = Path.Combine(reportDir, "ime-window.png");
                if (ImeNative.GetForegroundWindow() != hwnd) throw new ForegroundAbort();
                ImeNative.DwmFlush();
                ImeNative.GetWindowRect(hwnd, out var wr);
                ImeNative.Capture(wr, preflight);
                notes.Add($"preflight screenshot {preflight} requires visual review");
            }

            async Task RunCase(string name, Func<List<string>, Task<(string Status, List<string> Shots)>> body)
            {
                var facts = new List<string>();
                ctx.PositionChecks.Clear();
                bool? Pos() => ctx.PositionChecks.Count == 0 ? null : ctx.PositionChecks.All(p => p.Anchored);
                try { var r = await body(facts); cases.Add(new ImeCase(name, r.Status, !gpuMode, Pos(), facts, Candidates(), r.Shots)); }
                catch (ForegroundAbort ex) { facts.Add(ex.Message); cases.Add(new ImeCase(name, "aborted", !gpuMode, Pos(), facts, Candidates(), [])); }
                catch (Exception ex) { facts.Add(ex.Message); cases.Add(new ImeCase(name, "fail", !gpuMode, Pos(), facts, Candidates(), [])); }
            }

            await RunCase("empty-input", f => CaseEmptyInput(ctx, f));
            await RunCase("enter-does-not-submit", f => CaseEnterDoesNotSubmit(ctx, f));
            await RunCase("cjk-arrows", f => CaseCjkArrows(ctx, f));
            await RunCase("right-edge", f => CaseRightEdge(ctx, f));
            await RunCase("split-font-popout", f => CaseSplitFontPopout(ctx, f));
        }
        catch (Exception ex) { failure = ex.ToString(); }
        finally
        {
            // Restore this thread's original layout/IME state, then close only
            // the windows and PTYs this run created.
            if (oldLayout != IntPtr.Zero) ImeNative.ActivateKeyboardLayout(oldLayout, 0);
            if (hwnd != IntPtr.Zero && oldOpen is { } open) ImeNative.SetImeOpen(hwnd, open);
            if (loadedHkl != IntPtr.Zero && loadedHkl != oldLayout) ImeNative.UnloadKeyboardLayout(loadedHkl);
            window?.Close();
            Environment.SetEnvironmentVariable("TERMINALHUB_SETTINGS_DIR", oldSettings);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            File.WriteAllText(Program.Report, JsonSerializer.Serialize(new
            {
                CapturedUtc = DateTime.UtcNow,
                Rendering = gpuMode ? "platform-default (GPU when offered by the driver stack)" : "Software+RedirectionSurface (forced)",
                DisplayAdapters = adapters,
                HardwareGpuPresent = adapters.Any(a => a.Contains("[hardware]", StringComparison.Ordinal)),
                Method = "Production MainWindow + two real pwsh ConPTY sessions (PSReadLine, fixed 'IME> ' prompt). Keystrokes via strictly foreground-guarded SendInput batches — any non-test foreground aborts the case; zh-CN layout activated on this thread only and restored afterwards. Composition observed via _preedit and real candidate windows; screenshots are BitBlt screen pixels clipped to the test HWND rect only and only exist in software mode. Candidate POSITION is auto-judged by window geometry: the caret rect the app reports to the OS (CursorRectangle) is mapped via PointToScreen and each visible IME-class candidate HWND's GetWindowRect must anchor near it (caret column inside the candidate's horizontal reach ±clamp slack, candidate top just below — or bottom just above — the caret line). NeedsVisualReview stays true only where screenshots exist.",
                Screens = screens, Scaling = scaling,
                ActiveKeyboardLayout = $"0x{(long)activeHkl:x}", ActiveImeDescription = imeName,
                Notes = notes, Cases = cases, Failure = failure
            }, new JsonSerializerOptions { WriteIndented = true }));
            var code = failure is not null || cases.Any(c => c.Status.StartsWith("fail", StringComparison.Ordinal)) ? 1
                : cases.Any(c => c.Status.StartsWith("partial", StringComparison.Ordinal)
                    || c.Status.StartsWith("unverified", StringComparison.Ordinal)
                    || c.Status.StartsWith("aborted", StringComparison.Ordinal)) ? 2 : 0;
            desktop.Shutdown(code);
        }
    }

    private static IntPtr Hwnd(WindowBase w) => w.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    private static TerminalView ViewFor(ImeContext ctx, TerminalSessionModel s) => ctx.Window
        .GetVisualDescendants().OfType<TerminalView>()
        .Single(v => !v.IsPreview && ReferenceEquals(v.Emulator, s.Emulator) && v.IsEffectivelyVisible);
    private static async Task<TerminalView?> PollView(ImeContext ctx, TerminalSessionModel s, int ms)
    {
        var until = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < until)
        {
            var v = ctx.Window.GetVisualDescendants().OfType<TerminalView>()
                .FirstOrDefault(v => !v.IsPreview && ReferenceEquals(v.Emulator, s.Emulator) && v.IsEffectivelyVisible);
            if (v is not null) return v;
            await Task.Delay(80);
        }
        return null;
    }
    private static string SessionName(ImeContext ctx, TerminalEmulator? emu)
        => ctx.Vm.SessionCards.FirstOrDefault(c => ReferenceEquals(c.Model.Emulator, emu))?.Model.Name ?? "detached/unknown";
    private static string? Preedit(TerminalView v) => (string?)PreeditField.GetValue(v);
    private static Rect ClientRect(TerminalView v) => ((TextInputMethodClient)ImeClientField.GetValue(v)!).CursorRectangle;
    private static double CellW(TerminalView v) => (double)CellWField.GetValue(v)!;
    private static TerminalFrame Frame(TerminalSessionModel s) => s.Emulator.Buffer.CaptureFrame();
    private static string InputLine(TerminalSessionModel s)
    {
        var f = Frame(s);
        return ScreenBuffer.FlattenRow(f.Cells.AsSpan(f.CursorY * f.Columns, f.Columns)).Text;
    }
    private static int PromptCount(TerminalSessionModel s)
    {
        var text = s.Emulator.Buffer.TailText(400);
        var (count, i) = (0, 0);
        while ((i = text.IndexOf("IME>", i, StringComparison.Ordinal)) >= 0) { count++; i += 4; }
        return count;
    }
    private static bool Cjk(char c) => c is >= '㐀' and <= '鿿';
    private static string Q(string? s) => s is null ? "null" : $"'{s}'";
    private static string Describe(List<(string Class, ImeNative.Rect Bounds)> list)
        => list.Count == 0 ? "none" : string.Join("; ", list.Select(c => $"{c.Class}@{c.Bounds}"));
    private static List<CandidateRecord> Candidates()
        => ImeNative.CandidateWindows().Select(c => new CandidateRecord(c.Class, c.Bounds.ToString())).ToList();
    private static void LettersNi(IntPtr owner) { ImeNative.Tap(owner, (ushort)'N'); ImeNative.Tap(owner, (ushort)'I'); }
    private static async Task<bool> Poll(Func<bool> cond, int ms)
    {
        var until = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < until) { if (cond()) return true; await Task.Delay(80); }
        return cond();
    }
    private static async Task Until(Func<bool> cond)
    {
        if (!await Poll(cond, 25000)) throw new TimeoutException("the real pwsh 'IME> ' prompt did not arrive");
    }
    /// <summary>Screenshot the test HWND's own rect — nothing else. Requires
    /// our window foreground at capture time, otherwise the case aborts.
    /// GPU/default-rendering runs skip pixels entirely: under real GPU
    /// composition the framebuffer BitBlt reads may not contain this window.
    /// </summary>
    private static void TakeShot(List<string> shots, ImeContext ctx, IntPtr owner, string file)
    {
        if (ctx.GpuMode) return;
        if (ImeNative.GetForegroundWindow() != owner) throw new ForegroundAbort();
        ImeNative.DwmFlush();
        ImeNative.GetWindowRect(owner, out var r);
        var path = Path.Combine(ctx.ReportDir, file);
        ImeNative.Capture(r, path);
        shots.Add(path);
    }
    /// <summary>Auto-judge whether a system candidate window is anchored at the
    /// composition caret — renderer-independent window geometry, valid under
    /// GPU composition where pixel captures are not. The caret rect the app
    /// reports to the OS (CursorRectangle, client coords) is mapped with
    /// PointToScreen; a candidate HWND anchors when the caret column lands
    /// inside its horizontal reach (right-edge clamping shifts the window left,
    /// so slack extends leftward) and its top sits just below — or its bottom
    /// just above — the caret line. Returns null when nothing to judge.</summary>
    private static bool? JudgeCandidatePosition(ImeContext ctx, TerminalView view,
        List<(string Class, ImeNative.Rect Bounds)> cands, string label, List<string> facts)
    {
        if (cands.Count == 0) { facts.Add($"{label}CandidateAnchor=none (no candidate window)"); return null; }
        var r = ClientRect(view);
        var tl = view.PointToScreen(r.TopLeft);
        var bl = view.PointToScreen(r.BottomLeft);
        var anchored = false;
        foreach (var c in cands)
        {
            var b = c.Bounds;
            var dBelow = b.Top - bl.Y;   // candidate top vs caret bottom (below-line anchoring)
            var dAbove = tl.Y - b.Bottom; // caret top vs candidate bottom (above-line anchoring)
            var horiz = b.Left - tl.X <= 32 && tl.X - b.Right <= 320; // caret column inside span, slack for screen-edge clamping
            var vert = dBelow is >= -16 and <= 64 || dAbove is >= -16 and <= 48;
            if (horiz && vert) anchored = true;
            facts.Add($"{label}Candidate {c.Class} rect=({b.Left},{b.Top},{b.Width}x{b.Height}) caret=({tl.X},{tl.Y}-{bl.Y}) dBelow={dBelow} dAbove={dAbove} anchored={horiz && vert}");
        }
        ctx.PositionChecks.Add((label, anchored));
        return anchored;
    }
    private static async Task FreshPrompt(ImeContext ctx, TerminalSessionModel s, List<string> facts)
    {
        var n = PromptCount(s);
        ImeNative.CtrlC(ctx.Hwnd);
        if (!await Poll(() => PromptCount(s) > n, 5000)) facts.Add("Ctrl+C produced no new prompt within 5s");
    }
    private static async Task CancelComposition(TerminalView view, IntPtr owner)
    {
        if (Preedit(view) is null) return;
        ImeNative.Tap(owner, 0x1B);
        await Poll(() => Preedit(view) is null, 6000);
    }
    private static List<string> PreeditHolders(ImeContext ctx, TerminalView except)
        => ctx.Window.GetVisualDescendants().OfType<TerminalView>()
            .Where(v => !v.IsPreview && !ReferenceEquals(v, except) && Preedit(v) is { Length: > 0 })
            .Select(v => SessionName(ctx, v.Emulator)).ToList();
    private static bool FocusedOn(ImeContext ctx, TerminalView view)
        => ReferenceEquals(ctx.Window.FocusManager?.GetFocusedElement(), view);
}
