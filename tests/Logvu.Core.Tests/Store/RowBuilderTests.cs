using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using static Logvu.Core.Tests.Store.StoreFixtures;

namespace Logvu.Core.Tests.Store;

public sealed class RowBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Writes one file, probes it as the only member, and returns the member.</summary>
    private static Member ProbeFile(ConfigTestDirectory dir, string content, string? format = null)
    {
        dir.Write("SheetKraft.log", content);
        LogvuConfig config = StoreTestConfig.Create(dir, format: format);
        return CatalogOf(dir, config, "SheetKraft.log").Ordered.Single();
    }

    /// <summary>Builds the row of the event at <paramref name="index"/> of the member's file.</summary>
    private static (EventRow Row, LogEvent Event) BuildAt(Member member, int index)
    {
        EventExtent extent = Extents(member)[index];
        using FileStream stream = Open(member.Path);
        return RowBuilder.Build(stream, member, extent, new ParseContext(TimeZoneInfo.Utc));
    }

    private static string Header(string message) => Line(T0, "ERROR", message);

    [Fact]
    public void Preview_is_the_first_line_of_the_message_and_the_message_keeps_the_rest()
    {
        using var dir = new ConfigTestDirectory();
        Member member = ProbeFile(dir, Header("first line") + "System.InvalidOperationException: boom\n   at Run()\n");

        (EventRow row, LogEvent ev) = BuildAt(member, 0);

        Assert.Equal("first line", row.Preview);
        Assert.Equal(new RowKey(member.Generation, 0), row.Key);
        Assert.Equal(LogLevel.Error, row.Level);
        Assert.Equal("ERROR", row.LevelText);
        Assert.Equal(T0, row.Time);
        Assert.False(row.IsRaw);
        Assert.Contains("   at Run()", ev.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', row.Preview);
    }

    [Theory]
    [InlineData(511, 511)]
    [InlineData(512, 512)]
    [InlineData(513, 512)]
    [InlineData(5000, 512)]
    public void Preview_is_cut_to_512_characters(int length, int expectedLength)
    {
        using var dir = new ConfigTestDirectory();
        string message = new string('m', length);
        Member member = ProbeFile(dir, Header(message) + "   at Run()\n");

        EventRow row = BuildAt(member, 0).Row;

        Assert.Equal(expectedLength, row.Preview.Length);
        Assert.Equal(message[..expectedLength], row.Preview);
    }

    [Fact]
    public void Preview_cut_does_not_separate_a_surrogate_pair()
    {
        using var dir = new ConfigTestDirectory();
        // The pair occupies characters 511 and 512 of the message: keeping 512 characters would keep a lone high surrogate.
        string split = new string('x', 511) + "\U0001F600" + "tail";
        // The pair occupies characters 512 and 513: a 512-character cut is already clean.
        string whole = new string('x', 512) + "\U0001F600";
        Member member = ProbeFile(dir, Header(split) + "\n" + Header(whole) + "\n");

        EventRow first = BuildAt(member, 0).Row;
        EventRow second = BuildAt(member, 1).Row;

        Assert.Equal(new string('x', 511), first.Preview);
        Assert.False(char.IsHighSurrogate(first.Preview[^1]));
        Assert.Equal(new string('x', 512), second.Preview);
    }

    [Fact]
    public void Raw_row_for_a_malformed_json_line_has_the_line_as_preview_and_no_time()
    {
        using var dir = new ConfigTestDirectory();
        const string good = "{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"INFO\",\"msg\":\"ok\"}";
        const string broken = "{\"time\":\"2026-11-18T09:00:01Z\",\"level\":\"ERR";
        Member member = ProbeFile(dir, good + "\n" + broken + "\n", format: "slog-json");

        (EventRow goodRow, LogEvent goodEvent) = BuildAt(member, 0);
        (EventRow row, LogEvent ev) = BuildAt(member, 1);

        Assert.False(goodRow.IsRaw);
        Assert.Equal(LogLevel.Info, goodRow.Level);
        Assert.Equal(T0, goodRow.Time);
        Assert.False(goodEvent.IsRaw);

        Assert.True(row.IsRaw);
        Assert.True(ev.IsRaw);
        Assert.NotNull(ev.ParseError);
        Assert.Null(row.Time);
        Assert.Equal(LogLevel.Unknown, row.Level);
        Assert.Equal(broken, row.Preview);
        Assert.Equal(broken, ev.Message);
    }

    [Fact]
    public void Truncated_huge_json_line_is_a_raw_row_with_a_parse_error()
    {
        using var dir = new ConfigTestDirectory();
        string huge = "{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"INFO\",\"msg\":\"" + new string('x', RowBuilder.TextCap + 4096) + "\"}";
        Member member = ProbeFile(dir, huge + "\n", format: "slog-json");

        (EventRow row, LogEvent ev) = BuildAt(member, 0);

        Assert.True(row.IsRaw);
        Assert.NotNull(ev.ParseError);
        Assert.Null(row.Time);
        Assert.Equal(RowBuilder.PreviewCap, row.Preview.Length);
        Assert.StartsWith("{\"time\":", row.Preview, StringComparison.Ordinal);
        Assert.Equal(new RowKey(member.Generation, 0), row.Key);
        Assert.True(row.Extent.End - row.Extent.Start > RowBuilder.TextCap);
    }

    [Fact]
    public void Text_event_longer_than_the_cap_is_parsed_from_its_prefix()
    {
        using var dir = new ConfigTestDirectory();
        string body = string.Concat(Enumerable.Repeat(new string('y', 1023) + "\n", 1100));
        Member member = ProbeFile(dir, Header("long event") + body);

        (EventRow row, LogEvent ev) = BuildAt(member, 0);

        Assert.False(row.IsRaw);
        Assert.Equal(LogLevel.Error, row.Level);
        Assert.Equal(T0, row.Time);
        Assert.Equal("long event", row.Preview);
        Assert.StartsWith("long event\n", ev.Message, StringComparison.Ordinal);
        Assert.True(ev.Message.Length <= RowBuilder.TextCap);
    }

    [Fact]
    public void Orphan_and_provisional_flags_come_from_the_extent()
    {
        using var dir = new ConfigTestDirectory();
        Member member = ProbeFile(dir, "prose before the first header\n" + Header("after"));

        EventRow orphan = BuildAt(member, 0).Row;
        EventRow dated = BuildAt(member, 1).Row;

        Assert.True(orphan.Orphan);
        Assert.True(orphan.IsRaw);
        Assert.Equal(new RowKey(member.Generation, 0), orphan.Key);
        Assert.Equal("prose before the first header", orphan.Preview);
        Assert.Null(orphan.Time);
        Assert.False(dated.Orphan);
        Assert.False(dated.Provisional);
    }

    [Fact]
    public void Build_rejects_a_member_without_a_scheme()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("SheetKraft.log", Header("no scheme"));
        LogvuConfig config = StoreTestConfig.Create(dir);
        Catalog catalog = new(config);
        Member member = catalog.Add(dir.Full("SheetKraft.log"), "some-identity");
        using FileStream stream = Open(member.Path);

        Assert.Throws<InvalidOperationException>(() =>
            RowBuilder.Build(stream, member, new EventExtent(0, stream.Length, stream.Length, false, false), new ParseContext(TimeZoneInfo.Utc)));
    }
}
