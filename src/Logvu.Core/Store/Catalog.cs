using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Shared.Core;

namespace Logvu.Core.Store;

/// <summary>
/// The members of a configured set, and their timeline order. Owned by the store's single worker: it is not thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordering.</b> The timeline holds the members in state <see cref="MemberState.UnknownTime"/> and
/// <see cref="MemberState.Dated"/>, and the members that expired from one of those states, in this order: the unknown-time group
/// first, ordered by basename (ordinal); then the dated members, by first time as an instant. An expired member keeps the group and
/// the first time it had, so it stays where it was and history can still be traversed through it. Members with equal times are ordered as follows. When the configuration sets an active file, a
/// rotation name (<c>&lt;activeStem&gt;&lt;n&gt;&lt;activeExt&gt;</c>, with the active file itself as 0) is older the higher <c>n</c>
/// is, and rotation names come before the other names. Any other pair is ordered by basename (ordinal). The order is therefore a
/// single lexicographic key: time, then rotation group, then rotation number, then basename, then generation. That makes it a
/// total order, which a sort requires; a pairwise rule that mixed rotation and other names would not be transitive.
/// </para>
/// <para>
/// <b>No stale order.</b> The order is cached, keyed by a version counter. Every change that can move a member (its state, its
/// first time, its basename) goes through a <see cref="Member"/> method, and each of those bumps the version. Reading
/// <see cref="Ordered"/> or <see cref="Compare"/> rebuilds the order whenever the version has moved. A caller cannot change a
/// member's position without the next read seeing it, and a list returned by <see cref="Ordered"/> is never modified afterwards.
/// </para>
/// </remarks>
public sealed class Catalog
{
    private readonly LogvuConfig _config;
    private readonly List<Member> _members = [];
    private readonly Dictionary<int, Member> _byGeneration = [];
    private int _lastGeneration;

    private long _version;
    private long _orderVersion = -1;
    private ReadOnlyCollection<Member> _ordered = new(Array.Empty<Member>());
    private Dictionary<int, int> _positions = [];
    private CatalogSnapshot? _snapshot;

    /// <summary>Creates an empty catalog for a configured set.</summary>
    /// <param name="config">The configuration. Its active file decides how equal times are ordered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <c>null</c>.</exception>
    public Catalog(LogvuConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    /// <summary>Every member, expired ones included, in generation order. This is a live view: do not enumerate it while adding members.</summary>
    public IEnumerable<Member> All => _members.AsReadOnly();

    /// <summary>
    /// The members in the timeline, oldest first: the unknown-time group, then the dated members. Expired members that were in one of
    /// those groups are included at their place. Pending members are not in it. The list is a snapshot that is never changed
    /// afterwards; the members in it are live and may change.
    /// </summary>
    public IReadOnlyList<Member> Ordered
    {
        get
        {
            EnsureOrder();
            return _ordered;
        }
    }

    /// <summary>
    /// The timeline as an immutable snapshot, for readers on other threads. The snapshot is cached: while the catalog's version is
    /// unchanged, the same instance is returned. Any change to a member bumps the version, so the next call returns a new snapshot.
    /// </summary>
    /// <returns>The snapshot of the current version.</returns>
    /// <exception cref="InvalidOperationException">A member in the timeline has no scheme. Members in the timeline always have one.</exception>
    public CatalogSnapshot Snapshot()
    {
        if (_snapshot is null || _snapshot.Version != _version)
        {
            _snapshot = new CatalogSnapshot(_version, BuildInfos());
        }

        return _snapshot;
    }

    /// <summary>
    /// Adds a member for a file that has just been found. It is <see cref="MemberState.Pending"/> until it is probed.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="identity">The identity of the file.</param>
    /// <returns>The new member, whose generation is greater than that of every member added before.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> or <paramref name="identity"/> is <c>null</c> or empty.</exception>
    /// <exception cref="InvalidOperationException">A member that has not expired already has this identity.</exception>
    public Member Add(string path, string identity)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(identity);

        Member? held = FindByIdentity(identity);
        if (held is not null)
        {
            throw new InvalidOperationException(
                $"Identity {identity} is already held by generation {held.Generation}. Expire that member before adding the file again.");
        }

        _lastGeneration = checked(_lastGeneration + 1);
        var member = new Member(this, _lastGeneration, path, identity);
        _members.Add(member);
        _byGeneration.Add(member.Generation, member);
        MarkChanged();
        return member;
    }

    /// <summary>The member with the given generation, expired ones included.</summary>
    /// <returns>The member, or <c>null</c> when no member has that generation.</returns>
    public Member? FindByGeneration(int generation) =>
        _byGeneration.TryGetValue(generation, out Member? member) ? member : null;

    /// <summary>
    /// The member that is not expired and has the given identity. An expired member does not count: a file system may reuse the
    /// identity of a deleted file, and the new file is then a new member.
    /// </summary>
    /// <returns>The member, or <c>null</c> when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="identity"/> is <c>null</c>.</exception>
    public Member? FindByIdentity(string identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        foreach (Member member in _members)
        {
            if (member.State != MemberState.Expired && string.Equals(member.Identity, identity, StringComparison.Ordinal))
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>The member before <paramref name="member"/> in the timeline, which is older.</summary>
    /// <returns>The member, or <c>null</c> at the start of the timeline or when <paramref name="member"/> is not in it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <c>null</c>.</exception>
    public Member? Previous(Member member)
    {
        ArgumentNullException.ThrowIfNull(member);
        IReadOnlyList<Member> order = Ordered;
        if (!_positions.TryGetValue(member.Generation, out int position) || position == 0)
        {
            return null;
        }

        return order[position - 1];
    }

    /// <summary>The member after <paramref name="member"/> in the timeline, which is newer.</summary>
    /// <returns>The member, or <c>null</c> at the end of the timeline or when <paramref name="member"/> is not in it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <c>null</c>.</exception>
    public Member? Next(Member member)
    {
        ArgumentNullException.ThrowIfNull(member);
        IReadOnlyList<Member> order = Ordered;
        if (!_positions.TryGetValue(member.Generation, out int position) || position + 1 >= order.Count)
        {
            return null;
        }

        return order[position + 1];
    }

    /// <summary>
    /// Compares two events in the timeline: by the timeline position of their members, then by offset. Events of one member are
    /// compared by offset.
    /// </summary>
    /// <returns>A negative number, zero, or a positive number as <paramref name="a"/> is before, the same as, or after <paramref name="b"/>.</returns>
    /// <exception cref="ArgumentException">A key's generation is not in the timeline order (its member is pending, or unknown to the catalog).</exception>
    public int Compare(RowKey a, RowKey b)
    {
        int positionA = PositionOf(a.Generation, nameof(a));
        int positionB = PositionOf(b.Generation, nameof(b));
        return positionA != positionB ? positionA.CompareTo(positionB) : a.Offset.CompareTo(b.Offset);
    }

    /// <summary>Called by a member whenever a change could move it in the timeline.</summary>
    internal void MarkChanged() => _version++;

    private List<MemberInfo> BuildInfos()
    {
        IReadOnlyList<Member> order = Ordered;
        var infos = new List<MemberInfo>(order.Count);
        foreach (Member member in order)
        {
            ILogScheme scheme = member.Scheme
                ?? throw new InvalidOperationException($"Generation {member.Generation} is in the timeline without a scheme.");
            infos.Add(new MemberInfo(
                member.Generation,
                member.Path,
                member.Name,
                member.Identity,
                scheme,
                member.TimelineState,
                member.IsReadable,
                member.ReadCursor,
                member.FirstTime,
                member.IsLive,
                member.Diagnostic));
        }

        return infos;
    }

    private int PositionOf(int generation, string parameter)
    {
        EnsureOrder();
        if (!_positions.TryGetValue(generation, out int position))
        {
            throw new ArgumentException($"Generation {generation} is not in the timeline order.", parameter);
        }

        return position;
    }

    private void EnsureOrder()
    {
        if (_orderVersion == _version)
        {
            return;
        }

        var ordered = _members
            .Where(member => member.TimelineState is MemberState.UnknownTime or MemberState.Dated)
            .ToList();
        ordered.Sort(CompareTimeline);

        var positions = new Dictionary<int, int>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            positions.Add(ordered[i].Generation, i);
        }

        _ordered = ordered.AsReadOnly();
        _positions = positions;
        _orderVersion = _version;
    }

    private int CompareTimeline(Member a, Member b)
    {
        if (a.TimelineState != b.TimelineState)
        {
            return a.TimelineState == MemberState.UnknownTime ? -1 : 1;
        }

        if (a.TimelineState == MemberState.Dated)
        {
            int byTime = a.FirstTime!.Value.CompareTo(b.FirstTime!.Value);
            if (byTime != 0)
            {
                return byTime;
            }

            int byRotation = CompareRotation(a.Name, b.Name);
            if (byRotation != 0)
            {
                return byRotation;
            }
        }

        int byName = string.CompareOrdinal(a.Name, b.Name);
        return byName != 0 ? byName : a.Generation.CompareTo(b.Generation);
    }

    /// <summary>
    /// Orders two names of equal time: rotation names first, the higher number first. A name that is not a rotation name is ordered
    /// after all of them and by basename, so the comparison is consistent with the rest of the timeline order.
    /// </summary>
    private int CompareRotation(string a, string b)
    {
        BigInteger? numberA = RotationNumber(a);
        BigInteger? numberB = RotationNumber(b);
        if (numberA is null || numberB is null)
        {
            return (numberA is null ? 1 : 0) - (numberB is null ? 1 : 0);
        }

        return numberB.Value.CompareTo(numberA.Value);
    }

    /// <summary>
    /// The rotation number of a name: 0 for the active file itself, <c>n</c> for <c>&lt;activeStem&gt;&lt;n&gt;&lt;activeExt&gt;</c>, and
    /// <c>null</c> for any other name, or when no active file is configured.
    /// </summary>
    private BigInteger? RotationNumber(string name)
    {
        if (_config.ActiveFile is not { } active)
        {
            return null;
        }

        StringComparer comparer = PathComparison.Comparer;
        if (comparer.Equals(name, active))
        {
            return BigInteger.Zero;
        }

        string stem = System.IO.Path.GetFileNameWithoutExtension(active);
        string extension = System.IO.Path.GetExtension(active);
        if (name.Length <= stem.Length + extension.Length
            || !comparer.Equals(name[..stem.Length], stem)
            || !comparer.Equals(name[^extension.Length..], extension))
        {
            return null;
        }

        ReadOnlySpan<char> digits = name.AsSpan(stem.Length, name.Length - stem.Length - extension.Length);
        foreach (char c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return null;
            }
        }

        return BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
