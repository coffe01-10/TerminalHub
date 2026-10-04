using System.Text;

namespace TerminalHub.Core.Terminal;

/// <summary>
/// VT100/xterm escape-sequence parser driving a <see cref="ScreenBuffer"/>.
/// Handles CSI/OSC/DCS subset, UTF-8 input, SGR attributes, alt-screen, scroll regions.
/// </summary>
public sealed class VtParser
{
    private enum State { Ground, Escape, Csi, Osc, Dcs, EscIntermediate, CharsetDesignate, SosPmApc }

    private readonly ScreenBuffer _buffer;
    private readonly Action<byte[]>? _responder;

    private State _state = State.Ground;
    private int _charsetTarget;

    // CSI accumulation. ':' separates subparameters of the current parameter
    // (SGR 38:2:R:G:B, 4:3). It is not another ';'.
    private readonly List<int> _params = new();
    private readonly List<int[]?> _subparams = new();
    private readonly List<int> _currentSubs = new();
    private int _currentParam;
    private int _paramValue = -1;
    private bool _hasParam;
    private bool _inSub;
    private bool _privateMarker;   // '?', '>', '!', etc.
    private char _privateChar;
    private readonly List<char> _intermediates = new();

    // OSC/DCS string accumulation — bounded: a runaway program emitting an
    // unterminated OSC must not grow memory without limit.
    private const int MaxOscBytes = 64 * 1024;
    // OSC command digits are capped far below that — no legitimate OSC code
    // needs more digits, and an unterminated digit flood must not grow _osc.
    private const int MaxOscCommandDigits = 8;
    private const int MaxCsiParams = 64;
    private const int MaxIntermediates = 8;
    private readonly StringBuilder _osc = new();
    private int _oscCommand;

    // UTF-8 incremental decode
    private int _utf8Remaining;
    private int _utf8Value;
    private int _utf8Min;
    private readonly List<byte> _pendingInput = [];
    internal byte[] PendingInput => _pendingInput.ToArray();
    /// <summary>The last emitted rune was U+200D ZWJ — the next rune joins the same cluster.</summary>
    private bool _afterZwj;

    // OSC/DCS byte-level UTF-8 tracking: a CJK trail byte such as 0x9C (in
    // "本" = E6 9C AC) is payload data, not the 8-bit ST terminator (0x9C).
    private int _stringUtf8Remaining;

    // Replies (DA / DECRPM / OSC queries / kitty flags) are queued while parsing
    // under SyncRoot and written back AFTER the lock is released — ConPtySession.Write
    // blocks on WriteFile and must never stall the render lock.
    private readonly List<byte[]> _responses = new();
    private readonly List<(char Marker, int? ExitCode, string? Text, int Line, int Column, long Removed)> _commandMarkers = new();
    public event Action<char, int?>? CommandMarker;
    /// <summary>Invoked as each marker arrives, inside the buffer lock.
    /// Capture anchors and directory before later bytes change the screen or cwd.</summary>
    public Action<IReadOnlyList<(char Marker, int? ExitCode, string? Text, int Line, int Column, long Removed)>>? ObserveCommands { get; set; }
    public event Action? CommandsObserved;
    public Func<bool, string>? DefaultColorQuery { get; set; }

    /// <summary>Raised after a batch of input has been applied (renderer hint).</summary>
    public event Action? BufferChanged;
    /// <summary>Recording subscribers only enqueue bytes; PTY replies stay outside the lock.</summary>
    public event Action<ReadOnlyMemory<byte>>? DataApplied;

    /// <summary>BEL (0x07) in the ground state — the app turns it into a
    /// visual notification instead of a sound.</summary>
    public event Action? Bell;

    public ScreenBuffer Buffer => _buffer;

    public VtParser(ScreenBuffer buffer, Action<byte[]>? responder = null)
    {
        _buffer = buffer;
        _responder = responder;
    }

    public void Feed(ReadOnlySpan<byte> data)
    {
        byte[][] responses;
        (char Marker, int? ExitCode, string? Text, int Line, int Column, long Removed)[] commandMarkers;
        lock (_buffer.SyncRoot)
        {
            if (data.Length > 0) _buffer.BumpVersion();
            foreach (var b in data)
            {
                if (_pendingInput.Count > 0 || _state != State.Ground || _utf8Remaining > 0 || b == 0x1b || b >= 0x80) _pendingInput.Add(b);
                FeedByte(b);
                if (_state == State.Ground && _utf8Remaining == 0) _pendingInput.Clear();
                else if (_pendingInput.Count > MaxOscBytes + 128) _pendingInput.RemoveRange(0, _pendingInput.Count - MaxOscBytes);
            }
            responses = _responses.ToArray();
            _responses.Clear();
            commandMarkers = _commandMarkers.ToArray();
            _commandMarkers.Clear();
            if (DataApplied is { } applied && data.Length > 0) applied(data.ToArray());
        }
        // Replies (DA, DECRPM, OSC queries, kitty flags) go out AFTER releasing
        // SyncRoot — ConPtySession.Write blocks on WriteFile and must never stall
        // the render lock.
        foreach (var r in responses)
            _responder?.Invoke(r);
        foreach (var marker in commandMarkers) CommandMarker?.Invoke(marker.Marker, marker.ExitCode);
        if (commandMarkers.Length > 0) CommandsObserved?.Invoke();
        BufferChanged?.Invoke();
    }

    private void Respond(byte[] bytes) => _responses.Add(bytes);
    private void Respond(string ascii) => _responses.Add(Encoding.ASCII.GetBytes(ascii));

    public void Feed(string text) => Feed(Encoding.UTF8.GetBytes(text));

    /// <summary>A recording gap breaks any partial control or UTF-8 sequence.</summary>
    internal void DiscardPendingInput()
    {
        lock (_buffer.SyncRoot)
        {
            _utf8Remaining = 0;
            _stringUtf8Remaining = 0;
            _pendingInput.Clear();
            _osc.Clear();
            Enter(State.Ground);
        }
    }

    private void FeedByte(byte b)
    {
        // Anywhere transitions. OSC/DCS/SOS handlers must see ESC themselves
        // (ESC \ is the ST terminator), so only intercept here for other states.
        // In the escape state itself a second ESC restarts the sequence
        // (vt500) — tmux's nested "ESC ESC \" must not print the backslash.
        if (b == 0x1B && _state is State.Ground or State.Escape or State.Csi
            or State.EscIntermediate or State.CharsetDesignate)
        {
            Enter(State.Escape);
            return;
        }

        switch (_state)
        {
            case State.Ground: GroundByte(b); break;
            case State.Escape: EscapeByte(b); break;
            case State.Csi: CsiByte(b); break;
            case State.Osc: OscByte(b); break;
            case State.Dcs:
            case State.SosPmApc:
                // BEL/ESC can never occur inside a UTF-8 sequence; 0x9C CAN —
                // only treat it as ST when no continuation byte is pending.
                if (b == 0x07) { _stringUtf8Remaining = 0; Enter(State.Ground); }              // BEL
                else if (b == 0x9C && _stringUtf8Remaining == 0) { _stringUtf8Remaining = 0; Enter(State.Ground); } // ST
                else if (b == 0x1B) { _stringUtf8Remaining = 0; Enter(State.Escape); }         // ESC \ handled via Escape
                else if (_stringUtf8Remaining > 0 && b is >= 0x80 and < 0xC0) _stringUtf8Remaining--;
                else if (b >= 0xC0) _stringUtf8Remaining = b >= 0xF0 ? 3 : b >= 0xE0 ? 2 : 1;
                break;
            case State.EscIntermediate: EscIntermediateByte(b); break;
            case State.CharsetDesignate: CharsetByte(b); break;
        }
    }

    private void Enter(State s)
    {
        _state = s;
        _afterZwj = false;          // any escape/control breaks a pending join
        if (s is State.Escape)
        {
            _intermediates.Clear(); // fresh ESC sequence — no stale intermediates
        }
        else if (s is State.Csi)
        {
            _params.Clear();
            _subparams.Clear();
            ResetCurrentParameter();
            _privateMarker = false;
            _privateChar = '\0';
            _intermediates.Clear();
        }
        else if (s is State.Osc or State.Dcs or State.SosPmApc)
        {
            _osc.Clear();
            _oscCommand = -1;
            _stringUtf8Remaining = 0;
        }
    }

    // ---------- ground ----------

    private void GroundByte(byte b)
    {
        // UTF-8 continuation handling first.
        if (_utf8Remaining > 0)
        {
            if (b is >= 0x80 and < 0xC0)
            {
                _utf8Value = (_utf8Value << 6) | (b & 0x3F);
                if (--_utf8Remaining == 0)
                {
                    var rune = _utf8Value;
                    // Overlong, above U+10FFFF, or a surrogate is not a scalar.
                    // EmitRune also rejects these so ConvertFromUtf32 cannot throw.
                    if (rune < _utf8Min || !IsUnicodeScalar(rune)) rune = 0xFFFD;
                    EmitRune(rune);
                }
                return;
            }
            _utf8Remaining = 0; // invalid continuation: resync
            _afterZwj = false;
        }

        switch (b)
        {
            case 0x00: case 0x7F: break;                        // NUL/DEL ignored
            case >= 0x07 and <= 0x0F: ExecuteControl(b); break;
            case 0x9B: Enter(State.Csi); break;                 // 8-bit CSI
            case 0x9D: Enter(State.Osc); break;
            // 8-bit C1: the string states swallow the whole payload instead of
            // printing each byte as U+FFFD text; IND/NEL/RI act as controls.
            case 0x84: _afterZwj = false; _buffer.Index(); break;   // 8-bit IND
            case 0x85: _afterZwj = false; _buffer.NextLine(); break; // 8-bit NEL
            case 0x8D: _afterZwj = false; _buffer.ReverseIndex(); break; // 8-bit RI
            case 0x90: Enter(State.Dcs); break;                 // 8-bit DCS
            case 0x98: case 0x9E: case 0x9F: Enter(State.SosPmApc); break; // SOS/PM/APC
            case >= 0x80 and <= 0x9F: _afterZwj = false; break; // other C1 (SS2/SS3/ST/...) — swallow
            case >= 0xA0 and < 0xC0: _afterZwj = false; _buffer.PutChar('\uFFFD'); break;
            case >= 0xC0 and < 0xE0: _utf8Value = b & 0x1F; _utf8Remaining = 1; _utf8Min = 0x80; break;
            case >= 0xE0 and < 0xF0: _utf8Value = b & 0x0F; _utf8Remaining = 2; _utf8Min = 0x800; break;
            case >= 0xF0: _utf8Value = b & 0x07; _utf8Remaining = 3; _utf8Min = 0x10000; break;
            default: _afterZwj = false; _buffer.PutChar((char)b); break;
        }
    }

    /// <summary>
    /// C0 execution shared by the ground and CSI states — vt500 semantics run
    /// these controls without discarding an accumulating CSI sequence.
    /// </summary>
    private void ExecuteControl(byte b)
    {
        switch (b)
        {
            case 0x07: Bell?.Invoke(); break;                   // BEL → visual bell
            case 0x08: _afterZwj = false; _buffer.Backspace(); break;
            case 0x09: _afterZwj = false; _buffer.Tab(); break;
            case 0x0A: case 0x0B: case 0x0C: _afterZwj = false; _buffer.LineFeed(); break;
            case 0x0D: _afterZwj = false; _buffer.CarriageReturn(); break;
            case 0x0E: _afterZwj = false; _buffer.ShiftOut(); break;
            case 0x0F: _afterZwj = false; _buffer.ShiftIn(); break;
        }
    }

    /// <summary>
    /// Emit a decoded code point: zero-width runes, ZWJ followers and the second
    /// regional indicator of a flag extend the previous cell's cluster; every
    /// other rune starts a new cell (supplementary planes included).
    /// </summary>
    private static bool IsUnicodeScalar(int rune)
        => (uint)rune <= 0x10FFFF && rune is not (>= 0xD800 and <= 0xDFFF);

    private void EmitRune(int rune)
    {
        if (!IsUnicodeScalar(rune)) rune = 0xFFFD;
        var joins = _afterZwj
            || GraphemeWidth.IsZeroWidthRune(rune)
            || (GraphemeWidth.IsRegionalIndicator(rune) && _buffer.LastGlyphIsRegionalIndicator);
        if (!joins || !_buffer.AppendToLastGrapheme(rune))
            _buffer.PutRune(rune);
        _afterZwj = rune == 0x200D;
    }

    // ---------- escape ----------

    private void EscapeByte(byte b)
    {
        switch (b)
        {
            case (byte)'[': Enter(State.Csi); break;
            case (byte)']': Enter(State.Osc); break;
            case (byte)'P': Enter(State.Dcs); break;
            case (byte)'X': case (byte)'^': case (byte)'_': Enter(State.SosPmApc); break;
            case (byte)'(': _charsetTarget = 0; _state = State.CharsetDesignate; break;
            case (byte)')': _charsetTarget = 1; _state = State.CharsetDesignate; break;
            case (byte)'7': _buffer.SaveCursor(); Enter(State.Ground); break;
            case (byte)'8': _buffer.RestoreCursor(); Enter(State.Ground); break;
            case (byte)'D': _buffer.Index(); Enter(State.Ground); break;
            case (byte)'E': _buffer.NextLine(); Enter(State.Ground); break;
            case (byte)'M': _buffer.ReverseIndex(); Enter(State.Ground); break;
            case (byte)'H': Enter(State.Ground); break;         // HTS
            case (byte)'c': Reset(); Enter(State.Ground); break; // RIS
            // DECKPAM / DECKPNM — application vs numeric keypad, not DECCKM.
            case (byte)'=': _buffer.ApplicationKeypad = true; Enter(State.Ground); break;
            case (byte)'>': _buffer.ApplicationKeypad = false; Enter(State.Ground); break;
            case >= 0x20 and <= 0x2F:
                _intermediates.Add((char)b);
                _state = State.EscIntermediate;
                break;
            default: Enter(State.Ground); break;
        }
    }

    private void EscIntermediateByte(byte b)
    {
        if (b >= 0x20 && b <= 0x2F)
        {
            if (_intermediates.Count < MaxIntermediates) _intermediates.Add((char)b);
            return;
        }
        // Final byte — DECALN (ESC # 8) fills the screen with 'E'.
        if (_intermediates.Count > 0 && _intermediates[0] == '#' && b == (byte)'8')
            _buffer.AlignTest();
        Enter(State.Ground);
    }

    private void CharsetByte(byte b)
    {
        var special = b is (byte)'0';   // '0' = DEC special graphics, 'B' = ASCII
        _buffer.DesignateCharset(_charsetTarget, special);
        Enter(State.Ground);
    }

    private void Reset()
    {
        // Leave the alternate screen before erasing, or RIS would clear the
        // alternate grid and leave the primary screen untouched.
        _buffer.UseAlternateScreen(false);
        _buffer.ApplicationCursorKeys = false;
        _buffer.ApplicationKeypad = false;
        _buffer.DesignateCharset(0, false);
        _buffer.DesignateCharset(1, false);
        _buffer.SelectCharset(0);
        _buffer.CurrentAttrs = CellAttrs.None;
        _buffer.CurrentHyperlink = null;
        _buffer.CurrentFg = TerminalColor.Default;
        _buffer.CurrentBg = TerminalColor.Default;
        _buffer.AutoWrap = true;
        _buffer.BracketedPaste = false;
        _buffer.MouseTracking = 0;
        _buffer.SgrMouse = false;
        _buffer.FocusReporting = false;
        _buffer.SetSynchronizedOutput(false);
        _buffer.InsertMode = false;
        _buffer.SetCursorVisible(true);
        _buffer.SetOriginMode(false);
        _buffer.ResetKittyKeyboard();
        _buffer.ResetScrollRegion();
        _buffer.ResetSavedCursor(bothScreens: true);
        _buffer.ClearScrollback();
        _buffer.EraseInDisplay(2);
        _buffer.CursorPosition(1, 1);
    }

    /// <summary>DECSTR (CSI ! p): modes and SGR, without erasing or leaving the alt screen.</summary>
    private void SoftReset()
    {
        var b = _buffer;
        b.CurrentAttrs = CellAttrs.None;
        b.CurrentHyperlink = null;
        b.CurrentFg = TerminalColor.Default;
        b.CurrentBg = TerminalColor.Default;
        b.AutoWrap = true;
        b.InsertMode = false;
        b.ApplicationCursorKeys = false;
        b.ApplicationKeypad = false;
        b.SetCursorVisible(true);
        b.DesignateCharset(0, false);
        b.DesignateCharset(1, false);
        b.SelectCharset(0);
        b.ResetScrollRegion();
        // DECSTR changes the addressing mode without moving the current cursor.
        b.OriginMode = false;
        b.ResetSavedCursor();
    }

    // ---------- CSI ----------

    private void CsiByte(byte b)
    {
        // vt500: a C0 control inside a CSI sequence executes at once and the
        // sequence keeps accumulating (CAN/SUB cancel it); DEL is ignored.
        // ESC was already intercepted as an anywhere-transition in FeedByte.
        if (b < 0x20)
        {
            if (b is 0x18 or 0x1A) Enter(State.Ground);
            else ExecuteControl(b);
            return;
        }
        if (b == 0x7F) return;
        if (b is >= (byte)'0' and <= (byte)'9')
        {
            _currentParam = Math.Min(_currentParam * 10 + (b - '0'), 0xFFFFFF);
            _hasParam = true;
            return;
        }
        switch (b)
        {
            case (byte)';':
                PushParameter();
                return;
            case (byte)':':
                // First colon keeps the parameter value; later colons push subparameters.
                // An empty field (38:2::R:G:B) is stored as -1, not as zero.
                if (!_inSub)
                {
                    _paramValue = _hasParam ? _currentParam : -1;
                    _inSub = true;
                }
                else if (_currentSubs.Count < MaxCsiParams)
                    _currentSubs.Add(_hasParam ? _currentParam : -1);
                _currentParam = 0;
                _hasParam = false;
                return;
            // ECMA-48 private parameter bytes 0x3C–0x3F (? < = >) plus the
            // intermediates xterm abuses as markers (! ' " $ SP).
            case (byte)'?': case (byte)'<': case (byte)'=': case (byte)'>':
            case (byte)'!': case (byte)'\'': case (byte)'"': case (byte)'$': case (byte)' ':
                if (_params.Count == 0 && !_hasParam && !_privateMarker)
                {
                    _privateMarker = true;
                    _privateChar = (char)b;
                    return;
                }
                if (_intermediates.Count < MaxIntermediates) _intermediates.Add((char)b);
                return;
            case >= 0x20 and <= 0x2F:
                if (_intermediates.Count < MaxIntermediates) _intermediates.Add((char)b);
                return;
        }

        // Final byte 0x40-0x7E
        if (b is >= 0x40 and <= 0x7E)
        {
            if (_hasParam || _inSub || _currentSubs.Count > 0 || _params.Count > 0 || _currentParam != 0)
                PushParameter();
            DispatchCsi((char)b);
        }
        Enter(State.Ground);
    }

    private void ResetCurrentParameter()
    {
        _currentSubs.Clear();
        _currentParam = 0;
        _paramValue = -1;
        _hasParam = false;
        _inSub = false;
    }

    private void PushParameter()
    {
        if (_params.Count >= MaxCsiParams)
        {
            ResetCurrentParameter();
            return;
        }
        int value;
        if (_inSub)
        {
            if (_currentSubs.Count < MaxCsiParams)
                _currentSubs.Add(_hasParam ? _currentParam : -1);
            value = _paramValue;
        }
        else value = _hasParam ? _currentParam : -1;
        _params.Add(value);
        _subparams.Add(_currentSubs.Count > 0 ? _currentSubs.ToArray() : null);
        ResetCurrentParameter();
    }

    private int Param(int i, int def = 1)
        => i < _params.Count && _params[i] >= 0 ? _params[i] : def;

    private int Subparam(int paramIndex, int subIndex, int def)
    {
        if (paramIndex >= _subparams.Count || _subparams[paramIndex] is not { } subs || subIndex >= subs.Length)
            return def;
        return subs[subIndex] >= 0 ? subs[subIndex] : def;
    }

    private void DispatchCsi(char final)
    {
        var b = _buffer;
        if (_privateMarker && _privateChar == '?')
        {
            HandleDecSet(final);
            return;
        }
        // Keyboard protocol negotiation must not fall through to legacy cursor restore.
        if (_privateMarker && _privateChar is '>' or '<' or '=')
        {
            if (_privateChar == '>' && final == 'c')
            {
                Respond("\x1b[>0;1;0c");   // DA2 — secondary device attributes
                return;
            }
            // kitty keyboard protocol: CSI >flags u push, CSI <count u pop,
            // CSI =flags;mode u set (1 = assign, 2 = set bits, 3 = clear bits).
            if (final == 'u')
            {
                var flags = Param(0, 0);
                switch (_privateChar)
                {
                    case '>': b.KittyPush(flags); break;
                    case '<': b.KittyPop(flags < 1 ? 1 : flags); break;
                    case '=': b.KittySet(flags, Param(1, 1)); break;
                }
            }
            return;
        }
        if (_privateMarker && _privateChar == '!' && final == 'p')
        {
            SoftReset();
            return;
        }

        switch (final)
        {
            case 'A': b.CursorUp(Param(0)); break;
            case 'B': case 'e': b.CursorDown(Param(0)); break;
            case 'C': case 'a': b.CursorForward(Param(0)); break;
            case 'D': b.CursorBack(Param(0)); break;
            case 'E': b.CursorNextLine(Param(0)); break;
            case 'F': b.CursorPrevLine(Param(0)); break;
            case 'G': case '`': b.CursorHorizontalAbsolute(Param(0)); break;
            case 'd': b.CursorVerticalAbsolute(Param(0)); break;
            case 'H': case 'f': b.CursorPosition(Param(0), Param(1)); break;
            case 'J': b.EraseInDisplay(Param(0, 0)); break;
            case 'K': b.EraseInLine(Param(0, 0)); break;
            case 'L': b.InsertLines(Param(0)); break;
            case 'M': b.DeleteLines(Param(0)); break;
            case 'P': b.DeleteChars(Param(0)); break;
            case 'S': b.ScrollUpRegion(b.ScrollRegionTop, b.ScrollRegionBottom, Param(0)); break;
            case 'T': b.ScrollDownRegion(b.ScrollRegionTop, b.ScrollRegionBottom, Param(0)); break;
            case 'X': b.EraseChars(Param(0)); break;
            case '@': b.InsertChars(Param(0)); break;
            case 'm': HandleSgr(); break;
            case 'r': b.SetScrollRegion(Param(0), Param(1, b.Rows)); break;
            case 's': b.SaveCursor(); break;
            case 'u': b.RestoreCursor(); break;
            case 'h': HandleSetMode(true); break;
            case 'l': HandleSetMode(false); break;
            case 'b': // REP — repeat last char; approximate: noop (needs last-char tracking)
                break;
            case 'c': Respond("\x1b[?1;2c"); break;  // DA: vt220
            case 'n':
                if (Param(0, 0) == 6) // DSR cursor position report
                {
                    // Origin mode reports the row relative to the scroll-region top.
                    var row = b.OriginMode ? b.CursorY - b.ScrollRegionTop + 1 : b.CursorY + 1;
                    Respond($"\x1b[{row};{b.CursorX + 1}R");
                }
                else if (Param(0, 0) == 5)
                    Respond("\x1b[0n");
                break;
            case 't': break;   // window ops — ignore
            case 'q': break;   // DECSCUSR cursor style — renderer shows default
        }
    }

    private void HandleSetMode(bool set)
    {
        var b = _buffer;
        foreach (var p in _params)
        {
            switch (p)
            {
                case 4: b.InsertMode = set; break;
                case 20: break; // LNM — we always treat LF as index
            }
        }
    }

    private void HandleDecSet(char final)
    {
        var b = _buffer;
        if (final == 'p' && _intermediates.Contains('$'))
        {
            var mode = Param(0, 0);
            var status = mode switch
            {
                2026 => b.SynchronizedOutput ? 1 : 2,
                2004 => b.BracketedPaste ? 1 : 2,
                1000 or 1002 or 1003 => b.MouseTracking == mode ? 1 : 2,
                1004 => b.FocusReporting ? 1 : 2,
                1006 => b.SgrMouse ? 1 : 2,
                25 => b.CursorVisible ? 1 : 2,
                1049 => b.OnAlternateScreen ? 1 : 2,
                _ => 0
            };
            Respond($"\x1b[?{mode};{status}$y");
            return;
        }
        if (final == 'u')
        {
            // kitty keyboard flags query → CSI ?flags u
            Respond($"\x1b[?{b.KittyKeyboardFlags}u");
            return;
        }
        if (final == 'h' || final == 'l')
        {
            var set = final == 'h';
            foreach (var p in _params)
            {
                switch (p)
                {
                    case 1: b.ApplicationCursorKeys = set; break;
                    case 6: b.SetOriginMode(set); break;
                    case 7: b.AutoWrap = set; break;
                    case 25: b.SetCursorVisible(set); break;
                    case 1000: case 1002: case 1003:
                        if (set) b.MouseTracking = p;
                        else if (b.MouseTracking == p) b.MouseTracking = 0;
                        break;
                    case 1004: b.FocusReporting = set; break;
                    case 1006: b.SgrMouse = set; break;
                    case 47: case 1047: b.UseAlternateScreen(set); break;
                    case 1048: if (set) b.SaveCursor(); else b.RestoreCursor(); break;
                    case 1049:
                        if (set) { b.SaveCursor(); b.UseAlternateScreen(true); }
                        else { b.UseAlternateScreen(false); b.RestoreCursor(); }
                        break;
                    case 2004: b.BracketedPaste = set; break;
                    case 2026: b.SetSynchronizedOutput(set); break;
                }
            }
        }
        else if (final == 'r')
        {
            b.ResetScrollRegion();
        }
    }

    private void HandleSgr()
    {
        var b = _buffer;
        if (_params.Count == 0) { b.CurrentAttrs = CellAttrs.None; b.CurrentFg = TerminalColor.Default; b.CurrentBg = TerminalColor.Default; return; }

        for (var i = 0; i < _params.Count; i++)
        {
            var p = _params[i] < 0 ? 0 : _params[i];
            switch (p)
            {
                case 0:
                    b.CurrentAttrs = CellAttrs.None;
                    b.CurrentFg = TerminalColor.Default;
                    b.CurrentBg = TerminalColor.Default;
                    break;
                case 1: b.CurrentAttrs |= CellAttrs.Bold; break;
                case 2: b.CurrentAttrs |= CellAttrs.Dim; break;
                case 3: b.CurrentAttrs |= CellAttrs.Italic; break;
                case 4:
                    // 4:0 off, 4:1..4:5 underline styles. 4:3 is curly underline, not italic.
                    if (Subparam(i, 0, 1) == 0) b.CurrentAttrs &= ~CellAttrs.Underline;
                    else b.CurrentAttrs |= CellAttrs.Underline;
                    break;
                case 5: case 6: b.CurrentAttrs |= CellAttrs.Blink; break;
                case 7: b.CurrentAttrs |= CellAttrs.Inverse; break;
                case 8: b.CurrentAttrs |= CellAttrs.Hidden; break;
                case 9: b.CurrentAttrs |= CellAttrs.Strike; break;
                case 21: case 22: b.CurrentAttrs &= ~(CellAttrs.Bold | CellAttrs.Dim); break;
                case 23: b.CurrentAttrs &= ~CellAttrs.Italic; break;
                case 24: b.CurrentAttrs &= ~CellAttrs.Underline; break;
                case 25: b.CurrentAttrs &= ~CellAttrs.Blink; break;
                case 27: b.CurrentAttrs &= ~CellAttrs.Inverse; break;
                case 28: b.CurrentAttrs &= ~CellAttrs.Hidden; break;
                case 29: b.CurrentAttrs &= ~CellAttrs.Strike; break;
                case 39: b.CurrentFg = TerminalColor.Default; break;
                case 49: b.CurrentBg = TerminalColor.Default; break;
                case >= 30 and <= 37: b.CurrentFg = TerminalColor.Indexed(p - 30); break;
                case >= 40 and <= 47: b.CurrentBg = TerminalColor.Indexed(p - 40); break;
                case >= 90 and <= 97: b.CurrentFg = TerminalColor.Indexed(p - 90 + 8); break;
                case >= 100 and <= 107: b.CurrentBg = TerminalColor.Indexed(p - 100 + 8); break;
                case 38: case 48:
                    TerminalColor color;
                    if (i < _subparams.Count && _subparams[i] is { Length: > 0 } subs)
                        color = ColorFromSubs(subs);
                    else
                    {
                        int consumed;
                        (color, consumed) = ParseExtendedColor(i);
                        i += consumed;
                    }
                    if (p == 38) b.CurrentFg = color; else b.CurrentBg = color;
                    break;
                case 58: // underline color — consume args, ignore
                    // Colon components belong to this parameter; do not eat the
                    // following semicolon SGR attributes (e.g. 58:2::R:G:B;5;1).
                    if (i < _subparams.Count && _subparams[i] is not null) break;
                    if (i + 1 < _params.Count && _params[i + 1] == 5) i += 2;
                    else if (i + 1 < _params.Count && _params[i + 1] == 2) i += 4;
                    break;
                case 10: case 11: b.SelectCharset(0); break; // default charset
            }
        }
    }

    private (TerminalColor color, int consumed) ParseExtendedColor(int i)
    {
        // 38;5;n  or  38;2;r;g;b
        if (i + 2 < _params.Count && _params[i + 1] == 5)
            return (TerminalColor.Indexed(_params[i + 2]), 2);
        if (i + 4 < _params.Count && _params[i + 1] == 2)
            return (TerminalColor.Rgb(Byte(_params[i + 2]), Byte(_params[i + 3]), Byte(_params[i + 4])), 4);
        return (TerminalColor.Default, 0);
    }

    /// <summary>ISO-8613-6 colon form: 2:R:G:B, 2::R:G:B, or 2:colorspace:R:G:B. 5:index stays indexed.</summary>
    private static TerminalColor ColorFromSubs(int[] subs)
    {
        if (subs.Length >= 2 && subs[0] == 5)
            return TerminalColor.Indexed(subs[1] < 0 ? 0 : subs[1]);
        if (subs[0] != 2) return TerminalColor.Default;
        var start = 1;
        var components = subs.Length - 1;
        // A colorspace id is present when the slot is empty or four numbers follow the 2.
        if (components >= 4 || (components > 0 && subs[1] < 0)) start = 2;
        return TerminalColor.Rgb(ByteAt(subs, start), ByteAt(subs, start + 1), ByteAt(subs, start + 2));
    }

    private static int ByteAt(int[] values, int index)
        => index < 0 || index >= values.Length ? 0 : Byte(values[index]);

    private static int Byte(int value) => value < 0 ? 0 : value > 255 ? 255 : value;

    // ---------- OSC ----------

    private void OscByte(byte b)
    {
        if (b == 0x07) { DispatchOsc(); Enter(State.Ground); return; }  // BEL
        if (b == 0x1B) { DispatchOsc(); Enter(State.Escape); return; }  // ESC \ (7-bit ST)
        // 0x9C is the 8-bit ST — but also a legal UTF-8 trail byte, so only
        // terminate when no multi-byte sequence is in progress.
        if (b == 0x9C && _stringUtf8Remaining == 0) { DispatchOsc(); Enter(State.Ground); return; }

        // Payload arrives as a UTF-8 byte stream: track lead/continuation bytes
        // so CJK trail bytes (incl. 0x9C) are kept as data, never terminators.
        if (_stringUtf8Remaining > 0)
        {
            if (b is >= 0x80 and < 0xC0)
            {
                _stringUtf8Remaining--;
                if (_oscCommand >= 0 && _osc.Length < MaxOscBytes) _osc.Append((char)b);
                return;
            }
            _stringUtf8Remaining = 0;   // invalid continuation — resync, handle byte below
        }
        else if (b >= 0xC0)
        {
            _stringUtf8Remaining = b >= 0xF0 ? 3 : b >= 0xE0 ? 2 : 1;
            if (_oscCommand >= 0 && _osc.Length < MaxOscBytes) _osc.Append((char)b);
            return;
        }

        if (_oscCommand < 0)
        {
            if (_oscCommand == -2) return;      // malformed OSC — swallow until terminator
            if (char.IsDigit((char)b))
            {
                if (_osc.Length < MaxOscCommandDigits) _osc.Append((char)b);
            }
            else if (b == (byte)';')
            {
                _oscCommand = int.TryParse(_osc.ToString(), out var c) ? c : -2;
                _osc.Clear();
            }
            else
            {
                _oscCommand = -2;                // malformed — swallow until terminator
                _osc.Clear();                    //   instead of splashing payload to screen
            }
            return;
        }
        if (_osc.Length < MaxOscBytes) _osc.Append((char)b);
    }

    private void DispatchOsc()
    {
        var text = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(_osc.ToString()));
        switch (_oscCommand)
        {
            case 0: case 1: case 2:
                _buffer.SetTitle(text);
                break;
            case 8:
                var separator = text.IndexOf(';');
                if (separator >= 0) _buffer.CurrentHyperlink = text[(separator + 1)..] is { Length: > 0 } url ? url : null;
                break;
            case 133:
                var markSplit = text.IndexOf(';');
                var head = markSplit < 0 ? text : text[..markSplit];
                var rest = markSplit < 0 ? "" : text[(markSplit + 1)..];
                if (head is "A" or "B" or "C" or "D" or "E")
                {
                    int? exit = null;
                    if (head == "D")
                    {
                        var codeText = rest.Split(';')[0];
                        if (int.TryParse(codeText, out var code)) exit = code;
                    }
                    var markedColumn = _buffer.PendingWrap ? _buffer.Columns : _buffer.CursorX;
                    var marker = (head[0], exit, head == "E" ? rest : null,
                        _buffer.ScrollbackCount + _buffer.CursorY, markedColumn, _buffer.RemovedLineCount);
                    _commandMarkers.Add(marker);
                    ObserveCommands?.Invoke([marker]);
                }
                break;
            case 777: break; // notifications
            case 4: break;   // palette query/set
            case 10: case 11:
                if (text == "?")
                {
                    var rgb = DefaultColorQuery?.Invoke(_oscCommand == 10)
                        ?? (_oscCommand == 10 ? "cccc/cccc/cccc" : "0c0c/0c0c/0c0c");
                    Respond($"\x1b]{_oscCommand};rgb:{rgb}\x1b\\");
                }
                break;
            case 9 when text.StartsWith("9;"):
                _buffer.SetCwd(text[2..]);
                break;
            case 7: // cwd report (OSC 7) — file://host/path or absolute path
                if (TryParseOsc7(text, out var cwd))
                    _buffer.SetCwd(cwd);
                break;
            default: break;
        }
    }

    /// <summary>Parse OSC 7 payload: <c>file://host/path</c>, <c>file:///path</c>, or a bare absolute path.</summary>
    public static bool TryParseOsc7(string text, out string cwd)
    {
        cwd = "";
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        // Bare absolute path (some shells).
        if (text.StartsWith('/') || (text.Length >= 3 && char.IsLetter(text[0]) && text[1] == ':'))
        {
            cwd = text;
            return true;
        }
        if (!text.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            {
                // file:///path → LocalPath; file://host/path → AbsolutePath
                // (LocalPath becomes a UNC-looking \\host\path on some runtimes).
                var candidate = uri.Host.Length > 0 && uri.Host is not "localhost" and not "127.0.0.1"
                    ? uri.AbsolutePath
                    : (uri.IsFile ? uri.LocalPath : uri.AbsolutePath);
                if (candidate.StartsWith("\\", StringComparison.Ordinal)
                    || candidate.StartsWith("//", StringComparison.Ordinal))
                    candidate = uri.AbsolutePath;
                cwd = Uri.UnescapeDataString(candidate);
                // "/D:/x" (leading slash before a drive letter) → "D:/x".
                if (cwd.Length >= 3 && cwd[0] == '/' && char.IsLetter(cwd[1]) && cwd[2] == ':')
                    cwd = cwd[1..];
                // '/'-rooted POSIX paths stay verbatim: rewriting them with '\' on
                // Windows would yield a relative-looking path that is neither a valid
                // Windows path nor cd-able in the WSL/git-bash session that sent it.
                return cwd.Length > 0;
            }
        }
        catch (UriFormatException) { }
        return false;
    }

}
