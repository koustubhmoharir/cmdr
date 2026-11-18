namespace Logvu.Core.Store;

/// <summary>Identifies an event: the session generation of its file and the byte offset of its first byte.</summary>
/// <param name="Generation">The generation of the member the event is in.</param>
/// <param name="Offset">The offset of the event's start within that member's file.</param>
public readonly record struct RowKey(int Generation, long Offset);
