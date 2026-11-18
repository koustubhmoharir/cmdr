using Logvu.Core.Parsing;

namespace Logvu.Core.Reading;

/// <summary>
/// Groups the lines of a stream into events, by byte extent. Forward and backward framing produce the same extents for
/// the same data. The framer reads through its own <see cref="LineReader"/> and holds no per-enumeration state, so
/// several enumerations may run over one framer.
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
    private readonly Stream _stream;
    private readonly ILogScheme _scheme;
    private readonly bool _live;
    private readonly LineReader _reader;

    /// <summary>Creates a framer over a stream.</summary>
    /// <param name="stream">A seekable stream. It is read, never disposed; it may grow while framing when <paramref name="live"/> is set.</param>
    /// <param name="scheme">The scheme whose headers delimit events.</param>
    /// <param name="live">Whether the stream is still being written. Then an unterminated last line is held back, and the last event of a text file is provisional.</param>
    /// <param name="blockSize">The block size of the underlying <see cref="LineReader"/>.</param>
    /// <param name="textCap">
    /// The most bytes of a line that are decoded for header matching. A text scheme's header must lie within it, and a
    /// JSON line is a header whether or not it is cut.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> or <paramref name="scheme"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockSize"/> or <paramref name="textCap"/> is below 1.</exception>
    public EventFramer(Stream stream, ILogScheme scheme, bool live, int blockSize = 64 * 1024, int textCap = LineReader.TextCap)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(scheme);
        _stream = stream;
        _scheme = scheme;
        _live = live;
        _reader = new LineReader(stream, blockSize, textCap);
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
        return Forward(from);
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
        return Backward(before);
    }

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
        long contentStart = LineReader.ContentStart(_stream);
        long dataEnd = DataEnd(_stream, _reader, _live);
        ArgumentOutOfRangeException.ThrowIfLessThan(lineStart, contentStart, nameof(lineStart));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(lineStart, dataEnd, nameof(lineStart));

        RawLine line = _reader.ReadForward(lineStart, dataEnd).First();
        if (IsHeader(line))
        {
            return lineStart;
        }

        foreach (RawLine previous in _reader.ReadBackward(lineStart))
        {
            if (IsHeader(previous))
            {
                return previous.Start;
            }
        }

        return contentStart;
    }

    /// <summary>
    /// The offset where the readable data ends. A closed stream ends at its length. A live stream ends after its last
    /// terminated line, so an unterminated tail is held back.
    /// </summary>
    internal static long DataEnd(Stream stream, LineReader reader, bool live)
    {
        if (!live)
        {
            return stream.Length;
        }

        using IEnumerator<RawLine> last = reader.ReadBackward(stream.Length).GetEnumerator();
        if (!last.MoveNext())
        {
            return stream.Length;
        }

        return last.Current.Terminated ? last.Current.End : last.Current.Start;
    }

    private IEnumerable<EventExtent> Forward(long from)
    {
        long dataEnd = DataEnd(_stream, _reader, _live);
        long start = Math.Max(from, LineReader.ContentStart(_stream));
        if (start >= dataEnd)
        {
            yield break;
        }

        bool open = false;
        long eventStart = 0;
        long headerEnd = 0;
        bool orphan = false;
        foreach (RawLine line in _reader.ReadForward(start, dataEnd))
        {
            bool header = IsHeader(line);
            if (open && header)
            {
                yield return Extent(eventStart, line.Start, headerEnd, orphan, dataEnd);
                open = false;
            }

            if (!open)
            {
                open = true;
                eventStart = line.Start;
                headerEnd = line.End;
                orphan = !header;
            }
        }

        if (open)
        {
            yield return Extent(eventStart, dataEnd, headerEnd, orphan, dataEnd);
        }
    }

    private IEnumerable<EventExtent> Backward(long before)
    {
        long dataEnd = DataEnd(_stream, _reader, _live);
        long contentStart = LineReader.ContentStart(_stream);
        long eventEnd = Math.Min(before, dataEnd);
        if (eventEnd <= contentStart)
        {
            yield break;
        }

        // Lines arrive last first. Each header closes the event that runs from it to the end of the group above it.
        RawLine first = default;
        foreach (RawLine line in _reader.ReadBackward(eventEnd))
        {
            first = line;
            if (IsHeader(line))
            {
                yield return Extent(line.Start, eventEnd, line.End, false, dataEnd);
                eventEnd = line.Start;
            }
        }

        // No header above the rest: they are the orphan run, which starts at the content start.
        if (eventEnd > contentStart)
        {
            yield return Extent(contentStart, eventEnd, first.End, true, dataEnd);
        }
    }

    private EventExtent Extent(long start, long end, long headerEnd, bool orphan, long dataEnd) =>
        new(start, end, headerEnd, _live && !_scheme.IsJson && end == dataEnd, orphan);

    /// <summary>
    /// Whether a line starts an event. A JSON line that is cut at the text cap is a header whatever its prefix: its
    /// blankness cannot be seen from the prefix, and a line with content must not be folded into the previous event.
    /// </summary>
    private bool IsHeader(RawLine line) => (_scheme.IsJson && line.Truncated) || _scheme.IsHeader(line.Text);
}
