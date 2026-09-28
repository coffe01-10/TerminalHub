using System.Text;
using System.Text.RegularExpressions;

namespace TerminalHub.Core.Logging;

/// <summary>
/// Incremental UTF-8 decoder + line splitter for PTY byte streams.
/// Feed arbitrary chunks; complete lines are emitted via <see cref="LineReceived"/>.
/// Line semantics: \r\n or \n completes a line; a lone \r is a carriage-return
/// redraw (progress bars) and restarts the pending line instead of emitting it.
/// </summary>
public sealed class Utf8LineDecoder
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _line = new();
    private bool _pendingCR;

    /// <summary>Raised for each completed line (no trailing newline).</summary>
    public event Action<string>? LineReceived;

    public void Feed(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return;
        var max = _decoder.GetCharCount(data, false);
        var buf = new char[max];
        _decoder.GetChars(data, buf, false);

        for (var i = 0; i < buf.Length; i++)
        {
            var ch = buf[i];

            // Resolve a \r that ended the previous chunk: \n → CRLF line end,
            // anything else → lone-CR redraw (restart the line).
            if (_pendingCR)
            {
                _pendingCR = false;
                if (ch == '\n') { Emit(); continue; }
                _line.Clear();
            }

            if (ch == '\r')
            {
                if (i + 1 < buf.Length)
                {
                    if (buf[i + 1] == '\n') { Emit(); i++; }
                    else _line.Clear(); // CR-only redraw → restart the pending line
                }
                else _pendingCR = true; // \r at chunk end — decide on next chunk
            }
            else if (ch == '\n')
            {
                Emit();
            }
            else
            {
                _line.Append(ch);
            }
        }
    }

    private void Emit()
    {
        var text = AnsiText.Strip(_line.ToString());
        _line.Clear();
        if (text.Length > 0) LineReceived?.Invoke(text);
    }
}

/// <summary>Strip ANSI escape sequences (OSC / CSI / charset / simple escapes).</summary>
public static partial class AnsiText
{
    [GeneratedRegex(@"\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)|\x1b\[[0-9;?>=!]*[ -/]*[@-~]|\x1b[()#][0-9A-Za-z]|\x1b[@-Z\\-_]|\x1b[>=]", RegexOptions.Compiled)]
    private static partial Regex AnsiPattern();

    public static string Strip(string input)
        => string.IsNullOrEmpty(input) ? input : AnsiPattern().Replace(input, "");
}
