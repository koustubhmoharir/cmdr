using System.Text;
using Shared.Core;

namespace Cfgedit.Core.Toml;

/// <summary>
/// Reads the strings and comments of TOML text, and decodes them. Each reader starts at its opening delimiter and returns the
/// node with its exact span; a text that breaks a rule throws a <see cref="CfgeditException"/> at the offending character.
/// The rules are the TOML 1.0 ones: tab is the only control character allowed in a string or a comment, a line break is LF
/// or CRLF (a lone CR is an error), and a value of U+D800 to U+DFFF that is not part of a pair is an error.
/// </summary>
internal static class TomlStrings
{
    private const string LoneSurrogateMessage = "a lone surrogate is not valid in TOML";
    private const string LoneCarriageReturnMessage = "a carriage return must be followed by a line feed";

    /// <summary>A syntax error at an offset, with its line and column.</summary>
    internal static CfgeditException Fail(SourceText source, int offset, string message)
    {
        LinePosition at = source.GetPosition(offset);
        return new CfgeditException(message)
        {
            Line = at.Line,
            Column = at.Column,
        };
    }

    /// <summary>What is at an offset, for an error message: the character, or the end of the line or of the text.</summary>
    internal static string Describe(string s, int i)
    {
        if (i >= s.Length)
        {
            return "end of file";
        }

        char c = s[i];
        if (c is '\n' or '\r')
        {
            return "end of line";
        }

        if (c < 0x20 || c == 0x7F)
        {
            return $"U+{(int)c:X4}";
        }

        if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
        {
            return $"'{s.Substring(i, 2)}'";
        }

        return $"'{c}'";
    }

    /// <summary>Reads a single-line basic string, <c>"…"</c>, that starts at <paramref name="start"/>.</summary>
    internal static TomlString ReadBasic(SourceText source, int start)
    {
        string s = source.Text;
        var sb = new StringBuilder();
        int i = start + 1;
        while (true)
        {
            if (i >= s.Length)
            {
                throw Fail(source, start, "a basic string is not closed");
            }

            char c = s[i];
            if (c == '"')
            {
                i++;
                break;
            }

            if (c == '\\')
            {
                i = ReadEscape(source, i, sb);
                continue;
            }

            if (c is '\n' or '\r')
            {
                throw Fail(source, start, "a basic string must close on the same line");
            }

            i = AppendCharacter(source, i, sb);
        }

        return new TomlString(start, i, TomlStringForm.Basic, s.Substring(start, i - start), sb.ToString());
    }

    /// <summary>Reads a single-line literal string, <c>'…'</c>, that starts at <paramref name="start"/>.</summary>
    internal static TomlString ReadLiteral(SourceText source, int start)
    {
        string s = source.Text;
        var sb = new StringBuilder();
        int i = start + 1;
        while (true)
        {
            if (i >= s.Length)
            {
                throw Fail(source, start, "a literal string is not closed");
            }

            char c = s[i];
            if (c == '\'')
            {
                i++;
                break;
            }

            if (c is '\n' or '\r')
            {
                throw Fail(source, start, "a literal string must close on the same line");
            }

            i = AppendCharacter(source, i, sb);
        }

        return new TomlString(start, i, TomlStringForm.Literal, s.Substring(start, i - start), sb.ToString());
    }

    /// <summary>
    /// Reads a multi-line basic string, <c>"""…"""</c>, that starts at <paramref name="start"/>. A newline right after the
    /// opening delimiter is trimmed. A line-ending backslash trims the whitespace and newlines after it. A closing delimiter
    /// may be preceded by one or two quotation marks, which are part of the value; a run of six or more is an error.
    /// </summary>
    internal static TomlString ReadMultiLineBasic(SourceText source, int start)
    {
        string s = source.Text;
        var sb = new StringBuilder();
        int i = SkipOpeningNewline(s, start + 3);
        while (true)
        {
            if (i >= s.Length)
            {
                throw Fail(source, start, "a multi-line basic string is not closed");
            }

            char c = s[i];
            if (c == '"')
            {
                int run = RunLength(s, i, '"');
                if (run >= 3)
                {
                    if (run > 5)
                    {
                        throw Fail(source, i, "a multi-line string cannot end with more than two quotation marks before its closing delimiter");
                    }

                    sb.Append('"', run - 3);
                    i += run;
                    break;
                }

                sb.Append('"', run);
                i += run;
                continue;
            }

            if (c == '\\')
            {
                i = ReadMultiLineBackslash(source, i, sb);
                continue;
            }

            if (c == '\n')
            {
                sb.Append('\n');
                i++;
                continue;
            }

            if (c == '\r')
            {
                i = ReadCarriageReturnNewline(source, i, sb);
                continue;
            }

            i = AppendCharacter(source, i, sb);
        }

        return new TomlString(start, i, TomlStringForm.MultiLineBasic, s.Substring(start, i - start), sb.ToString());
    }

    /// <summary>
    /// Reads a multi-line literal string, <c>'''…'''</c>, that starts at <paramref name="start"/>. It has no escapes. The rules for
    /// the opening newline and for the quotation marks before the closing delimiter are those of multi-line basic strings.
    /// </summary>
    internal static TomlString ReadMultiLineLiteral(SourceText source, int start)
    {
        string s = source.Text;
        var sb = new StringBuilder();
        int i = SkipOpeningNewline(s, start + 3);
        while (true)
        {
            if (i >= s.Length)
            {
                throw Fail(source, start, "a multi-line literal string is not closed");
            }

            char c = s[i];
            if (c == '\'')
            {
                int run = RunLength(s, i, '\'');
                if (run >= 3)
                {
                    if (run > 5)
                    {
                        throw Fail(source, i, "a multi-line string cannot end with more than two quotation marks before its closing delimiter");
                    }

                    sb.Append('\'', run - 3);
                    i += run;
                    break;
                }

                sb.Append('\'', run);
                i += run;
                continue;
            }

            if (c == '\n')
            {
                sb.Append('\n');
                i++;
                continue;
            }

            if (c == '\r')
            {
                i = ReadCarriageReturnNewline(source, i, sb);
                continue;
            }

            i = AppendCharacter(source, i, sb);
        }

        return new TomlString(start, i, TomlStringForm.MultiLineLiteral, s.Substring(start, i - start), sb.ToString());
    }

    /// <summary>
    /// Reads a comment that starts at <paramref name="start"/> (its <c>#</c>). The comment runs to the end of the line, not
    /// including the line break. Control characters other than tab are errors.
    /// </summary>
    internal static TomlComment ReadComment(SourceText source, int start)
    {
        string s = source.Text;
        int i = start + 1;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\n')
            {
                break;
            }

            if (c == '\r')
            {
                if (i + 1 < s.Length && s[i + 1] == '\n')
                {
                    break;
                }

                throw Fail(source, i, LoneCarriageReturnMessage);
            }

            if (c < 0x20 ? c != '\t' : c == 0x7F)
            {
                throw Fail(source, i, $"control character U+{(int)c:X4} is not allowed in a comment");
            }

            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i += 2;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                throw Fail(source, i, LoneSurrogateMessage);
            }

            i++;
        }

        return new TomlComment(start, i, s.Substring(start + 1, i - start - 1));
    }

    /// <summary>
    /// Reads a backslash escape at <paramref name="i"/> (the backslash), appends its character, and returns the offset after it.
    /// The escapes are those of TOML 1.0: <c>\b \t \n \f \r \" \\ \uXXXX \UXXXXXXXX</c>.
    /// </summary>
    internal static int ReadEscape(SourceText source, int i, StringBuilder sb)
    {
        string s = source.Text;
        if (i + 1 >= s.Length)
        {
            throw Fail(source, i, "an escape sequence is not complete");
        }

        switch (s[i + 1])
        {
            case 'b':
                sb.Append('\b');
                return i + 2;
            case 't':
                sb.Append('\t');
                return i + 2;
            case 'n':
                sb.Append('\n');
                return i + 2;
            case 'f':
                sb.Append('\f');
                return i + 2;
            case 'r':
                sb.Append('\r');
                return i + 2;
            case '"':
                sb.Append('"');
                return i + 2;
            case '\\':
                sb.Append('\\');
                return i + 2;
            case 'u':
                return ReadUnicodeEscape(source, i, 4, sb);
            case 'U':
                return ReadUnicodeEscape(source, i, 8, sb);
            default:
                throw Fail(source, i, $"invalid escape sequence: {Describe(s, i + 1)} follows the backslash");
        }
    }

    /// <summary>
    /// Appends the character at <paramref name="i"/> to a string, after checking it: a control character other than tab, and a
    /// lone surrogate, are errors. A surrogate pair is appended whole. Returns the offset after the character.
    /// </summary>
    internal static int AppendCharacter(SourceText source, int i, StringBuilder sb)
    {
        string s = source.Text;
        char c = s[i];
        if (c < 0x20 ? c != '\t' : c == 0x7F)
        {
            throw Fail(source, i, $"control character U+{(int)c:X4} is not allowed in a string");
        }

        if (char.IsHighSurrogate(c))
        {
            if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                sb.Append(c).Append(s[i + 1]);
                return i + 2;
            }

            throw Fail(source, i, LoneSurrogateMessage);
        }

        if (char.IsLowSurrogate(c))
        {
            throw Fail(source, i, LoneSurrogateMessage);
        }

        sb.Append(c);
        return i + 1;
    }

    /// <summary>
    /// A line-ending backslash in a multi-line basic string, or an escape. When the backslash ends its line (only spaces and
    /// tabs may come between it and the line break), the whitespace and line breaks up to the next other character are removed.
    /// </summary>
    private static int ReadMultiLineBackslash(SourceText source, int i, StringBuilder sb)
    {
        string s = source.Text;
        int j = i + 1;
        while (j < s.Length && s[j] is ' ' or '\t')
        {
            j++;
        }

        bool endsLine = j < s.Length && (s[j] == '\n' || (s[j] == '\r' && j + 1 < s.Length && s[j + 1] == '\n'));
        if (!endsLine)
        {
            if (j > i + 1)
            {
                throw Fail(source, i, "invalid escape sequence: spaces after a backslash are allowed only at the end of a line");
            }

            return ReadEscape(source, i, sb);
        }

        int k = j;
        while (k < s.Length)
        {
            if (s[k] is ' ' or '\t' or '\n')
            {
                k++;
            }
            else if (s[k] == '\r' && k + 1 < s.Length && s[k + 1] == '\n')
            {
                k += 2;
            }
            else if (s[k] == '\r')
            {
                throw Fail(source, k, LoneCarriageReturnMessage);
            }
            else
            {
                break;
            }
        }

        return k;
    }

    private static int ReadUnicodeEscape(SourceText source, int i, int digits, StringBuilder sb)
    {
        string s = source.Text;
        long value = 0;
        for (int k = 0; k < digits; k++)
        {
            int at = i + 2 + k;
            int digit = at < s.Length ? HexValue(s[at]) : -1;
            if (digit < 0)
            {
                throw Fail(source, i, $"a \\{s[i + 1]} escape needs {digits} hexadecimal digits");
            }

            value = value * 16 + digit;
        }

        if (value > 0x10FFFF || (value >= 0xD800 && value <= 0xDFFF))
        {
            throw Fail(source, i, "a unicode escape must be a Unicode scalar value");
        }

        sb.Append(char.ConvertFromUtf32((int)value));
        return i + 2 + digits;
    }

    /// <summary>A carriage return that starts a CRLF line break. It is read as LF. A lone carriage return is an error.</summary>
    private static int ReadCarriageReturnNewline(SourceText source, int i, StringBuilder sb)
    {
        string s = source.Text;
        if (i + 1 < s.Length && s[i + 1] == '\n')
        {
            sb.Append('\n');
            return i + 2;
        }

        throw Fail(source, i, LoneCarriageReturnMessage);
    }

    /// <summary>The offset after a line break that immediately follows an opening delimiter, or the same offset when there is none.</summary>
    private static int SkipOpeningNewline(string s, int i)
    {
        if (i < s.Length && s[i] == '\n')
        {
            return i + 1;
        }

        return i + 1 < s.Length && s[i] == '\r' && s[i + 1] == '\n' ? i + 2 : i;
    }

    private static int RunLength(string s, int i, char c)
    {
        int n = 0;
        while (i + n < s.Length && s[i + n] == c)
        {
            n++;
        }

        return n;
    }

    private static int HexValue(char c)
    {
        if (c >= '0' && c <= '9')
        {
            return c - '0';
        }

        if (c >= 'a' && c <= 'f')
        {
            return c - 'a' + 10;
        }

        if (c >= 'A' && c <= 'F')
        {
            return c - 'A' + 10;
        }

        return -1;
    }
}
