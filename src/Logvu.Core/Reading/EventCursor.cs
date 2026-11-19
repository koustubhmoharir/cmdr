using Logvu.Core.Parsing;

namespace Logvu.Core.Reading;

/// <summary>
/// The events of one enumeration of a stream, one at a time, with their text read from the same blocks that framed them. It is the
/// allocation-free form of the sequences <see cref="EventFramer"/> returns: <see cref="MoveNext"/> gives the same extents in the same
/// order, and <see cref="ReadText"/> gives the text of the event last returned.
/// </summary>
/// <remarks>
/// A cursor holds one <see cref="LineScanner"/>, whose length is taken when the cursor is made. It is not safe for concurrent use.
/// </remarks>
internal sealed class EventCursor
{
    private readonly LineScanner _scanner;
    private readonly ILogScheme _scheme;
    private readonly bool _live;
    private readonly bool _byteOrderMark;
    private readonly long _dataEnd;
    private readonly long _contentStart;
    private readonly bool _backward;

    // The forward walk: the next line to read, and the event being built.
    private long _position;
    private bool _open;
    private long _eventStart;
    private long _headerEnd;
    private bool _orphan;

    // The backward walk: the end of the event being built, and the end of the earliest line read so far.
    private long _eventEnd;
    private long _earliestEnd;

    private byte[] _text = new byte[256];

    private EventCursor(Stream stream, ILogScheme scheme, bool live, int blockSize, int textCap, bool backward, long from)
    {
        _scheme = scheme;
        _live = live;
        _scanner = new LineScanner(stream, blockSize, textCap);
        _contentStart = _scanner.ContentStart;
        _byteOrderMark = _contentStart == 3;
        _dataEnd = EventFramer.DataEnd(stream, _scanner, live);
        _backward = backward;
        if (backward)
        {
            _eventEnd = Math.Min(from, _dataEnd);
            _position = _eventEnd;
        }
        else
        {
            _position = Math.Max(from, _contentStart);
        }
    }

    /// <summary>A cursor over the events from <paramref name="from"/> forward, as <see cref="EventFramer.ReadForward"/> gives them.</summary>
    internal static EventCursor Forward(Stream stream, ILogScheme scheme, bool live, int blockSize, int textCap, long from) =>
        new(stream, scheme, live, blockSize, textCap, backward: false, from);

    /// <summary>A cursor over the events before <paramref name="before"/>, as <see cref="EventFramer.ReadBackward"/> gives them.</summary>
    internal static EventCursor Backward(Stream stream, ILogScheme scheme, bool live, int blockSize, int textCap, long before) =>
        new(stream, scheme, live, blockSize, textCap, backward: true, before);

    /// <summary>Moves to the next event of the walk.</summary>
    /// <param name="extent">The event, when there is one.</param>
    /// <returns><c>false</c> when the walk is over.</returns>
    public bool MoveNext(out EventExtent extent) => _backward ? NextBackward(out extent) : NextForward(out extent);

    /// <summary>
    /// The text of an event of this walk, as <see cref="EventText.Read"/> gives it for the same cap. The bytes are copied from the cursor's
    /// blocks, so a text read near its framing costs no file access of its own.
    /// </summary>
    /// <param name="extent">An event that this cursor returned.</param>
    /// <param name="maxBytes">The most bytes of the event that are read.</param>
    /// <exception cref="IOException">The bytes cannot be read.</exception>
    public string ReadText(EventExtent extent, int maxBytes)
    {
        bool byteOrderMark = _byteOrderMark && extent.Start == 0 && extent.End >= 3;
        (long start, int count, bool truncated) = EventText.TextBytes(extent, byteOrderMark, maxBytes);
        if (_text.Length < count)
        {
            _text = new byte[Math.Max(count, _text.Length * 2)];
        }

        _scanner.CopyOut(start, _text.AsSpan(0, count));
        return EventText.Decode(_text.AsSpan(0, count), truncated);
    }

    private bool NextForward(out EventExtent extent)
    {
        while (_position < _dataEnd)
        {
            LineView line = _scanner.Forward(_position, _dataEnd);
            _position = line.End;
            bool header = IsHeader(line);
            if (_open && header)
            {
                extent = Extent(_eventStart, line.Start, _headerEnd, _orphan);
                StartEvent(line, header);
                return true;
            }

            if (!_open)
            {
                StartEvent(line, header);
            }
        }

        if (_open)
        {
            _open = false;
            extent = Extent(_eventStart, _dataEnd, _headerEnd, _orphan);
            return true;
        }

        extent = default;
        return false;
    }

    private bool NextBackward(out EventExtent extent)
    {
        // Lines arrive last first. Each header closes the event that runs from it to the end of the group above it.
        while (_position > _contentStart)
        {
            LineView line = _scanner.Backward(_position, _contentStart);
            _earliestEnd = line.End;
            _position = line.Start;
            if (IsHeader(line))
            {
                extent = Extent(line.Start, _eventEnd, line.End, orphan: false);
                _eventEnd = line.Start;
                return true;
            }
        }

        // No header above the rest: they are the orphan run, which starts at the content start.
        if (_eventEnd > _contentStart)
        {
            extent = Extent(_contentStart, _eventEnd, _earliestEnd, orphan: true);
            _eventEnd = _contentStart;
            return true;
        }

        extent = default;
        return false;
    }

    /// <summary>Starts an event at a line of the forward walk.</summary>
    private void StartEvent(in LineView line, bool header)
    {
        _open = true;
        _eventStart = line.Start;
        _headerEnd = line.End;
        _orphan = !header;
    }

    private EventExtent Extent(long start, long end, long headerEnd, bool orphan) =>
        new(start, end, headerEnd, _live && !_scheme.IsJson && end == _dataEnd, orphan);

    /// <summary>
    /// Whether a line starts an event. A JSON line that is cut at the text cap is a header whatever its prefix: its blankness cannot be seen
    /// from the prefix, and a line with content must not be folded into the previous event.
    /// </summary>
    private bool IsHeader(in LineView line) => (_scheme.IsJson && line.Truncated) || _scheme.IsHeader(_scanner.Decode(line));
}
