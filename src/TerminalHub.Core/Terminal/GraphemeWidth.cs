using System.Globalization;
using System.Text;

namespace TerminalHub.Core.Terminal;

/// <summary>
/// Terminal cell-width rules shared by the screen buffer, the renderer, the IME
/// anchor and text selection. A "cell width" is the number of grid columns a
/// grapheme occupies: 0 for zero-width code points (combining marks, variation
/// selectors, format characters — they join the preceding glyph), 2 for
/// East-Asian wide chars and emoji, 1 otherwise.
/// </summary>
public static class GraphemeWidth
{
    /// <summary>Cell width of one Unicode code point (0, 1 or 2).</summary>
    public static int OfRune(int rune)
    {
        if (IsZeroWidthRune(rune)) return 0;
        return IsWideRune(rune) ? 2 : 1;
    }

    /// <summary>Width of a UTF-16 code unit. Kept for the old CharWidth call sites.</summary>
    public static int OfChar(char ch) => OfRune(ch);

    /// <summary>
    /// Zero-width code points: they extend the previous grapheme cluster instead
    /// of occupying a cell (combining Mn/Me marks, spacing-dependent Mc marks,
    /// format chars like ZWJ, variation selectors).
    /// </summary>
    public static bool IsZeroWidthRune(int rune)
    {
        if (rune is 0x200D or 0xFE0E or 0xFE0F) return true;         // ZWJ, VS15/VS16
        if (rune is >= 0xFE00 and <= 0xFE0F) return true;           // VS1..VS16
        if (rune is >= 0xE0100 and <= 0xE01EF) return true;         // VS17..VS256
        // Emoji skin-tone modifiers are category Sk, outside the mark
        // categories below — but UAX #29 classifies them as Extend: they join
        // the base emoji as one cluster instead of taking 2 cells of their own.
        if (rune is >= 0x1F3FB and <= 0x1F3FF) return true;
        // U+00AD SOFT HYPHEN is category Format but renders as a visible
        // hyphen in terminals — wcwidth treats it as 1 cell, not zero.
        if (rune is 0x00AD) return false;
        var category = rune <= 0xFFFF
            ? CharUnicodeInfo.GetUnicodeCategory((char)rune)
            : CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(rune), 0);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.Format;
    }

    /// <summary>Regional indicator (flag letters); two of them render as one flag glyph.</summary>
    public static bool IsRegionalIndicator(int rune) => rune is >= 0x1F1E6 and <= 0x1F1FF;

    /// <summary>
    /// True when this mark turns the base char into emoji presentation (VS16,
    /// enclosing keycap, skin-tone modifier). The buffer widens the cell in
    /// place when possible.
    /// </summary>
    public static bool EmojiWidthTrigger(int rune)
        => rune is 0xFE0F or 0x20E3 || rune is >= 0x1F3FB and <= 0x1F3FF;

    /// <summary>Chars that take emoji (2-cell) presentation when followed by VS16.</summary>
    public static bool IsEmojiBase(char ch) =>
        ch is '#' or '*' or >= '0' and <= '9'
        || ch is >= '‰' and <= '〹'     // U+2030..U+3299: ‰ ↔ ☀ ✈ ✨ ❤ ♂ ♀ …
        || char.IsSurrogate(ch);                          // supplementary runes are emoji-width anyway

    /// <summary>Wide (2-cell) code points: East-Asian wide/fullwidth plus emoji blocks.</summary>
    public static bool IsWideRune(int rune) =>
        rune >= 0x1100 && (
            rune <= 0x115F ||                                       // Hangul Jamo
            rune is 0x2329 or 0x232A ||
            // Discrete EAW=W emoji ranges in the BMP, outside the big CJK blocks
            // (each missing range drifts the cursor one column per character):
            (rune >= 0x231A && rune <= 0x231B) ||                   // ⌚⌛
            (rune >= 0x23E9 && rune <= 0x23EC) ||                   // ⏩⏪⏭⏮
            rune is 0x23F0 or 0x23F3 ||                             // ⏰⏳
            (rune >= 0x25FD && rune <= 0x25FE) ||                   // ◼◻
            (rune >= 0x2614 && rune <= 0x2615) ||                   // ☔☕
            (rune >= 0x2648 && rune <= 0x2653) ||                   // ♈..♓
            rune is 0x267F or 0x2693 or 0x26A1 ||                   // ♿⚓⚡
            (rune >= 0x26AA && rune <= 0x26AB) ||                   // ⚪⚫
            (rune >= 0x26BD && rune <= 0x26BE) ||                   // ⚽⚾
            (rune >= 0x26C4 && rune <= 0x26C5) ||                   // ⛄⛅
            rune is 0x26CE or 0x26D4 or 0x26EA ||                   // ⛎🚫⛪
            (rune >= 0x26F2 && rune <= 0x26F3) ||                   // ⛲⛳
            rune is 0x26F5 or 0x26FA or 0x26FD ||                   // ⛵⛺⛽
            rune is 0x2705 ||                                       // ✅
            (rune >= 0x270A && rune <= 0x270B) ||                   // ✊✋
            rune is 0x2728 or 0x274C or 0x274E ||                   // ✨❌❎
            (rune >= 0x2753 && rune <= 0x2755) ||                   // ❓❔❕
            rune is 0x2757 ||                                       // ❗
            (rune >= 0x2795 && rune <= 0x2797) ||                   // ➕➖➗
            rune is 0x27B0 or 0x27BF ||                             // ➿➾
            (rune >= 0x2B1B && rune <= 0x2B1C) ||                   // ⬛⬜
            rune is 0x2B50 or 0x2B55 ||                             // ⭐⭕
            (rune >= 0x2E80 && rune <= 0xA4CF && rune != 0x303F) || // CJK radicals .. Yi
            (rune >= 0xAC00 && rune <= 0xD7A3) ||                   // Hangul syllables
            (rune >= 0xF900 && rune <= 0xFAFF) ||                   // CJK compat ideographs
            (rune >= 0xFE30 && rune <= 0xFE6F) ||                   // CJK compat forms
            (rune >= 0xFF00 && rune <= 0xFF60) ||                   // fullwidth forms
            (rune >= 0xFFE0 && rune <= 0xFFE6) ||                   // fullwidth signs
            (rune >= 0x1F000 && rune <= 0x1F0FF) ||                 // mahjong / playing cards
            (rune >= 0x1F1E6 && rune <= 0x1F1FF) ||                 // regional indicators (flags)
            (rune >= 0x1F300 && rune <= 0x1FAFF) ||                 // emoji & pictographs
            (rune >= 0x20000 && rune <= 0x3FFFD));                  // CJK ext B+ / compat supplement

    /// <summary>
    /// Cell width of a display string (IME preedit, copied text): zero-width
    /// runes join their cluster and count 0; regional-indicator pairs count once.
    /// </summary>
    public static int OfText(string text)
    {
        var width = 0;
        var pendingRI = false;
        foreach (var rune in EnumerateRunes(text))
        {
            if (IsZeroWidthRune(rune)) continue;
            if (IsRegionalIndicator(rune))
            {
                if (pendingRI) { pendingRI = false; continue; }
                pendingRI = true;
                width += 2;
                continue;
            }
            pendingRI = false;
            var w = OfRune(rune);
            width += w < 1 ? 1 : w;
        }
        return width;
    }

    /// <summary>
    /// Cell width of one grapheme cluster already extracted from text (a cell's
    /// <c>Char+Tail</c>, a preedit slice). VS16 after an emoji-capable base flips
    /// the cluster to 2 cells even when the base alone was narrow; two regional
    /// indicators count once.
    /// </summary>
    public static int OfCluster(string cluster)
    {
        var width = 0;
        var emoji = false;
        var seenRI = false;
        foreach (var rune in EnumerateRunes(cluster))
        {
            if (IsZeroWidthRune(rune))
            {
                if (EmojiWidthTrigger(rune)) emoji = true;
                continue;
            }
            if (IsRegionalIndicator(rune))
            {
                if (seenRI) continue;
                seenRI = true;
                width = Math.Max(width, 2);
                continue;
            }
            var w = OfRune(rune);
            width += w < 1 ? 1 : w;
        }
        if (emoji && width < 2) width = 2;
        return width;
    }

    /// <summary>Iterate a string as Unicode scalar values (surrogate-safe).</summary>
    public static IEnumerable<int> EnumerateRunes(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return char.ConvertToUtf32(ch, text[i + 1]);
                i++;
            }
            else yield return ch;
        }
    }
}
