using System.Text;
using System.Text.RegularExpressions;

namespace Cfgedit.Core.Yaml;

/// <summary>
/// Writes keys and scalar values as YAML text, and classifies plain text by the YAML 1.2 core schema. Every token written parses back
/// to the text given: a value written after <c>key: </c> (at the key's column) decodes to the same string, with LF and with CRLF as
/// the document's line break. A string with a lone surrogate is refused, because no YAML text can hold one.
/// </summary>
/// <remarks>
/// The styles are kept when they can hold the value. A plain scalar cannot hold a value with a leading or trailing blank, a
/// <c>: </c> or <c> #</c>, a flow indicator, or a leading indicator character. A single-quoted scalar cannot hold a line break. A
/// literal block cannot hold a carriage return or a character that is not printable. A double-quoted scalar holds every string.
/// A folded scalar is never kept: a folded value is written as a literal block, which is exact, where folding a value back would
/// need the same breaks.
/// </remarks>
public static class YamlEncoder
{
    private const string LoneSurrogateMessage = "a YAML string cannot contain a lone surrogate";

    /// <summary>The longest an implicit key may be, in characters, as the parser reads it.</summary>
    private const int MaxImplicitKeyLength = 1024;

    private static readonly Regex NullPattern = new(@"\A(?:~|null|Null|NULL)?\z", RegexOptions.CultureInvariant);
    private static readonly Regex BooleanPattern = new(@"\A(?:true|True|TRUE|false|False|FALSE)\z", RegexOptions.CultureInvariant);
    private static readonly Regex IntegerPattern = new(@"\A(?:[-+]?[0-9]+|0o[0-7]+|0x[0-9a-fA-F]+)\z", RegexOptions.CultureInvariant);

    private static readonly Regex FloatPattern = new(
        @"\A(?:[-+]?(?:\.[0-9]+|[0-9]+(?:\.[0-9]*)?)(?:[eE][-+]?[0-9]+)?|[-+]?\.(?:inf|Inf|INF)|\.(?:nan|NaN|NAN))\z",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The core-schema kind of a plain scalar's text: null (including the empty text), boolean, integer, float, or else a string.
    /// </summary>
    /// <param name="plainText">The text of a plain scalar, as written, or the value of an untagged plain scalar.</param>
    public static YamlCoreKind CoreSchemaKind(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        if (NullPattern.IsMatch(plainText))
        {
            return YamlCoreKind.Null;
        }

        if (BooleanPattern.IsMatch(plainText))
        {
            return YamlCoreKind.Boolean;
        }

        if (IntegerPattern.IsMatch(plainText))
        {
            return YamlCoreKind.Integer;
        }

        return FloatPattern.IsMatch(plainText) ? YamlCoreKind.Float : YamlCoreKind.String;
    }

    /// <summary>
    /// Writes a key. A name that is a plain scalar, and that the core schema reads as a string, is written plain. Any other name is
    /// written double-quoted, so that it stays a string and stays on one line.
    /// </summary>
    /// <param name="name">The key's text.</param>
    /// <returns>The key, without the colon.</returns>
    /// <exception cref="CfgeditException">The name contains a lone surrogate.</exception>
    public static string EncodeKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        CheckSurrogates(name);
        string key;
        if (CanBePlain(name) && CoreSchemaKind(name) == YamlCoreKind.String)
        {
            key = name;
        }
        else
        {
            var sb = new StringBuilder(name.Length + 2);
            AppendDoubleQuoted(sb, name);
            key = sb.ToString();
        }

        if (key.Length > MaxImplicitKeyLength)
        {
            throw new CfgeditException($"a key longer than {MaxImplicitKeyLength} characters cannot be written");
        }

        return key;
    }

    /// <summary>
    /// Writes a scalar value in the plainest style that can hold it, or in <paramref name="preferred"/> when that style can hold it.
    /// Plain is kept only when the value stays a string, if <paramref name="mustStayString"/> holds. The order of the fallbacks is
    /// plain, single-quoted, double-quoted, with a value that has a line break written as a literal block.
    /// </summary>
    /// <param name="value">The text to write.</param>
    /// <param name="preferred">The style to keep when it can hold the value, or null to choose the plainest style.</param>
    /// <param name="indent">
    /// The column of the key or dash that owns the value. A literal block's lines are written at this column plus two, so the block
    /// is indented more than its owner.
    /// </param>
    /// <param name="newline">The document's line break, LF or CRLF. Each line break of a literal block is written as this.</param>
    /// <param name="mustStayString">
    /// True when the value must read back as a string: a plain text that the core schema reads as null, boolean, integer or float is
    /// then quoted. False lets a plain text keep its plain style whatever it reads as.
    /// </param>
    /// <returns>
    /// The scalar's text. It has no line break after its last line: a literal block ends with its last line, and the caller writes
    /// the line break that ends the entry.
    /// </returns>
    /// <exception cref="CfgeditException">The value contains a lone surrogate.</exception>
    /// <exception cref="ArgumentException">The newline is not LF or CRLF, or the indent is negative.</exception>
    public static string EncodeScalar(string value, YamlScalarStyle? preferred, int indent, string newline, bool mustStayString)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(newline);
        ArgumentOutOfRangeException.ThrowIfNegative(indent);
        if (newline is not ("\n" or "\r\n"))
        {
            throw new ArgumentException("The newline must be LF or CRLF.", nameof(newline));
        }

        CheckSurrogates(value);
        bool canPlain = CanBePlain(value) && (!mustStayString || CoreSchemaKind(value) == YamlCoreKind.String);
        bool canSingle = CanBeSingle(value);
        bool canLiteral = CanBeLiteral(value);
        bool hasBreak = value.Contains('\n');

        YamlScalarStyle style;
        if (preferred == YamlScalarStyle.Plain && canPlain)
        {
            style = YamlScalarStyle.Plain;
        }
        else if (preferred == YamlScalarStyle.SingleQuoted && canSingle)
        {
            style = YamlScalarStyle.SingleQuoted;
        }
        else if (preferred == YamlScalarStyle.DoubleQuoted)
        {
            style = YamlScalarStyle.DoubleQuoted;
        }
        else if (preferred is YamlScalarStyle.Literal or YamlScalarStyle.Folded && canLiteral)
        {
            style = YamlScalarStyle.Literal;
        }
        else if (hasBreak)
        {
            style = canLiteral ? YamlScalarStyle.Literal : YamlScalarStyle.DoubleQuoted;
        }
        else if (canPlain)
        {
            style = YamlScalarStyle.Plain;
        }
        else
        {
            style = canSingle ? YamlScalarStyle.SingleQuoted : YamlScalarStyle.DoubleQuoted;
        }

        switch (style)
        {
            case YamlScalarStyle.Plain:
                return value;
            case YamlScalarStyle.SingleQuoted:
                return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
            case YamlScalarStyle.Literal:
                return EncodeLiteral(value, indent, newline);
            default:
                var sb = new StringBuilder(value.Length + 2);
                AppendDoubleQuoted(sb, value);
                return sb.ToString();
        }
    }

    /// <summary>
    /// A literal block: the header, then each line on its own line at <paramref name="indent"/> plus two. The chomping indicator is
    /// <c>-</c> when the value has no trailing line break, none when it has one, and <c>+</c> when it has more, with the empty lines
    /// written after the last line. An indentation indicator is written when the first non-empty line starts with a space.
    /// </summary>
    private static string EncodeLiteral(string value, int indent, string newline)
    {
        int trailing = 0;
        while (trailing < value.Length && value[value.Length - 1 - trailing] == '\n')
        {
            trailing++;
        }

        string body = value[..(value.Length - trailing)];
        string[] lines = body.Length == 0 ? [] : body.Split('\n');
        bool keep = trailing >= 2 || (body.Length == 0 && trailing == 1);
        int extraEmpty = body.Length == 0 ? trailing : trailing - 1;

        string firstContent = lines.FirstOrDefault(line => line.Length > 0) ?? string.Empty;
        bool needsIndicator = firstContent.StartsWith(' ');

        var sb = new StringBuilder();
        sb.Append('|');
        if (needsIndicator)
        {
            sb.Append('2');
        }

        if (keep)
        {
            sb.Append('+');
        }
        else if (trailing == 0)
        {
            sb.Append('-');
        }

        string pad = new(' ', indent + 2);
        foreach (string line in lines)
        {
            sb.Append(newline);
            if (line.Length > 0)
            {
                sb.Append(pad).Append(line);
            }
        }

        if (keep)
        {
            for (int k = 0; k < extraEmpty; k++)
            {
                sb.Append(newline);
            }
        }

        return sb.ToString();
    }

    /// <summary>True when the text can be a plain scalar that reads back as itself, wherever it is written on one line.</summary>
    private static bool CanBePlain(string value)
    {
        if (value.Length == 0 || IsBlankChar(value[0]) || IsBlankChar(value[^1]))
        {
            return false;
        }

        if (value.StartsWith("---", StringComparison.Ordinal) || value.StartsWith("...", StringComparison.Ordinal))
        {
            return false;
        }

        if ("-?:,[]{}#&*!|>'\"%@`".Contains(value[0]))
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (!IsWritable(c) || c is ',' or '[' or ']' or '{' or '}')
            {
                return false;
            }

            if (c == ':' && (i + 1 == value.Length || IsBlankChar(value[i + 1])))
            {
                return false;
            }

            if (c == '#' && IsBlankChar(value[i - 1]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when every character can sit in a single-quoted scalar on one line.</summary>
    private static bool CanBeSingle(string value)
    {
        foreach (char c in value)
        {
            if (!IsWritable(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when every character can sit in a literal block, and the value is not empty.</summary>
    private static bool CanBeLiteral(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (c != '\n' && !IsWritable(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True for a character that can be written as itself in a plain, single-quoted or literal scalar: printable, not a line break,
    /// and not a byte order mark (which is escaped instead). A surrogate is writable only as part of a pair, which the callers check.
    /// </summary>
    private static bool IsWritable(char c)
    {
        return c == '\t'
            || (c >= 0x20 && c <= 0x7E)
            || (c >= 0xA0 && c <= 0xD7FF)
            || (c >= 0xE000 && c <= 0xFFFD && c != '\uFEFF')
            || char.IsSurrogate(c);
    }

    private static bool IsBlankChar(char c) => c is ' ' or '\t';

    /// <summary>Writes a double-quoted scalar, escaping what cannot be written as itself. Surrogate pairs are written as they are.</summary>
    private static void AppendDoubleQuoted(StringBuilder sb, string value)
    {
        sb.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                sb.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }

            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\0': sb.Append("\\0"); break;
                case '\a': sb.Append("\\a"); break;
                case '\b': sb.Append("\\b"); break;
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\v': sb.Append("\\v"); break;
                case '\f': sb.Append("\\f"); break;
                case '\r': sb.Append("\\r"); break;
                case '\u001B': sb.Append("\\e"); break;
                default:
                    if (IsWritable(c))
                    {
                        sb.Append(c);
                    }
                    else if (c < 0x100)
                    {
                        sb.Append("\\x").Append(((int)c).ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                    }

                    break;
            }
        }

        sb.Append('"');
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
}
