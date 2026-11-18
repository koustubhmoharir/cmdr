namespace Logvu.Core.Store;

/// <summary>
/// An immutable view of the timeline: the members in timeline order, as copies. Taken with <see cref="Catalog.Snapshot"/> on the store's
/// worker and handed to readers on other threads, which may use it freely while the catalog changes.
/// </summary>
/// <remarks>
/// The order is the catalog's order at the time of the snapshot, since the snapshot is built from <see cref="Catalog.Ordered"/>. Its
/// <see cref="Compare"/> is the catalog's comparison over the same positions.
/// </remarks>
public sealed class CatalogSnapshot
{
    private readonly Dictionary<int, int> _positions;

    /// <summary>Creates a snapshot. Only <see cref="Catalog.Snapshot"/> creates one.</summary>
    /// <param name="version">The catalog version the snapshot was taken at.</param>
    /// <param name="ordered">The members in timeline order, oldest first. The list is kept as given and must not change.</param>
    internal CatalogSnapshot(long version, IReadOnlyList<MemberInfo> ordered)
    {
        Version = version;
        Ordered = ordered;
        _positions = new Dictionary<int, int>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            _positions.Add(ordered[i].Generation, i);
        }
    }

    /// <summary>The catalog version the snapshot was taken at. Two snapshots of one catalog with equal versions are the same snapshot.</summary>
    public long Version { get; }

    /// <summary>The members in timeline order, oldest first: the unknown-time group, then the dated members, expired ones included at their place.</summary>
    public IReadOnlyList<MemberInfo> Ordered { get; }

    /// <summary>The member with the given generation, if it is in the timeline.</summary>
    /// <returns>The member, or <c>null</c> when no member of the timeline has that generation (it is unknown, or still pending).</returns>
    public MemberInfo? Find(int generation) =>
        _positions.TryGetValue(generation, out int position) ? Ordered[position] : null;

    /// <summary>The member before <paramref name="member"/> in the timeline, which is older.</summary>
    /// <returns>The member, or <c>null</c> at the start of the timeline or when <paramref name="member"/> is not in the snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <c>null</c>.</exception>
    public MemberInfo? Previous(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (!_positions.TryGetValue(member.Generation, out int position) || position == 0)
        {
            return null;
        }

        return Ordered[position - 1];
    }

    /// <summary>The member after <paramref name="member"/> in the timeline, which is newer.</summary>
    /// <returns>The member, or <c>null</c> at the end of the timeline or when <paramref name="member"/> is not in the snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <c>null</c>.</exception>
    public MemberInfo? Next(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (!_positions.TryGetValue(member.Generation, out int position) || position + 1 >= Ordered.Count)
        {
            return null;
        }

        return Ordered[position + 1];
    }

    /// <summary>
    /// Compares two events in the timeline: by the timeline position of their members, then by offset. Same semantics as
    /// <see cref="Catalog.Compare"/>.
    /// </summary>
    /// <returns>A negative number, zero, or a positive number as <paramref name="a"/> is before, the same as, or after <paramref name="b"/>.</returns>
    /// <exception cref="ArgumentException">A key's generation is not in the snapshot's timeline order.</exception>
    public int Compare(RowKey a, RowKey b)
    {
        int positionA = PositionOf(a.Generation, nameof(a));
        int positionB = PositionOf(b.Generation, nameof(b));
        return positionA != positionB ? positionA.CompareTo(positionB) : a.Offset.CompareTo(b.Offset);
    }

    private int PositionOf(int generation, string parameter)
    {
        if (!_positions.TryGetValue(generation, out int position))
        {
            throw new ArgumentException($"Generation {generation} is not in the timeline order.", parameter);
        }

        return position;
    }
}
