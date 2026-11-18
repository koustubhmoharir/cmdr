using Logvu.Core.Parsing;

namespace Logvu.Core.Store;

/// <summary>What <see cref="MemberProbe.Probe"/> found about a file's start.</summary>
/// <param name="Scheme">The scheme the file is read with, or <c>null</c> when none has been found yet.</param>
/// <param name="State">The state the member takes: <see cref="MemberState.Pending"/>, <see cref="MemberState.Dated"/> or <see cref="MemberState.UnknownTime"/>.</param>
/// <param name="FirstEventOffset">The byte offset of the first event, or the content start when there is none.</param>
/// <param name="FirstTime">The time of the first event, when <paramref name="State"/> is <see cref="MemberState.Dated"/>.</param>
/// <param name="Diagnostic">Why the member has no time, or <c>null</c>.</param>
/// <param name="Length">The length of the stream when it was probed.</param>
public sealed record ProbeResult(
    ILogScheme? Scheme,
    MemberState State,
    long FirstEventOffset,
    DateTimeOffset? FirstTime,
    string? Diagnostic,
    long Length);
