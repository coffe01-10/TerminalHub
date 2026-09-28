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

    // CSI accumulation
    private readonly List<int> _params = new();
    private int _currentParam;
    private bool _hasParam;
    private bool _privateMarker;   // '?', '>', '!', etc.
    private char _privateChar;
    private readonly List<char> _intermediates = new();

    // OSC/DCS string accumulation
    private readonly StringBuilder _osc = new();
    private int _oscCommand;

    // UTF-8 incremental decode
    private int _utf8Remaining;
    private int _utf8Value;
    private int _utf8Min;

    /// <summary>Raised after a batch of input has been applied (renderer hint).</summary>
    public event Action? BufferChanged;

    public ScreenBuffer Buffer => _buffer;

    public VtParser(ScreenBuffer buffer, Action<byte[]>? responder = null)
    {
        _buffer = buffer;
        _responder = responder;
    }

    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            FeedByte(b);
        BufferChanged?.Invoke();
    }

    public void Feed(string text) => Feed(Encoding.UTF8.GetBytes(text));

    private void FeedByte(byte b)
    {
        // Anywhere transitions. OSC/DCS/SOS handlers must see ESC themselves
        // (ESC \ is the ST terminator), so only intercept here for other states.
        if (b == 0x1B && _state is State.Ground or State.Csi or State.EscIntermediate or State.CharsetDesignate)
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
                if (b == 0x07 || b == 0x9C) Enter(State.Ground);   // BEL / ST
                else if (b == 0x1B) Enter(State.Escape);           // ESC \ handled via Escape
                break;
            case State.EscIntermediate: EscIntermediateByte(b); break;
            case State.CharsetDesignate: CharsetByte(b); break;
        }
    }

    private void Enter(State s)
    {
        _state = s;
        if (s is State.Csi)
        {
            _params.Clear();
            _currentParam = 0;
            _hasParam = false;
            _privateMarker = false;
            _privateChar = '\0';
            _intermediates.Clear();
        }
        else if (s is State.Osc or State.Dcs or State.SosPmApc)
        {
            _osc.Clear();
            _oscCommand = -1;
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
                    if (rune < _utf8Min) rune = 0xFFFD;         // overlong
                    if (rune > 0xFFFF)
                        _buffer.PutChar('\uFFFD');              // beyond BMP: fallback glyph
                    else
                        _buffer.PutChar((char)rune);
                }
                return;
            }
            _utf8Remaining = 0; // invalid continuation: resync
        }

        switch (b)
        {
            case 0x00: case 0x7F: break;                        // NUL/DEL ignored
            case 0x07: break;                                   // BEL
            case 0x08: _buffer.Backspace(); break;
            case 0x09: _buffer.Tab(); break;
            case 0x0A: case 0x0B: case 0x0C: _buffer.LineFeed(); break;
            case 0x0D: _buffer.CarriageReturn(); break;
            case 0x0E: _buffer.ShiftOut(); break;
            case 0x0F: _buffer.ShiftIn(); break;
            case 0x9B: Enter(State.Csi); break;                 // 8-bit CSI
            case 0x9D: Enter(State.Osc); break;
            case >= 0x80 and < 0xC0: _buffer.PutChar('\uFFFD'); break;
            case >= 0xC0 and < 0xE0: _utf8Value = b & 0x1F; _utf8Remaining = 1; _utf8Min = 0x80; break;
            case >= 0xE0 and < 0xF0: _utf8Value = b & 0x0F; _utf8Remaining = 2; _utf8Min = 0x800; break;
            case >= 0xF0: _utf8Value = b & 0x07; _utf8Remaining = 3; _utf8Min = 0x10000; break;
            default: _buffer.PutChar((char)b); break;
        }
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
            case (byte)'#': _state = State.EscIntermediate; break;
            case (byte)'7': _buffer.SaveCursor(); Enter(State.Ground); break;
            case (byte)'8': _buffer.RestoreCursor(); Enter(State.Ground); break;
            case (byte)'D': _buffer.Index(); Enter(State.Ground); break;
            case (byte)'E': _buffer.NextLine(); Enter(State.Ground); break;
            case (byte)'M': _buffer.ReverseIndex(); Enter(State.Ground); break;
            case (byte)'H': Enter(State.Ground); break;         // HTS
            case (byte)'c': Reset(); Enter(State.Ground); break; // RIS
            case (byte)'=': _buffer.ApplicationCursorKeys = true; Enter(State.Ground); break;
            case (byte)'>': _buffer.ApplicationCursorKeys = false; Enter(State.Ground); break;
            case >= 0x20 and <= 0x2F:
                _intermediates.Add((char)b);
                _state = State.EscIntermediate;
                break;
            default: Enter(State.Ground); break;
        }
    }

    private void EscIntermediateByte(byte b)
    {
        if (b == (byte)'#')
        {
            // already in intermediate via ESC # — shouldn't happen, treat as ground
            Enter(State.Ground);
            return;
        }
        if (b >= 0x20 && b <= 0x2F)
        {
            _intermediates.Add((char)b);
            return;
        }
        // Final byte
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
        _buffer.CurrentAttrs = CellAttrs.None;
        _buffer.CurrentFg = TerminalColor.Default;
        _buffer.CurrentBg = TerminalColor.Default;
        _buffer.AutoWrap = true;
        _buffer.InsertMode = false;
        _buffer.SetCursorVisible(true);
        _buffer.SetOriginMode(false);
        _buffer.ResetScrollRegion();
        _buffer.EraseInDisplay(2);
        _buffer.CursorPosition(1, 1);
    }

    // ---------- CSI ----------

    private void CsiByte(byte b)
    {
        if (b is >= (byte)'0' and <= (byte)'9')
        {
            _currentParam = _currentParam * 10 + (b - '0');
            _hasParam = true;
            return;
        }
        switch (b)
        {
            case (byte)';':
                _params.Add(_hasParam ? _currentParam : -1);
                _currentParam = 0;
                _hasParam = false;
                return;
            case (byte)'?': case (byte)'>': case (byte)'!': case (byte)'\'': case (byte)'"': case (byte)'$': case (byte)' ':
                if (_params.Count == 0 && !_hasParam && !_privateMarker)
                {
                    _privateMarker = true;
                    _privateChar = (char)b;
                    return;
                }
                _intermediates.Add((char)b);
                return;
            case >= 0x20 and <= 0x2F:
                _intermediates.Add((char)b);
                return;
        }

        // Final byte 0x40-0x7E
        if (b is >= 0x40 and <= 0x7E)
        {
            if (_hasParam || _params.Count > 0 || _currentParam != 0)
                _params.Add(_hasParam ? _currentParam : (_params.Count == 0 ? _currentParam : -1));
            DispatchCsi((char)b);
        }
        Enter(State.Ground);
    }

    private int Param(int i, int def = 1)
        => i < _params.Count && _params[i] >= 0 ? _params[i] : def;

    private void DispatchCsi(char final)
    {
        var b = _buffer;
        if (_privateMarker && _privateChar == '?')
        {
            HandleDecSet(final);
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
            case 'S': b.ScrollUpRegion(0, b.Rows - 1, Param(0)); break;
            case 'T': b.ScrollDownRegion(0, b.Rows - 1, Param(0)); break;
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
            case 'c': _responder?.Invoke("\x1b[?1;2c"u8.ToArray()); break;  // DA: vt220
            case 'n':
                if (Param(0, 0) == 6) // DSR cursor position report
                    _responder?.Invoke(Encoding.ASCII.GetBytes($"\x1b[{b.CursorY + 1};{b.CursorX + 1}R"));
                else if (Param(0, 0) == 5)
                    _responder?.Invoke("\x1b[0n"u8.ToArray());
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
                    case 47: case 1047: b.UseAlternateScreen(set); break;
                    case 1048: if (set) b.SaveCursor(); else b.RestoreCursor(); break;
                    case 1049:
                        if (set) { b.SaveCursor(); b.UseAlternateScreen(true); }
                        else { b.UseAlternateScreen(false); b.RestoreCursor(); }
                        break;
                    case 2004: break; // bracketed paste — pass-through flag (renderer handles)
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
                case 4: b.CurrentAttrs |= CellAttrs.Underline; break;
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
                    var (color, consumed) = ParseExtendedColor(i);
                    if (p == 38) b.CurrentFg = color; else b.CurrentBg = color;
                    i += consumed;
                    break;
                case 58: // underline color — consume args, ignore
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
            return (TerminalColor.Rgb(_params[i + 2], _params[i + 3], _params[i + 4]), 4);
        return (TerminalColor.Default, 0);
    }

    // ---------- OSC ----------

    private void OscByte(byte b)
    {
        if (b == 0x07) { DispatchOsc(); Enter(State.Ground); return; }  // BEL
        if (b == 0x9C) { DispatchOsc(); Enter(State.Ground); return; }  // ST (8-bit)
        if (b == 0x1B) { DispatchOsc(); Enter(State.Escape); return; }  // ESC \ (7-bit ST)

        if (_oscCommand < 0)
        {
            if (char.IsDigit((char)b))
            {
                _osc.Append((char)b);
            }
            else if (b == (byte)';')
            {
                _oscCommand = int.TryParse(_osc.ToString(), out var c) ? c : -2;
                _osc.Clear();
            }
            else
            {
                Enter(State.Ground); // malformed
            }
            return;
        }
        _osc.Append((char)b);
    }

    private void DispatchOsc()
    {
        var text = _osc.ToString();
        switch (_oscCommand)
        {
            case 0: case 1: case 2:
                _buffer.SetTitle(text);
                break;
            case 8: break;   // hyperlink — ignore payload
            case 9: case 777: break; // notifications
            case 4: break;   // palette query/set
            case 7: break;   // cwd report (OSC 7) — could track cwd
            default: break;
        }
    }
}
