namespace Cmdr.Core.Output;

/// <summary>
/// The line references of a range of an <see cref="OutputBuffer"/>, read under one lock by <see cref="OutputBuffer.Capture"/>.
/// Immutable: later writes to the buffer do not change it. Holds references only, not text, so it costs one reference per
/// captured line. Line numbers are absolute, as in the buffer; the partial lines are numbered after the completed ones.
/// </summary>
public sealed class BufferSnapshot
{
    private readonly OutputLine[] _lines;
    private readonly long _start;

    internal BufferSnapshot(long firstLineNumber, long nextLineNumber, long version, long lastLineNumber, long start, OutputLine[] lines)
    {
        FirstLineNumber = firstLineNumber;
        NextLineNumber = nextLineNumber;
        Version = version;
        LastLineNumber = lastLineNumber;
        _start = start;
        _lines = lines;
    }

    /// <summary>The buffer's <see cref="OutputBuffer.FirstLineNumber"/> when the snapshot was taken.</summary>
    public long FirstLineNumber { get; }

    /// <summary>The buffer's <see cref="OutputBuffer.NextLineNumber"/> when the snapshot was taken.</summary>
    public long NextLineNumber { get; }

    /// <summary>The buffer's <see cref="OutputBuffer.Version"/> when the snapshot was taken.</summary>
    public long Version { get; }

    /// <summary>
    /// The last line number when the snapshot was taken: the partial lines included. <see cref="FirstLineNumber"/> - 1 when the
    /// buffer had no lines.
    /// </summary>
    public long LastLineNumber { get; }

    /// <summary>The line with an absolute number, or null when it is not in the snapshot (outside the captured range, or no line).</summary>
    public OutputLine? LineAt(long number)
    {
        if (number < _start || number > LastLineNumber)
        {
            return null;
        }

        return _lines[number - _start];
    }

    /// <summary>
    /// Up to <paramref name="count"/> captured lines from absolute number <paramref name="from"/>, clipped to the captured range.
    /// <paramref name="first"/> is the number of the first line returned, which is larger than <paramref name="from"/> when the
    /// range starts later.
    /// </summary>
    public (long First, IReadOnlyList<OutputLine> Lines) LinesFrom(long from, long count)
    {
        long start = Math.Max(from, _start);
        long end = Math.Min(LastLineNumber, from + count - 1);
        if (count <= 0 || start > end)
        {
            return (start, Array.Empty<OutputLine>());
        }

        var result = new OutputLine[end - start + 1];
        Array.Copy(_lines, start - _start, result, 0, result.Length);
        return (start, result);
    }
}
