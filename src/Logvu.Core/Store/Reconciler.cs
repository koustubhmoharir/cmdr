using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Shared.Core;

namespace Logvu.Core.Store;

/// <summary>
/// The synchronous core of live tailing and rotation handling. Each <see cref="Run"/> reconciles the catalog with the directory and
/// reads what changed into the recent-row ring, and returns the changes as a <see cref="ChangeBatch"/>. It starts no threads and holds
/// no timers or watchers: the store calls it from its single worker. It is not thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// <b>A run.</b> (1) Enumerate the set and take each path's identity, retrying a transient failure. (2) Match known members by identity: a
/// member whose identity is at another path is relocated, keeping its generation and offsets; one whose identity is gone expires, with a
/// retention gap when bytes were observed and not read. Nothing expires when the directory could not be read, or when the member's own path
/// failed its identity read. (3) Check every readable member against its read-through point and its
/// fingerprint: a shorter file is a truncation, and a file whose first bytes differ from the fingerprint is another file (an inode number
/// can be reused by a file re-created in place). Either way the old generation expires, reporting bytes it observed and did not read as a
/// gap, and the identity gets a fresh generation read from 0. (4) Add the identities that are new.
/// (5) Probe every pending member; one that leaves pending is in the timeline. (6) Choose the live member. (7) On the initial run, set
/// every readable member's cursor: the live member's cursor is the start of its provisional event, or its live end; any other member's
/// cursor is its length, so that its content is history. (8) Tail: read the bytes past each readable member's read-through point, in
/// timeline order, and publish the rows. A member that stopped being live is drained with closed framing.
/// </para>
/// <para>
/// <b>Read-through point.</b> A member's events are read through its <see cref="Member.ReadCursor"/>, which is an event boundary. When the
/// member has a provisional row, the read-through point is that row's end, since its cursor is at the row's start. Truncation and retention
/// gaps compare with the read-through point, so a provisional event that was published is not counted as unread.
/// </para>
/// <para>
/// <b>Handles.</b> Each read opens the member's file for one step and disposes it before the next. No handle outlives a step.
/// </para>
/// <para>
/// <b>Work limit.</b> At most <see cref="WorkLimitPerMember"/> bytes of event extents are published for a member in one run. The first
/// event of a run is always published, so one larger event still makes progress. When more remains, the cursor stops at an event boundary,
/// and <see cref="ChangeBatch.MorePending"/> is set; the next run continues from there.
/// </para>
/// <para>
/// <b>Ring.</b> The ring must be built with <see cref="Catalog.Compare"/> as its order. A row that sorts before the ring's newest row and is
/// not in the ring cannot be appended. The ring is then cleared, <see cref="ChangeBatch.RingReset"/> is set, and the rows go on into the empty
/// ring. Every row is still reported in the batch. At the end of such a run the ring is warmed again with the newest history and the live
/// provisional row, so that it holds the newest rows contiguously.
/// </para>
/// <para>
/// <b>Directory.</b> A logs directory that is missing while members are known sets <see cref="ChangeBatch.DirectoryError"/> and expires
/// nothing; the members' reads fail until it is back. A missing directory with no known members is the empty set.
/// </para>
/// <para>
/// <b>Failures.</b> A member that cannot be read in a step is recorded in <see cref="ChangeBatch.Errors"/> and skipped for the rest of that
/// run. An I/O error that happens after some rows of a member were published leaves the cursor after the last published row, so those rows
/// are not published again. In a tail-only run, a path that is not found or names another file is not an error: the file is being rotated,
/// so the run asks for a rescan (<see cref="ChangeBatch.RescanNeeded"/>), which relocates or expires the member.
/// </para>
/// </remarks>
public sealed class Reconciler
{
    /// <summary>The most bytes of event extents published for one member in one run.</summary>
    public const long WorkLimitPerMember = 4L * 1024 * 1024;

    /// <summary>The number of bytes after a member's content start that identify its content.</summary>
    private const int FingerprintBytes = 256;

    /// <summary>The most history rows a reset ring is warmed with.</summary>
    private const int RewarmRows = 2000;

    private readonly LogvuConfig _config;
    private readonly Catalog _catalog;
    private readonly RowRing _ring;
    private readonly ParseContext _context;
    private readonly Dictionary<int, Tracking> _tracking = [];
    private bool _started;
    private int _retryCount = 3;

    /// <summary>Creates a reconciler. Its first <see cref="Run"/> is the initial run.</summary>
    /// <param name="config">The configuration of the set.</param>
    /// <param name="catalog">The catalog the reconciler maintains. It must be empty or hold only members the reconciler added.</param>
    /// <param name="ring">The recent-row ring the reconciler appends to. It must be ordered by <see cref="Catalog.Compare"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public Reconciler(LogvuConfig config, Catalog catalog, RowRing ring)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ring);
        _config = config;
        _catalog = catalog;
        _ring = ring;
        _context = new ParseContext(config.TimeZone, config.Columns);
    }

    /// <summary>The identity of a path, as the rescan takes it. Tests replace it to simulate transient failures.</summary>
    internal Func<string, string?> IdentityOf { get; set; } = FileIdentity.TryGet;

    /// <summary>The delay between two attempts to read a path's identity. Tests set it to zero.</summary>
    internal TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// The number of retries of a path's identity read while the file still exists. Tests may lower it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    internal int RetryCount
    {
        get => _retryCount;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _retryCount = value;
        }
    }

    /// <summary>
    /// Reconciles the catalog with the directory, when <paramref name="rescanDirectory"/> is set, and reads the new bytes of the members.
    /// </summary>
    /// <param name="rescanDirectory">
    /// Whether to enumerate the set and match its files with the members. The first run must rescan: it is the initial run, which
    /// makes the members' existing content history.
    /// </param>
    /// <returns>What changed in this run.</returns>
    /// <exception cref="InvalidOperationException">The first run does not rescan the directory.</exception>
    public ChangeBatch Run(bool rescanDirectory)
    {
        if (!_started && !rescanDirectory)
        {
            throw new InvalidOperationException("The first run must rescan the directory: it is the initial run.");
        }

        var run = new RunChanges(initial: !_started) { Rescan = rescanDirectory };
        _started = true;

        Scan? scan = rescanDirectory ? ScanDirectory(run) : null;
        if (scan is not null)
        {
            MatchKnownMembers(scan, run);
        }

        CheckLengths(run, allMembers: rescanDirectory);

        if (scan is not null)
        {
            AddNewIdentities(scan, run);
        }

        ProbePending(run);
        AssignLive(run);

        if (run.Initial)
        {
            AssignDiscoveryCursors(run);
        }

        TailPass(run);

        if (run.RingReset)
        {
            Rewarm(run);
        }

        return new ChangeBatch(
            run.Initial,
            run.Added,
            run.Updated,
            run.Expired,
            run.Gaps,
            run.CatalogChanged,
            run.RingReset,
            run.Errors,
            run.DirectoryError,
            run.MorePending,
            run.Opened.Where(generation => !run.Failed.Contains(generation)).Order(),
            run.RescanNeeded);
    }

    /// <summary>The files found by a rescan, and the paths whose identity could not be read.</summary>
    private sealed class Scan
    {
        /// <summary>The distinct identities found, each with the first path that names it, in enumeration order.</summary>
        public List<(string Identity, string Path)> Files { get; } = [];

        /// <summary>The identity of every found path, for matching.</summary>
        public Dictionary<string, string> ByIdentity { get; } = new(StringComparer.Ordinal);

        /// <summary>The paths whose identity could not be read this round. Their members are not expired.</summary>
        public HashSet<string> Skipped { get; } = new(PathComparison.Comparer);
    }

    /// <summary>The per-member state the reconciler keeps between runs.</summary>
    private sealed class Tracking
    {
        /// <summary>The length the member had at its last successful open.</summary>
        public long Observed { get; set; }

        /// <summary>The key of the member's provisional row, or <c>null</c> when it has none.</summary>
        public RowKey? Provisional { get; set; }

        /// <summary>The end of the provisional event, which is the read-through point while the event is provisional.</summary>
        public long ProvisionalEnd { get; set; }

        /// <summary>The provisional row as last published, which a warmed ring takes back.</summary>
        public EventRow? ProvisionalRow { get; set; }

        /// <summary>
        /// The first bytes of the content, read when the member was first observed. A later open whose bytes differ means the path's file is
        /// another content stream, even when its identity is the same (an inode number reused by a file re-created in place).
        /// </summary>
        public byte[]? Fingerprint { get; set; }
    }

    /// <summary>What one run changed. Converted to a <see cref="ChangeBatch"/> at the end of the run.</summary>
    private sealed class RunChanges(bool initial)
    {
        public bool Initial { get; } = initial;

        public List<EventRow> Added { get; } = [];

        public List<EventRow> Updated { get; } = [];

        public List<int> Expired { get; } = [];

        public List<RetentionGap> Gaps { get; } = [];

        public List<MemberReadError> Errors { get; } = [];

        /// <summary>The generations that had a read error in this run, or were skipped for a rotation. They are not read again in it.</summary>
        public HashSet<int> Failed { get; } = [];

        /// <summary>The generations whose file was opened in this run, with the identity verified.</summary>
        public HashSet<int> Opened { get; } = [];

        /// <summary>Whether the run is a rescan. A tail-only run does not report a rotation as an error.</summary>
        public bool Rescan { get; init; }

        /// <summary>Whether a tail-only run found a path that no longer names its member's file.</summary>
        public bool RescanNeeded { get; set; }

        public bool CatalogChanged { get; set; }

        public bool RingReset { get; set; }

        public bool MorePending { get; set; }

        public string? DirectoryError { get; set; }
    }

    /// <summary>
    /// Step 1. Enumerates the set and takes the identity of each path, or records why the set cannot be enumerated.
    /// </summary>
    /// <returns>The scan, or <c>null</c> when the directory could not be enumerated.</returns>
    private Scan? ScanDirectory(RunChanges run)
    {
        // A directory that vanished while members are known is a hiccup, such as a share going away: nothing expires until it is back.
        if (!Directory.Exists(_config.LogsDirectory) && _catalog.All.Any(member => member.State != MemberState.Expired))
        {
            run.DirectoryError = "logs directory not found";
            return null;
        }

        IReadOnlyList<string> paths;
        try
        {
            paths = FileSet.Enumerate(_config);
        }
        catch (FileSetException ex)
        {
            run.DirectoryError = ex.Message;
            return null;
        }

        var scan = new Scan();
        foreach (string path in paths)
        {
            string? identity = ReadIdentity(path);
            if (identity is null)
            {
                scan.Skipped.Add(path);
            }
            else if (scan.ByIdentity.TryAdd(identity, path))
            {
                scan.Files.Add((identity, path));
            }
        }

        return scan;
    }

    /// <summary>
    /// The identity of a path. A null result is retried while the file exists, up to <see cref="RetryCount"/> times, <see cref="RetryDelay"/>
    /// apart. A path that still gives none is skipped by the caller.
    /// </summary>
    private string? ReadIdentity(string path)
    {
        string? identity = IdentityOf(path);
        for (int attempt = 0; identity is null && attempt < _retryCount && File.Exists(path); attempt++)
        {
            if (RetryDelay > TimeSpan.Zero)
            {
                Thread.Sleep(RetryDelay);
            }

            identity = IdentityOf(path);
        }

        return identity;
    }

    /// <summary>
    /// Step 2. Relocates the members whose identity is at another path, and expires those whose identity is gone. A member whose own path
    /// was skipped is kept.
    /// </summary>
    private void MatchKnownMembers(Scan scan, RunChanges run)
    {
        var relocations = new List<(Member Member, string Path)>();
        var gone = new List<Member>();
        foreach (Member member in _catalog.All)
        {
            if (member.State == MemberState.Expired)
            {
                continue;
            }

            if (scan.ByIdentity.TryGetValue(member.Identity, out string? path))
            {
                if (!PathComparison.Comparer.Equals(path, member.Path))
                {
                    relocations.Add((member, path));
                }
            }
            else if (!scan.Skipped.Contains(member.Path))
            {
                gone.Add(member);
            }
        }

        foreach ((Member member, string path) in relocations)
        {
            member.Relocate(path);
            run.CatalogChanged = true;
        }

        foreach (Member member in gone)
        {
            Expire(member, run, withGap: true);
        }
    }

    /// <summary>
    /// Expires a member: its bytes past the read-through point are reported as a gap when <paramref name="withGap"/> is set, and it leaves
    /// the tracking table.
    /// </summary>
    private void Expire(Member member, RunChanges run, bool withGap)
    {
        Tracking tracking = TrackingOf(member);
        if (withGap)
        {
            long lost = tracking.Observed - ReadThrough(member, tracking);
            if (lost > 0)
            {
                run.Gaps.Add(new RetentionGap(member.Generation, member.Name, lost));
            }
        }

        member.Expire();
        _tracking.Remove(member.Generation);
        run.Expired.Add(member.Generation);
        run.CatalogChanged = true;
    }

    /// <summary>
    /// Step 3. Checks readable members against their read-through points and fingerprints. A file whose first bytes differ from the
    /// fingerprint is another file, and a shorter file was truncated: either way the member is replaced. A rescan checks every member;
    /// a tail-only run checks only the live member, members with a provisional row and members with observed bytes still unread (a
    /// backlog spread over several runs), so that the frequent tail polls do not open
    /// every backup (each open is a moment in which, on Windows, a producer's delete-and-rename could collide with the reader). A late
    /// write to an otherwise finished backup is therefore found by the next rescan.
    /// </summary>
    private void CheckLengths(RunChanges run, bool allMembers)
    {
        foreach (Member member in _catalog.All.Where(m => m.IsReadable).ToList())
        {
            Tracking tracked = TrackingOf(member);
            if (!allMembers && !member.IsLive && tracked.Provisional is null && tracked.Observed <= ReadThrough(member, tracked))
            {
                continue;
            }

            if (!ObserveMember(member, run, out long length, out bool matches))
            {
                continue;
            }

            Tracking tracking = TrackingOf(member);
            if (!matches || length < ReadThrough(member, tracking))
            {
                Replace(member, run);
            }
            else
            {
                tracking.Observed = length;
            }
        }
    }

    /// <summary>
    /// Expires a member whose file was replaced or truncated, and adds its identity again as a new member, which is read from 0. The bytes
    /// the old member had observed and not read are reported as a gap.
    /// </summary>
    /// <returns>The new member.</returns>
    private Member Replace(Member member, RunChanges run)
    {
        Expire(member, run, withGap: true);
        return _catalog.Add(member.Path, member.Identity);
    }

    /// <summary>Step 4. Adds a member for each identity that no member holds.</summary>
    private void AddNewIdentities(Scan scan, RunChanges run)
    {
        foreach ((string identity, string path) in scan.Files)
        {
            if (_catalog.FindByIdentity(identity) is null)
            {
                _catalog.Add(path, identity);
                run.CatalogChanged = true;
            }
        }
    }

    /// <summary>
    /// Step 5. Probes every pending member. A member whose probe finds an event leaves pending, and takes the probe's state. A pending result
    /// is not applied, so a pending member does not change the catalog from one run to the next. A pending member whose file was replaced
    /// is replaced, and the new member is probed from the same open.
    /// </summary>
    private void ProbePending(RunChanges run)
    {
        foreach (Member member in _catalog.All.Where(m => m.State == MemberState.Pending).ToList())
        {
            try
            {
                if (!TryOpen(member, run, out FileStream? stream))
                {
                    continue;
                }

                using FileStream opened = stream!;
                Member target = CheckFingerprint(opened, TrackingOf(member)) ? member : Replace(member, run);
                ProbeResult result = MemberProbe.Probe(opened, _config, ProbeLive(target));
                TrackingOf(target).Observed = result.Length;
                if (result.State != MemberState.Pending)
                {
                    target.Apply(result);
                    run.CatalogChanged = true;
                }
            }
            catch (IOException ex)
            {
                Fail(member, run, ex.Message);
            }
        }
    }

    /// <summary>
    /// Whether the probe reads a member as one still being written. With no active file every member may be the live one, so every member
    /// is read as live; the probe then holds back an unterminated line, which the drain accepts later.
    /// </summary>
    private bool ProbeLive(Member member) =>
        _config.ActiveFile is null || PathComparison.Comparer.Equals(member.Name, _config.ActiveFile);

    /// <summary>
    /// Step 6. The live member is the one named by the active file when it is in the timeline; otherwise the newest readable member. Every
    /// member's <see cref="Member.IsLive"/> is set to match.
    /// </summary>
    private void AssignLive(RunChanges run)
    {
        Member? live = null;
        if (_config.ActiveFile is { } active)
        {
            live = _catalog.Ordered.LastOrDefault(m => m.IsReadable && PathComparison.Comparer.Equals(m.Name, active));
        }

        live ??= _catalog.Ordered.LastOrDefault(m => m.IsReadable);

        foreach (Member member in _catalog.All)
        {
            bool isLive = ReferenceEquals(member, live);
            if (member.IsLive != isLive)
            {
                member.IsLive = isLive;
                run.CatalogChanged = true;
            }
        }
    }

    /// <summary>
    /// Step 7, initial run only. The live member's cursor is the start of its last event when that event is provisional, otherwise its live
    /// end. Every other member's cursor is its length, so its content is history.
    /// </summary>
    private void AssignDiscoveryCursors(RunChanges run)
    {
        foreach (Member member in _catalog.Ordered)
        {
            if (!member.IsReadable || run.Failed.Contains(member.Generation))
            {
                continue;
            }

            try
            {
                if (!member.IsLive)
                {
                    member.ReadCursor = TrackingOf(member).Observed;
                    continue;
                }

                if (!TryOpen(member, run, out FileStream? stream))
                {
                    continue;
                }

                using FileStream opened = stream!;
                member.ReadCursor = DiscoveryCursor(opened, member.Scheme!);
            }
            catch (IOException ex)
            {
                Fail(member, run, ex.Message);
            }
        }
    }

    /// <summary>
    /// The cursor of a live file that has been read as history: the start of its last event when that event is provisional, else the live
    /// end. The last event is found by framing backward from the live end.
    /// </summary>
    private static long DiscoveryCursor(Stream stream, ILogScheme scheme)
    {
        long dataEnd = EventFramer.DataEnd(stream, live: true);
        foreach (EventExtent last in new EventFramer(stream, scheme, live: true).ReadBackward(dataEnd))
        {
            return last.Provisional ? last.Start : dataEnd;
        }

        return dataEnd;
    }

    /// <summary>
    /// Step 8. Publishes the new bytes of each readable member, in timeline order. A member that is not live is drained with closed
    /// framing. A member's read stops at the work limit and continues in the next run.
    /// </summary>
    private void TailPass(RunChanges run)
    {
        foreach (Member member in _catalog.Ordered)
        {
            if (!member.IsReadable || run.Failed.Contains(member.Generation))
            {
                continue;
            }

            try
            {
                ReadNewEvents(member, run);
            }
            catch (IOException ex)
            {
                Fail(member, run, ex.Message);
            }
        }
    }

    /// <summary>
    /// Reads the bytes past the member's read-through point, if there are any, and publishes its events from its cursor on. A live member
    /// is framed as live; the others as closed. The cursor moves after each published event.
    /// </summary>
    private void ReadNewEvents(Member member, RunChanges run)
    {
        Tracking tracking = TrackingOf(member);
        bool drain = !member.IsLive;
        long readThrough = ReadThrough(member, tracking);
        if (drain && tracking.Provisional is null && tracking.Observed <= readThrough)
        {
            return;
        }

        if (!TryOpen(member, run, out FileStream? stream))
        {
            return;
        }

        using FileStream opened = stream!;
        if (!CheckFingerprint(opened, tracking))
        {
            // Replaced since the last check: the next run's length check replaces the member.
            Fail(member, run, "the file was replaced since it was last checked");
            return;
        }

        if (member.ReadCursor > opened.Length)
        {
            Fail(member, run, "the file is shorter than its read cursor");
            return;
        }

        long end = drain ? opened.Length : EventFramer.DataEnd(opened, live: true);
        if (end <= readThrough && !(drain && tracking.Provisional is not null))
        {
            return;
        }

        var framer = new EventFramer(opened, member.Scheme!, live: member.IsLive);
        long budget = 0;
        foreach (EventExtent extent in framer.ReadForward(member.ReadCursor))
        {
            long size = extent.End - extent.Start;
            if (budget > 0 && budget + size > WorkLimitPerMember)
            {
                run.MorePending = true;
                break;
            }

            (EventRow row, _, _) = RowBuilder.Build(opened, member.Scheme, member.Generation, extent, _context);
            Publish(row, isUpdate: tracking.Provisional == row.Key, run);
            budget += size;

            if (extent.Provisional)
            {
                tracking.Provisional = row.Key;
                tracking.ProvisionalEnd = extent.End;
                tracking.ProvisionalRow = row;
                member.ReadCursor = extent.Start;
            }
            else
            {
                tracking.Provisional = null;
                tracking.ProvisionalRow = null;
                member.ReadCursor = extent.End;
            }
        }
    }

    /// <summary>
    /// Rebuilds the ring after a reset, so that it holds the newest history again, contiguous: the newest history rows, read from the
    /// members' cursors, then the live member's provisional row, which is not history.
    /// </summary>
    private void Rewarm(RunChanges run)
    {
        _ring.Clear();
        RowPage history = new HistoryReader(_config).ReadNewest(
            _catalog.Snapshot(), Math.Min(_ring.Capacity, RewarmRows), filter: null, scannedBytes: null, CancellationToken.None);
        run.Errors.AddRange(history.Errors);
        _ring.PrependOlder(history.Rows);

        Member? live = _catalog.All.FirstOrDefault(member => member.IsLive && member.IsReadable);
        if (live is not null && TrackingOf(live).ProvisionalRow is { } provisional
            && (_ring.Newest is not { } newest || _catalog.Compare(provisional.Key, newest) > 0))
        {
            _ring.Append(provisional);
        }
    }

    /// <summary>
    /// Records a published row, and appends it to the ring. A row that sorts before the ring's newest row and is not in the ring clears the
    /// ring first.
    /// </summary>
    private void Publish(EventRow row, bool isUpdate, RunChanges run)
    {
        (isUpdate ? run.Updated : run.Added).Add(row);
        if (_ring.Count > 0 && !_ring.Contains(row.Key) && _catalog.Compare(row.Key, _ring.Newest!.Value) <= 0)
        {
            _ring.Clear();
            run.RingReset = true;
        }

        _ring.Append(row);
    }

    /// <summary>
    /// The length of a member's file and whether its first bytes still match its fingerprint, read by opening it for one step. A failure is
    /// recorded and the member is skipped for the run.
    /// </summary>
    private bool ObserveMember(Member member, RunChanges run, out long length, out bool matches)
    {
        length = 0;
        matches = true;
        try
        {
            if (!TryOpen(member, run, out FileStream? stream))
            {
                return false;
            }

            using FileStream opened = stream!;
            length = opened.Length;
            matches = CheckFingerprint(opened, TrackingOf(member));
            return true;
        }
        catch (IOException ex)
        {
            Fail(member, run, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Whether the file's first bytes match the member's fingerprint. The fingerprint is taken on the first open that finds content, and a
    /// later open compares as many bytes as both have, then extends a short fingerprint to the bytes now available. A file shorter than
    /// the fingerprint is compared on the bytes it has.
    /// </summary>
    private static bool CheckFingerprint(FileStream stream, Tracking tracking)
    {
        long start = LineReader.ContentStart(stream);
        long available = stream.Length - start;
        if (available <= 0)
        {
            return true;
        }

        if (tracking.Fingerprint is null)
        {
            tracking.Fingerprint = ReadBytes(stream, start, (int)Math.Min(available, FingerprintBytes));
            return true;
        }

        int count = (int)Math.Min(available, tracking.Fingerprint.Length);
        if (!ReadBytes(stream, start, count).AsSpan().SequenceEqual(tracking.Fingerprint.AsSpan(0, count)))
        {
            return false;
        }

        // A fingerprint taken while the file was short grows with it, up to its full size.
        if (tracking.Fingerprint.Length < FingerprintBytes && available > tracking.Fingerprint.Length)
        {
            tracking.Fingerprint = ReadBytes(stream, start, (int)Math.Min(available, FingerprintBytes));
        }

        return true;
    }

    private static byte[] ReadBytes(Stream stream, long start, int count)
    {
        var bytes = new byte[count];
        stream.Position = start;
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// Opens a member's path with its identity verified. A failure is recorded as a read error of the member, except that a tail-only run
    /// that finds the path missing or naming another file records nothing: the member is skipped for the run and a rescan is requested.
    /// </summary>
    private bool TryOpen(Member member, RunChanges run, out FileStream? stream)
    {
        if (MemberFile.TryOpen(member.Path, member.Identity, out stream, out _, out string? error))
        {
            run.Opened.Add(member.Generation);
            return true;
        }

        if (!run.Rescan && error is MemberFile.NotFound or MemberFile.IdentityChanged)
        {
            run.Failed.Add(member.Generation);
            run.RescanNeeded = true;
            return false;
        }

        Fail(member, run, error ?? "the file cannot be opened");
        return false;
    }

    private static void Fail(Member member, RunChanges run, string message)
    {
        run.Errors.Add(new MemberReadError(member.Generation, member.Path, message));
        run.Failed.Add(member.Generation);
    }

    /// <summary>The point through which the member's events have been read: its provisional event's end, or else its cursor.</summary>
    private static long ReadThrough(Member member, Tracking tracking) =>
        tracking.Provisional is null ? member.ReadCursor : tracking.ProvisionalEnd;

    /// <summary>The tracking of a member, created on first use.</summary>
    private Tracking TrackingOf(Member member)
    {
        if (!_tracking.TryGetValue(member.Generation, out Tracking? tracking))
        {
            tracking = new Tracking();
            _tracking.Add(member.Generation, tracking);
        }

        return tracking;
    }
}
