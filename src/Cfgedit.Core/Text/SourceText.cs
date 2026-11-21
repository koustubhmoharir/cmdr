using System.Text;

namespace Cfgedit.Core.Text;

/// <summary>
/// The text of a file as read, with its encoding and byte order mark. It is immutable and is the single source of truth:
/// every syntax tree is computed from it, and every edit produces a new instance. <see cref="Encode"/> reproduces the
/// bytes it was decoded from, for every valid input.
/// </summary>
public sealed class SourceText
{
    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16LeStrict = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly Encoding Utf16BeStrict = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

    private readonly int[] _lineStarts;

    private SourceText(string text, Encoding encoding, bool hasBom)
    {
        Text = text;
        Encoding = encoding;
        HasBom = hasBom;
        _lineStarts = ComputeLineStarts(text);
        (Newline, EndsWithNewline) = ComputeNewlines(text);
    }

    /// <summary>The decoded text, without the byte order mark.</summary>
    public string Text { get; }

    /// <summary>The strict encoding: it throws on invalid input and emits no preamble itself.</summary>
    public Encoding Encoding { get; }

    /// <summary>True when the bytes started with a byte order mark.</summary>
    public bool HasBom { get; }

    /// <summary>
    /// The dominant line break in <see cref="Text"/>: "\r\n", "\n" or "\r". <see cref="Environment.NewLine"/> when the text has
    /// none.
    /// </summary>
    public string Newline { get; }

    /// <summary>True when the text ends with a line break.</summary>
    public bool EndsWithNewline { get; }

    /// <summary>
    /// Decodes the bytes of a JSON-family file. A UTF-8 (EF BB BF), UTF-16 LE (FF FE) or UTF-16 BE (FE FF) byte order mark
    /// selects the encoding and is removed; without one the bytes are UTF-8. Decoding is strict.
    /// </summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="path">The file's path, carried by any error. Null when unknown.</param>
    /// <exception cref="CfgeditException">The bytes are not valid in the encoding.</exception>
    public static SourceText Decode(ReadOnlySpan<byte> bytes, string? path = null)
    {
        Encoding encoding;
        int bomLength;
        bool hasBom;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = Utf8Strict;
            bomLength = 3;
            hasBom = true;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Utf16LeStrict;
            bomLength = 2;
            hasBom = true;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Utf16BeStrict;
            bomLength = 2;
            hasBom = true;
        }
        else
        {
            encoding = Utf8Strict;
            bomLength = 0;
            hasBom = false;
        }

        ReadOnlySpan<byte> body = bytes[bomLength..];
        string text;
        if (ReferenceEquals(encoding, Utf8Strict))
        {
            text = DecodeUtf8(body, bomLength, path);
        }
        else
        {
            text = DecodeUtf16(body, bomLength, encoding == Utf16BeStrict, path);
        }

        return new SourceText(text, encoding, hasBom);
    }

    /// <summary>
    /// Creates a source text from text that is already decoded: a new document or a test. The encoding is mapped to its
    /// strict form (UTF-8, UTF-16 LE or BE).
    /// </summary>
    /// <exception cref="ArgumentException">The encoding is not UTF-8 or UTF-16.</exception>
    public static SourceText Create(string text, Encoding encoding, bool hasBom)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(encoding);
        return new SourceText(text, StrictFor(encoding), hasBom);
    }

    /// <summary>The same encoding and byte order mark with different text. Newline and EndsWithNewline are recomputed.</summary>
    public SourceText WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SourceText(text, Encoding, HasBom);
    }

    /// <summary>
    /// The bytes of this text: the byte order mark when <see cref="HasBom"/> holds, then <see cref="Text"/> in
    /// <see cref="Encoding"/>.
    /// </summary>
    /// <exception cref="CfgeditException">The text holds a lone surrogate, which the encoding cannot represent.</exception>
    public byte[] Encode()
    {
        byte[] body;
        try
        {
            body = Encoding.GetBytes(Text);
        }
        catch (EncoderFallbackException ex)
        {
            LinePosition at = GetPosition(ex.Index);
            throw new CfgeditException(
                $"text cannot be saved as {Encoding.EncodingName}: a lone surrogate at line {at.Line}, column {at.Column}",
                ex)
            {
                Line = at.Line,
                Column = at.Column,
            };
        }

        if (!HasBom)
        {
            return body;
        }

        byte[] bom = ByteOrderMark(Encoding);
        byte[] result = new byte[bom.Length + body.Length];
        bom.CopyTo(result, 0);
        body.CopyTo(result, bom.Length);
        return result;
    }

    /// <summary>
    /// The 1-based line and UTF-16 column of an offset. CRLF, a lone CR and a lone LF are each one line break; U+2028 and
    /// U+2029 are not. An offset past the end is the position just after the last character.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The offset is negative.</exception>
    public LinePosition GetPosition(int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        int clamped = Math.Min(offset, Text.Length);

        // The last line start that is not after the offset.
        int low = 0;
        int high = _lineStarts.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (_lineStarts[mid] <= clamped)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return new LinePosition(low + 1, clamped - _lineStarts[low] + 1);
    }

    /// <summary>
    /// The offset of a line and column, the inverse of <see cref="GetPosition"/>. A column may point at either character of
    /// a CRLF, and on the last line it may point just after the last character.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The line or column is outside the text.</exception>
    public int GetOffset(LinePosition position)
    {
        if (position.Line < 1 || position.Line > _lineStarts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "The line is outside the text.");
        }

        int lineIndex = position.Line - 1;
        int lineStart = _lineStarts[lineIndex];
        bool isLast = lineIndex == _lineStarts.Length - 1;
        int lineEnd = isLast ? Text.Length : _lineStarts[lineIndex + 1];
        int maxColumn = isLast ? lineEnd - lineStart + 1 : lineEnd - lineStart;
        if (position.Column < 1 || position.Column > maxColumn)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "The column is outside the line.");
        }

        return lineStart + position.Column - 1;
    }

    private static Encoding StrictFor(Encoding encoding)
    {
        return encoding.CodePage switch
        {
            65001 => Utf8Strict,
            1200 => Utf16LeStrict,
            1201 => Utf16BeStrict,
            _ => throw new ArgumentException("Only UTF-8 and UTF-16 (LE and BE) are supported.", nameof(encoding)),
        };
    }

    private static byte[] ByteOrderMark(Encoding encoding)
    {
        if (ReferenceEquals(encoding, Utf8Strict))
        {
            return [0xEF, 0xBB, 0xBF];
        }

        return ReferenceEquals(encoding, Utf16BeStrict) ? [0xFE, 0xFF] : [0xFF, 0xFE];
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> body, int bomLength, string? path)
    {
        try
        {
            return Utf8Strict.GetString(body);
        }
        catch (DecoderFallbackException ex)
        {
            int byteOffset = bomLength + ex.Index;

            // The last good character is the one before the invalid bytes, when there is one.
            string prefix = Encoding.UTF8.GetString(body[..ex.Index]);
            LinePosition? at = null;
            if (prefix.Length > 0)
            {
                int lastStart = prefix.Length - 1;
                if (char.IsLowSurrogate(prefix[lastStart]) && lastStart > 0 && char.IsHighSurrogate(prefix[lastStart - 1]))
                {
                    lastStart--;
                }

                at = PositionIn(prefix, lastStart);
            }

            throw InvalidBytes($"not valid UTF-8 at byte {byteOffset}", path, at);
        }
    }

    private static string DecodeUtf16(ReadOnlySpan<byte> body, int bomLength, bool bigEndian, string? path)
    {
        string encodingName = bigEndian ? "UTF-16 BE" : "UTF-16 LE";
        if (body.Length % 2 != 0)
        {
            throw InvalidBytes($"not valid {encodingName} at byte {bomLength + body.Length - 1}: odd number of bytes", path, null);
        }

        // Find the first unpaired surrogate by hand, so that the byte offset is exact.
        int units = body.Length / 2;
        for (int i = 0; i < units; i++)
        {
            char c = ReadUnit(body, i, bigEndian);
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < units && char.IsLowSurrogate(ReadUnit(body, i + 1, bigEndian)))
                {
                    i++;
                    continue;
                }

                throw UnpairedSurrogate(body, i, bomLength, bigEndian, encodingName, path);
            }

            if (char.IsLowSurrogate(c))
            {
                throw UnpairedSurrogate(body, i, bomLength, bigEndian, encodingName, path);
            }
        }

        return (bigEndian ? Utf16BeStrict : Utf16LeStrict).GetString(body);
    }

    private static CfgeditException UnpairedSurrogate(
        ReadOnlySpan<byte> body, int unit, int bomLength, bool bigEndian, string encodingName, string? path)
    {
        int byteOffset = bomLength + unit * 2;
        LinePosition? at = null;
        if (unit > 0)
        {
            string prefix = (bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode).GetString(body[..(unit * 2)]);
            int lastStart = prefix.Length - 1;
            if (char.IsLowSurrogate(prefix[lastStart]) && lastStart > 0 && char.IsHighSurrogate(prefix[lastStart - 1]))
            {
                lastStart--;
            }

            at = PositionIn(prefix, lastStart);
        }

        return InvalidBytes($"not valid {encodingName} at byte {byteOffset}: unpaired surrogate", path, at);
    }

    private static char ReadUnit(ReadOnlySpan<byte> body, int unit, bool bigEndian)
    {
        int b = unit * 2;
        return bigEndian
            ? (char)((body[b] << 8) | body[b + 1])
            : (char)((body[b + 1] << 8) | body[b]);
    }

    private static CfgeditException InvalidBytes(string message, string? path, LinePosition? at)
    {
        return new CfgeditException(message)
        {
            Path = path,
            Line = at?.Line,
            Column = at?.Column,
        };
    }

    /// <summary>The position of a character in a string, counting from the start of the string.</summary>
    private static LinePosition PositionIn(string text, int offset)
    {
        return new SourceText(text, Utf8Strict, false).GetPosition(offset);
    }

    private static int[] ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                starts.Add(i + 1);
            }
            else if (c == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return [.. starts];
    }

    private static (string Newline, bool EndsWithNewline) ComputeNewlines(string text)
    {
        int crlf = 0;
        int lf = 0;
        int cr = 0;
        int crlfFirst = -1;
        int lfFirst = -1;
        int crFirst = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    if (crlf++ == 0)
                    {
                        crlfFirst = i;
                    }

                    i++;
                }
                else
                {
                    if (cr++ == 0)
                    {
                        crFirst = i;
                    }
                }
            }
            else if (c == '\n')
            {
                if (lf++ == 0)
                {
                    lfFirst = i;
                }
            }
        }

        string newline = Environment.NewLine;
        int best = 0;
        int bestFirst = int.MaxValue;
        Consider("\r\n", crlf, crlfFirst);
        Consider("\n", lf, lfFirst);
        Consider("\r", cr, crFirst);

        bool endsWithNewline = text.Length > 0 && (text[^1] == '\n' || text[^1] == '\r');
        return (newline, endsWithNewline);

        void Consider(string candidate, int count, int first)
        {
            if (count == 0)
            {
                return;
            }

            // A tie goes to the break that occurs first.
            if (count > best || (count == best && first < bestFirst))
            {
                best = count;
                bestFirst = first;
                newline = candidate;
            }
        }
    }
}

/// <summary>A position in a text: a 1-based line and a 1-based UTF-16 column.</summary>
/// <param name="Line">The 1-based line.</param>
/// <param name="Column">The 1-based column, counted in UTF-16 units.</param>
public readonly record struct LinePosition(int Line, int Column);
