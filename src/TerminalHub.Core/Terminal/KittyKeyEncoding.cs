namespace TerminalHub.Core.Terminal;

/// <summary>The slice of the kitty keyboard protocol (CSI u) that AI CLIs
/// actually depend on, applied only after the CLI negotiated the disambiguate
/// flag. Arrow, Home, End and paging keys deliberately keep their legacy
/// encodings: those already distinguish modifiers, and a wrong functional-key
/// number would break cursor movement.</summary>
public static class KittyKeyEncoding
{
    public const int Disambiguate = 1;

    /// <summary>CSI u for Enter (13), Tab (9) or Backspace (127) held with a
    /// modifier, or null to keep the legacy byte. Unmodified keys stay legacy
    /// so a plain Enter still submits.</summary>
    public static string? Functional(int flags, int code, int modifiers)
    {
        if ((flags & Disambiguate) == 0 || modifiers <= 1) return null;
        if (code is not (13 or 9 or 127)) return null;
        return $"\x1b[{code};{modifiers}u";
    }

    /// <summary>CSI u for a Ctrl or Alt letter, or null to keep the legacy
    /// control byte / ESC prefix. Shift on its own is excluded: it changes the
    /// character, which arrives through text input.</summary>
    public static string? TextKey(int flags, char character, int modifiers)
    {
        if ((flags & Disambiguate) == 0 || modifiers <= 1) return null;
        if (character is < 'a' or > 'z') return null;
        return $"\x1b[{character};{modifiers}u";
    }
}
