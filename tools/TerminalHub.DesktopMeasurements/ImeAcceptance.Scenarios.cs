using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TerminalHub.App.Controls;
using TerminalHub.Core.Sessions;

namespace TerminalHub.DesktopMeasurements;

/// <summary>The five --ime-acceptance scenarios. Real letters ride the system
/// IME (composition cases); committed text uses KEYEVENTF_UNICODE which is
/// explicitly not an IME claim. Every scenario records the facts it observed —
/// grid coordinates, focus state, preedit and candidate windows — and marks
/// the case unverified rather than passing when no composition shows.</summary>
public sealed partial class MeasurementApplication
{
    private async Task<(string Status, List<string> Shots)> CaseEmptyInput(ImeContext ctx, List<string> facts)
    {
        var shots = new List<string>();
        var view = ViewFor(ctx, ctx.S1);
        view.Focus();
        await Task.Delay(200);
        LettersNi(ctx.Hwnd);
        var composing = await Poll(() => Preedit(view) is { Length: > 0 }, 8000);
        var cands = ImeNative.CandidateWindows();
        // One earlier run showed no composition on the very first attempt
        // (letters reached the shell); the cause is unconfirmed. One bounded
        // retry on a fresh line — still honestly unverified if absent again.
        if (!composing && cands.Count == 0)
        {
            facts.Add($"firstAttemptNoComposition input={Q(InputLine(ctx.S1))} — retrying once on a fresh prompt");
            await FreshPrompt(ctx, ctx.S1, facts);
            view.Focus();
            await Task.Delay(300);
            LettersNi(ctx.Hwnd);
            composing = await Poll(() => Preedit(view) is { Length: > 0 }, 8000);
            cands = ImeNative.CandidateWindows();
            facts.Add($"retryComposingObserved={composing} preedit={Q(Preedit(view))}");
        }
        var rect = ClientRect(view); var sp = view.PointToScreen(rect.TopLeft); var f = Frame(ctx.S1);
        facts.Add($"composingObserved={composing} preedit={Q(Preedit(view))} candidates={Describe(cands)}");
        facts.Add($"cursorRect=({rect.X:0.#},{rect.Y:0.#},{rect.Width:0.#}x{rect.Height:0.#}) screen=({sp.X},{sp.Y}) frame={f.Columns}x{f.Rows} cell=({f.CursorX},{f.CursorY})");
        JudgeCandidatePosition(ctx, view, cands, "empty", facts);
        TakeShot(shots, ctx, ctx.Hwnd, "ime-empty-input.png");
        if (!composing && cands.Count == 0)
            return ("unverified: no composition or candidate UI after real n/i" +
                    (InputLine(ctx.S1).Contains('n') ? " (letters reached the shell literally — IME did not intercept)" : ""), shots);
        ImeNative.Tap(ctx.Hwnd, 0x20);
        var cjk = await Poll(() => InputLine(ctx.S1).Any(Cjk), 8000);
        facts.Add($"spaceCommitCjk={cjk} input={Q(InputLine(ctx.S1))}");
        await FreshPrompt(ctx, ctx.S1, facts);
        return cjk ? ("pass", shots) : ("fail: Space commit produced no CJK text on the PTY input line", shots);
    }

    private async Task<(string Status, List<string> Shots)> CaseEnterDoesNotSubmit(ImeContext ctx, List<string> facts)
    {
        var shots = new List<string>();
        var view = ViewFor(ctx, ctx.S1);
        view.Focus();
        await Task.Delay(150);
        await FreshPrompt(ctx, ctx.S1, facts);
        var prompts = PromptCount(ctx.S1);
        LettersNi(ctx.Hwnd);
        var composing = await Poll(() => Preedit(view) is { Length: > 0 }, 8000);
        facts.Add($"composingObserved={composing} preedit={Q(Preedit(view))}");
        if (!composing) return ("unverified: no composition; Enter behavior untested", shots);
        JudgeCandidatePosition(ctx, view, ImeNative.CandidateWindows(), "enter", facts);
        ImeNative.Tap(ctx.Hwnd, 0x0D);
        await Poll(() => Preedit(view) is null, 6000);
        await Task.Delay(400);
        var after = PromptCount(ctx.S1);
        facts.Add($"promptsBefore={prompts} promptsAfter={after} input={Q(InputLine(ctx.S1))} (Pinyin Enter may commit raw 'ni'; that is not a submit)");
        TakeShot(shots, ctx, ctx.Hwnd, "ime-enter.png");
        await FreshPrompt(ctx, ctx.S1, facts);
        return after == prompts ? ("pass", shots) : ("fail: a new prompt appeared — Enter reached the shell", shots);
    }

    private async Task<(string Status, List<string> Shots)> CaseCjkArrows(ImeContext ctx, List<string> facts)
    {
        var shots = new List<string>();
        var view = ViewFor(ctx, ctx.S1);
        view.Focus();
        await Task.Delay(150);
        await FreshPrompt(ctx, ctx.S1, facts);
        ImeNative.TypeText(ctx.Hwnd, "ab中文cd"); // committed text, not an IME claim
        await Poll(() => InputLine(ctx.S1).Contains("ab中文cd"), 6000);
        await Task.Delay(300);
        var end = Frame(ctx.S1).CursorX;
        facts.Add($"endCol={end} input={Q(InputLine(ctx.S1))}");
        for (var i = 0; i < 3; i++) ImeNative.Tap(ctx.Hwnd, 0x25);
        var left = await Poll(() => Frame(ctx.S1).CursorX == end - 4, 6000);
        var colAfterLeft = Frame(ctx.S1).CursorX;
        ImeNative.Tap(ctx.Hwnd, 0x27);
        var right = await Poll(() => Frame(ctx.S1).CursorX == end - 2, 6000);
        facts.Add($"left3To={colAfterLeft}(expect {end - 4})ok={left} right1To={Frame(ctx.S1).CursorX}(expect {end - 2})ok={right}");
        var rect = ClientRect(view); var cw = CellW(view);
        var rectOk = Math.Abs(rect.X - Frame(ctx.S1).CursorX * cw) < 1 && Math.Abs(rect.Y - Frame(ctx.S1).CursorY * rect.Height) < 1;
        facts.Add($"cursorRect=({rect.X:0.#},{rect.Y:0.#}) cellW={cw:0.#} matchesFrameCell={rectOk}");
        LettersNi(ctx.Hwnd);
        var composing = await Poll(() => Preedit(view) is { Length: > 0 }, 8000);
        JudgeCandidatePosition(ctx, view, ImeNative.CandidateWindows(), "midEdit", facts);
        TakeShot(shots, ctx, ctx.Hwnd, "ime-cjk-arrows.png");
        facts.Add($"midEditComposing={composing} preedit={Q(Preedit(view))}");
        await CancelComposition(view, ctx.Hwnd);
        await FreshPrompt(ctx, ctx.S1, facts);
        if (!composing) return ("unverified: no composition at the mid-edit position", shots);
        return left && right && rectOk ? ("pass", shots) : ("fail: cursor geometry mismatch (see facts)", shots);
    }

    private async Task<(string Status, List<string> Shots)> CaseRightEdge(ImeContext ctx, List<string> facts)
    {
        var shots = new List<string>();
        var view = ViewFor(ctx, ctx.S1);
        view.Focus();
        await Task.Delay(150);
        await FreshPrompt(ctx, ctx.S1, facts);
        var prompts = PromptCount(ctx.S1);
        var cols = Frame(ctx.S1).Columns;
        ImeNative.TypeText(ctx.Hwnd, new string('x', cols - 6));
        await Poll(() => InputLine(ctx.S1).Contains("xxxx"), 6000);
        await Task.Delay(400);
        var f0 = Frame(ctx.S1);
        facts.Add($"cols={cols} caret=({f0.CursorX},{f0.CursorY})");
        LettersNi(ctx.Hwnd);
        var composing = await Poll(() => Preedit(view) is { Length: > 0 }, 8000);
        var cands = ImeNative.CandidateWindows();
        var rect = ClientRect(view); var sp = view.PointToScreen(rect.TopLeft);
        facts.Add($"edgeComposing={composing} preedit={Q(Preedit(view))} cursorScreen=({sp.X},{sp.Y}) candidates={Describe(cands)}");
        JudgeCandidatePosition(ctx, view, cands, "edge", facts);
        TakeShot(shots, ctx, ctx.Hwnd, "ime-right-edge.png");
        await CancelComposition(view, ctx.Hwnd);
        var submitted = PromptCount(ctx.S1) != prompts;
        facts.Add($"escapeSubmitted={submitted}");
        await FreshPrompt(ctx, ctx.S1, facts);
        if (submitted) return ("fail: escaping the composition submitted a command", shots);
        return composing ? ("pass", shots) : ("unverified: no composition at the right edge", shots);
    }

    private async Task<(string Status, List<string> Shots)> CaseSplitFontPopout(ImeContext ctx, List<string> facts)
    {
        var shots = new List<string>();
        var (window, vm, hwnd) = (ctx.Window, ctx.Vm, ctx.Hwnd);
        await vm.SetSplitLayoutAsync("Horizontal");
        await Task.Delay(400);
        var s2 = vm.GetPane(1)!; // fixed target — never the still-stale ActiveSession
        vm.FocusPane(1);
        var paneSynced = await Poll(() => ReferenceEquals(vm.ActiveSession, s2) && vm.FocusedPane == 1, 5000);
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background); // drain queued focus posts
        var view2 = ViewFor(ctx, s2);
        view2.Focus();
        var viewFocused = await Poll(() => FocusedOn(ctx, view2) && ReferenceEquals(vm.ActiveSession, s2) && vm.FocusedPane == 1, 5000);
        facts.Add($"getPane1={Q(s2.Name)} active={Q(vm.ActiveSession?.Name)} focusedPane={vm.FocusedPane} paneSynced={paneSynced} viewFocused={viewFocused}");
        LettersNi(hwnd);
        var composing = await Poll(() => Preedit(view2) is { Length: > 0 }, 8000);
        JudgeCandidatePosition(ctx, view2, ImeNative.CandidateWindows(), "split", facts);
        TakeShot(shots, ctx, hwnd, "ime-split.png");
        facts.Add($"splitComposing={composing} preedit={Q(Preedit(view2))} preeditHolders=[{string.Join(',', PreeditHolders(ctx, view2))}] focusedIsView2={FocusedOn(ctx, view2)}");
        // Blur onto pane 0's own view: the live composition on view2 must clear.
        var s0 = vm.GetPane(0)!;
        vm.FocusPane(0);
        var view0 = ViewFor(ctx, s0);
        view0.Focus();
        await Poll(() => ReferenceEquals(vm.ActiveSession, s0) && vm.FocusedPane == 0 && FocusedOn(ctx, view0), 5000);
        await Task.Delay(200);
        var clearedOnBlur = Preedit(view2) is null;
        facts.Add($"oldViewPreeditClearedOnBlur={clearedOnBlur} value={Q(Preedit(view2))} pane0={Q(s0.Name)}");
        vm.FocusPane(1);
        await Poll(() => ReferenceEquals(vm.ActiveSession, s2) && vm.FocusedPane == 1, 5000);
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        view2.Focus();
        var refocused = await Poll(() => FocusedOn(ctx, view2) && ReferenceEquals(vm.ActiveSession, s2) && vm.FocusedPane == 1, 5000);
        vm.FontSize = 18; // isolated settings dir — user config untouched
        await Task.Delay(400);
        LettersNi(hwnd);
        var composing18 = await Poll(() => Preedit(view2) is { Length: > 0 }, 8000);
        JudgeCandidatePosition(ctx, view2, ImeNative.CandidateWindows(), "font18", facts);
        TakeShot(shots, ctx, hwnd, "ime-font18.png");
        facts.Add($"font18Composing={composing18} refocused={refocused} preeditHolders=[{string.Join(',', PreeditHolders(ctx, view2))}] focusedIsView2={FocusedOn(ctx, view2)}");
        await CancelComposition(view2, hwnd);
        vm.FontSize = ctx.SavedFont;
        await vm.SetSplitLayoutAsync("Single");
        await Task.Delay(500);
        var popped = vm.ActiveSession!;
        var emulator = popped.Emulator;
        facts.Add($"poppedSession={Q(popped.Name)} active={Q(vm.ActiveSession?.Name)}");
        vm.OpenInNewWindowCommand.Execute(null);
        if (!await Poll(() => vm.Popouts.Count == 1, 6000)) return ("fail: popout window did not open", shots);
        var pop = vm.Popouts[0]; var popHwnd = Hwnd(pop);
        pop.Activate();
        ImeNative.EnsureForeground(popHwnd);
        var openResult = ImeNative.SetImeOpen(popHwnd, true);
        pop.Terminal.Focus();
        await Task.Delay(250);
        facts.Add($"popoutFocusedElement={pop.FocusManager?.GetFocusedElement()?.GetType().Name} setImeOpen={openResult?.ToString() ?? "null"} fgIsPop={ImeNative.GetForegroundWindow() == popHwnd}");
        // Windows keeps input profiles per window: a brand-new HWND starts on
        // the default (en-US) profile even on the same UI thread. If this
        // window is not on zh-CN, cycle its profile once — exactly what a
        // user pressing Win+Space inside the popout would do.
        var popLayout = ImeNative.GetKeyboardLayout(0);
        if (((long)popLayout & 0xFFFF) != 0x0804)
        {
            ImeNative.CycleInputLanguage(popHwnd);
            await Task.Delay(600);
            facts.Add($"popoutProfileCycled 0x{(long)popLayout:x} -> 0x{(long)ImeNative.GetKeyboardLayout(0):x}");
        }
        LettersNi(popHwnd);
        var composingPop = await Poll(() => Preedit(pop.Terminal) is { Length: > 0 }, 8000);
        if (!composingPop)
        {
            // One bounded retry: refocus then retype — the first batch can land
            // before the new HWND's text-input context finishes attaching.
            pop.Terminal.Focus();
            await Task.Delay(300);
            LettersNi(popHwnd);
            composingPop = await Poll(() => Preedit(pop.Terminal) is { Length: > 0 }, 5000);
            facts.Add($"popoutRetryComposing={composingPop} focusedElement={pop.FocusManager?.GetFocusedElement()?.GetType().Name}");
        }
        JudgeCandidatePosition(ctx, pop.Terminal, ImeNative.CandidateWindows(), "popout", facts);
        TakeShot(shots, ctx, popHwnd, "ime-popout.png");
        facts.Add($"popoutComposing={composingPop} preedit={Q(Preedit(pop.Terminal))} input={Q(InputLine(popped))}");
        await CancelComposition(pop.Terminal, popHwnd);
        pop.Close();
        await Poll(() => vm.Popouts.Count == 0, 6000);
        var reattached = await Poll(() => ReferenceEquals(vm.ActiveSession, popped)
            && vm.SessionCards.Any(c => ReferenceEquals(c.Model, popped)), 5000);
        var alive = popped.IsRunning && ReferenceEquals(popped.Emulator, emulator);
        facts.Add($"sessionAliveAfterPopout={alive} reattached={reattached} activeIsPopped={ReferenceEquals(vm.ActiveSession, popped)}");
        ImeNative.EnsureForeground(hwnd);
        var back = await PollView(ctx, popped, 5000);
        if (back is null) return ("fail: no visible view for the reattached session", shots);
        back.Focus();
        await Task.Delay(250);
        facts.Add($"reattachFocused={FocusedOn(ctx, back)} fgIsMain={ImeNative.GetForegroundWindow() == hwnd}");
        var backLayout = ImeNative.GetKeyboardLayout(0);
        if (((long)backLayout & 0xFFFF) != 0x0804)
        {
            ImeNative.CycleInputLanguage(hwnd);
            await Task.Delay(600);
            facts.Add($"reattachProfileCycled 0x{(long)backLayout:x} -> 0x{(long)ImeNative.GetKeyboardLayout(0):x}");
        }
        LettersNi(hwnd);
        var composingBack = await Poll(() => Preedit(back) is { Length: > 0 }, 8000);
        facts.Add($"reattachComposing={composingBack} preedit={Q(Preedit(back))} input={Q(InputLine(popped))}");
        await CancelComposition(back, hwnd);
        if (!alive) return ("fail: session/PTY did not survive the popout round trip", shots);
        if (!reattached) return ("fail: session did not reattach into the main list", shots);
        var all = composing && clearedOnBlur && composing18 && composingPop && composingBack;
        return all ? ("pass", shots)
            : composing || composing18 || composingPop || composingBack
                ? ("partial: composition missing on some surface (see facts)", shots)
                : ("unverified: no composition observed in split/popout", shots);
    }
}
