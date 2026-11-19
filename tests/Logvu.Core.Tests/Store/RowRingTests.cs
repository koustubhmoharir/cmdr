using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;

namespace Logvu.Core.Tests.Store;

public sealed class RowRingTests
{
    /// <summary>The order of the keys in these tests: by generation, then by offset.</summary>
    private static int Compare(RowKey a, RowKey b) =>
        a.Generation != b.Generation ? a.Generation.CompareTo(b.Generation) : a.Offset.CompareTo(b.Offset);

    private static RowKey K(int generation, long offset) => new(generation, offset);

    private static EventRow Row(int generation, long offset, string preview = "") =>
        new(K(generation, offset), new EventExtent(offset, offset + 10, offset, false, false), null, null, LogLevel.Info, null, preview, false, false, false);

    private static RowKey[] Keys(IEnumerable<EventRow> rows) => rows.Select(row => row.Key).ToArray();

    private static string[] Previews(IEnumerable<EventRow> rows) => rows.Select(row => row.Preview).ToArray();

    [Fact]
    public void Constructor_rejects_a_capacity_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RowRing(0, Compare));
    }

    [Fact]
    public void Empty_ring_has_no_rows_and_covers_nothing()
    {
        var ring = new RowRing(3, Compare);

        Assert.Equal(3, ring.Capacity);
        Assert.Equal(0, ring.Count);
        Assert.Null(ring.Oldest);
        Assert.Null(ring.Newest);
        Assert.False(ring.CoversNewest);
        Assert.Empty(ring.Snapshot());
        Assert.False(ring.TryNewest(1, out _, out _));
        Assert.False(ring.TryOlder(K(1, 0), 1, out _, out _));
        Assert.False(ring.TryNewer(K(1, 0), 1, out _, out _));
    }

    [Fact]
    public void Append_evicts_the_oldest_row_at_capacity()
    {
        var ring = new RowRing(3, Compare);

        ring.Append(Row(1, 0));
        ring.Append(Row(1, 10));
        ring.Append(Row(1, 20));
        Assert.True(ring.CoversNewest);
        Assert.Equal(3, ring.Count);
        Assert.Equal(K(1, 0), ring.Oldest);

        ring.Append(Row(1, 30));
        ring.Append(Row(1, 40));

        Assert.Equal(3, ring.Count);
        Assert.Equal(K(1, 20), ring.Oldest);
        Assert.Equal(K(1, 40), ring.Newest);
        Assert.Equal(new[] { K(1, 20), K(1, 30), K(1, 40) }, Keys(ring.Snapshot()));
        Assert.False(ring.TryOlder(K(1, 10), 1, out _, out _), "an evicted key is not in the ring");
        Assert.True(ring.TryOlder(K(1, 30), 5, out IReadOnlyList<EventRow> older, out bool atStart));
        Assert.Equal(new[] { K(1, 20) }, Keys(older));
        Assert.True(atStart);
    }

    [Fact]
    public void Appending_the_newest_key_again_replaces_that_row()
    {
        var ring = new RowRing(3, Compare);
        ring.Append(Row(1, 0, "first"));
        IReadOnlyList<EventRow> before = ring.Snapshot();

        ring.Append(Row(1, 0, "grown"));

        Assert.Equal(1, ring.Count);
        Assert.Equal(K(1, 0), ring.Newest);
        Assert.Equal(new[] { "grown" }, Previews(ring.Snapshot()));
        Assert.NotSame(before, ring.Snapshot());
    }

    [Fact]
    public void Appending_an_older_key_that_is_present_replaces_it_in_place()
    {
        var ring = new RowRing(3, Compare);
        ring.Append(Row(1, 0, "a"));
        ring.Append(Row(1, 10, "b"));
        ring.Append(Row(1, 20, "c"));

        ring.Append(Row(1, 10, "b2"));

        Assert.Equal(3, ring.Count);
        Assert.Equal(new[] { K(1, 0), K(1, 10), K(1, 20) }, Keys(ring.Snapshot()));
        Assert.Equal(new[] { "a", "b2", "c" }, Previews(ring.Snapshot()));
    }

    [Fact]
    public void Replacing_a_present_key_does_not_evict_anything()
    {
        var ring = new RowRing(2, Compare);
        ring.Append(Row(1, 0, "a"));
        ring.Append(Row(1, 10, "b"));

        ring.Append(Row(1, 0, "a2"));

        Assert.Equal(new[] { "a2", "b" }, Previews(ring.Snapshot()));
        Assert.Equal(K(1, 0), ring.Oldest);
    }

    [Fact]
    public void Append_rejects_a_row_older_than_the_newest_row()
    {
        var ring = new RowRing(3, Compare);
        ring.Append(Row(1, 0));
        ring.Append(Row(1, 10));

        Assert.Throws<ArgumentException>(() => ring.Append(Row(1, 5)));
        Assert.Throws<ArgumentException>(() => ring.Append(Row(0, 99)));

        Assert.Equal(new[] { K(1, 0), K(1, 10) }, Keys(ring.Snapshot()));
    }

    [Fact]
    public void Append_rejects_an_evicted_key_as_out_of_order()
    {
        var ring = new RowRing(2, Compare);
        ring.Append(Row(1, 0));
        ring.Append(Row(1, 10));
        ring.Append(Row(1, 20));

        Assert.Throws<ArgumentException>(() => ring.Append(Row(1, 0)));
        Assert.Equal(new[] { K(1, 10), K(1, 20) }, Keys(ring.Snapshot()));
    }

    [Fact]
    public void Prepend_fills_only_the_free_space_with_the_rows_next_to_the_ring()
    {
        var ring = new RowRing(4, Compare);
        ring.Append(Row(2, 0));
        ring.Append(Row(2, 10));

        ring.PrependOlder(new[] { Row(1, 0), Row(1, 10), Row(1, 20), Row(1, 30) });

        Assert.Equal(4, ring.Count);
        Assert.Equal(new[] { K(1, 20), K(1, 30), K(2, 0), K(2, 10) }, Keys(ring.Snapshot()));
        Assert.Equal(K(1, 20), ring.Oldest);
        Assert.Equal(K(2, 10), ring.Newest);
    }

    [Fact]
    public void Prepend_into_a_full_ring_changes_nothing_and_keeps_the_snapshot()
    {
        var ring = new RowRing(2, Compare);
        ring.Append(Row(1, 0));
        ring.Append(Row(1, 10));
        IReadOnlyList<EventRow> before = ring.Snapshot();

        ring.PrependOlder(new[] { Row(0, 0) });

        Assert.Equal(new[] { K(1, 0), K(1, 10) }, Keys(ring.Snapshot()));
        Assert.Same(before, ring.Snapshot());
    }

    [Fact]
    public void Prepend_on_an_empty_ring_keeps_the_newest_rows_and_covers_the_newest()
    {
        var ring = new RowRing(2, Compare);

        ring.PrependOlder(new[] { Row(1, 0), Row(1, 10), Row(1, 20), Row(1, 30) });

        Assert.True(ring.CoversNewest);
        Assert.Equal(new[] { K(1, 20), K(1, 30) }, Keys(ring.Snapshot()));
        Assert.True(ring.TryNewest(2, out IReadOnlyList<EventRow> newest, out bool atStart));
        Assert.Equal(new[] { K(1, 20), K(1, 30) }, Keys(newest));
        Assert.False(atStart);
    }

    [Fact]
    public void Prepend_of_no_rows_into_an_empty_ring_records_that_the_newest_rows_are_known_to_be_none()
    {
        var ring = new RowRing(2, Compare);

        ring.PrependOlder(Array.Empty<EventRow>());

        Assert.True(ring.CoversNewest);
        Assert.Equal(0, ring.Count);
        Assert.True(ring.TryNewest(3, out IReadOnlyList<EventRow> rows, out bool atStart));
        Assert.Empty(rows);
        Assert.True(atStart);
    }

    [Fact]
    public void Prepend_rejects_rows_that_are_not_oldest_first_or_not_older_than_the_ring()
    {
        var ring = new RowRing(4, Compare);
        ring.Append(Row(1, 20));

        Assert.Throws<ArgumentException>(() => ring.PrependOlder(new[] { Row(1, 10), Row(1, 0) }));
        Assert.Throws<ArgumentException>(() => ring.PrependOlder(new[] { Row(1, 20) }));
        Assert.Throws<ArgumentException>(() => ring.PrependOlder(new[] { Row(1, 30) }));
        Assert.Equal(new[] { K(1, 20) }, Keys(ring.Snapshot()));
    }

    [Fact]
    public void Warm_up_across_negative_sequence_numbers_keeps_lookups_and_order()
    {
        var ring = new RowRing(3, Compare);
        ring.Append(Row(1, 20));
        ring.PrependOlder(new[] { Row(1, 0), Row(1, 10) });

        Assert.Equal(new[] { K(1, 0), K(1, 10), K(1, 20) }, Keys(ring.Snapshot()));
        Assert.True(ring.TryNewer(K(1, 0), 5, out IReadOnlyList<EventRow> newer, out bool atEnd));
        Assert.Equal(new[] { K(1, 10), K(1, 20) }, Keys(newer));
        Assert.True(atEnd);
    }

    [Fact]
    public void TryOlder_serves_rows_older_than_an_anchor_and_reports_the_ring_start()
    {
        var ring = new RowRing(4, Compare);
        foreach (long offset in new long[] { 0, 10, 20, 30 })
        {
            ring.Append(Row(1, offset));
        }

        Assert.True(ring.TryOlder(K(1, 30), 1, out IReadOnlyList<EventRow> one, out bool atStart));
        Assert.Equal(new[] { K(1, 20) }, Keys(one));
        Assert.False(atStart);

        Assert.True(ring.TryOlder(K(1, 20), 2, out IReadOnlyList<EventRow> two, out atStart));
        Assert.Equal(new[] { K(1, 0), K(1, 10) }, Keys(two));
        Assert.False(atStart);

        Assert.True(ring.TryOlder(K(1, 20), 3, out IReadOnlyList<EventRow> short3, out atStart));
        Assert.Equal(new[] { K(1, 0), K(1, 10) }, Keys(short3));
        Assert.True(atStart);

        Assert.True(ring.TryOlder(K(1, 0), 2, out IReadOnlyList<EventRow> none, out atStart));
        Assert.Empty(none);
        Assert.True(atStart);
    }

    [Fact]
    public void TryOlder_with_an_anchor_that_is_not_in_the_ring_is_not_served()
    {
        var ring = new RowRing(4, Compare);
        ring.Append(Row(1, 10));
        ring.Append(Row(1, 20));

        Assert.False(ring.TryOlder(K(1, 5), 1, out _, out _));
        Assert.False(ring.TryOlder(K(1, 15), 1, out _, out _));
        Assert.False(ring.TryOlder(K(2, 0), 1, out _, out _));
    }

    [Fact]
    public void TryNewer_serves_rows_newer_than_an_anchor_and_reports_the_ring_end()
    {
        var ring = new RowRing(4, Compare);
        foreach (long offset in new long[] { 0, 10, 20, 30 })
        {
            ring.Append(Row(1, offset));
        }

        Assert.True(ring.TryNewer(K(1, 0), 2, out IReadOnlyList<EventRow> two, out bool atEnd));
        Assert.Equal(new[] { K(1, 10), K(1, 20) }, Keys(two));
        Assert.False(atEnd);

        Assert.True(ring.TryNewer(K(1, 0), 5, out IReadOnlyList<EventRow> rest, out atEnd));
        Assert.Equal(new[] { K(1, 10), K(1, 20), K(1, 30) }, Keys(rest));
        Assert.True(atEnd);

        Assert.True(ring.TryNewer(K(1, 30), 1, out IReadOnlyList<EventRow> none, out atEnd));
        Assert.Empty(none);
        Assert.True(atEnd);

        Assert.False(ring.TryNewer(K(1, 25), 1, out _, out _));
    }

    [Fact]
    public void TryNewest_serves_the_newest_rows_once_the_ring_covers_them()
    {
        var ring = new RowRing(3, Compare);
        ring.Append(Row(1, 0));
        ring.Append(Row(1, 10));
        ring.Append(Row(1, 20));
        ring.Append(Row(1, 30));

        Assert.True(ring.TryNewest(2, out IReadOnlyList<EventRow> two, out bool atStart));
        Assert.Equal(new[] { K(1, 20), K(1, 30) }, Keys(two));
        Assert.False(atStart);

        Assert.True(ring.TryNewest(3, out IReadOnlyList<EventRow> all, out atStart));
        Assert.Equal(new[] { K(1, 10), K(1, 20), K(1, 30) }, Keys(all));
        Assert.False(atStart);

        Assert.True(ring.TryNewest(5, out IReadOnlyList<EventRow> more, out atStart));
        Assert.Equal(new[] { K(1, 10), K(1, 20), K(1, 30) }, Keys(more));
        Assert.True(atStart);

        Assert.Throws<ArgumentOutOfRangeException>(() => ring.TryNewest(0, out _, out _));
    }

    [Fact]
    public void Range_queries_reject_a_count_below_one()
    {
        var ring = new RowRing(3, Compare);
        ring.Append(Row(1, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => ring.TryOlder(K(1, 0), 0, out _, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.TryNewer(K(1, 0), 0, out _, out _));
    }

    [Fact]
    public void Snapshot_is_the_same_instance_until_the_ring_changes()
    {
        var ring = new RowRing(3, Compare);
        ring.Append(Row(1, 0));
        ring.Append(Row(1, 10));

        IReadOnlyList<EventRow> first = ring.Snapshot();
        Assert.Same(first, ring.Snapshot());
        Assert.Equal(new[] { K(1, 0), K(1, 10) }, Keys(first));

        ring.PrependOlder(Array.Empty<EventRow>());
        Assert.Same(first, ring.Snapshot());

        ring.Append(Row(1, 10, "grown"));
        IReadOnlyList<EventRow> replaced = ring.Snapshot();
        Assert.NotSame(first, replaced);
        Assert.Equal(new[] { "", "grown" }, Previews(replaced));
        Assert.Same(replaced, ring.Snapshot());

        ring.Append(Row(1, 20));
        IReadOnlyList<EventRow> appended = ring.Snapshot();
        Assert.NotSame(replaced, appended);
        Assert.Equal(new[] { K(1, 0), K(1, 10), K(1, 20) }, Keys(appended));

        // An earlier snapshot is never changed by later changes.
        Assert.Equal(new[] { K(1, 0), K(1, 10) }, Keys(first));
        Assert.Equal(new[] { "", "" }, Previews(first));
    }
}
