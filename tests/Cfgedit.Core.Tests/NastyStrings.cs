namespace Cfgedit.Core.Tests;

/// <summary>
/// A shared corpus of nasty strings: empty, both quote kinds, backslashes, every control character, U+007F, U+2028, U+2029,
/// lone surrogates, astral characters, ZWJ and combining sequences, markup characters, line breaks, and a long string. Later
/// milestones use it too.
/// </summary>
internal static class NastyStrings
{
    /// <summary>The corpus, in a fixed order.</summary>
    public static IReadOnlyList<string> All { get; } = Build();

    private static List<string> Build()
    {
        var list = new List<string>
        {
            "",
            "\"",
            "'",
            "\"'",
            "'\"",
            "say \"hi\"",
            "it's",
            "\\",
            "\\\\",
            "C:\\path\\file",
            "\\u0041",
            "\\\"",
            "\u007F",
            "\u2028",
            "\u2029",
            "a\u2028b\u2029c",
            "\uD800",
            "\uDC00",
            "\uD800x",
            "x\uDFFF",
            "\uD83D\uD83D\uDE00",
            "\U0001F600",
            "a\U0001F600b",
            "\U0001F468\u200D\U0001F469\u200D\U0001F467",
            "e\u0301",
            "e\u0301\u0323",
            "]]>",
            "<![CDATA[x]]>",
            "&",
            "&amp;",
            "<",
            "<tag attr=\"v\"/>",
            "\r\n",
            "\r",
            "\n",
            "a\rb\r\nc\nd",
            "\u00A0\uFEFF\u0085",
            "\u0080\u009F",
            "\u00E9 \u65E5\u672C\u8A9E",
            "\0",
            "\u00001",
            "\u0000\u0001\u0002",
            "\t\v\f\b",
        };

        // Every control character from U+0000 to U+001F, alone and inside text.
        for (char c = '\0'; c <= '\u001F'; c++)
        {
            list.Add(c.ToString());
            list.Add("a" + c + "b");
        }

        // A 10,000-character string holding every awkward character in turn.
        string unit = "ab\"\\\n'\u2028";
        string repeated = string.Concat(Enumerable.Repeat(unit, 10000 / unit.Length + 1));
        list.Add(repeated[..10000]);

        return list;
    }
}
