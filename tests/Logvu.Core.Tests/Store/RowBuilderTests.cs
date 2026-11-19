using System.Text;
using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Tests.Config;
using Logvu.Core.Tests.Detail;
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
        (EventRow row, LogEvent ev, _) = RowBuilder.Build(stream, member.Scheme, member.Generation, extent, new ParseContext(TimeZoneInfo.Utc));
        return (row, ev);
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
            RowBuilder.Build(stream, member.Scheme, member.Generation, new EventExtent(0, stream.Length, stream.Length, false, false), new ParseContext(TimeZoneInfo.Utc)));
    }

    private const string SlogPrefix = "{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"INFO\",\"msg\":\"ok\",";

    private const string SlogEvent =
        "{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"INFO\",\"msg\":\"ok\",\"status\":503,\"req\":{\"path\":\"/api\",\"tags\":[\"a\",\"b\"]},\"err\":{\"code\":\"E1\"},\"ok\":true}";

    /// <summary>The columns of the given paths, each with the default heading and width.</summary>
    private static ColumnSpec[] Columns(params string[] paths) => paths.Select(path => new ColumnSpec(path)).ToArray();

    /// <summary>Builds the row of one event text with the given scheme and columns, as the store does for a read event.</summary>
    private static EventRow RowOf(string text, ILogScheme scheme, IReadOnlyList<ColumnSpec> columns)
    {
        var extent = new EventExtent(0, Encoding.UTF8.GetByteCount(text), Encoding.UTF8.GetByteCount(text), false, false);
        (EventRow row, _) = RowBuilder.BuildFromText(text, scheme, 1, extent, new ParseContext(TimeZoneInfo.Utc, columns));
        return row;
    }

    [Fact]
    public void Json_column_values_are_scalars_as_text_and_objects_and_arrays_as_raw_json()
    {
        EventRow row = RowOf(SlogEvent, BuiltInSchemes.SlogJson, Columns("status", "req.path", "req.tags[1]", "req", "req.tags", "ok", "err.code", "msg"));

        Assert.Equal(
            new string?[] { "503", "/api", "b", "{\"path\":\"/api\",\"tags\":[\"a\",\"b\"]}", "[\"a\",\"b\"]", "true", "E1", "ok" },
            row.Columns);
    }

    [Fact]
    public void Json_column_value_missing_at_any_step_is_null()
    {
        EventRow row = RowOf(SlogEvent, BuiltInSchemes.SlogJson, Columns("nope", "req.missing", "req.path.x", "req.tags[5]", "status.x", "status[0]"));

        Assert.Equal(new string?[] { null, null, null, null, null, null }, row.Columns);
    }

    [Fact]
    public void Json_column_value_keeps_only_the_first_line_of_a_multi_line_string()
    {
        const string text = SlogPrefix + "\"detail\":\"line one\\nline two\"}";

        EventRow row = RowOf(text, BuiltInSchemes.SlogJson, Columns("detail"));

        Assert.Equal(new string?[] { "line one" }, row.Columns);
    }

    [Theory]
    [InlineData(255, 255)]
    [InlineData(256, 256)]
    [InlineData(257, 256)]
    [InlineData(300, 256)]
    public void Column_value_is_cut_to_256_characters(int length, int expectedLength)
    {
        string value = new string('v', length);
        string text = SlogPrefix + "\"big\":\"" + value + "\"}";

        EventRow row = RowOf(text, BuiltInSchemes.SlogJson, Columns("big"));

        Assert.Equal(RowBuilder.ColumnCap, 256);
        Assert.Equal(value[..expectedLength], row.Columns[0]);
    }

    [Fact]
    public void Column_value_cut_does_not_separate_a_surrogate_pair()
    {
        // The pair occupies characters 255 and 256: a 256-character cut would keep a lone high surrogate.
        string split = new string('x', 255) + "\U0001F600" + "tail";
        // The pair occupies characters 254 and 255: a 256-character cut is already clean.
        string whole = new string('x', 254) + "\U0001F600" + "tail";

        EventRow first = RowOf(SlogPrefix + "\"big\":\"" + split + "\"}", BuiltInSchemes.SlogJson, Columns("big"));
        EventRow second = RowOf(SlogPrefix + "\"big\":\"" + whole + "\"}", BuiltInSchemes.SlogJson, Columns("big"));

        Assert.Equal(new string('x', 255), first.Columns[0]);
        Assert.Equal(new string('x', 254) + "\U0001F600", second.Columns[0]);
    }

    [Fact]
    public void Text_column_values_are_the_named_fields_of_the_header()
    {
        string text = Line(T0, "ERROR", "boom").TrimEnd('\n');

        // The message is a field of a text event, so a column named message has its value.
        EventRow row = RowOf(text, BuiltInSchemes.Log4NetCore, Columns("thread", "logger", "message", "level", "missing", "Thread"));

        Assert.Equal(new string?[] { "1", "SheetKraft.Engine", "boom", "ERROR", null, null }, row.Columns);
    }

    [Fact]
    public void A_JSON_column_named_message_gives_the_event_message_when_the_object_has_no_message_member()
    {
        string text = """{"time":"2026-11-18T10:20:31.007+05:30","level":"ERROR","msg":"request failed","err":{"message":"db down"}}""";

        EventRow row = RowOf(text, BuiltInSchemes.SlogJson, Columns("message", "err.message", "missing"));

        Assert.Equal(new string?[] { "request failed: db down", "db down", null }, row.Columns);
    }

    [Fact]
    public void Raw_event_has_null_column_values()
    {
        const string broken = "{\"time\":\"2026-11-18T09:00:01Z\",\"level\":\"ERR";

        EventRow row = RowOf(broken, BuiltInSchemes.SlogJson, Columns("status", "level"));

        Assert.True(row.IsRaw);
        Assert.Equal(new string?[] { null, null }, row.Columns);
    }

    [Fact]
    public void Without_columns_every_row_shares_one_empty_list()
    {
        EventRow json = RowOf(SlogEvent, BuiltInSchemes.SlogJson, Array.Empty<ColumnSpec>());
        EventRow text = RowOf(Line(T0, "ERROR", "boom").TrimEnd('\n'), BuiltInSchemes.Log4NetCore, Array.Empty<ColumnSpec>());

        Assert.Empty(json.Columns);
        Assert.Empty(text.Columns);
        Assert.Same(json.Columns, text.Columns);
    }

    [Fact]
    public void Header_rows_have_no_column_values()
    {
        string text = Line(T0, "ERROR", "boom").TrimEnd('\n');
        var extent = new EventExtent(0, Encoding.UTF8.GetByteCount(text), Encoding.UTF8.GetByteCount(text), false, false);

        EventRow? head = RowBuilder.BuildHead(text, BuiltInSchemes.Log4NetCore, 1, extent, new ParseContext(TimeZoneInfo.Utc, Columns("thread")));

        Assert.NotNull(head);
        Assert.Empty(head.Columns);
    }

    /// <summary>The preview of one event text built with the given scheme and message fields, as the store does for a read event.</summary>
    private static string PreviewOf(string text, ILogScheme scheme, params string[] messageFields)
    {
        var extent = new EventExtent(0, Encoding.UTF8.GetByteCount(text), Encoding.UTF8.GetByteCount(text), false, false);
        (EventRow row, _) = RowBuilder.BuildFromText(text, scheme, 1, extent, new ParseContext(TimeZoneInfo.Utc, messageFields: messageFields));
        return row.Preview;
    }

    [Fact]
    public void Message_fields_follow_the_first_line_in_configuration_order_with_two_spaces_before_the_first()
    {
        Assert.Equal("ok  status=503 req.path=/api", PreviewOf(SlogEvent, BuiltInSchemes.SlogJson, "status", "req.path"));
        Assert.Equal("ok  req.path=/api status=503", PreviewOf(SlogEvent, BuiltInSchemes.SlogJson, "req.path", "status"));
    }

    [Fact]
    public void Message_fields_skip_missing_paths()
    {
        Assert.Equal("ok  status=503", PreviewOf(SlogEvent, BuiltInSchemes.SlogJson, "nope", "status", "req.missing", "status.x"));
    }

    [Fact]
    public void Message_fields_take_nested_and_indexed_paths()
    {
        Assert.Equal("ok  err.code=E1 req.tags[1]=b", PreviewOf(SlogEvent, BuiltInSchemes.SlogJson, "err.code", "req.tags[1]"));
    }

    [Fact]
    public void Message_field_object_and_array_values_are_compact_json()
    {
        Assert.Equal(
            "ok  req={\"path\":\"/api\",\"tags\":[\"a\",\"b\"]} req.tags=[\"a\",\"b\"] ok=true",
            PreviewOf(SlogEvent, BuiltInSchemes.SlogJson, "req", "req.tags", "ok"));
    }

    [Fact]
    public void Message_field_named_message_shows_nothing_without_a_json_message_member()
    {
        Assert.Equal("ok", PreviewOf(SlogEvent, BuiltInSchemes.SlogJson, "message"));
    }

    [Fact]
    public void Message_field_named_message_shows_a_json_message_member()
    {
        string text = SlogPrefix + "\"message\":\"from the object\"}";

        Assert.Equal("ok  message=from the object", PreviewOf(text, BuiltInSchemes.SlogJson, "message"));
    }

    [Fact]
    public void Message_fields_follow_the_first_line_of_a_multi_line_message()
    {
        string text = "{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"INFO\",\"msg\":\"first\\nsecond\",\"status\":503}";

        EventRow row = RowOf(text, BuiltInSchemes.SlogJson, Array.Empty<ColumnSpec>());
        string preview = PreviewOf(text, BuiltInSchemes.SlogJson, "status");

        Assert.Equal("first  status=503", preview);
        Assert.DoesNotContain('\n', preview);
        Assert.Equal("first", row.Preview);
    }

    [Fact]
    public void Message_fields_of_an_empty_message_have_no_leading_spaces()
    {
        string text = "{\"time\":\"2026-11-18T09:00:00Z\",\"level\":\"INFO\",\"msg\":\"\",\"status\":503,\"req\":{\"path\":\"/api\"}}";

        Assert.Equal("status=503 req.path=/api", PreviewOf(text, BuiltInSchemes.SlogJson, "status", "req.path"));
    }

    [Fact]
    public void Message_field_values_keep_only_the_first_line()
    {
        string text = SlogPrefix + "\"detail\":\"line one\\nline two\"}";

        Assert.Equal("ok  detail=line one", PreviewOf(text, BuiltInSchemes.SlogJson, "detail"));
    }

    [Fact]
    public void Message_fields_are_cut_with_the_whole_preview_at_the_cap()
    {
        string message = new string('m', 490);
        string text = SlogPrefix.Replace("\"msg\":\"ok\"", "\"msg\":\"" + message + "\"") + "\"status\":503,\"req\":{\"path\":\"/api/x/y/z\"}}";

        string preview = PreviewOf(text, BuiltInSchemes.SlogJson, "status", "req.path");

        Assert.Equal(RowBuilder.PreviewCap, preview.Length);
        Assert.Equal((message + "  status=503 req.path=/api/x/y/z")[..RowBuilder.PreviewCap], preview);
    }

    [Fact]
    public void A_message_at_the_cap_leaves_no_room_for_message_fields()
    {
        string message = new string('m', RowBuilder.PreviewCap);
        string text = SlogPrefix.Replace("\"msg\":\"ok\"", "\"msg\":\"" + message + "\"") + "\"status\":503}";

        Assert.Equal(message, PreviewOf(text, BuiltInSchemes.SlogJson, "status"));
    }

    [Fact]
    public void Message_fields_do_not_change_the_message_or_the_columns()
    {
        var extent = new EventExtent(0, Encoding.UTF8.GetByteCount(SlogEvent), Encoding.UTF8.GetByteCount(SlogEvent), false, false);

        (EventRow row, LogEvent ev) = RowBuilder.BuildFromText(SlogEvent, BuiltInSchemes.SlogJson, 1, extent, new ParseContext(TimeZoneInfo.Utc, Columns("status"), ["status"]));

        Assert.Equal("ok", ev.Message);
        Assert.Equal(new string?[] { "503" }, row.Columns);
        Assert.Equal("ok  status=503", row.Preview);
    }

    [Fact]
    public void Message_fields_leave_a_raw_json_event_unchanged()
    {
        const string broken = "{\"time\":\"2026-11-18T09:00:01Z\",\"level\":\"ERR";

        Assert.Equal(broken, PreviewOf(broken, BuiltInSchemes.SlogJson, "status", "level"));
    }

    [Fact]
    public void Message_fields_leave_a_text_event_unchanged()
    {
        string text = Line(T0, "ERROR", "boom").TrimEnd('\n');

        Assert.Equal("boom", PreviewOf(text, BuiltInSchemes.Log4NetCore, "thread", "logger", "message"));
    }

    [Fact]
    public void Message_fields_apply_to_a_serilog_event()
    {
        string text = DetailRig.JsonEvents("serilog.json")[0];
        var extent = new EventExtent(0, Encoding.UTF8.GetByteCount(text), Encoding.UTF8.GetByteCount(text), false, false);

        (EventRow row, LogEvent ev) = RowBuilder.BuildFromText(
            text,
            BuiltInSchemes.SerilogJson,
            1,
            extent,
            new ParseContext(TimeZoneInfo.Utc, messageFields: ["Properties.SourceContext", "Properties.Missing", "Properties.RunId"]));

        Assert.Equal("Started run 42", ev.Message);
        Assert.Equal("Started run 42  Properties.SourceContext=SampleDotNetApp.Runner Properties.RunId=42", row.Preview);
    }
}
