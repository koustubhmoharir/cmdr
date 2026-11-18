namespace Logvu.Core.Store;

/// <summary>
/// What one <see cref="Reconciler.Run"/> changed. Immutable. The rows are the ones the run published: the caller applies them to its
/// view in the order given.
/// </summary>
public sealed class ChangeBatch
{
    /// <summary>Creates a batch. Only the reconciler creates one; the lists are copied.</summary>
    internal ChangeBatch(
        bool initial,
        IEnumerable<EventRow> added,
        IEnumerable<EventRow> updated,
        IEnumerable<int> expired,
        IEnumerable<RetentionGap> gaps,
        bool catalogChanged,
        bool ringReset,
        IEnumerable<MemberReadError> errors,
        string? directoryError,
        bool morePending,
        IEnumerable<int> readGenerations,
        bool rescanNeeded)
    {
        Initial = initial;
        Added = added.ToList().AsReadOnly();
        Updated = updated.ToList().AsReadOnly();
        Expired = expired.ToList().AsReadOnly();
        Gaps = gaps.ToList().AsReadOnly();
        CatalogChanged = catalogChanged;
        RingReset = ringReset;
        Errors = errors.ToList().AsReadOnly();
        DirectoryError = directoryError;
        MorePending = morePending;
        ReadGenerations = readGenerations.ToList().AsReadOnly();
        RescanNeeded = rescanNeeded;
    }

    /// <summary>
    /// Whether the run was the initial one. The members' existing content is then history: <see cref="Added"/> holds only the provisional
    /// row of the live member, if any.
    /// </summary>
    public bool Initial { get; }

    /// <summary>
    /// The rows whose keys were not published before, in timeline order. A provisional row appears here once, when it is first published;
    /// its later changes are in <see cref="Updated"/>.
    /// </summary>
    public IReadOnlyList<EventRow> Added { get; }

    /// <summary>
    /// The rows whose keys were published before: a provisional row that grew, or one that was finalized. Each is the row as it now is.
    /// </summary>
    public IReadOnlyList<EventRow> Updated { get; }

    /// <summary>The generations that expired in this run, by truncation or by disappearing from the set.</summary>
    public IReadOnlyList<int> Expired { get; }

    /// <summary>The members that expired with unread bytes.</summary>
    public IReadOnlyList<RetentionGap> Gaps { get; }

    /// <summary>Whether the membership, the timeline order, a path, the live member or a member's state changed.</summary>
    public bool CatalogChanged { get; }

    /// <summary>
    /// Whether the ring was cleared, because a row sorted before its newest row and could not be appended. The rows of the run are
    /// still reported in <see cref="Added"/> and <see cref="Updated"/>.
    /// </summary>
    public bool RingReset { get; }

    /// <summary>The members whose files could not be read in this run. The next run retries them.</summary>
    public IReadOnlyList<MemberReadError> Errors { get; }

    /// <summary>
    /// Why the directory could not be enumerated, or <c>null</c>. Known members are still read; nothing expired.
    /// </summary>
    public string? DirectoryError { get; }

    /// <summary>
    /// Whether a member had more events than one run reads. The store should run again straight away; the next run continues where this
    /// one stopped.
    /// </summary>
    public bool MorePending { get; }

    /// <summary>
    /// The generations whose file was opened and read without error in this run. A read error of such a generation, from an earlier run,
    /// no longer holds: the store drops it. Expired generations are not listed here; <see cref="Expired"/> covers them.
    /// </summary>
    public IReadOnlyList<int> ReadGenerations { get; }

    /// <summary>
    /// Whether a tail-only run found a member's path not naming its file (not found, or another file now): the file is being rotated. No
    /// error is recorded for it, and the store runs a rescan straight away, which relocates or expires the member.
    /// </summary>
    public bool RescanNeeded { get; }

    /// <summary>
    /// Whether nothing changed: no rows, no expiry, no gap, no catalog change, no ring reset, no error and no directory error.
    /// <see cref="Initial"/>, <see cref="MorePending"/>, <see cref="ReadGenerations"/> and <see cref="RescanNeeded"/> do not count.
    /// </summary>
    public bool IsEmpty =>
        Added.Count == 0
        && Updated.Count == 0
        && Expired.Count == 0
        && Gaps.Count == 0
        && !CatalogChanged
        && !RingReset
        && Errors.Count == 0
        && DirectoryError is null;
}
