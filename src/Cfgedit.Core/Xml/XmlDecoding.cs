using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Xml;

/// <summary>
/// Decodes the bytes of an XML document into a <see cref="SourceText"/>, following XML 1.0 Appendix F. The byte order mark
/// and the UTF-16 patterns choose the encoding first; the declared encoding applies only when neither does. Decoding is
/// strict, and the result must encode back to the same bytes, so that an edit can never change bytes the user did not
/// touch.
/// </summary>
public static class XmlDecoding
{
    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16LeStrict = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16BeStrict = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

    // The pseudo-attribute "encoding" in an XML declaration, single or double quoted, after whitespace.
    private static readonly Regex EncodingPseudoAttribute = new(
        @"\sencoding\s*=\s*(?:""([^""]*)""|'([^']*)')", RegexOptions.CultureInvariant);

    static XmlDecoding()
    {
        // Code pages such as windows-1252 and Shift_JIS are not built into .NET. Registering the provider here covers
        // every XML encoding lookup, including the one that XmlTextReader makes in the cross-check.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Decodes an XML document's bytes. The byte order mark is removed and recorded in <see cref="SourceText.HasBom"/>.
    /// </summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="path">The file's path, carried by any error. Null when unknown.</param>
    /// <exception cref="CfgeditException">
    /// The encoding is unsupported or contradicts the byte order mark, the bytes are not valid in the encoding, or the
    /// bytes do not round-trip through the encoding.
    /// </exception>
    public static SourceText Decode(ReadOnlySpan<byte> bytes, string? path)
    {
        Start start = DetectStart(bytes, path, out int bomLength);
        ReadOnlySpan<byte> body = bytes[bomLength..];

        bool isUtf16 = start is Start.Utf16LeBom or Start.Utf16BeBom or Start.Utf16LePattern or Start.Utf16BePattern;
        bool bigEndian = start is Start.Utf16BeBom or Start.Utf16BePattern;
        string? declaration = isUtf16
            ? ReadDeclaration(body, unitSize: 2, bigEndian)
            : ReadDeclaration(body, unitSize: 1, bigEndian: false);
        string? declaredName = declaration is null ? null : DeclaredEncodingName(declaration);
        Encoding? declared = declaredName is null ? null : ResolveDeclared(declaredName, path);

        Encoding encoding;
        bool hasBom;
        switch (start)
        {
            case Start.Utf8Bom:
                if (declared is not null && declared.CodePage != 65001)
                {
                    throw Contradiction($"the UTF-8 byte order mark contradicts the declared encoding '{declaredName}'", path);
                }

                encoding = Utf8Strict;
                hasBom = true;
                break;

            case Start.Utf16LeBom:
            case Start.Utf16BeBom:
            case Start.Utf16LePattern:
            case Start.Utf16BePattern:
                if (declared is not null && !IsUtf16(declared))
                {
                    string endian = bigEndian ? "BE" : "LE";
                    string found = start is Start.Utf16LeBom or Start.Utf16BeBom
                        ? $"UTF-16 {endian} byte order mark"
                        : $"UTF-16 {endian} pattern (no byte order mark)";
                    throw Contradiction($"the {found} contradicts the declared encoding '{declaredName}'", path);
                }

                encoding = bigEndian ? Utf16BeStrict : Utf16LeStrict;
                hasBom = start is Start.Utf16LeBom or Start.Utf16BeBom;
                break;

            default:
                if (declared is not null && (IsUtf16(declared) || IsUtf32(declared)))
                {
                    throw Contradiction(
                        $"the declared encoding '{declaredName}' has no matching byte order mark or pattern", path);
                }

                encoding = declared ?? Utf8Strict;
                hasBom = false;
                break;
        }

        string text = isUtf16
            ? DecodeUtf16(body, bigEndian, bomLength, path)
            : DecodeBytes(encoding, body, bomLength, path);

        return Verified(text, encoding, hasBom, bytes, path);
    }

    /// <summary>
    /// The encoding cross-check of the parsing pipeline. A stream <see cref="XmlTextReader"/> reads the first node of the
    /// bytes, and the encoding it reports must match <paramref name="decoded"/>'s encoding.
    /// </summary>
    /// <remarks>
    /// Decision: when the first read throws an <see cref="XmlException"/>, the check passes. The reader then has no
    /// encoding to report, and the full parse reports the error with its position. Empty input has no first node and
    /// passes for the same reason.
    /// </remarks>
    /// <exception cref="CfgeditException">The reader reports a different encoding from the one cfgedit decoded.</exception>
    internal static void CheckEncodingAgreesWithXmlReader(ReadOnlySpan<byte> bytes, SourceText decoded)
    {
        ArgumentNullException.ThrowIfNull(decoded);

        string? readerEncoding;
        try
        {
            readerEncoding = FirstNodeEncoding(bytes);
        }
        catch (XmlException)
        {
            return;
        }

        if (readerEncoding is null)
        {
            return;
        }

        if (!string.Equals(readerEncoding, decoded.Encoding.WebName, StringComparison.OrdinalIgnoreCase))
        {
            throw new CfgeditException(
                $"the XML reader reads the encoding as '{readerEncoding}' but cfgedit decoded it as '{decoded.Encoding.WebName}'");
        }
    }

    /// <summary>The number of leading bytes that the encoding check reads first. See <see cref="FirstNodeEncoding"/>.</summary>
    private const int FirstNodePrefixLength = 4096;

    private enum Start
    {
        Utf8Bom,
        Utf16LeBom,
        Utf16BeBom,
        Utf16LePattern,
        Utf16BePattern,
        AsciiCompatible,
    }

    private static Start DetectStart(ReadOnlySpan<byte> bytes, string? path, out int bomLength)
    {
        bomLength = 0;
        if (bytes.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
        {
            bomLength = 3;
            return Start.Utf8Bom;
        }

        if (bytes.StartsWith<byte>([0xFF, 0xFE, 0x00, 0x00]))
        {
            throw Unsupported("UTF-32 LE is not supported", path);
        }

        if (bytes.StartsWith<byte>([0xFF, 0xFE]))
        {
            bomLength = 2;
            return Start.Utf16LeBom;
        }

        if (bytes.StartsWith<byte>([0x00, 0x00, 0xFE, 0xFF]))
        {
            throw Unsupported("UTF-32 BE is not supported", path);
        }

        if (bytes.StartsWith<byte>([0xFE, 0xFF]))
        {
            bomLength = 2;
            return Start.Utf16BeBom;
        }

        if (bytes.StartsWith<byte>([0x3C, 0x00, 0x3F, 0x00]))
        {
            return Start.Utf16LePattern;
        }

        if (bytes.StartsWith<byte>([0x00, 0x3C, 0x00, 0x3F]))
        {
            return Start.Utf16BePattern;
        }

        return Start.AsciiCompatible;
    }

    /// <summary>
    /// The XML declaration as ASCII text, from the start of the body through its "?&gt;". Units above ASCII become U+FFFD,
    /// which is enough to read the declaration. Null when the body does not start with a declaration or it has no end.
    /// </summary>
    private static string? ReadDeclaration(ReadOnlySpan<byte> body, int unitSize, bool bigEndian)
    {
        const string Prefix = "<?xml";
        var sb = new StringBuilder();
        int units = body.Length / unitSize;
        for (int i = 0; i < units; i++)
        {
            int unit = unitSize == 1
                ? body[i]
                : bigEndian
                    ? (body[2 * i] << 8) | body[(2 * i) + 1]
                    : (body[(2 * i) + 1] << 8) | body[2 * i];
            sb.Append(unit < 0x80 ? (char)unit : (char)0xFFFD);

            if (sb.Length <= Prefix.Length && sb[^1] != Prefix[sb.Length - 1])
            {
                return null;
            }

            if (sb.Length == Prefix.Length + 1 && !char.IsWhiteSpace(sb[^1]))
            {
                return null;
            }

            if (sb.Length > Prefix.Length && sb[^2] == '?' && sb[^1] == '>')
            {
                return sb.ToString();
            }
        }

        return null;
    }

    /// <summary>The value of the encoding pseudo-attribute, or null when the declaration has none.</summary>
    private static string? DeclaredEncodingName(string declaration)
    {
        Match match = EncodingPseudoAttribute.Match(declaration);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    private static Encoding ResolveDeclared(string name, string? path)
    {
        try
        {
            return Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw Unsupported($"unsupported encoding '{name}'", path);
        }
    }

    private static bool IsUtf16(Encoding encoding) => encoding.CodePage is 1200 or 1201;

    private static bool IsUtf32(Encoding encoding) => encoding.CodePage is 12000 or 12001;

    private static string DecodeUtf16(ReadOnlySpan<byte> body, bool bigEndian, int bomLength, string? path)
    {
        Encoding encoding = bigEndian ? Utf16BeStrict : Utf16LeStrict;
        if (body.Length % 2 != 0)
        {
            throw InvalidBytes(encoding, body, body.Length - 1, bomLength, path, "odd number of bytes");
        }

        // Find the first unpaired surrogate by hand: the decoder's own offset is not reliable for a lone high surrogate.
        int units = body.Length / 2;
        for (int i = 0; i < units; i++)
        {
            char c = ReadUnit(body, i, bigEndian);
            if (char.IsHighSurrogate(c) && i + 1 < units && char.IsLowSurrogate(ReadUnit(body, i + 1, bigEndian)))
            {
                i++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                throw InvalidBytes(encoding, body, i * 2, bomLength, path, "unpaired surrogate");
            }
        }

        return DecodeBytes(encoding, body, bomLength, path);
    }

    private static char ReadUnit(ReadOnlySpan<byte> body, int unit, bool bigEndian)
    {
        int b = unit * 2;
        return bigEndian
            ? (char)((body[b] << 8) | body[b + 1])
            : (char)((body[b + 1] << 8) | body[b]);
    }

    /// <summary>
    /// Decodes strictly. windows-1252 maps the five undefined bytes to C1 controls in .NET, so they are rejected here
    /// explicitly; without this check they would decode and round-trip without any error.
    /// </summary>
    private static string DecodeBytes(Encoding encoding, ReadOnlySpan<byte> body, int bomLength, string? path)
    {
        if (encoding.CodePage == 1252)
        {
            for (int i = 0; i < body.Length; i++)
            {
                if (body[i] is 0x81 or 0x8D or 0x8F or 0x90 or 0x9D)
                {
                    throw InvalidBytes(encoding, body, i, bomLength, path, null);
                }
            }
        }

        try
        {
            return encoding.GetString(body);
        }
        catch (DecoderFallbackException ex)
        {
            throw InvalidBytes(encoding, body, ex.Index, bomLength, path, null);
        }
    }

    /// <summary>
    /// Checks that the text encodes back to the input bytes. The guard matters for multi-byte code pages where one byte
    /// sequence can decode to a character that encodes differently, for example ISO-2022-JP's ESC $ @ and ESC $ B.
    /// </summary>
    private static SourceText Verified(string text, Encoding encoding, bool hasBom, ReadOnlySpan<byte> bytes, string? path)
    {
        SourceText result = SourceText.CreateStrict(text, encoding, hasBom);
        byte[]? encoded = null;
        try
        {
            encoded = result.Encode();
        }
        catch (CfgeditException)
        {
            // An unencodable character can only come from a non-bijective mapping, so it is a round-trip failure too.
        }

        if (encoded is null || !encoded.AsSpan().SequenceEqual(bytes))
        {
            throw new CfgeditException(
                $"the file's bytes do not round-trip through encoding {encoding.WebName}; cfgedit cannot edit it safely")
            {
                Path = path,
            };
        }

        return result;
    }

    /// <summary>
    /// The first node's encoding as XmlTextReader reports it. Only the first node is read, so a prefix of the bytes is tried
    /// first, which avoids copying a large document. When the prefix gives no encoding, the whole document is read as before,
    /// and an XmlException from that read propagates to the caller.
    /// </summary>
    private static string? FirstNodeEncoding(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > FirstNodePrefixLength)
        {
            string? fromPrefix;
            try
            {
                fromPrefix = TryFirstNodeEncoding(bytes[..FirstNodePrefixLength]);
            }
            catch (XmlException)
            {
                fromPrefix = null;
            }

            if (fromPrefix is not null)
            {
                return fromPrefix;
            }
        }

        return TryFirstNodeEncoding(bytes);
    }

    private static string? TryFirstNodeEncoding(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new XmlTextReader(stream) { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
        return reader.Read() ? reader.Encoding?.WebName : null;
    }

    private static CfgeditException InvalidBytes(
        Encoding encoding, ReadOnlySpan<byte> body, int index, int bomLength, string? path, string? detail)
    {
        LinePosition? at = PositionOfLastGoodCharacter(encoding, body[..index]);
        string message = $"not valid {DisplayName(encoding)} at byte {bomLength + index}";
        if (detail is not null)
        {
            message += $": {detail}";
        }

        return new CfgeditException(message)
        {
            Path = path,
            Line = at?.Line,
            Column = at?.Column,
        };
    }

    /// <summary>The position of the last character decoded from the prefix, or null when there is none.</summary>
    private static LinePosition? PositionOfLastGoodCharacter(Encoding encoding, ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length == 0)
        {
            return null;
        }

        string text;
        try
        {
            text = encoding.GetString(prefix);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        if (text.Length == 0)
        {
            return null;
        }

        int last = text.Length - 1;
        if (char.IsLowSurrogate(text[last]) && last > 0 && char.IsHighSurrogate(text[last - 1]))
        {
            last--;
        }

        return SourceText.CreateStrict(text, encoding, hasBom: false).GetPosition(last);
    }

    private static string DisplayName(Encoding encoding) => encoding.CodePage switch
    {
        65001 => "UTF-8",
        1200 => "UTF-16 LE",
        1201 => "UTF-16 BE",
        _ => encoding.WebName,
    };

    private static CfgeditException Unsupported(string message, string? path) => new(message) { Path = path };

    private static CfgeditException Contradiction(string message, string? path) => new(message) { Path = path };
}
