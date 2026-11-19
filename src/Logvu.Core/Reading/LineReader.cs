using System.Text;

namespace Logvu.Core.Reading;

/// <summary>
/// Splits a UTF-8 stream into lines by byte offset, forward from a line start or backward from a line end. Memory is
/// bounded by twice the block size plus the text cap, whatever the line length. The reader does not own the stream; it
/// sets <see cref="Stream.Position"/> before each read.
/// </summary>
public sealed class LineReader
{
    /// <summary>The default text cap: the most bytes of a line that are decoded into <see cref="RawLine.Text"/>.</summary>
    public const int TextCap = 8 * 1024;

    private static readonly UTF8Encoding Utf8 = new(false, false);

    private readonly Stream _stream;
    private readonly int _blockSize;
    private readonly int _textCap;

    /// <param name="stream">A seekable stream. It is read, never disposed.</param>
    /// <param name="blockSize">The number of bytes read at a time.</param>
    /// <param name="textCap">The most bytes of a line decoded into <see cref="RawLine.Text"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockSize"/> or <paramref name="textCap"/> is below 1.</exception>
    public LineReader(Stream stream, int blockSize = 64 * 1024, int textCap = TextCap)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(textCap, 1);
        _stream = stream;
        _blockSize = blockSize;
        _textCap = textCap;
    }

    /// <summary>The offset where the content starts: 3 when the stream begins with a UTF-8 byte order mark, otherwise 0.</summary>
    public static long ContentStart(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.Length < 3)
        {
            return 0;
        }

        Span<byte> head = stackalloc byte[3];
        stream.Position = 0;
        stream.ReadExactly(head);
        return head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
    }

    /// <summary>
    /// The lines in order, starting at <paramref name="from"/> and continuing while a line starts before
    /// <paramref name="limit"/>. Only bytes before <paramref name="limit"/> are read, so a line that runs past it ends
    /// there and is unterminated. A <paramref name="limit"/> beyond the stream length is clamped to it.
    /// </summary>
    /// <param name="from">A line start. An offset inside the byte order mark is moved to the content start.</param>
    /// <param name="limit">The offset at which reading stops.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> is outside [0, stream length].</exception>
    public IEnumerable<RawLine> ReadForward(long from, long limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, _stream.Length, nameof(from));
        return Forward(from, limit);
    }

    /// <summary>
    /// The lines in reverse order, starting with the line that ends at <paramref name="before"/> and continuing
    /// backward to the content start. An unterminated tail before <paramref name="before"/> comes first.
    /// </summary>
    /// <param name="before">A line end (the offset after a <c>\n</c>) or the stream length.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="before"/> is outside [0, stream length].</exception>
    public IEnumerable<RawLine> ReadBackward(long before)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(before);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(before, _stream.Length, nameof(before));
        return Backward(before);
    }

    private IEnumerable<RawLine> Forward(long from, long limit)
    {
        var scanner = new LineScanner(_stream, _blockSize, _textCap);
        long end = Math.Min(limit, scanner.Length);
        long start = Math.Max(from, scanner.ContentStart);
        while (start < end)
        {
            RawLine line = ForwardLine(scanner, start, end);
            yield return line;
            start = line.End;
        }
    }

    private IEnumerable<RawLine> Backward(long before)
    {
        var scanner = new LineScanner(_stream, _blockSize, _textCap);
        long bom = scanner.ContentStart;
        long end = before;
        while (end > bom)
        {
            RawLine line = BackwardLine(scanner, end, bom);
            yield return line;
            end = line.Start;
        }
    }

    /// <summary>The line starting at <paramref name="start"/>, as an owned <see cref="RawLine"/>. Kept out of the iterators, which cannot hold a view.</summary>
    private static RawLine ForwardLine(LineScanner scanner, long start, long end) => Raw(scanner.Forward(start, end));

    /// <summary>The line ending at <paramref name="end"/>, as an owned <see cref="RawLine"/>.</summary>
    private static RawLine BackwardLine(LineScanner scanner, long end, long bom) => Raw(scanner.Backward(end, bom));

    /// <summary>The owned copy of a view, with its text decoded.</summary>
    private static RawLine Raw(in LineView line) =>
        new(line.Start, line.End, line.Terminated, Utf8.GetString(line.Prefix[..line.TextLength]), line.Truncated);
}
