using Logvu.Core.Parsing;
using Logvu.Core.Reading;

namespace Logvu.Core.Store;

/// <summary>
/// One event as a table row: its identity and extent, and the normalized fields the table shows. Built by <see cref="RowBuilder"/>.
/// </summary>
/// <param name="Key">The event's row key: its member's generation and its start offset.</param>
/// <param name="Extent">The event's bytes in its file.</param>
/// <param name="Time">The event's time, or <c>null</c> when it has none.</param>
/// <param name="TimeText">The time as written in the log, or <c>null</c>.</param>
/// <param name="Level">The normalized level.</param>
/// <param name="LevelText">The level as written in the log, or <c>null</c>.</param>
/// <param name="Source">The logger or source context, or <c>null</c>.</param>
/// <param name="Preview">The first line of the message, cut to <see cref="RowBuilder.PreviewCap"/> characters.</param>
/// <param name="IsRaw">Whether the event could not be parsed as a record of its scheme.</param>
/// <param name="Orphan">Whether the event is the run of lines before a file's first header.</param>
/// <param name="Provisional">Whether the event is the last one of a live text file and may still be extended.</param>
public sealed record EventRow(
    RowKey Key,
    EventExtent Extent,
    DateTimeOffset? Time,
    string? TimeText,
    LogLevel Level,
    string? LevelText,
    string? Source,
    string Preview,
    bool IsRaw,
    bool Orphan,
    bool Provisional);
