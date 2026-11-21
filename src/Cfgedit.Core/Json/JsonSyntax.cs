using System.Buffers;
using System.Globalization;
using System.Text;

namespace Cfgedit.Core.Json;

/// <summary>
/// The JSON-family rules for literals: string encoding and decoding, identifier names, numbers and keys. The lexer
/// validates strings and identifiers with the same code that <see cref="DecodeString"/> uses.
/// </summary>
public static class JsonSyntax
{
    /// <summary>U+2028, written as a constant so that no source file holds the raw character.</summary>
    internal const char LineSeparator = (char)0x2028;

    /// <summary>U+2029, written as a constant so that no source file holds the raw character.</summary>
    internal const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// Returns the full string literal for a value, quotes included. The quote character in use and the backslash are
    /// escaped, as are the short escapes <c>\b \f \n \r \t</c>, other characters below U+0020, U+007F, U+2028, U+2029 and lone
    /// surrogates (as lowercase <c>\uXXXX</c>). Everything else is written literally.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="quote">The quote to use: <c>"</c>, or <c>'</c> for JSON5 only.</param>
    /// <param name="dialect">The dialect the literal is for.</param>
    /// <exception cref="ArgumentException">The quote is not valid for the dialect.</exception>
    public static string EncodeString(string value, char quote, JsonDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckQuote(quote, dialect);

        var sb = new StringBuilder(value.Length + 2);
        sb.Append(quote);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == quote || c == '\\')
            {
                sb.Append('\\').Append(c);
                continue;
            }

            switch (c)
            {
                case '\b':
                    sb.Append("\\b");
                    continue;
                case '\f':
                    sb.Append("\\f");
                    continue;
                case '\n':
                    sb.Append("\\n");
                    continue;
                case '\r':
                    sb.Append("\\r");
                    continue;
                case '\t':
                    sb.Append("\\t");
                    continue;
            }

            if (c < 0x20 || c == 0x7F || c == LineSeparator || c == ParagraphSeparator)
            {
                AppendUnicodeEscape(sb, c);
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                sb.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                AppendUnicodeEscape(sb, c);
                continue;
            }

            sb.Append(c);
        }

        sb.Append(quote);
        return sb.ToString();
    }

    /// <summary>
    /// True when the name is an ES5 IdentifierName, without escapes: a start character (a Unicode letter, <c>$</c> or
    /// <c>_</c>), then parts (start characters, Mn, Mc, Nd, Pc, U+200C or U+200D). False for an empty string.
    /// </summary>
    public static bool IsIdentifierName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0)
        {
            return false;
        }

        int i = 0;
        bool first = true;
        while (i < name.Length)
        {
            if (!TryDecodeRune(name, i, out Rune rune, out int width))
            {
                return false;
            }

            if (!(first ? IsIdentifierStart(rune) : IsIdentifierPart(rune)))
            {
                return false;
            }

            first = false;
            i += width;
        }

        return true;
    }

    /// <summary>
    /// Returns the key as it should be written. In JSON5 a key with no original quote is written bare when it is an
    /// identifier name. Otherwise the key is a string literal in the original quote, or in the preferred quote when there
    /// was none.
    /// </summary>
    /// <param name="name">The decoded key.</param>
    /// <param name="originalQuote">The quote the key had, or null for a bare identifier key.</param>
    /// <param name="preferredQuote">The quote to use when there is no original quote.</param>
    /// <param name="dialect">The dialect the key is for.</param>
    public static string EncodeKey(string name, char? originalQuote, char preferredQuote, JsonDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (dialect == JsonDialect.Json5 && originalQuote is null && IsIdentifierName(name))
        {
            return name;
        }

        return EncodeString(name, originalQuote ?? preferredQuote, dialect);
    }

    /// <summary>
    /// True when the text is a number in the dialect's grammar. Json and Jsonc use RFC 8259's number grammar. Json5 adds
    /// hexadecimal integers, a leading or trailing decimal point, an optional sign, and the signed or unsigned
    /// <c>Infinity</c> and <c>NaN</c>.
    /// </summary>
    public static bool IsValidNumber(string text, JsonDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(text);
        return dialect == JsonDialect.Json5 ? IsValidJson5Number(text) : IsValidJsonNumber(text);
    }

    /// <summary>
    /// Returns the decoded value of a string literal, quotes included. It is the same code the lexer uses.
    /// </summary>
    /// <exception cref="CfgeditException">The literal is not a single valid string literal for the dialect.</exception>
    public static string DecodeString(string literal, JsonDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(literal);
        if (literal.Length == 0 || !IsQuote(literal[0], dialect))
        {
            throw new CfgeditException("not a string literal");
        }

        var sb = new StringBuilder(literal.Length);
        if (!TryScanString(literal, 0, dialect, sb, out int end, out _, out string? error))
        {
            throw new CfgeditException(error!);
        }

        if (end != literal.Length)
        {
            throw new CfgeditException("unexpected text after the string literal");
        }

        return sb.ToString();
    }

    // ----- Internal helpers shared with the lexer -----

    /// <summary>True for the quote characters the dialect allows.</summary>
    internal static bool IsQuote(char c, JsonDialect dialect)
    {
        return c == '"' || (c == '\'' && dialect == JsonDialect.Json5);
    }

    /// <summary>
    /// Scans the string literal that starts at <paramref name="start"/> (its opening quote). It decodes into
    /// <paramref name="value"/> when that is not null. On success <paramref name="end"/> is the offset after the closing
    /// quote. On failure <paramref name="errorIndex"/> is the offset of the problem and <paramref name="error"/> says what
    /// it is.
    /// </summary>
    internal static bool TryScanString(
        string text, int start, JsonDialect dialect, StringBuilder? value,
        out int end, out int errorIndex, out string? error)
    {
        char quote = text[start];
        bool json5 = dialect == JsonDialect.Json5;
        int i = start + 1;
        end = -1;
        errorIndex = -1;
        error = null;

        while (true)
        {
            if (i >= text.Length)
            {
                return Fail(start, "unterminated string", out errorIndex, out error);
            }

            char c = text[i];
            if (c == quote)
            {
                end = i + 1;
                return true;
            }

            if (c == '\\')
            {
                if (i + 1 >= text.Length)
                {
                    return Fail(start, "unterminated string", out errorIndex, out error);
                }

                char e = text[i + 1];
                switch (e)
                {
                    case '"':
                    case '\\':
                    case '/':
                        value?.Append(e);
                        i += 2;
                        continue;
                    case 'b':
                        value?.Append('\b');
                        i += 2;
                        continue;
                    case 'f':
                        value?.Append('\f');
                        i += 2;
                        continue;
                    case 'n':
                        value?.Append('\n');
                        i += 2;
                        continue;
                    case 'r':
                        value?.Append('\r');
                        i += 2;
                        continue;
                    case 't':
                        value?.Append('\t');
                        i += 2;
                        continue;
                    case 'u':
                        if (!TryHex(text, i + 2, 4, out int unit))
                        {
                            return Fail(i, "invalid \\u escape: expected four hex digits", out errorIndex, out error);
                        }

                        value?.Append((char)unit);
                        i += 6;
                        continue;
                    case '\'' when json5:
                        value?.Append('\'');
                        i += 2;
                        continue;
                    case 'v' when json5:
                        value?.Append('\v');
                        i += 2;
                        continue;
                    case 'x' when json5:
                        if (!TryHex(text, i + 2, 2, out int hex))
                        {
                            return Fail(i, "invalid \\x escape: expected two hex digits", out errorIndex, out error);
                        }

                        value?.Append((char)hex);
                        i += 4;
                        continue;
                    case '0' when json5:
                        if (i + 2 < text.Length && IsDigit(text[i + 2]))
                        {
                            return Fail(i, "\\0 must not be followed by a digit", out errorIndex, out error);
                        }

                        value?.Append('\0');
                        i += 2;
                        continue;
                    case '\r' when json5:
                        // A line continuation: CRLF is one line break, and it decodes to nothing.
                        i += (i + 2 < text.Length && text[i + 2] == '\n') ? 3 : 2;
                        continue;
                    case '\n' when json5:
                    case LineSeparator when json5:
                    case ParagraphSeparator when json5:
                        i += 2;
                        continue;
                }

                if (!json5)
                {
                    return Fail(i, $"invalid escape sequence \\{Bare(e)}", out errorIndex, out error);
                }

                if (IsDigit(e))
                {
                    return Fail(i, "octal escape sequences are not allowed", out errorIndex, out error);
                }

                // Any other character escapes itself. A surrogate pair stays whole.
                if (char.IsHighSurrogate(e) && i + 2 < text.Length && char.IsLowSurrogate(text[i + 2]))
                {
                    value?.Append(e).Append(text[i + 2]);
                    i += 3;
                    continue;
                }

                value?.Append(e);
                i += 2;
                continue;
            }

            if (c == '\n' || c == '\r')
            {
                return Fail(i, "a line break must be escaped in a string", out errorIndex, out error);
            }

            // RFC 8259 allows every character from U+0020 unescaped, U+2028 and U+2029 included.
            if (!json5 && c < 0x20)
            {
                return Fail(i, $"control character U+{(int)c:X4} must be escaped in a string", out errorIndex, out error);
            }

            value?.Append(c);
            i++;
        }
    }

    /// <summary>
    /// Scans an identifier key from <paramref name="start"/>. A part is any identifier part, or a <c>\uXXXX</c> escape
    /// whose character is one. The decoded name goes into <paramref name="name"/> when it is not null. The scan stops at the
    /// first character that cannot continue the identifier, unless it is a backslash, which must be a valid escape.
    /// </summary>
    internal static bool TryScanIdentifier(
        string text, int start, StringBuilder? name,
        out int end, out int errorIndex, out string? error)
    {
        int i = start;
        bool first = true;
        end = -1;
        errorIndex = -1;
        error = null;

        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                if (i + 1 >= text.Length || text[i + 1] != 'u')
                {
                    return Fail(i, "invalid escape in identifier: only \\uXXXX is allowed", out errorIndex, out error);
                }

                if (!TryHex(text, i + 2, 4, out int unit))
                {
                    return Fail(i, "invalid \\u escape: expected four hex digits", out errorIndex, out error);
                }

                bool valid = Rune.TryCreate((char)unit, out Rune escaped)
                    && (first ? IsIdentifierStart(escaped) : IsIdentifierPart(escaped));
                if (!valid)
                {
                    return Fail(i, "escape does not produce a valid identifier character", out errorIndex, out error);
                }

                name?.Append((char)unit);
                i += 6;
                first = false;
                continue;
            }

            if (!TryDecodeRune(text, i, out Rune rune, out int width))
            {
                break;
            }

            if (!(first ? IsIdentifierStart(rune) : IsIdentifierPart(rune)))
            {
                break;
            }

            name?.Append(text, i, width);
            i += width;
            first = false;
        }

        if (first)
        {
            return Fail(start, "expected an identifier", out errorIndex, out error);
        }

        end = i;
        return true;
    }

    /// <summary>ES5 IdentifierStart: a Unicode letter, <c>$</c> or <c>_</c>.</summary>
    internal static bool IsIdentifierStart(Rune rune)
    {
        if (rune.Value == '$' || rune.Value == '_')
        {
            return true;
        }

        if (rune.Value < 0x80)
        {
            return IsAsciiLetter(rune.Value);
        }

        return IsLetter(Rune.GetUnicodeCategory(rune));
    }

    /// <summary>ES5 IdentifierPart: an identifier start, Mn, Mc, Nd, Pc, U+200C or U+200D.</summary>
    internal static bool IsIdentifierPart(Rune rune)
    {
        if (IsIdentifierStart(rune) || rune.Value == 0x200C || rune.Value == 0x200D)
        {
            return true;
        }

        if (rune.Value < 0x80)
        {
            return rune.Value >= '0' && rune.Value <= '9';
        }

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.NonSpacingMark => true,
            UnicodeCategory.SpacingCombiningMark => true,
            UnicodeCategory.DecimalDigitNumber => true,
            UnicodeCategory.ConnectorPunctuation => true,
            _ => false,
        };
    }

    /// <summary>Decodes the rune at <paramref name="index"/>. False for a lone surrogate.</summary>
    internal static bool TryDecodeRune(string text, int index, out Rune rune, out int width)
    {
        OperationStatus status = Rune.DecodeFromUtf16(text.AsSpan(index), out rune, out width);
        return status == OperationStatus.Done;
    }

    internal static bool IsDigit(char c)
    {
        return c >= '0' && c <= '9';
    }

    /// <summary>Reads exactly <paramref name="count"/> hex digits at <paramref name="start"/>.</summary>
    internal static bool TryHex(string text, int start, int count, out int value)
    {
        value = 0;
        if (start + count > text.Length)
        {
            return false;
        }

        for (int i = start; i < start + count; i++)
        {
            int digit = HexValue(text[i]);
            if (digit < 0)
            {
                return false;
            }

            value = value * 16 + digit;
        }

        return true;
    }

    /// <summary>A character as it reads in a message: itself when it is visible, otherwise its code.</summary>
    internal static string Bare(char c)
    {
        if (char.IsSurrogate(c) || char.IsControl(c) || char.IsWhiteSpace(c))
        {
            return $"U+{(int)c:X4}";
        }

        return c.ToString();
    }

    /// <summary>A rune as it reads in a message, quoted when it is visible, otherwise its code.</summary>
    internal static string Quoted(Rune rune)
    {
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        bool invisible = category is UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator
            or UnicodeCategory.SpaceSeparator;
        if (invisible)
        {
            return $"U+{rune.Value:X4}";
        }

        return $"'{rune}'";
    }

    private static bool IsValidJsonNumber(string text)
    {
        int i = 0;
        if (i < text.Length && text[i] == '-')
        {
            i++;
        }

        if (i >= text.Length)
        {
            return false;
        }

        if (text[i] == '0')
        {
            i++;
        }
        else if (text[i] >= '1' && text[i] <= '9')
        {
            i = SkipDigits(text, i);
        }
        else
        {
            return false;
        }

        if (i < text.Length && text[i] == '.')
        {
            i++;
            int afterPoint = SkipDigits(text, i);
            if (afterPoint == i)
            {
                return false;
            }

            i = afterPoint;
        }

        if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
        {
            i++;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
            {
                i++;
            }

            int afterExponent = SkipDigits(text, i);
            if (afterExponent == i)
            {
                return false;
            }

            i = afterExponent;
        }

        return i == text.Length;
    }

    private static bool IsValidJson5Number(string text)
    {
        int i = 0;
        if (i < text.Length && (text[i] == '+' || text[i] == '-'))
        {
            i++;
        }

        string unsigned = text[i..];
        if (unsigned is "Infinity" or "NaN")
        {
            return true;
        }

        if (unsigned.Length >= 2 && unsigned[0] == '0' && (unsigned[1] == 'x' || unsigned[1] == 'X'))
        {
            if (unsigned.Length == 2)
            {
                return false;
            }

            for (int h = 2; h < unsigned.Length; h++)
            {
                if (HexValue(unsigned[h]) < 0)
                {
                    return false;
                }
            }

            return true;
        }

        int intStart = i;
        int afterInt = SkipDigits(text, i);
        int intDigits = afterInt - intStart;
        if (intDigits > 1 && text[intStart] == '0')
        {
            return false;
        }

        i = afterInt;
        if (i < text.Length && text[i] == '.')
        {
            i++;
            int afterFraction = SkipDigits(text, i);
            if (intDigits == 0 && afterFraction == i)
            {
                return false;
            }

            i = afterFraction;
        }
        else if (intDigits == 0)
        {
            return false;
        }

        if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
        {
            i++;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
            {
                i++;
            }

            int afterExponent = SkipDigits(text, i);
            if (afterExponent == i)
            {
                return false;
            }

            i = afterExponent;
        }

        return i == text.Length;
    }

    private static int SkipDigits(string text, int i)
    {
        while (i < text.Length && IsDigit(text[i]))
        {
            i++;
        }

        return i;
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

    private static bool IsLetter(UnicodeCategory category)
    {
        return category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.LetterNumber;
    }

    private static bool IsAsciiLetter(int value)
    {
        return (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z');
    }

    private static void CheckQuote(char quote, JsonDialect dialect)
    {
        if (quote == '"')
        {
            return;
        }

        if (quote == '\'' && dialect == JsonDialect.Json5)
        {
            return;
        }

        throw new ArgumentException(
            quote == '\''
                ? "A single quote is only valid in JSON5."
                : "The quote must be a double quote, or a single quote in JSON5.",
            nameof(quote));
    }

    private static void AppendUnicodeEscape(StringBuilder sb, char c)
    {
        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
    }

    private static bool Fail(int at, string message, out int errorIndex, out string? error)
    {
        errorIndex = at;
        error = message;
        return false;
    }
}
