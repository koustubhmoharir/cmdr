namespace Logvu.Core.Reading;

/// <summary>
/// One event of a file: the bytes <c>[Start, End)</c>. The event's first line ends at <see cref="HeaderEnd"/>.
/// </summary>
/// <param name="Start">The offset of the first byte: the event's header line, or the content start for an orphan run.</param>
/// <param name="End">The offset after the last byte: the next event's start, or the data end.</param>
/// <param name="HeaderEnd">The offset after the event's first line, including its line terminator.</param>
/// <param name="Provisional">
/// Whether the event is the last one of a live text file, so that more continuation lines may still extend it.
/// </param>
/// <param name="Orphan">
/// Whether the event is the run of non-header lines before the first header of a file (text schemes), or the leading
/// blank lines of a JSON file.
/// </param>
public readonly record struct EventExtent(long Start, long End, long HeaderEnd, bool Provisional, bool Orphan);
