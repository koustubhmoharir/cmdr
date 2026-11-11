using System.Text;

namespace Cmdr.Tui;

/// <summary>The result of unescaping a text: the value, or the error that prevents it. Error is null when valid.</summary>
internal readonly record struct EscapeParseResult(string Value, string? Error);

/// <summary>
/// The stop input field: a text with C-style escapes so that control characters can be typed. Supported escapes are
/// <c>\n \r \t \\ \e</c> (ESC) and <c>\xHH</c>. Pure.
/// </summary>
internal static class EscapeText
{
    /// <summary>Escapes backslashes and control characters. Null is written as the empty text.</summary>
    public static string Format(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\u001b': sb.Append("\\e"); break;
                default:
                    if (c < 0x20 || c == 0x7f)
                    {
                        sb.Append("\\x").Append(((int)c).ToString("X2"));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Unescapes the text. An unknown escape, a trailing backslash, or a malformed <c>\x</c> is an error.</summary>
    public static EscapeParseResult Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            if (i + 1 >= text.Length)
            {
                return Fail("a trailing backslash; write \\\\ for a backslash.");
            }

            char e = text[++i];
            switch (e)
            {
                case '\\': sb.Append('\\'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'e': sb.Append('\u001b'); break;
                case 'x':
                    if (i + 2 < text.Length
                        && IsHex(text[i + 1])
                        && IsHex(text[i + 2]))
                    {
                        sb.Append((char)Convert.ToInt32(text.Substring(i + 1, 2), 16));
                        i += 2;
                        break;
                    }

                    return Fail("\\x must be followed by two hex digits.");
                default:
                    return Fail($"unknown escape '\\{e}'. Use \\n \\r \\t \\\\ \\e or \\xHH.");
            }
        }

        return new EscapeParseResult(sb.ToString(), null);
    }

    private static EscapeParseResult Fail(string message) => new(string.Empty, "Stop input: " + message);

    private static bool IsHex(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
