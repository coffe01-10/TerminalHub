using System.Text;
using TerminalHub.Core.Terminal;

namespace TerminalHub.App.Controls;

public sealed record TerminalInput(byte[] Bytes, string? Paste = null)
{
    public static TerminalInput Text(string text) => new(Encoding.UTF8.GetBytes(text));
    public void SendTo(TerminalEmulator emulator)
    {
        if (Paste is { } text) emulator.PasteText(text);
        else emulator.SendBytes(Bytes);
    }
}
