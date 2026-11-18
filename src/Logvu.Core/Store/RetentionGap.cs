namespace Logvu.Core.Store;

/// <summary>
/// Bytes of a member that expired before the reader reached them: the file was gone with events in it that were never read.
/// </summary>
/// <param name="Generation">The generation of the member that expired.</param>
/// <param name="Name">The basename the member had when it expired.</param>
/// <param name="LostBytes">The unread bytes: the length last observed for the member, less its read-through point.</param>
public sealed record RetentionGap(int Generation, string Name, long LostBytes);
