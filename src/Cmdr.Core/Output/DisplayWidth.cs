using System.Globalization;
using System.Text;

namespace Cmdr.Core.Output;

/// <summary>
/// The one display-width model for text: the number of terminal columns a string occupies. Width is measured per
/// grapheme cluster (a <see cref="StringInfo"/> text element), so a base character with combining marks or an emoji
/// sequence counts as one unit.
/// </summary>
/// <remarks>
/// The width of a single grapheme comes from <see cref="GraphemeWidth"/>. Core uses <see cref="DefaultGraphemeWidth"/>
/// unless the application plugs in a different measure; the UI plugs in Terminal.Gui's measure so that layout matches
/// what is drawn. The function is an approximation of what terminals do.
/// </remarks>
public static class DisplayWidth
{
    private static Func<string, int> _graphemeWidth = DefaultGraphemeWidth;

    /// <summary>
    /// The width in terminal columns of one grapheme cluster (a <see cref="StringInfo"/> text element). Results below 0
    /// are treated as 0. Control characters should return 1 so that layout never collapses; the default does.
    /// </summary>
    /// <remarks>
    /// Set once at startup, before any output is parsed. The value is read without synchronization and is not meant to
    /// change while text is being measured.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public static Func<string, int> GraphemeWidth
    {
        get => _graphemeWidth;
        set => _graphemeWidth = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The display width of <paramref name="text"/>: the sum of <see cref="GraphemeWidth"/> over its grapheme clusters.</summary>
    public static int Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Of(text.AsSpan());
    }

    /// <summary>The display width of <paramref name="text"/>: the sum of <see cref="GraphemeWidth"/> over its grapheme clusters.</summary>
    public static int Of(ReadOnlySpan<char> text)
    {
        int total = 0;
        int index = 0;
        while (index < text.Length)
        {
            int length = StringInfo.GetNextTextElementLength(text[index..]);
            total += OfGrapheme(text.Slice(index, length));
            index += length;
        }

        return total;
    }

    /// <summary>
    /// The width of one grapheme cluster: <see cref="GraphemeWidth"/>, at least 0. A single printable ASCII character is 1
    /// without calling it (every measure agrees), so plain text is measured without allocating.
    /// </summary>
    public static int OfGrapheme(ReadOnlySpan<char> grapheme)
    {
        if (grapheme.Length == 1 && grapheme[0] is >= ' ' and <= '~')
        {
            return 1;
        }

        return grapheme.IsEmpty ? 0 : Math.Max(0, _graphemeWidth(grapheme.ToString()));
    }

    /// <summary>
    /// The default width of one grapheme cluster, used when no other measure is plugged in. It approximates terminal
    /// behavior: 0 for an empty grapheme; 2 if any rune is wide (East Asian Wide or Fullwidth, or emoji) or the grapheme is
    /// a pair of regional indicators (a flag); 0 if every rune is zero-width (combining marks, format characters,
    /// variation selectors, medial and final Hangul jamo); otherwise 1. A control character counts as 1, so that layout
    /// never collapses, and U+00AD (soft hyphen) counts as 1 although it is a format character.
    /// </summary>
    public static int DefaultGraphemeWidth(string grapheme)
    {
        ArgumentNullException.ThrowIfNull(grapheme);

        if (grapheme.Length == 0)
        {
            return 0;
        }

        bool allZeroWidth = true;
        bool regionalPair = true;
        int runeCount = 0;
        foreach (Rune rune in grapheme.EnumerateRunes())
        {
            runeCount++;
            if (IsWide(rune))
            {
                return 2;
            }

            if (!IsRegionalIndicator(rune))
            {
                regionalPair = false;
            }

            if (!IsZeroWidth(rune))
            {
                allZeroWidth = false;
            }
        }

        if (regionalPair && runeCount == 2)
        {
            return 2;
        }

        return allZeroWidth ? 0 : 1;
    }

    private static bool IsRegionalIndicator(Rune rune) => rune.Value is >= 0x1F1E6 and <= 0x1F1FF;

    private static bool IsZeroWidth(Rune rune)
    {
        int value = rune.Value;
        if (value == 0x00AD)
        {
            return false; // Soft hyphen is a format character that terminals draw as one column.
        }

        if ((value >= 0xFE00 && value <= 0xFE0F) || (value >= 0xE0100 && value <= 0xE01EF))
        {
            return true; // Variation selectors.
        }

        if (value >= 0x1160 && value <= 0x11FF)
        {
            return true; // Hangul medial vowels and final consonants.
        }

        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format;
    }

    private static bool IsWide(Rune rune)
    {
        int value = rune.Value;
        int lo = 0;
        int hi = WideRanges.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            (int first, int last) = WideRanges[mid];
            if (value < first)
            {
                hi = mid - 1;
            }
            else if (value > last)
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Code point ranges (inclusive, sorted, non-overlapping) of East Asian Wide and Fullwidth characters and of emoji
    /// that terminals draw two columns wide.
    /// </summary>
    private static readonly (int First, int Last)[] WideRanges =
    {
        (0x1100, 0x115F),
        (0x231A, 0x231B),
        (0x2329, 0x232A),
        (0x23E9, 0x23EC),
        (0x23F0, 0x23F0),
        (0x23F3, 0x23F3),
        (0x25FD, 0x25FE),
        (0x2614, 0x2615),
        (0x2648, 0x2653),
        (0x267F, 0x267F),
        (0x2693, 0x2693),
        (0x26A1, 0x26A1),
        (0x26AA, 0x26AB),
        (0x26BD, 0x26BE),
        (0x26C4, 0x26C5),
        (0x26CE, 0x26CE),
        (0x26D4, 0x26D4),
        (0x26EA, 0x26EA),
        (0x26F2, 0x26F3),
        (0x26F5, 0x26F5),
        (0x26FA, 0x26FA),
        (0x26FD, 0x26FD),
        (0x2705, 0x2705),
        (0x270A, 0x270B),
        (0x2728, 0x2728),
        (0x274C, 0x274C),
        (0x274E, 0x274E),
        (0x2753, 0x2755),
        (0x2757, 0x2757),
        (0x2795, 0x2797),
        (0x27B0, 0x27B0),
        (0x27BF, 0x27BF),
        (0x2B1B, 0x2B1C),
        (0x2B50, 0x2B50),
        (0x2B55, 0x2B55),
        (0x2E80, 0x303E),
        (0x3041, 0x33FF),
        (0x3400, 0x4DBF),
        (0x4E00, 0x9FFF),
        (0xA000, 0xA4CF),
        (0xA960, 0xA97F),
        (0xAC00, 0xD7A3),
        (0xF900, 0xFAFF),
        (0xFE10, 0xFE19),
        (0xFE30, 0xFE6F),
        (0xFF00, 0xFF60),
        (0xFFE0, 0xFFE6),
        (0x1F004, 0x1F004),
        (0x1F0CF, 0x1F0CF),
        (0x1F18E, 0x1F18E),
        (0x1F191, 0x1F19A),
        (0x1F200, 0x1F251),
        (0x1F300, 0x1F64F), // Includes the skin tone modifiers U+1F3FB-U+1F3FF.
        (0x1F680, 0x1F6FF),
        (0x1F7E0, 0x1F7EB),
        (0x1F90C, 0x1F9FF),
        (0x1FA70, 0x1FAFF),
        (0x20000, 0x2FFFD),
        (0x30000, 0x3FFFD),
    };
}
