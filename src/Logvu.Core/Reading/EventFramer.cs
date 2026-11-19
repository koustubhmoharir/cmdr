using Logvu.Core.Parsing;

namespace Logvu.Core.Reading;

/// <summary>
/// Groups the lines of a stream into events, by byte extent. Forward and backward framing produce the same extents for
/// the same data. The framer reads through its own line scanner and holds no per-enumeration state, so several
/// enumerations may run over one framer.
/// </summary>
/// <remarks>
/// <para>
/// An event is a header line followed by every following non-header line. For JSON schemes every non-blank line is a
/// header, so blank lines attach to the preceding event. Lines before the first header of a file form one orphan event.
/// For JSON, that is the leading blank lines.
/// </para>
/// <para>
/// The data end depends on <paramref name="live"/>. A closed stream ends at its length. A live stream ends after its
/// last terminated line, so an unterminated tail belongs to no event. The data end is taken from the stream each time an
/// enumeration starts, or each time <see cref="FindEventStart"/> is called, so a live stream may grow between calls.
/// </para>
/// </remarks>
public sealed class EventFramer
{
    /// <summary>The block size used when the caller does not give one.</summary>
    private const int DefaultBlockSize = 64 * 1024;

    private readonly Stream _stream;
    private readonly ILogScheme _scheme;
    private readonly bool _live;
    private readonly int _blockSize;
    private readonly int _textCap;

    /// <summary>Creates a framer over a stream.</summary>
    /// <param name="stream">A seekable stream. It is read, never disposed; it may grow while framing when <paramref name="live"/> is set.</param>
    /// <param name="scheme">The scheme whose headers delimit events.</param>
    /// <param name="live">Whether the stream is still being written. Then an unterminated last line is held back, and the last event of a text file is provisional.</param>
    /// <param name="blockSize">The block size of the underlying line scanner.</param>
    /// <param name="textCap">
    /// The most bytes of a line that are decoded for header matching. A text scheme's header must lie within it, and a
    /// JSON line is a header whether or not it is cut.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> or <paramref name="scheme"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockSize"/> or <paramref name="textCap"/> is below 1.</exception>
    public EventFramer(Stream stream, ILogScheme scheme, bool live, int blockSize = DefaultBlockSize, int textCap = LineReader.TextCap)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(textCap, 1);
        _stream = stream;
        _scheme = scheme;
        _live = live;
        _blockSize = blockSize;
        _textCap = textCap;
    }

    /// <summary>
    /// The events in order, from <paramref name="from"/> up to the data end. The enumeration reads the data end when it starts.
    /// </summary>
    /// <param name="from">An event start, or the content start (or 0, which is the same as the content start).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> is negative or beyond the stream length.</exception>
    public IEnumerable<EventExtent> ReadForward(long from)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(from);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(from, _stream.Length, nameof(from));
        return Enumerate(Forward(from));
    }

    /// <summary>
    /// The events in reverse order, from the one that ends at <paramref name="before"/> back to the orphan event or the
    /// first header. The enumeration reads the data end when it starts.
    /// </summary>
    /// <param name="before">An event end, or any offset at or beyond the data end, which is clamped to the data end.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="before"/> is negative.</exception>
    public IEnumerable<EventExtent> ReadBackward(long before)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(before);
        return Enumerate(Backward(before));
    }

    /// <summary>The events of a cursor, as a sequence. The cursor is made when the enumeration starts.</summary>
    internal EventCursor Forward(long from) => EventCursor.Forward(_stream, _scheme, _live, _blockSize, _textCap, from);

    /// <summary>
    /// A cursor over the events before <paramref name="before"/>, which is clamped to the data end. The cursor reads the data end when it
    /// is made.
    /// </summary>
    internal EventCursor Backward(long before) => EventCursor.Backward(_stream, _scheme, _live, _blockSize, _textCap, before);

    /// <summary>The start of the event that contains the line starting at <paramref name="lineStart"/>.</summary>
    /// <remarks>
    /// That is the line's own offset when it is a header, the nearest header before it otherwise, or the content start
    /// when the line is part of the orphan run. The data end is read from the stream at the time of the call.
    /// </remarks>
    /// <param name="lineStart">The first byte of a line, at or after the content start and before the data end.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="lineStart"/> is before the content start, or at or past the data end: an unterminated live tail is in no event.
    /// </exception>
    public long FindEventStart(long lineStart)
    {
        var scanner = new LineScanner(_stream, _blockSize, _textCap);
        long contentStart = scanner.ContentStart;
        long dataEnd = DataEnd(_stream, scanner, _live);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineStart, contentStart, nameof(lineStart));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lineStart, dataEnd, nameof(lineStart));

        LineView line = scanner.Forward(lineStart, dataEnd);
        if (IsHeader(scanner, line))
        {
            return lineStart;
        }

        // The header search walks back from the line: each step reads the line that ends where the previous one started.
        for (long end = lineStart; end > contentStart;)
        {
            LineView previous = scanner.Backward(end, contentStart);
            if (IsHeader(scanner, previous))
            {
                return previous.Start;
            }

            end = previous.Start;
        }

        return contentStart;
    }

    /// <summary>
    /// The offset where the readable data ends: the stream's length when it is closed, else the end of its last terminated line.
    /// </summary>
    /// <param name="stream">A seekable stream.</param>
    /// <param name="live">Whether the stream is still being written.</param>
    internal static long DataEnd(Stream stream, bool live) =>
        DataEnd(stream, new LineScanner(stream, DefaultBlockSize, LineReader.TextCap), live);

    /// <summary>
    /// The offset where the readable data ends. A closed stream ends at its length. A live stream ends after its last
    /// terminated line, so an unterminated tail is held back.
    /// </summary>
    internal static long DataEnd(Stream stream, LineScanner scanner, bool live)
    {
        if (!live)
        {
            return stream.Length;
        }

        long length = scanner.Length;
        if (length <= scanner.ContentStart)
        {
            return length;
        }

        LineView last = scanner.Backward(length, scanner.ContentStart);
        return last.Terminated ? last.End : last.Start;
    }

    /// <summary>Whether a line starts an event. See <see cref="EventCursor"/>, which applies the same rule.</summary>
    private bool IsHeader(LineScanner scanner, in LineView line) => (_scheme.IsJson && line.Truncated) || _scheme.IsHeader(scanner.Decode(line));

    private static IEnumerable<EventExtent> Enumerate(EventCursor cursor)
    {
        while (cursor.MoveNext(out EventExtent extent))
        {
            yield return extent;
        }
    }
}
