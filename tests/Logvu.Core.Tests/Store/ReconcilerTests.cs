using System.Globalization;
using System.Text;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using Shared.Core;
using static Logvu.Core.Tests.Store.StoreFixtures;

namespace Logvu.Core.Tests.Store;

public sealed class ReconcilerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    private const long MiB = 1024 * 1024;

    /// <summary>A slog JSON line, as the Go handler writes it: time, level and message.</summary>
    private static string Json(DateTimeOffset time, string level, string message) =>
        $"{{\"time\":\"{time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)}\",\"level\":\"{level}\",\"msg\":\"{message}\"}}\n";

    /// <summary>Log lines whose total length is at least <paramref name="bytes"/>, one event per line, each 10 ms after the last.</summary>
    private static string Backlog(DateTimeOffset start, long bytes)
    {
        var text = new StringBuilder();
        for (int i = 0; text.Length < bytes; i++)
        {
            text.Append(Line(start.AddMilliseconds(i * 10), "INFO", $"backlog {i} {new string('x', 64)}"));
        }

        return text.ToString();
    }

    /// <summary>The keys of every event of a file that starts at or after <paramref name="from"/>, framed as a closed file.</summary>
    private static RowKey[] KeysFrom(Member member, long from) =>
        Extents(member).Where(extent => extent.Start >= from).Select(extent => new RowKey(member.Generation, extent.Start)).ToArray();

    /// <summary>The rotation of the design table: A deleted, B renamed to SheetKraft2.log, C to SheetKraft1.log, D created as SheetKraft.log.</summary>
    private static void Rotate(ReconcilerRig rig, string newest)
    {
        File.Delete(rig.Path("SheetKraft2.log"));
        File.Move(rig.Path("SheetKraft1.log"), rig.Path("SheetKraft2.log"));
        File.Move(rig.Path("SheetKraft.log"), rig.Path("SheetKraft1.log"));
        rig.Write("SheetKraft.log", newest);
    }

    [Fact]
    public void The_first_run_must_rescan_the_directory()
    {
        using var dir = new ConfigTestDirectory();
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);

        Assert.Throws<InvalidOperationException>(() => rig.Run(rescan: false));
    }

    [Fact]
    public void Initial_run_publishes_only_the_live_provisional_row_and_places_the_cursors()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft2.log", Events(T0, "two", 3));
        dir.Write("SheetKraft1.log", Events(T0.AddHours(1), "one", 3));
        dir.Write("SheetKraft.log", Events(T0.AddHours(2), "live", 4));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);

        ChangeBatch batch = rig.Run(rescan: true);

        Assert.True(batch.Initial);
        EventRow row = Assert.Single(batch.Added);
        Assert.True(row.Provisional);
        Assert.Empty(batch.Updated);
        Assert.Empty(batch.Expired);
        Assert.Empty(batch.Gaps);
        Assert.Empty(batch.Errors);
        Assert.False(batch.RingReset);
        Assert.False(batch.MorePending);
        Assert.Null(batch.DirectoryError);

        Member two = rig.Find("SheetKraft2.log");
        Member one = rig.Find("SheetKraft1.log");
        Member active = rig.Find("SheetKraft.log");
        Assert.Equal(new[] { "SheetKraft2.log", "SheetKraft1.log", "SheetKraft.log" }, rig.Catalog.Ordered.Select(m => m.Name));
        Assert.Equal(new FileInfo(two.Path).Length, two.ReadCursor);
        Assert.Equal(new FileInfo(one.Path).Length, one.ReadCursor);
        Assert.Equal(Extents(active).Last().Start, active.ReadCursor);
        Assert.Equal(row.Key, new RowKey(active.Generation, active.ReadCursor));
        Assert.True(active.IsLive);
        Assert.False(one.IsLive);
        Assert.False(two.IsLive);
        Assert.Equal(row.Key, Assert.Single(rig.Ring.Snapshot()).Key);

        Assert.True(rig.Run().IsEmpty);
    }

    [Fact]
    public void Appends_finalize_the_provisional_row_and_add_the_new_events_in_order()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "live", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        EventRow first = Assert.Single(rig.Run(rescan: true).Added);

        rig.Append("SheetKraft.log", Line(T0.AddMinutes(1), "INFO", "next one") + Line(T0.AddMinutes(2), "WARN", "next two"));
        ChangeBatch grown = rig.Run();
        Assert.Equal(first.Key, Assert.Single(grown.Updated).Key);
        Assert.False(grown.Updated[0].Provisional);
        Assert.Equal(new[] { "next one", "next two" }, grown.Added.Select(row => row.Preview));
        Assert.False(grown.Added[0].Provisional);
        Assert.True(grown.Added[1].Provisional);
        RowKey provisional = grown.Added[1].Key;
        Assert.Equal(provisional, new RowKey(rig.Find("SheetKraft.log").Generation, rig.Find("SheetKraft.log").ReadCursor));

        rig.Append("SheetKraft.log", "  continued\n");
        ChangeBatch continued = rig.Run();
        Assert.Empty(continued.Added);
        EventRow grownTail = Assert.Single(continued.Updated);
        Assert.Equal(provisional, grownTail.Key);
        Assert.True(grownTail.Provisional);

        string partial = Line(T0.AddMinutes(3), "INFO", "partial").TrimEnd('\n');
        rig.Append("SheetKraft.log", partial);
        Assert.True(rig.Run().IsEmpty);

        rig.Append("SheetKraft.log", "\n");
        ChangeBatch completed = rig.Run();
        Assert.Equal(new[] { provisional }, ReconcilerRig.Keys(completed.Updated));
        EventRow started = Assert.Single(completed.Added);
        Assert.Equal("partial", started.Preview);
        Assert.True(started.Provisional);

        Assert.Equal(rig.AddedKeys.Count, rig.AddedKeys.Distinct().Count());
    }

    [Fact]
    public void Numbered_rotation_expires_the_deleted_file_relocates_the_next_and_drains_the_active_one()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft2.log", Events(T0, "a", 3));
        dir.Write("SheetKraft1.log", Events(T0.AddHours(1), "b", 3));
        dir.Write("SheetKraft.log", Events(T0.AddHours(2), "c", 4));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        EventRow cProvisional = Assert.Single(rig.Run(rescan: true).Added);
        Member a = rig.Find("SheetKraft2.log");
        Member b = rig.Find("SheetKraft1.log");
        Member c = rig.Find("SheetKraft.log");
        long bCursor = b.ReadCursor;

        // Written to C after the initial run, and not yet read: one complete event, then a continuation line, then a last event.
        rig.Append(
            "SheetKraft.log",
            Line(T0.AddHours(3), "INFO", "late one") + "  late continuation\n" + Line(T0.AddHours(3).AddSeconds(10), "WARN", "late two"));
        Rotate(rig, Events(T0.AddHours(4), "d", 2));

        ChangeBatch batch = rig.Run(rescan: true);

        Assert.Equal(new[] { a.Generation }, batch.Expired);
        Assert.Empty(batch.Gaps);
        Assert.Equal(new[] { cProvisional.Key }, ReconcilerRig.Keys(batch.Updated));
        Assert.Equal(new[] { "late one", "late two", "d event 0", "d event 1" }, batch.Added.Select(row => row.Preview));

        Member d = rig.Find("SheetKraft.log");
        Assert.Equal(new[] { c.Generation, c.Generation, d.Generation, d.Generation }, batch.Added.Select(row => row.Key.Generation));
        Assert.True(d.Generation > c.Generation);
        Assert.False(batch.Added[1].Provisional);
        Assert.True(batch.Added[3].Provisional);
        Assert.Equal(batch.Added[3].Key.Offset, d.ReadCursor);
        Assert.True(d.IsLive);
        Assert.False(c.IsLive);

        Assert.Same(b, rig.Find("SheetKraft2.log"));
        Assert.Equal(rig.Path("SheetKraft2.log"), b.Path);
        Assert.Equal(bCursor, b.ReadCursor);

        RowPage history = new HistoryReader(rig.Config).ReadOlder(
            rig.Catalog.Snapshot(), new RowKey(c.Generation, 0), 100, null, null, CancellationToken.None);
        Assert.Equal(3, history.Rows.Count);
        Assert.All(history.Rows, row => Assert.Equal(b.Generation, row.Key.Generation));
        Assert.Empty(history.Errors);

        Assert.Equal(rig.AddedKeys.Count, rig.AddedKeys.Distinct().Count());
    }

    [Fact]
    public void A_backlog_on_a_backup_keeps_draining_on_tail_only_runs()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft1.log", Events(T0, "a", 3));
        dir.Write("SheetKraft.log", Events(T0.AddHours(2), "c", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);
        Member backup = rig.Find("SheetKraft1.log");

        rig.Append("SheetKraft1.log", Backlog(T0.AddHours(1), 9 * MiB));
        Assert.True(rig.Run(rescan: true).MorePending);
        long cursor = backup.ReadCursor;

        // Tail-only runs keep reading the backup while it has observed bytes unread, until it is done.
        ChangeBatch next = rig.Run();
        Assert.True(backup.ReadCursor > cursor);
        while (next.MorePending)
        {
            next = rig.Run();
        }

        Assert.Equal(new FileInfo(backup.Path).Length, backup.ReadCursor);
    }

    [Fact]
    public void Expiring_a_member_with_unread_bytes_reports_the_exact_gap()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft2.log", Events(T0, "a", 3));
        dir.Write("SheetKraft.log", Events(T0.AddHours(2), "c", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);
        Member a = rig.Find("SheetKraft2.log");

        // A backlog larger than one run reads. The run stops at its work limit, and what it did not read is the unread part.
        rig.Append("SheetKraft2.log", Backlog(T0.AddHours(1), 9 * MiB));
        // A finished backup's growth is found by a rescan (tail-only runs do not open it).
        ChangeBatch partial = rig.Run(rescan: true);
        Assert.True(partial.MorePending);
        long lengthBefore = new FileInfo(a.Path).Length;
        long cursorBefore = a.ReadCursor;
        Assert.True(cursorBefore < lengthBefore);

        File.Delete(rig.Path("SheetKraft2.log"));
        File.Move(rig.Path("SheetKraft.log"), rig.Path("SheetKraft1.log"));
        rig.Write("SheetKraft.log", Events(T0.AddHours(3), "d", 2));

        ChangeBatch batch = rig.Run(rescan: true);

        Assert.Equal(new[] { a.Generation }, batch.Expired);
        RetentionGap gap = Assert.Single(batch.Gaps);
        Assert.Equal(a.Generation, gap.Generation);
        Assert.Equal("SheetKraft2.log", gap.Name);
        Assert.Equal(lengthBefore - cursorBefore, gap.LostBytes);
    }

    [Fact]
    public void Two_rotations_between_two_runs_publish_every_surviving_event_exactly_once()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft2.log", Events(T0, "a", 3));
        dir.Write("SheetKraft1.log", Events(T0.AddHours(1), "b", 3));
        dir.Write("SheetKraft.log", Events(T0.AddHours(2), "c", 4));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        EventRow cProvisional = Assert.Single(rig.Run(rescan: true).Added);

        Rotate(rig, Events(T0.AddHours(3), "d", 3));
        // Second rotation, before any run: B is deleted, C takes SheetKraft2.log, D takes SheetKraft1.log, and E is created.
        File.Delete(rig.Path("SheetKraft2.log"));
        File.Move(rig.Path("SheetKraft1.log"), rig.Path("SheetKraft2.log"));
        File.Move(rig.Path("SheetKraft.log"), rig.Path("SheetKraft1.log"));
        rig.Write("SheetKraft.log", Events(T0.AddHours(4), "e", 2));

        ChangeBatch batch = rig.Run(rescan: true);

        Member d = rig.Find("SheetKraft1.log");
        Member e = rig.Find("SheetKraft.log");
        Assert.Equal(new[] { cProvisional.Key }, ReconcilerRig.Keys(batch.Updated));
        Assert.True(e.IsLive);

        // C's provisional event was published by the initial run and is not new; D and E were not read before this run, so every one of their
        // events is new.
        var expected = new HashSet<RowKey>(KeysFrom(d, 0).Concat(KeysFrom(e, 0)))
        {
            cProvisional.Key,
        };
        var published = rig.AddedKeys;
        Assert.Equal(published.Count, published.Distinct().Count());
        Assert.True(expected.SetEquals(published), "The published keys differ from the surviving events.");
    }

    [Fact]
    public void Runs_without_changes_are_empty_and_a_rescan_repeats_nothing()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft1.log", Events(T0, "b", 2));
        dir.Write("SheetKraft.log", Events(T0.AddHours(1), "c", 3));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);

        Assert.True(rig.Run().IsEmpty);
        Assert.True(rig.Run(rescan: true).IsEmpty);
        Assert.True(rig.Run(rescan: true).IsEmpty);
    }

    [Fact]
    public void Truncating_the_active_file_expires_its_generation_and_reads_the_new_content_from_zero()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "old", 4));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);
        Member old = rig.Find("SheetKraft.log");

        rig.Write("SheetKraft.log", Events(T0.AddHours(1), "new", 2));
        ChangeBatch batch = rig.Run(rescan: true);

        Member fresh = rig.Find("SheetKraft.log");
        Assert.NotEqual(old.Generation, fresh.Generation);
        Assert.Equal(new[] { old.Generation }, batch.Expired);
        Assert.Empty(batch.Gaps);
        Assert.Empty(batch.Updated);
        Assert.Equal(new[] { "new event 0", "new event 1" }, batch.Added.Select(row => row.Preview));
        Assert.Equal(new[] { fresh.Generation, fresh.Generation }, batch.Added.Select(row => row.Key.Generation));
        Assert.True(batch.Added[1].Provisional);
        Assert.True(fresh.IsLive);
    }

    /// <summary>
    /// A file deleted and a new file created under the same name in one run. On ext4 the new file may reuse the deleted file's inode number;
    /// its birth time then tells the two apart, so the old generation expires and nothing of the new file is read as the old one's.
    /// </summary>
    [Fact]
    public void A_file_replaced_under_the_same_name_expires_the_old_generation_and_adds_the_new_content()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "old", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);
        Member old = rig.Find("SheetKraft.log");

        File.Delete(rig.Path("SheetKraft.log"));
        rig.Write("SheetKraft.log", Events(T0.AddHours(1), "new", 5));
        ChangeBatch batch = rig.Run(rescan: true);

        Member fresh = rig.Find("SheetKraft.log");
        Assert.NotEqual(old.Generation, fresh.Generation);
        Assert.Equal(new[] { old.Generation }, batch.Expired);
        Assert.Equal(new[] { "new event 0", "new event 1", "new event 2", "new event 3", "new event 4" }, batch.Added.Select(row => row.Preview));
        Assert.Equal(Enumerable.Repeat(fresh.Generation, 5), batch.Added.Select(row => row.Key.Generation));
        Assert.True(batch.Added[4].Provisional);
        Assert.True(fresh.IsLive);
    }

    [Fact]
    public void Dated_files_without_an_active_file_make_the_newest_live_and_drain_the_previous_one()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write(
            "log20261118.json",
            Json(T0, "INFO", "first") + Json(T0.AddSeconds(1), "WARN", "second") + Json(T0.AddSeconds(2), "ERROR", "unterminated").TrimEnd('\n'));
        ReconcilerRig rig = ReconcilerRig.Dated(dir);
        Assert.Empty(rig.Run(rescan: true).Added);
        Member previous = rig.Find("log20261118.json");
        Assert.True(previous.IsLive);

        dir.Write("log20261119.json", Json(T0.AddDays(1), "INFO", "next one") + Json(T0.AddDays(1).AddSeconds(1), "INFO", "next two"));
        ChangeBatch batch = rig.Run(rescan: true);

        Member next = rig.Find("log20261119.json");
        Assert.Equal(new[] { "log20261118.json", "log20261119.json" }, rig.Catalog.Ordered.Select(m => m.Name));
        Assert.Equal(new[] { "unterminated", "next one", "next two" }, batch.Added.Select(row => row.Preview));
        Assert.Equal(new[] { previous.Generation, next.Generation, next.Generation }, batch.Added.Select(row => row.Key.Generation));
        Assert.All(batch.Added, row => Assert.False(row.Provisional));
        Assert.Empty(batch.Updated);
        Assert.Empty(batch.Expired);
        Assert.False(previous.IsLive);
        Assert.True(next.IsLive);
        Assert.Equal(new FileInfo(previous.Path).Length, previous.ReadCursor);
    }

    [Fact]
    public void An_empty_active_file_after_rotation_stays_pending_until_its_first_line()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "old", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        EventRow provisional = Assert.Single(rig.Run(rescan: true).Added);
        Member old = rig.Find("SheetKraft.log");

        File.Move(rig.Path("SheetKraft.log"), rig.Path("SheetKraft1.log"));
        rig.Write("SheetKraft.log", string.Empty);
        ChangeBatch rotated = rig.Run(rescan: true);

        Assert.Empty(rotated.Added);
        Assert.Empty(rotated.Updated);
        Member next = rig.Find("SheetKraft.log");
        Assert.Equal(MemberState.Pending, next.State);
        Assert.DoesNotContain(next, rig.Catalog.Ordered);
        Assert.True(rig.Run(rescan: true).IsEmpty);

        rig.Append("SheetKraft.log", Line(T0.AddHours(1), "INFO", "first line"));
        ChangeBatch first = rig.Run(rescan: true);

        Assert.Equal(new[] { provisional.Key }, ReconcilerRig.Keys(first.Updated));
        EventRow added = Assert.Single(first.Added);
        Assert.Equal("first line", added.Preview);
        Assert.True(added.Provisional);
        Assert.False(old.IsLive);
        Assert.True(next.IsLive);
    }

    [Fact]
    public void A_missing_directory_is_an_empty_set_until_it_appears()
    {
        using var dir = new ConfigTestDirectory();
        ReconcilerRig rig = ReconcilerRig.NumberedIn(dir, "logs");

        ChangeBatch empty = rig.Run(rescan: true);

        Assert.True(empty.Initial);
        Assert.True(empty.IsEmpty);
        Assert.Null(empty.DirectoryError);

        dir.Write("logs/SheetKraft1.log", Events(T0, "one", 3));
        dir.Write("logs/SheetKraft.log", Events(T0.AddHours(1), "live", 2));
        ChangeBatch found = rig.Run(rescan: true);

        Assert.False(found.Initial);
        Assert.Equal(5, found.Added.Count);
        Assert.True(found.Added[4].Provisional);
        Assert.Equal(new[] { "SheetKraft1.log", "SheetKraft.log" }, rig.Catalog.Ordered.Select(m => m.Name));
        Assert.True(rig.Find("SheetKraft.log").IsLive);
    }

    [Fact]
    public void A_late_write_to_an_earlier_member_resets_the_ring_and_its_rows_are_still_reported()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft1.log", Events(T0, "old", 3));
        dir.Write("SheetKraft.log", Events(T0.AddHours(1), "live", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);

        rig.Append("SheetKraft.log", Line(T0.AddHours(2), "INFO", "newer one") + Line(T0.AddHours(2).AddSeconds(5), "INFO", "newer two"));
        ChangeBatch newer = rig.Run();
        Assert.False(newer.RingReset);

        Member old = rig.Find("SheetKraft1.log");
        rig.Append("SheetKraft1.log", Line(T0.AddMinutes(20), "ERROR", "late write") + "  late stack\n");
        // A finished backup is only re-checked by a rescan; a tail-only run does not open it.
        Assert.Empty(rig.Run().Added);
        ChangeBatch late = rig.Run(rescan: true);

        Assert.True(late.RingReset);
        EventRow row = Assert.Single(late.Added);
        Assert.Equal("late write", row.Preview);
        Assert.Equal(old.Generation, row.Key.Generation);
        // The reset ring is warmed with the newest history, which includes the late row, and then the live provisional row.
        RowKey[] ringKeys = ReconcilerRig.Keys(rig.Ring.Snapshot());
        Assert.Contains(row.Key, ringKeys);
        Member live = rig.Find("SheetKraft.log");
        Assert.Equal(new RowKey(live.Generation, live.ReadCursor), ringKeys[^1]);
    }

    [Fact]
    public void A_transient_identity_failure_is_retried_and_the_new_file_is_found_in_the_same_run()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0.AddHours(1), "live", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);

        dir.Write("SheetKraft1.log", Events(T0, "old", 2));
        string path = rig.Path("SheetKraft1.log");
        int calls = 0;
        rig.Reconciler.IdentityOf = p => p == path && calls++ < 2 ? null : FileIdentity.TryGet(p);

        ChangeBatch batch = rig.Run(rescan: true);

        Assert.Equal(3, calls);
        Assert.Empty(batch.Errors);
        Assert.Empty(batch.Expired);
        Member found = rig.Find("SheetKraft1.log");
        Assert.Equal(new[] { found.Generation, found.Generation }, batch.Added.Select(row => row.Key.Generation));
    }

    [Fact]
    public void A_member_whose_path_keeps_failing_its_identity_read_is_not_expired()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "live", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);
        Member active = rig.Find("SheetKraft.log");

        string path = rig.Path("SheetKraft.log");
        rig.Reconciler.IdentityOf = p => p == path ? null : FileIdentity.TryGet(p);
        rig.Append("SheetKraft.log", Line(T0.AddHours(1), "INFO", "after") + Line(T0.AddHours(1).AddSeconds(1), "INFO", "after two"));

        ChangeBatch batch = rig.Run(rescan: true);

        Assert.Empty(batch.Expired);
        Assert.Empty(batch.Errors);
        Assert.Null(batch.DirectoryError);
        Assert.Equal(new[] { "after", "after two" }, batch.Added.Select(row => row.Preview));
        Assert.Single(batch.Updated);
        Assert.True(active.IsLive);
        Assert.Equal(MemberState.Dated, active.State);
    }

    [Fact]
    public void A_backlog_is_published_over_several_runs_without_skips_or_repeats()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "live", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);
        Member active = rig.Find("SheetKraft.log");
        long historyEnd = rig.Batches[0].Added.Single().Key.Offset;

        rig.Append("SheetKraft.log", Backlog(T0.AddHours(1), 10 * MiB));

        int runs = 0;
        bool morePending;
        do
        {
            ChangeBatch batch = rig.Run();
            runs++;
            morePending = batch.MorePending;
            Assert.True(runs < 100, "The backlog is not drained.");
        }
        while (morePending);

        Assert.True(runs >= 3, "Ten megabytes at four megabytes per run takes at least three runs.");
        RowKey[] expected = KeysFrom(active, historyEnd);
        Assert.Equal(expected.Length, rig.AddedKeys.Count);
        Assert.Equal(expected.Length, rig.AddedKeys.Distinct().Count());
        Assert.True(new HashSet<RowKey>(expected).SetEquals(rig.AddedKeys));
        Assert.True(rig.Run().IsEmpty);
    }

    [Fact]
    public void An_unterminated_last_line_of_a_rotated_file_is_accepted_when_it_is_drained()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "live", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        EventRow provisional = Assert.Single(rig.Run(rescan: true).Added);

        rig.Append("SheetKraft.log", Line(T0.AddHours(1), "INFO", "partial").TrimEnd('\n'));
        Assert.True(rig.Run().IsEmpty);

        File.Move(rig.Path("SheetKraft.log"), rig.Path("SheetKraft1.log"));
        rig.Write("SheetKraft.log", Events(T0.AddHours(2), "next", 1));
        ChangeBatch batch = rig.Run(rescan: true);

        EventRow finalized = Assert.Single(batch.Updated);
        Assert.Equal(provisional.Key, finalized.Key);
        Assert.False(finalized.Provisional);

        Member drained = rig.Find("SheetKraft1.log");
        Assert.Equal(new[] { "partial", "next event 0" }, batch.Added.Select(row => row.Preview));
        EventRow partial = batch.Added[0];
        Assert.False(partial.Provisional);
        Assert.Equal(new FileInfo(drained.Path).Length, partial.Extent.End);
        Assert.True(batch.Added[1].Provisional);
    }

    [Fact]
    public void A_rewrite_in_place_with_content_longer_than_the_read_cursor_starts_a_new_generation()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "old", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);
        Member old = rig.Find("SheetKraft.log");

        // The same file, rewritten in place (log4net with appendToFile=false), and longer than what was read: its identity is unchanged.
        rig.Write("SheetKraft.log", Events(T0.AddHours(1), "new", 6));
        ChangeBatch batch = rig.Run(rescan: true);

        Member fresh = rig.Find("SheetKraft.log");
        Assert.NotEqual(old.Generation, fresh.Generation);
        Assert.Equal(new[] { old.Generation }, batch.Expired);
        Assert.Empty(batch.Gaps);
        Assert.Empty(batch.Updated);
        Assert.Equal(Enumerable.Range(0, 6).Select(i => $"new event {i}"), batch.Added.Select(row => row.Preview));
        Assert.Equal(Enumerable.Repeat(fresh.Generation, 6), batch.Added.Select(row => row.Key.Generation));
        Assert.True(batch.Added[5].Provisional);
    }

    [Fact]
    public void Truncating_a_file_with_unread_bytes_reports_them_as_a_gap()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Events(T0, "old", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);
        Member active = rig.Find("SheetKraft.log");

        // A backlog that one run does not finish: what is left is unread when the file is truncated.
        rig.Append("SheetKraft.log", Backlog(T0.AddHours(1), 9 * MiB));
        Assert.True(rig.Run().MorePending);
        long lengthBefore = new FileInfo(active.Path).Length;
        long cursorBefore = active.ReadCursor;
        Assert.True(cursorBefore < lengthBefore);

        rig.Write("SheetKraft.log", Events(T0.AddHours(3), "new", 2));
        ChangeBatch batch = rig.Run(rescan: true);

        Assert.Equal(new[] { active.Generation }, batch.Expired);
        RetentionGap gap = Assert.Single(batch.Gaps);
        Assert.Equal(lengthBefore - cursorBefore, gap.LostBytes);
        Assert.Equal(new[] { "new event 0", "new event 1" }, batch.Added.Select(row => row.Preview));
    }

    [Fact]
    public void After_a_reset_the_ring_holds_the_newest_history_contiguously_followed_by_the_live_provisional_row()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft1.log", Events(T0, "old", 3));
        dir.Write("SheetKraft.log", Events(T0.AddHours(1), "live", 2));
        ReconcilerRig rig = ReconcilerRig.Numbered(dir);
        rig.Run(rescan: true);
        rig.Append("SheetKraft.log", Line(T0.AddHours(2), "INFO", "newer one") + Line(T0.AddHours(2).AddSeconds(5), "INFO", "newer two"));
        rig.Run();

        rig.Append("SheetKraft1.log", Line(T0.AddMinutes(20), "ERROR", "late write") + "  late stack\n");
        // A finished backup is only re-checked by a rescan; a tail-only run does not open it.
        Assert.Empty(rig.Run().Added);
        ChangeBatch late = rig.Run(rescan: true);
        Assert.True(late.RingReset);

        // The history the reader sees, then the live provisional row: exactly what the ring must hold, in that order.
        CatalogSnapshot snapshot = rig.Catalog.Snapshot();
        RowKey[] history = new HistoryReader(rig.Config)
            .ReadNewest(snapshot, 2000, filter: null, scannedBytes: null, CancellationToken.None)
            .Rows.Select(row => row.Key).ToArray();
        Member live = rig.Find("SheetKraft.log");
        RowKey[] expected = history.Append(new RowKey(live.Generation, live.ReadCursor)).ToArray();

        Assert.True(rig.Ring.CoversNewest);
        Assert.True(rig.Ring.TryNewest(rig.Ring.Count, out IReadOnlyList<EventRow> rows, out bool atRingStart));
        Assert.False(atRingStart);
        Assert.Equal(expected, rows.Select(row => row.Key));
        Assert.Equal(expected.Length, rig.Ring.Count);
    }

    [Fact]
    public void A_logs_directory_that_disappears_expires_nothing_and_matching_resumes_when_it_returns()
    {
        using var dir = new ConfigTestDirectory();
        ReconcilerRig rig = ReconcilerRig.NumberedIn(dir, "logs");
        dir.Write("logs/SheetKraft1.log", Events(T0, "one", 2));
        dir.Write("logs/SheetKraft.log", Events(T0.AddHours(1), "live", 2));
        rig.Run(rescan: true);

        Directory.Move(dir.Full("logs"), dir.Full("logs-away"));
        ChangeBatch gone = rig.Run(rescan: true);

        Assert.Equal("logs directory not found", gone.DirectoryError);
        Assert.Empty(gone.Expired);
        Assert.Empty(gone.Gaps);
        Assert.Equal(2, rig.Catalog.All.Count(member => member.State != MemberState.Expired));
        Assert.NotEmpty(gone.Errors);

        Directory.Move(dir.Full("logs-away"), dir.Full("logs"));
        ChangeBatch back = rig.Run(rescan: true);

        Assert.Null(back.DirectoryError);
        Assert.Empty(back.Expired);
        Assert.Empty(back.Errors);

        rig.Append("logs/SheetKraft.log", Line(T0.AddHours(2), "INFO", "after return"));
        ChangeBatch grown = rig.Run(rescan: true);
        Assert.Single(grown.Updated);
        EventRow added = Assert.Single(grown.Added);
        Assert.Equal("after return", added.Preview);
    }
}
