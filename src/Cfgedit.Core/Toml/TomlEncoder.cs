using System.Text;

namespace Cfgedit.Core.Toml;

/// <summary>
/// Writes keys and strings as TOML tokens, and checks number and date-time text against the TOML grammar. Every token written
/// parses back to the value given: <c>key = </c> followed by the encoded string and a line break decodes to that string, for
/// LF and for CRLF as the document's line break. A value that no TOML token can hold (a lone surrogate) is refused rather than
/// changed.
/// </summary>
public static class TomlEncoder
{
    private const string LoneSurrogateMessage = "a TOML string cannot contain a lone surrogate";

    /// <summary>
    /// Writes a key name. A non-empty name of ASCII letters, digits, <c>-</c> and <c>_</c> is written bare. Any other name is
    /// written as a basic-quoted key with escapes.
    /// </summary>
    /// <param name="name">The decoded name.</param>
    /// <returns>The key token.</returns>
    /// <exception cref="CfgeditException">The name contains a lone surrogate.</exception>
    public static string EncodeKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        CheckSurrogates(name);
        if (IsBareKey(name))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 2);
        sb.Append('"');
        AppendEscaped(sb, name);
        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Writes a string in the plainest form that can hold it. A value with a line break is written as a multi-line basic string.
    /// A value with a backslash or a quotation mark, and no apostrophe or control character other than tab, is written as a
    /// literal string, which needs no escapes. Any other value is written as a basic string with escapes.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="newline">The document's line break, LF or CRLF. It is written for each line break in a multi-line string.</param>
    /// <returns>The string token, with its delimiters.</returns>
    /// <exception cref="CfgeditException">The value contains a lone surrogate.</exception>
    /// <exception cref="ArgumentException">The newline is not LF or CRLF.</exception>
    public static string EncodeString(string value, string newline)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckNewline(newline);
        CheckSurrogates(value);

        if (HasLineBreak(value))
        {
            return WriteMultiLineBasic(value, newline);
        }

        if (CanBeLiteral(value) && (value.Contains('\\') || value.Contains('"')))
        {
            return WriteLiteral(value);
        }

        return WriteBasic(value);
    }

    /// <summary>
    /// Writes a string in the given form when that form can hold the value, and otherwise as <see cref="EncodeString"/> does. The
    /// forms hold these values:
    /// <list type="bullet">
    /// <item><description>Basic: any value (escapes are used).</description></item>
    /// <item><description>Literal: no apostrophe, no line break and no control character other than tab.</description></item>
    /// <item><description>Multi-line basic: any value.</description></item>
    /// <item><description>
    /// Multi-line literal: no run of three or more apostrophes, no control character other than tab and LF, and no CR (a CR
    /// cannot be written in a literal string, since CRLF reads back as LF).
    /// </description></item>
    /// </list>
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="preferred">The form the string had before it was edited.</param>
    /// <param name="newline">The document's line break, LF or CRLF.</param>
    /// <returns>The string token, with its delimiters.</returns>
    /// <exception cref="CfgeditException">The value contains a lone surrogate.</exception>
    /// <exception cref="ArgumentException">The newline is not LF or CRLF.</exception>
    public static string EncodeStringKeeping(string value, TomlStringForm preferred, string newline)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckNewline(newline);
        CheckSurrogates(value);

        switch (preferred)
        {
            case TomlStringForm.Basic:
                return WriteBasic(value);
            case TomlStringForm.Literal when CanBeLiteral(value):
                return WriteLiteral(value);
            case TomlStringForm.MultiLineBasic:
                return WriteMultiLineBasic(value, newline);
            case TomlStringForm.MultiLineLiteral when CanBeMultiLineLiteral(value):
                return WriteMultiLineLiteral(value, newline);
            default:
                return EncodeString(value, newline);
        }
    }

    /// <summary>True when the text is a valid TOML integer: decimal, hexadecimal, octal or binary, within the signed 64-bit range.</summary>
    /// <param name="text">The text, sign and prefix included.</param>
    public static bool IsValidInteger(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TomlScalars.ValidateInteger(text, out _, out _) is null;
    }

    /// <summary>True when the text is a valid TOML float: a decimal with a fraction or an exponent, or <c>inf</c> or <c>nan</c>, with an optional sign.</summary>
    /// <param name="text">The text.</param>
    public static bool IsValidFloat(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TomlScalars.ValidateFloat(text, out _) is null;
    }

    /// <summary>
    /// True when the text is a valid TOML date or time, and sets <paramref name="kind"/> to the kind it has. The text is checked
    /// for the grammar and for the ranges of its fields.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="kind">The kind of date or time, when the text is valid.</param>
    public static bool TryParseDateTime(string text, out TomlDateTimeKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TomlScalars.ValidateDateTime(text, out kind) is null;
    }

    private static bool IsBareKey(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!TomlParser.IsBareKeyCharacter(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasLineBreak(string value)
    {
        return value.Contains('\n') || value.Contains('\r');
    }

    /// <summary>A literal string can hold a value with no apostrophe, no control character other than tab, and no line break.</summary>
    private static bool CanBeLiteral(string value)
    {
        foreach (char c in value)
        {
            if (c == '\'' || (c < 0x20 ? c != '\t' : c == 0x7F))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanBeMultiLineLiteral(string value)
    {
        int apostrophes = 0;
        foreach (char c in value)
        {
            if (c == '\'')
            {
                apostrophes++;
                if (apostrophes >= 3)
                {
                    return false;
                }

                continue;
            }

            apostrophes = 0;
            if (c < 0x20 ? c is not ('\t' or '\n') : c == 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static string WriteBasic(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        AppendEscaped(sb, value);
        sb.Append('"');
        return sb.ToString();
    }

    private static string WriteLiteral(string value)
    {
        return "'" + value + "'";
    }

    /// <summary>
    /// A multi-line basic string. The line break after the opening delimiter is trimmed when read, so one is always written
    /// there: then a line break that begins the value is kept. A run of three or more quotation marks has each of its marks
    /// escaped, so that none of them can close the string.
    /// </summary>
    private static string WriteMultiLineBasic(string value, string newline)
    {
        var sb = new StringBuilder(value.Length + 16);
        sb.Append("\"\"\"").Append(newline);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '"')
            {
                int run = RunLength(value, i);
                if (run >= 3)
                {
                    for (int k = 0; k < run; k++)
                    {
                        sb.Append("\\\"");
                    }
                }
                else
                {
                    sb.Append('"', run);
                }

                i += run - 1;
                continue;
            }

            if (c == '\n')
            {
                sb.Append(newline);
            }
            else if (c == '\t')
            {
                sb.Append('\t');
            }
            else
            {
                AppendEscapedCharacter(sb, value, i);
            }
        }

        sb.Append("\"\"\"");
        return sb.ToString();
    }

    private static string WriteMultiLineLiteral(string value, string newline)
    {
        var sb = new StringBuilder(value.Length + 16);
        sb.Append("'''").Append(newline);
        foreach (char c in value)
        {
            if (c == '\n')
            {
                sb.Append(newline);
            }
            else
            {
                sb.Append(c);
            }
        }

        sb.Append("'''");
        return sb.ToString();
    }

    /// <summary>Appends the value with the escapes a basic string needs: quotation mark, backslash and the control characters.</summary>
    private static void AppendEscaped(StringBuilder sb, string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            AppendEscapedCharacter(sb, value, i);
        }
    }

    /// <summary>
    /// Appends the character at <paramref name="i"/> as a basic string writes it. A surrogate pair is appended whole. Control
    /// characters take their short escape when TOML has one, and <c>\uXXXX</c> otherwise.
    /// </summary>
    private static void AppendEscapedCharacter(StringBuilder sb, string value, int i)
    {
        char c = value[i];
        switch (c)
        {
            case '"':
                sb.Append("\\\"");
                return;
            case '\\':
                sb.Append("\\\\");
                return;
            case '\b':
                sb.Append("\\b");
                return;
            case '\t':
                sb.Append("\\t");
                return;
            case '\n':
                sb.Append("\\n");
                return;
            case '\f':
                sb.Append("\\f");
                return;
            case '\r':
                sb.Append("\\r");
                return;
        }

        if (c < 0x20 || c == 0x7F)
        {
            sb.Append("\\u").Append(((int)c).ToString("X4"));
            return;
        }

        sb.Append(c);
    }

    private static int RunLength(string value, int i)
    {
        int n = 0;
        while (i + n < value.Length && value[i + n] == '"')
        {
            n++;
        }

        return n;
    }

    private static void CheckSurrogates(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                throw new CfgeditException(LoneSurrogateMessage);
            }
        }
    }

    private static void CheckNewline(string newline)
    {
        ArgumentNullException.ThrowIfNull(newline);
        if (newline is not ("\n" or "\r\n"))
        {
            throw new ArgumentException("The line break must be LF or CRLF.", nameof(newline));
        }
    }
}
