namespace Cmdr.Core.Output;

/// <summary>
/// Thread-safe bounded buffer of output lines. Holds a ring of completed lines (the oldest are dropped
/// when full) followed by up to one partial line per stream (stdout, then stderr).
/// </summary>
public sealed class OutputBuffer
{
    private readonly object _sync = new();
    private readonly OutputLine?[] _ring;
    private int _head;
    private int _completedCount;
    private long _firstLineNumber;
    private OutputLine? _stdOutPartial;
    private OutputLine? _stdErrPartial;
    private long _version;

    /// <summary>Creates a buffer that retains up to <paramref name="capacity"/> completed lines.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public OutputBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _ring = new OutputLine?[capacity];
    }

    /// <summary>Maximum number of completed lines retained.</summary>
    public int Capacity => _ring.Length;

    /// <summary>Absolute number of the oldest retained completed line. Increases as lines are dropped.</summary>
    public long FirstLineNumber
    {
        get
        {
            lock (_sync)
            {
                return _firstLineNumber;
            }
        }
    }

    /// <summary>Absolute number the next appended completed line will receive.</summary>
    public long NextLineNumber
    {
        get
        {
            lock (_sync)
            {
                return _firstLineNumber + _completedCount;
            }
        }
    }

    /// <summary>Number of retained completed lines plus the current partial lines.</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return TotalLocked();
            }
        }
    }

    /// <summary>Incremented on every mutation. Lets a UI detect changes by polling.</summary>
    public long Version
    {
        get
        {
            lock (_sync)
            {
                return _version;
            }
        }
    }

    /// <summary>Appends completed lines, dropping the oldest lines when over capacity.</summary>
    public void Append(IReadOnlyList<OutputLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        lock (_sync)
        {
            if (lines.Count == 0)
            {
                return;
            }

            foreach (OutputLine line in lines)
            {
                if (_completedCount == _ring.Length)
                {
                    _ring[_head] = line;
                    _head = (_head + 1) % _ring.Length;
                    _firstLineNumber++;
                }
                else
                {
                    _ring[PhysicalIndex(_completedCount)] = line;
                    _completedCount++;
                }
            }

            _version++;
        }
    }

    /// <summary>Sets or clears (null) the partial line for <paramref name="source"/>, which must be StdOut or StdErr.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="source"/> is not StdOut or StdErr.</exception>
    public void SetPartial(OutputSource source, OutputLine? line)
    {
        lock (_sync)
        {
            switch (source)
            {
                case OutputSource.StdOut:
                    _stdOutPartial = line;
                    break;
                case OutputSource.StdErr:
                    _stdErrPartial = line;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(source), source, "Only StdOut and StdErr have partial lines.");
            }

            _version++;
        }
    }

    /// <summary>
    /// Returns lines by index relative to the oldest retained completed line, covering completed lines
    /// then the partial lines. The range is clamped to what exists; this never throws for out-of-range indices.
    /// </summary>
    public IReadOnlyList<OutputLine> GetLines(int start, int count)
    {
        lock (_sync)
        {
            int total = TotalLocked();
            if (count <= 0 || start >= total)
            {
                return Array.Empty<OutputLine>();
            }

            if (start < 0)
            {
                count += start;
                start = 0;
                if (count <= 0)
                {
                    return Array.Empty<OutputLine>();
                }
            }

            long end = Math.Min((long)total, (long)start + count);
            var result = new List<OutputLine>((int)(end - start));
            for (int i = start; i < end; i++)
            {
                result.Add(LineAtLocked(i));
            }

            return result;
        }
    }

    /// <summary>
    /// Returns the retained completed lines whose absolute number is at least
    /// <paramref name="absoluteLineNumber"/>. Clamped to <see cref="FirstLineNumber"/>. Partial lines are excluded.
    /// </summary>
    public IReadOnlyList<OutputLine> GetCompletedLinesFrom(long absoluteLineNumber)
    {
        lock (_sync)
        {
            long first = _firstLineNumber;
            long from = Math.Max(absoluteLineNumber, first);
            long index = from - first;
            if (index >= _completedCount)
            {
                return Array.Empty<OutputLine>();
            }

            var result = new List<OutputLine>(_completedCount - (int)index);
            for (int i = (int)index; i < _completedCount; i++)
            {
                result.Add(_ring[PhysicalIndex(i)]!);
            }

            return result;
        }
    }

    /// <summary>Drops all lines, including partials. <see cref="FirstLineNumber"/> becomes <see cref="NextLineNumber"/>.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _firstLineNumber += _completedCount;
            Array.Clear(_ring);
            _head = 0;
            _completedCount = 0;
            _stdOutPartial = null;
            _stdErrPartial = null;
            _version++;
        }
    }

    private int TotalLocked()
    {
        int total = _completedCount;
        if (_stdOutPartial is not null)
        {
            total++;
        }

        if (_stdErrPartial is not null)
        {
            total++;
        }

        return total;
    }

    private OutputLine LineAtLocked(int index)
    {
        if (index < _completedCount)
        {
            return _ring[PhysicalIndex(index)]!;
        }

        int offset = index - _completedCount;
        if (_stdOutPartial is not null)
        {
            if (offset == 0)
            {
                return _stdOutPartial;
            }

            offset--;
        }

        return _stdErrPartial!;
    }

    private int PhysicalIndex(int logicalIndex) => (_head + logicalIndex) % _ring.Length;
}
