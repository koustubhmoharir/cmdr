using System.Text;

namespace Cfgedit.Core.Env;

/// <summary>
/// Writes a value as the token that follows <c>=</c> in a .env line. Every token it writes parses back to the same value:
/// <c>KEY=</c> followed by the token and a line break decodes to the value given. Values that have no safe form are refused
/// rather than changed.
/// </summary>
public static class EnvEncoder
{
    /// <summary>
    /// Writes a value in the plainest form that every common loader reads the same way: unquoted when that is safe, else single
    /// quotes when the value has no <c>'</c> or CR, else double quotes with <c>\</c> and <c>"</c> escaped.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="newline">The document's line break, written for each line feed inside quotes: LF, CRLF or CR.</param>
    /// <returns>The value token, with its quotes if it has any.</returns>
    /// <exception cref="CfgeditException">The value contains a control character that a .env value cannot hold.</exception>
    /// <exception cref="ArgumentException">The newline is not LF, CRLF or CR.</exception>
    public static string Encode(string value, string newline)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckNewline(newline);
        CheckControls(value);

        if (CanBeUnquoted(value))
        {
            return value;
        }

        if (CanBeSingleQuoted(value))
        {
            return WriteSingle(value, newline);
        }

        return WriteDouble(value, newline);
    }

    /// <summary>
    /// Writes a value in the given quote style when that style can hold it, and otherwise in the plainest form, as
    /// <see cref="Encode"/> does. A style can hold a value when: unquoted needs no leading or trailing space or tab and none of
    /// <c>#</c>, <c>'</c>, <c>"</c>, <c>\</c>, <c>$</c>, a backtick or a line break; single quotes need no <c>'</c> and no CR;
    /// double quotes hold any value.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="preferred">The style the value had before it was edited.</param>
    /// <param name="newline">The document's line break.</param>
    /// <returns>The value token, with its quotes if it has any.</returns>
    /// <exception cref="CfgeditException">The value contains a control character that a .env value cannot hold.</exception>
    /// <exception cref="ArgumentException">The newline is not LF, CRLF or CR.</exception>
    public static string EncodeKeeping(string value, EnvQuote preferred, string newline)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckNewline(newline);
        CheckControls(value);

        switch (preferred)
        {
            case EnvQuote.None when CanBeUnquoted(value):
                return value;
            case EnvQuote.Single when CanBeSingleQuoted(value):
                return WriteSingle(value, newline);
            case EnvQuote.Double:
                return WriteDouble(value, newline);
            default:
                return Encode(value, newline);
        }
    }

    /// <summary>
    /// True when the value is written in double quotes and contains <c>$</c> or <c>\</c>. Loaders may expand <c>${VAR}</c>
    /// there, and they disagree on backslash escapes in double quotes (Node's dotenv decodes only <c>\n</c>), so the editor
    /// shows a warning. The value is written as it is, never changed.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="written">The quote style the value is written in.</param>
    public static bool NeedsLoaderWarning(string value, EnvQuote written)
    {
        ArgumentNullException.ThrowIfNull(value);
        return written == EnvQuote.Double && (value.Contains('$') || value.Contains('\\'));
    }

    /// <summary>The quote style of a value token, from its first character: a token that starts with <c>'</c> or <c>"</c> is quoted.</summary>
    /// <param name="token">A token as <see cref="Encode"/> or <see cref="EncodeKeeping"/> wrote it.</param>
    public static EnvQuote StyleOf(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return token.Length > 0 && token[0] == '\'' ? EnvQuote.Single
            : token.Length > 0 && token[0] == '"' ? EnvQuote.Double
            : EnvQuote.None;
    }

    private static bool CanBeUnquoted(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        if (IsBlank(value[0]) || IsBlank(value[^1]))
        {
            return false;
        }

        foreach (char c in value)
        {
            if (c is '#' or '\'' or '"' or '\\' or '$' or '`' or '\r' or '\n')
            {
                return false;
            }
        }

        return true;
    }

    // python-dotenv decodes \\ and \' inside single quotes, while Node's dotenv keeps them, so a value with a backslash pair, or
    // one ending in a backslash (which would escape the closing quote), is not single-quoted.
    private static bool CanBeSingleQuoted(string value)
    {
        return !value.Contains('\'') && !value.Contains('\r') && !value.Contains("\\\\", StringComparison.Ordinal) && !value.EndsWith('\\');
    }

    private static string WriteSingle(string value, string newline)
    {
        return "'" + value.Replace("\n", newline, StringComparison.Ordinal) + "'";
    }

    private static string WriteDouble(string value, string newline)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\n':
                    sb.Append(newline);
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// Refuses the control characters a .env value cannot hold. TAB, LF and CR are allowed: LF and CR are written as line breaks
    /// or as <c>\r</c>.
    /// </summary>
    private static void CheckControls(string value)
    {
        foreach (char c in value)
        {
            bool isControl = c <= '\u001F' || c == '\u007F';
            if (isControl && c is not ('\t' or '\n' or '\r'))
            {
                throw new CfgeditException($"a .env value cannot contain control character U+{(int)c:X4}");
            }
        }
    }

    private static void CheckNewline(string newline)
    {
        ArgumentNullException.ThrowIfNull(newline);
        if (newline is not ("\n" or "\r\n" or "\r"))
        {
            throw new ArgumentException("The line break must be LF, CRLF or CR.", nameof(newline));
        }
    }

    private static bool IsBlank(char c)
    {
        return c is ' ' or '\t';
    }
}
