using Logvu.Core.Parsing;

namespace Logvu.Core.Store;

/// <summary>
/// An immutable copy of a member of the timeline, as it was when a <see cref="CatalogSnapshot"/> was taken. Readers of history use
/// these copies and never touch the mutable <see cref="Member"/>.
/// </summary>
/// <param name="Generation">The session generation of the member.</param>
/// <param name="Path">The path the file had when the snapshot was taken.</param>
/// <param name="Name">The basename of <paramref name="Path"/>.</param>
/// <param name="Identity">The identity of the file.</param>
/// <param name="Scheme">The scheme the file is read with.</param>
/// <param name="TimelineState">
/// The group the member is in the timeline by: <see cref="MemberState.Dated"/> or <see cref="MemberState.UnknownTime"/>, which an expired
/// member keeps.
/// </param>
/// <param name="IsReadable">Whether the member's events can be read from its file (it is in the timeline and has not expired).</param>
/// <param name="ReadCursor">The byte offset up to which the file had been read.</param>
/// <param name="FirstTime">The time of the first event, or <c>null</c> when the member has none.</param>
/// <param name="IsLive">Whether the member was the live one.</param>
/// <param name="Diagnostic">Why the member has no time, or <c>null</c>.</param>
public sealed record MemberInfo(
    int Generation,
    string Path,
    string Name,
    string Identity,
    ILogScheme Scheme,
    MemberState TimelineState,
    bool IsReadable,
    long ReadCursor,
    DateTimeOffset? FirstTime,
    bool IsLive,
    string? Diagnostic);
