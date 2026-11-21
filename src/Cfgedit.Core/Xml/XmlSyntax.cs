using System.Text;
using System.Xml;

namespace Cfgedit.Core.Xml;

/// <summary>
/// The XML rules for values: which characters XML 1.0 allows, how values are escaped for attributes, text, CDATA and
/// comments, and the name productions. The encoders reject characters XML cannot hold rather than dropping them.
/// </summary>
public static class XmlSyntax
{
    /// <summary>
    /// The index of the first character that XML 1.0 does not allow, or -1. Those are C0 controls other than tab, LF and
    /// CR, U+FFFE, U+FFFF, and surrogates that are not part of a valid pair. Valid astral characters are allowed.
    /// </summary>
    public static int FindInvalidChar(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c < 0x20)
            {
                if (c is not ('\t' or '\n' or '\r'))
                {
                    return i;
                }

                continue;
            }

            if (c is (char)0xFFFE or (char)0xFFFF)
            {
                return i;
            }

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                return i;
            }

            if (char.IsLowSurrogate(c))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The content of an attribute value between the quotes. The quote in use, <c>&amp;</c> and <c>&lt;</c> are escaped, as are
    /// tab, LF and CR, so that attribute-value normalisation cannot change the value.
    /// </summary>
    /// <param name="value">The value to encode. It must hold no character XML cannot represent.</param>
    /// <param name="quote">The quote in use: <c>"</c> or <c>'</c>.</param>
    /// <exception cref="ArgumentException">The quote is not valid, or the value holds a character XML cannot represent.</exception>
    public static string EncodeAttributeValue(string value, char quote)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (quote is not ('"' or '\''))
        {
            throw new ArgumentException("The quote must be a double or single quote.", nameof(quote));
        }

        CheckChars(value);
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '"' when quote == '"':
                    sb.Append("&quot;");
                    break;
                case '\'' when quote == '\'':
                    sb.Append("&apos;");
                    break;
                case '\t':
                    sb.Append("&#9;");
                    break;
                case '\n':
                    sb.Append("&#10;");
                    break;
                case '\r':
                    sb.Append("&#13;");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The content of an element's text. <c>&amp;</c> and <c>&lt;</c> are escaped, and <c>&gt;</c> only directly after <c>]]</c>.
    /// LF is written as <paramref name="newline"/>, the document's line break, and CR as <c>&amp;#13;</c>, which
    /// would otherwise be read as a line break.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="newline">The document's line break: <c>\n</c>, <c>\r\n</c> or <c>\r</c>.</param>
    /// <exception cref="ArgumentException">The newline is not a line break, or the value holds a character XML cannot represent.</exception>
    public static string EncodeText(string value, string newline)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckNewline(newline);
        CheckChars(value);
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>' when i >= 2 && value[i - 1] == ']' && value[i - 2] == ']':
                    sb.Append("&gt;");
                    break;
                case '\n':
                    sb.Append(newline);
                    break;
                case '\r':
                    sb.Append("&#13;");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The content of one or more CDATA sections holding the value. Each <c>]]&gt;</c> is split as
    /// <c>]]]]&gt;&lt;![CDATA[&gt;</c>, and LF is written as <paramref name="newline"/>. CDATA cannot hold a CR, so the
    /// result is null when the value contains one; the editor reports that.
    /// </summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="newline">The document's line break: <c>\n</c>, <c>\r\n</c> or <c>\r</c>.</param>
    /// <exception cref="ArgumentException">The newline is not a line break, or the value holds a character XML cannot represent.</exception>
    public static string? EncodeCData(string value, string newline)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckNewline(newline);
        CheckChars(value);
        if (value.Contains('\r'))
        {
            return null;
        }

        return value
            .Replace("\n", newline, StringComparison.Ordinal)
            .Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal);
    }

    /// <summary>
    /// The value unchanged, as the content of a comment, or null when it contains <c>--</c> or ends with <c>-</c>, which a
    /// comment cannot hold.
    /// </summary>
    /// <exception cref="ArgumentException">The value holds a character XML cannot represent.</exception>
    public static string? EncodeComment(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckChars(value);
        if (value.Contains("--", StringComparison.Ordinal) || value.EndsWith('-'))
        {
            return null;
        }

        return value;
    }

    /// <summary>True when the name is an XML Name: the production that allows colons, as <see cref="XmlConvert.VerifyName"/> checks it.</summary>
    public static bool IsValidName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        try
        {
            XmlConvert.VerifyName(name);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>True when the name is an NCName, or an NCName, a colon and an NCName.</summary>
    public static bool IsValidQName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        int colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return IsNCName(name);
        }

        if (name.IndexOf(':', colon + 1) >= 0)
        {
            return false;
        }

        return IsNCName(name[..colon]) && IsNCName(name[(colon + 1)..]);
    }

    private static bool IsNCName(string name)
    {
        try
        {
            XmlConvert.VerifyNCName(name);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void CheckChars(string value)
    {
        int index = FindInvalidChar(value);
        if (index >= 0)
        {
            throw new ArgumentException(
                $"The character U+{(int)value[index]:X4} at index {index} cannot be represented in XML 1.0.", nameof(value));
        }
    }

    private static void CheckNewline(string newline)
    {
        ArgumentNullException.ThrowIfNull(newline);
        if (newline is not ("\n" or "\r\n" or "\r"))
        {
            throw new ArgumentException("The newline must be LF, CRLF or CR.", nameof(newline));
        }
    }
}
