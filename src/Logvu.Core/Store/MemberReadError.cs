namespace Logvu.Core.Store;

/// <summary>
/// A member whose events could not be read during a page read: its file was not found, its identity changed, or an I/O error
/// occurred. The read went on with the next member; the store reconciles the set.
/// </summary>
/// <param name="Generation">The generation of the member.</param>
/// <param name="Path">The path the member was read at.</param>
/// <param name="Message">Why the read failed.</param>
public sealed record MemberReadError(int Generation, string Path, string Message);
