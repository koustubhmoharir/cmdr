using Cmdr.Core.Output;

namespace Cmdr.Tui;

/// <summary>
/// The match counts of one <see cref="OutputBuffer"/>, kept per line, so the total and the number of matches before a line are cheap
/// while output streams. Each completed line is counted once, when it is first seen, and its count is dropped when the line is
/// evicted. The partial lines are recounted on every update, since they change. Pure apart from the buffer it reads. UI thread only.
/// </summary>
internal sealed class OutputSearchIndex
{
    private SearchPattern? _pattern;
    private OutputBuffer? _buffer;
    private long _version;
    private bool _haveVersion;

    // Counts of the completed lines [_firstNumber, _nextNumber), as a ring: _ring[_head] is the count of _firstNumber.
    private int[] _ring = Array.Empty<int>();
    private int _head;
    private int _ringCount;
    private long _firstNumber;
    private long _nextNumber;
    private long _completedSum;

    // Counts of the partial lines, the first of which is numbered _partialBase; at most two.
    private readonly int[] _partialCounts = new int[2];
    private int _partialLength;
    private long _partialBase;
    private long _partialSum;

    /// <summary>The pattern counted. Null when nothing is counted.</summary>
    public SearchPattern? Pattern => _pattern;

    /// <summary>Matches on all retained lines (completed and partial) as of the last <see cref="Update"/>.</summary>
    public int Total => (int)(_completedSum + _partialSum);

    /// <summary>Drops all counts and uses <paramref name="pattern"/> from now on. Null means nothing is counted, so <see cref="Total"/> is 0.</summary>
    public void Reset(SearchPattern? pattern)
    {
        _pattern = pattern;
        _buffer = null;
        ClearCounts();
    }

    /// <summary>
    /// Brings the counts up to date with <paramref name="buffer"/>. Returns at once when the buffer's version is the one last seen. A buffer
    /// other than the one last seen is handled like <see cref="Reset"/> with the same pattern. Does nothing when there is no pattern.
    /// </summary>
    public void Update(OutputBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        SearchPattern? pattern = _pattern;
        if (pattern is null)
        {
            return;
        }

        if (!ReferenceEquals(buffer, _buffer))
        {
            ClearCounts();
            _buffer = buffer;
        }

        if (_haveVersion && buffer.Version == _version)
        {
            return;
        }

        // Capturing from the first line not yet counted copies only the new lines; the snapshot starts at the buffer's first line when
        // the lines counted were evicted.
        BufferSnapshot snapshot = buffer.Capture(_nextNumber);
        DropBelow(snapshot.FirstLineNumber);

        long newCount = snapshot.NextLineNumber - _nextNumber;
        if (newCount > 0)
        {
            (_, IReadOnlyList<OutputLine> added) = snapshot.LinesFrom(_nextNumber, newCount);
            foreach (OutputLine line in added)
            {
                AddCompleted(pattern.Count(line.PlainText));
            }
        }

        _partialBase = snapshot.NextLineNumber;
        _partialLength = 0;
        _partialSum = 0;
        for (long number = snapshot.NextLineNumber; number <= snapshot.LastLineNumber; number++)
        {
            int count = pattern.Count(snapshot.LineAt(number)!.PlainText);
            _partialCounts[_partialLength++] = count;
            _partialSum += count;
        }

        _version = snapshot.Version;
        _haveVersion = true;
    }

    /// <summary>Matches on the retained lines numbered below <paramref name="line"/>, as of the last <see cref="Update"/>.</summary>
    public int CountBefore(long line)
    {
        long sum = 0;
        long completedEnd = Math.Min(line, _nextNumber);
        for (long number = _firstNumber; number < completedEnd; number++)
        {
            sum += RingAt((int)(number - _firstNumber));
        }

        for (int i = 0; i < _partialLength && _partialBase + i < line; i++)
        {
            sum += _partialCounts[i];
        }

        return (int)sum;
    }

    /// <summary>Matches on <paramref name="line"/>, as of the last <see cref="Update"/>. 0 for a line that is not retained.</summary>
    public int CountOn(long line)
    {
        if (line >= _firstNumber && line < _nextNumber)
        {
            return RingAt((int)(line - _firstNumber));
        }

        long partial = line - _partialBase;
        if (partial >= 0 && partial < _partialLength)
        {
            return _partialCounts[partial];
        }

        return 0;
    }

    /// <summary>Drops the counts of the completed lines below <paramref name="number"/>. When none are left, the range moves to start at it.</summary>
    private void DropBelow(long number)
    {
        if (number <= _firstNumber)
        {
            return;
        }

        int drop = (int)Math.Min(number - _firstNumber, _ringCount);
        for (int i = 0; i < drop; i++)
        {
            _completedSum -= _ring[_head];
            _head = (_head + 1) % _ring.Length;
        }

        _ringCount -= drop;
        _firstNumber += drop;
        if (_ringCount == 0)
        {
            _firstNumber = Math.Max(_firstNumber, number);
            _nextNumber = _firstNumber;
        }
    }

    private void AddCompleted(int count)
    {
        if (_ringCount == _ring.Length)
        {
            Grow();
        }

        _ring[(_head + _ringCount) % _ring.Length] = count;
        _ringCount++;
        _nextNumber++;
        _completedSum += count;
    }

    private void Grow()
    {
        var grown = new int[Math.Max(16, _ring.Length * 2)];
        for (int i = 0; i < _ringCount; i++)
        {
            grown[i] = RingAt(i);
        }

        _ring = grown;
        _head = 0;
    }

    private int RingAt(int index) => _ring[(_head + index) % _ring.Length];

    private void ClearCounts()
    {
        _haveVersion = false;
        _version = 0;
        _head = 0;
        _ringCount = 0;
        _firstNumber = 0;
        _nextNumber = 0;
        _completedSum = 0;
        _partialLength = 0;
        _partialBase = 0;
        _partialSum = 0;
    }
}
