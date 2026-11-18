using Logvu.Core.Config;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;

namespace Logvu.Core.Tests.Store;

public sealed class CatalogTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 18, 10, 20, 30, TimeSpan.Zero);

    private static Catalog NewCatalog(ConfigTestDirectory dir, string? activeFile = null, string prefix = "SheetKraft", string? pattern = null) =>
        new(pattern is null
            ? StoreTestConfig.Create(dir, activeFile: activeFile)
            : StoreTestConfig.Create(dir, prefix: prefix, pattern: pattern, activeFile: activeFile));

    private static Member Add(Catalog catalog, ConfigTestDirectory dir, string name) =>
        catalog.Add(dir.Full(name), "id-" + name);

    private static void SetDated(Member member, DateTimeOffset time) =>
        member.Apply(new ProbeResult(null, MemberState.Dated, 0, time, null, 0));

    private static void SetUnknown(Member member) =>
        member.Apply(new ProbeResult(RawLinesScheme.Instance, MemberState.UnknownTime, 0, null, "no time", 0));

    private static string[] Names(Catalog catalog) => catalog.Ordered.Select(m => m.Name).ToArray();

    [Fact]
    public void Numbered_rotation_with_equal_times_is_ordered_oldest_first()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir, activeFile: "SheetKraft.log");
        foreach (string name in new[] { "SheetKraft1.log", "SheetKraft.log", "SheetKraft2.log" })
        {
            SetDated(Add(catalog, dir, name), T);
        }

        Assert.Equal(new[] { "SheetKraft2.log", "SheetKraft1.log", "SheetKraft.log" }, Names(catalog));
    }

    [Fact]
    public void Rotation_numbers_compare_as_numbers()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir, activeFile: "SheetKraft.log");
        foreach (string name in new[] { "SheetKraft9.log", "SheetKraft.log", "SheetKraft10.log" })
        {
            SetDated(Add(catalog, dir, name), T);
        }

        Assert.Equal(new[] { "SheetKraft10.log", "SheetKraft9.log", "SheetKraft.log" }, Names(catalog));
    }

    [Fact]
    public void Equal_times_without_an_active_file_are_ordered_by_basename()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        foreach (string name in new[] { "SheetKraft2.log", "SheetKraft.log", "SheetKraft1.log" })
        {
            SetDated(Add(catalog, dir, name), T);
        }

        Assert.Equal(new[] { "SheetKraft.log", "SheetKraft1.log", "SheetKraft2.log" }, Names(catalog));
    }

    [Fact]
    public void Distinct_times_override_the_names()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir, activeFile: "SheetKraft.log");
        SetDated(Add(catalog, dir, "SheetKraft.log"), T.AddHours(1));
        SetDated(Add(catalog, dir, "SheetKraft2.log"), T);

        Assert.Equal(new[] { "SheetKraft2.log", "SheetKraft.log" }, Names(catalog));
    }

    [Fact]
    public void Times_with_different_offsets_are_compared_as_instants()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        // 12:00 at +05:30 is 06:30 UTC, which is earlier than 07:00 UTC even though its wall clock reads later.
        SetDated(Add(catalog, dir, "SheetKraft.log"), new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.FromHours(5.5)));
        SetDated(Add(catalog, dir, "SheetKraft1.log"), new DateTimeOffset(2026, 11, 18, 7, 0, 0, TimeSpan.Zero));

        Assert.Equal(new[] { "SheetKraft.log", "SheetKraft1.log" }, Names(catalog));
    }

    [Fact]
    public void Serilog_rolled_names_with_equal_times_are_ordered_by_name()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir, prefix: "log", pattern: @"^log[0-9]{8}(?:_[0-9]{3})?[.]json$");
        SetDated(Add(catalog, dir, "log20261118_001.json"), T);
        SetDated(Add(catalog, dir, "log20261118.json"), T);

        Assert.Equal(new[] { "log20261118.json", "log20261118_001.json" }, Names(catalog));
    }

    [Fact]
    public void Names_that_are_not_rotation_names_follow_rotation_names_at_equal_times()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir, activeFile: "SheetKraft.log");
        // "SheetKraft1x.log" has no number between stem and extension. Ordering it by name against "SheetKraft1.log" and
        // "SheetKraft2.log" would not be transitive with the rotation rule, so it comes after all rotation names.
        foreach (string name in new[] { "SheetKraft1x.log", "SheetKraft1.log", "SheetKraft2.log" })
        {
            SetDated(Add(catalog, dir, name), T);
        }

        Assert.Equal(new[] { "SheetKraft2.log", "SheetKraft1.log", "SheetKraft1x.log" }, Names(catalog));
    }

    [Fact]
    public void Unknown_time_members_come_first_in_basename_order()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        SetDated(Add(catalog, dir, "SheetKraft.log"), T);
        SetUnknown(Add(catalog, dir, "SheetKraft9.log"));
        SetUnknown(Add(catalog, dir, "SheetKraft2.log"));

        Assert.Equal(new[] { "SheetKraft2.log", "SheetKraft9.log", "SheetKraft.log" }, Names(catalog));
        Assert.Equal(MemberState.UnknownTime, catalog.Ordered[0].State);
    }

    [Fact]
    public void Pending_and_expired_members_are_not_ordered()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        Add(catalog, dir, "SheetKraft3.log");
        Member expired = Add(catalog, dir, "SheetKraft2.log");
        SetDated(expired, T);
        expired.Expire();
        Member dated = Add(catalog, dir, "SheetKraft.log");
        SetDated(dated, T);

        Assert.Equal(new[] { dated }, catalog.Ordered);
        Assert.Equal(3, catalog.All.Count());
    }

    [Fact]
    public void Previous_and_Next_stop_at_the_ends_of_the_timeline()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        Member a = Add(catalog, dir, "SheetKraft.log");
        Member b = Add(catalog, dir, "SheetKraft1.log");
        Member c = Add(catalog, dir, "SheetKraft2.log");
        SetDated(a, T);
        SetDated(b, T.AddHours(1));
        SetDated(c, T.AddHours(2));

        Assert.Null(catalog.Previous(a));
        Assert.Same(b, catalog.Next(a));
        Assert.Same(b, catalog.Previous(c));
        Assert.Same(c, catalog.Next(b));
        Assert.Null(catalog.Next(c));
    }

    [Fact]
    public void Previous_and_Next_of_a_member_outside_the_timeline_are_null()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        Member dated = Add(catalog, dir, "SheetKraft.log");
        SetDated(dated, T);
        Member pending = Add(catalog, dir, "SheetKraft1.log");

        Assert.Null(catalog.Previous(pending));
        Assert.Null(catalog.Next(pending));
    }

    [Fact]
    public void Compare_orders_members_by_position_and_events_within_a_member_by_offset()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        Member older = Add(catalog, dir, "SheetKraft.log");
        Member newer = Add(catalog, dir, "SheetKraft1.log");
        SetDated(older, T);
        SetDated(newer, T.AddHours(1));

        // A later offset in an older member still comes before any event of a newer member.
        Assert.True(catalog.Compare(new RowKey(older.Generation, 100), new RowKey(newer.Generation, 0)) < 0);
        Assert.True(catalog.Compare(new RowKey(newer.Generation, 0), new RowKey(older.Generation, 100)) > 0);
        Assert.True(catalog.Compare(new RowKey(older.Generation, 5), new RowKey(older.Generation, 10)) < 0);
        Assert.Equal(0, catalog.Compare(new RowKey(older.Generation, 10), new RowKey(older.Generation, 10)));
    }

    [Fact]
    public void Compare_throws_for_a_generation_outside_the_timeline()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        Member member = Add(catalog, dir, "SheetKraft.log");
        SetDated(member, T);
        Member expired = Add(catalog, dir, "SheetKraft1.log");
        SetDated(expired, T);
        expired.Expire();

        Assert.Throws<ArgumentException>(() => catalog.Compare(new RowKey(member.Generation, 0), new RowKey(expired.Generation, 0)));
        Assert.Throws<ArgumentException>(() => catalog.Compare(new RowKey(999, 0), new RowKey(member.Generation, 0)));
    }

    [Fact]
    public void Order_follows_changes_of_time_name_and_state_and_earlier_snapshots_do_not_change()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        Member alpha = Add(catalog, dir, "Alpha.log");
        Member beta = Add(catalog, dir, "Beta.log");
        SetDated(alpha, T);
        SetDated(beta, T);
        IReadOnlyList<Member> before = catalog.Ordered;
        Assert.Equal(new[] { alpha, beta }, before);

        // A later time moves a member.
        SetDated(alpha, T.AddHours(2));
        Assert.Equal(new[] { "Beta.log", "Alpha.log" }, Names(catalog));

        // A rename changes the basename, which orders members of equal time.
        SetDated(alpha, T);
        Assert.Equal(new[] { "Alpha.log", "Beta.log" }, Names(catalog));
        alpha.Relocate(dir.Full("Zulu.log"));
        Assert.Equal(new[] { "Beta.log", "Zulu.log" }, Names(catalog));
        Assert.Equal(dir.Full("Zulu.log"), alpha.Path);
        Assert.Equal("Zulu.log", alpha.Name);

        // Becoming unknown-time moves a member to the front.
        SetUnknown(beta);
        Assert.Equal(new[] { "Beta.log", "Zulu.log" }, Names(catalog));

        // Expiring removes it.
        alpha.Expire();
        Assert.Equal(new[] { "Beta.log" }, Names(catalog));

        // The list taken before the changes keeps its members and their order; the members themselves are live.
        Assert.Equal(new[] { alpha, beta }, before);
    }

    [Fact]
    public void Generations_are_unique_and_increase_across_adds_and_are_never_reused()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        Member first = Add(catalog, dir, "SheetKraft.log");
        Member second = Add(catalog, dir, "SheetKraft1.log");
        second.Expire();
        Member third = Add(catalog, dir, "SheetKraft2.log");

        Assert.Equal(1, first.Generation);
        Assert.Equal(2, second.Generation);
        Assert.Equal(3, third.Generation);
        Assert.Same(second, catalog.FindByGeneration(2));
        Assert.Null(catalog.FindByGeneration(4));
        Assert.Equal(MemberState.Pending, third.State);
    }

    [Fact]
    public void Identity_is_unique_among_members_that_have_not_expired()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        Member held = catalog.Add(dir.Full("SheetKraft.log"), "same");

        Assert.Throws<InvalidOperationException>(() => catalog.Add(dir.Full("SheetKraft1.log"), "same"));
        Assert.Same(held, catalog.FindByIdentity("same"));
        Assert.Null(catalog.FindByIdentity("other"));
    }

    [Fact]
    public void An_expired_identity_can_be_added_again_and_finds_the_new_member()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        Member old = catalog.Add(dir.Full("SheetKraft.log"), "reused");
        old.Expire();

        Member added = catalog.Add(dir.Full("SheetKraft1.log"), "reused");

        Assert.NotEqual(old.Generation, added.Generation);
        Assert.Same(added, catalog.FindByIdentity("reused"));
    }

    [Fact]
    public void Expired_member_cannot_change_but_can_be_expired_again()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        Member member = Add(catalog, dir, "SheetKraft.log");
        SetDated(member, T);
        member.Expire();

        Assert.Throws<InvalidOperationException>(() => SetDated(member, T));
        Assert.Throws<InvalidOperationException>(() => member.Relocate(dir.Full("Other.log")));
        member.Expire();
        Assert.Equal(MemberState.Expired, member.State);
    }

    [Fact]
    public void Dated_probe_result_needs_a_time()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);
        Member member = Add(catalog, dir, "SheetKraft.log");

        Assert.Throws<ArgumentException>(() => member.Apply(new ProbeResult(null, MemberState.Dated, 0, null, null, 0)));
        Assert.Throws<ArgumentException>(() => member.Apply(new ProbeResult(null, MemberState.Expired, 0, null, null, 0)));
    }

    [Fact]
    public void Add_rejects_an_empty_path_or_identity()
    {
        using var dir = new ConfigTestDirectory();
        Catalog catalog = NewCatalog(dir);

        Assert.Throws<ArgumentException>(() => catalog.Add(string.Empty, "id"));
        Assert.Throws<ArgumentException>(() => catalog.Add(dir.Full("SheetKraft.log"), string.Empty));
    }
}
