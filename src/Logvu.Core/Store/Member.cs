using Logvu.Core.Parsing;

namespace Logvu.Core.Store;

/// <summary>
/// One generation of a file in the set: one incarnation of a file's content, identified by the file's identity. Created by
/// <see cref="Catalog.Add"/>.
/// </summary>
/// <remarks>
/// <para>
/// A member is owned by the store's single worker. It is mutable and not thread-safe: all calls come from that worker.
/// </para>
/// <para>
/// Every change that can move the member in the timeline (its state, its first time, its basename) notifies its catalog, which
/// then rebuilds the order the next time it is read. No caller has to remember to invalidate anything. An expired member is
/// final: changing it throws.
/// </para>
/// </remarks>
public sealed class Member
{
    private readonly Catalog _owner;

    internal Member(Catalog owner, int generation, string path, string identity)
    {
        _owner = owner;
        Generation = generation;
        Identity = identity;
        Path = path;
        Name = System.IO.Path.GetFileName(path);
    }

    /// <summary>The session generation: unique within the catalog, increasing in the order members were added.</summary>
    public int Generation { get; }

    /// <summary>The current full path of the file. It changes when the file is renamed.</summary>
    public string Path { get; private set; }

    /// <summary>The basename of <see cref="Path"/>.</summary>
    public string Name { get; private set; }

    /// <summary>The file identity taken from the open handle, which does not change when the file is renamed.</summary>
    public string Identity { get; }

    /// <summary>The scheme the file is read with, or <c>null</c> until a scheme is found.</summary>
    public ILogScheme? Scheme { get; private set; }

    /// <summary>The state in the catalog.</summary>
    public MemberState State { get; private set; } = MemberState.Pending;

    /// <summary>The byte offset of the first event, or the content start when there is none.</summary>
    public long FirstEventOffset { get; private set; }

    /// <summary>The time of the first event, when <see cref="State"/> is <see cref="MemberState.Dated"/>.</summary>
    public DateTimeOffset? FirstTime { get; private set; }

    /// <summary>Why the member has no time, or <c>null</c>.</summary>
    public string? Diagnostic { get; private set; }

    /// <summary>The byte offset up to which the file has been read. Maintained by the store; it does not affect the order.</summary>
    public long ReadCursor { get; set; }

    /// <summary>Whether the member is the live one: the file named by the configured active file, or else the newest member.</summary>
    public bool IsLive { get; set; }

    /// <summary>Records what a probe found. The member takes the probe's scheme, state, first event and time.</summary>
    /// <param name="result">The probe's result. It may not report <see cref="MemberState.Expired"/>; use <see cref="Expire"/>.</param>
    /// <exception cref="InvalidOperationException">The member has expired.</exception>
    /// <exception cref="ArgumentException">The result is expired, or is dated without a time.</exception>
    public void Apply(ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ThrowIfExpired();
        if (result.State == MemberState.Expired)
        {
            throw new ArgumentException("A probe does not report an expired member.", nameof(result));
        }

        if (result.State == MemberState.Dated && result.FirstTime is null)
        {
            throw new ArgumentException("A dated member needs a first time.", nameof(result));
        }

        Scheme = result.Scheme;
        State = result.State;
        FirstEventOffset = result.FirstEventOffset;
        FirstTime = result.State == MemberState.Dated ? result.FirstTime : null;
        Diagnostic = result.Diagnostic;
        _owner.MarkChanged();
    }

    /// <summary>Records that the file now lives at another path, with the same identity. The name, which orders ties, follows it.</summary>
    /// <param name="path">The new full path.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <c>null</c> or empty.</exception>
    /// <exception cref="InvalidOperationException">The member has expired.</exception>
    public void Relocate(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ThrowIfExpired();
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        _owner.MarkChanged();
    }

    /// <summary>
    /// Marks the member expired: its file is gone. It leaves the timeline and keeps its generation, so its rows can still be found.
    /// Expiring an expired member does nothing.
    /// </summary>
    public void Expire()
    {
        if (State == MemberState.Expired)
        {
            return;
        }

        State = MemberState.Expired;
        _owner.MarkChanged();
    }

    private void ThrowIfExpired()
    {
        if (State == MemberState.Expired)
        {
            throw new InvalidOperationException($"Generation {Generation} has expired and cannot change.");
        }
    }
}
