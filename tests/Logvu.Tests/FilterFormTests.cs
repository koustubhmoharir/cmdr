using Logvu.Core.Detail;
using Logvu.Core.Filter;
using Logvu.Core.Parsing;
using Logvu.Tui;
using Shared.Core;

namespace Logvu.Tests;

/// <summary>
/// The filter editor's input rules: times in the config zone, field predicate lines, the spec built from the input, the pre-fill of the
/// editor from a filter, and the predicate a detail line filters on.
/// </summary>
public sealed class FilterFormTests
{
    // UTC+2 with no daylight saving, so the expected instants are easy to read.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test+02", TimeSpan.FromHours(2), "Test+02", "Test+02");

    // 23:30 UTC on the 18th is 01:30 on the 19th in the zone: "today" there is the 19th.
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 23, 30, 0, TimeSpan.Zero);

    private static FilterInput Empty => new(null, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false, false, false, string.Empty);

    [Fact]
    public void A_date_and_minute_is_a_time_in_the_zone()
    {
        DateTimeOffset? time = FilterForm.ParseTime("2026-11-18 10:30", "Time from", Zone, Now);

        Assert.Equal(new DateTimeOffset(2026, 11, 18, 10, 30, 0, TimeSpan.FromHours(2)), time);
    }

    [Fact]
    public void A_date_with_seconds_is_a_time_in_the_zone()
    {
        DateTimeOffset? time = FilterForm.ParseTime("2026-11-18 10:30:15", "Time from", Zone, Now);

        Assert.Equal(new DateTimeOffset(2026, 11, 18, 10, 30, 15, TimeSpan.FromHours(2)), time);
    }

    [Theory]
    [InlineData("10:30")]
    [InlineData("10:30:00")]
    public void A_time_alone_is_today_in_the_zone_not_in_utc(string text)
    {
        DateTimeOffset? time = FilterForm.ParseTime(text, "Time from", Zone, Now);

        Assert.Equal(new DateTimeOffset(2026, 11, 19, 10, 30, 0, TimeSpan.FromHours(2)), time);
    }

    [Fact]
    public void A_blank_time_is_no_time()
    {
        Assert.Null(FilterForm.ParseTime("   ", "Time from", Zone, Now));
    }

    [Theory]
    [InlineData("2026-11-18")]
    [InlineData("tomorrow")]
    [InlineData("2026-13-01 10:00")]
    [InlineData("25:00")]
    public void An_invalid_time_names_its_field(string text)
    {
        FilterInputException error = Assert.Throws<FilterInputException>(() => FilterForm.ParseTime(text, "Time to", Zone, Now));

        Assert.Equal("Time to", error.Field);
        Assert.Contains(text, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Predicate_lines_split_at_the_first_separator_and_keep_the_value_as_typed()
    {
        IReadOnlyList<FieldPredicate> predicates = FilterForm.ParsePredicates("user = alice\n\n  code = 42 \na = b = c\n   \n");

        Assert.Equal(
            [new FieldPredicate("user", "alice"), new FieldPredicate("code", "42 "), new FieldPredicate("a", "b = c")],
            predicates);
    }

    [Fact]
    public void Carriage_return_line_feeds_are_read_as_line_breaks()
    {
        IReadOnlyList<FieldPredicate> predicates = FilterForm.ParsePredicates("a = 1\r\nb = 2");

        Assert.Equal([new FieldPredicate("a", "1"), new FieldPredicate("b", "2")], predicates);
    }

    [Fact]
    public void An_empty_value_is_a_predicate_for_an_empty_field()
    {
        IReadOnlyList<FieldPredicate> predicates = FilterForm.ParsePredicates("a = ");

        Assert.Equal([new FieldPredicate("a", string.Empty)], predicates);
    }

    [Theory]
    [InlineData("a = 1\nnovalue", 2)]
    [InlineData("a = 1\n\n= value", 3)]
    public void A_malformed_line_names_the_field_and_the_line(string text, int line)
    {
        FilterInputException error = Assert.Throws<FilterInputException>(() => FilterForm.ParsePredicates(text));

        Assert.Equal("Field predicates", error.Field);
        Assert.Contains($"Line {line}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blank_predicate_text_has_no_predicates()
    {
        Assert.Empty(FilterForm.ParsePredicates("\n  \n"));
    }

    [Fact]
    public void The_spec_has_each_predicate_the_input_sets_and_trims_the_names()
    {
        var input = Empty with
        {
            MinLevel = LogLevel.Warn,
            Source = "  Orders  ",
            File = " app ",
            From = "2026-11-18 10:00",
            To = "2026-11-18 11:00",
            Text = "timeout ",
            CaseSensitive = true,
            Predicates = "Properties.OrderId = 42",
        };

        FilterSpec spec = FilterForm.Build(input, Zone, Now);

        Assert.Equal(LogLevel.Warn, spec.MinLevel);
        Assert.Equal("Orders", spec.SourceContains);
        Assert.Equal("app", spec.FileContains);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(2)), spec.From);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 11, 0, 0, TimeSpan.FromHours(2)), spec.To);
        Assert.Equal(new SearchQuery("timeout ", true, false, false), spec.Text);
        Assert.Equal([new FieldPredicate("Properties.OrderId", "42")], spec.Fields);
    }

    [Fact]
    public void An_empty_input_builds_an_empty_filter()
    {
        var filter = new EventFilter(FilterForm.Build(Empty, Zone, Now));

        Assert.True(filter.IsEmpty);
    }

    [Fact]
    public void A_regular_expression_that_does_not_compile_names_the_text_field()
    {
        FilterInputException error = Assert.Throws<FilterInputException>(() =>
            FilterForm.Build(Empty with { Text = "(", Regex = true }, Zone, Now));

        Assert.Equal("Text", error.Field);
    }

    [Fact]
    public void A_literal_text_with_regex_characters_is_not_a_regular_expression()
    {
        FilterSpec spec = FilterForm.Build(Empty with { Text = "(", Regex = false }, Zone, Now);

        Assert.Equal(new SearchQuery("(", false, false, false), spec.Text);
    }

    [Fact]
    public void A_time_to_before_time_from_names_the_latest_time()
    {
        FilterInputException error = Assert.Throws<FilterInputException>(() =>
            FilterForm.Build(Empty with { From = "2026-11-18 11:00", To = "2026-11-18 10:00" }, Zone, Now));

        Assert.Equal("Time to", error.Field);
    }

    [Fact]
    public void An_invalid_time_in_the_input_names_its_field()
    {
        FilterInputException error = Assert.Throws<FilterInputException>(() =>
            FilterForm.Build(Empty with { From = "yesterday" }, Zone, Now));

        Assert.Equal("Time from", error.Field);
    }

    [Fact]
    public void The_editor_shows_a_filter_and_builds_the_same_filter_from_what_it_shows()
    {
        FilterSpec spec = new(
            MinLevel: LogLevel.Error,
            SourceContains: "Orders",
            FileContains: "app",
            From: new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(2)),
            To: new DateTimeOffset(2026, 11, 18, 11, 30, 15, TimeSpan.FromHours(2)),
            Text: new SearchQuery("time.*out", false, true, true),
            Fields: [new FieldPredicate("user", "alice"), new FieldPredicate("code", "42")]);
        var active = new EventFilter(spec);

        FilterInput shown = FilterForm.InputOf(active, Zone);
        var rebuilt = new EventFilter(FilterForm.Build(shown, Zone, Now));

        Assert.Equal(LogLevel.Error, shown.MinLevel);
        Assert.Equal("2026-11-18 10:00:00", shown.From);
        Assert.Equal("2026-11-18 11:30:15", shown.To);
        Assert.True(shown.WholeWord);
        Assert.True(shown.Regex);
        Assert.Equal("user = alice\ncode = 42", shown.Predicates);
        Assert.Equal(active.Describe(), rebuilt.Describe());
    }

    [Fact]
    public void An_empty_editor_is_pre_filled_with_nothing()
    {
        FilterInput shown = FilterForm.InputOf(null, Zone);

        Assert.Equal(Empty, shown);
    }

    [Fact]
    public void Extending_a_filter_adds_the_predicate_once_and_keeps_the_rest()
    {
        var current = new EventFilter(new FilterSpec(MinLevel: LogLevel.Warn, Text: new SearchQuery("timeout", false, false, false)));
        var predicate = new FieldPredicate("user", "alice");

        EventFilter once = FilterForm.Extend(current, predicate);
        EventFilter twice = FilterForm.Extend(once, predicate);

        Assert.Equal([predicate], once.Fields);
        Assert.Equal(once.Fields, twice.Fields);
        Assert.Equal(LogLevel.Warn, twice.MinLevel);
        Assert.Equal("timeout", twice.Text!.Value.Text);
    }

    [Fact]
    public void Extending_no_filter_gives_a_filter_with_only_the_predicate()
    {
        EventFilter filter = FilterForm.Extend(null, new FieldPredicate("user", "alice"));

        Assert.Equal([new FieldPredicate("user", "alice")], filter.Fields);
        Assert.Null(filter.MinLevel);
        Assert.Null(filter.Text);
    }

    [Fact]
    public void A_field_line_filters_on_its_name_and_its_value()
    {
        var line = new DetailLine("user: alice", [], LineKind.Field, Indent: 6, NodeId: null, Scope: "user", Value: "alice");

        Assert.Equal(new FieldPredicate("user", "alice"), FilterForm.FieldPredicateOf(line));
    }

    [Fact]
    public void A_field_line_keeps_a_value_with_a_line_break_as_it_is()
    {
        var line = new DetailLine("trace: one\ntwo", [], LineKind.Field, Indent: 7, NodeId: null, Scope: "trace", Value: "one\ntwo");

        Assert.Equal(new FieldPredicate("trace", "one\ntwo"), FilterForm.FieldPredicateOf(line));
    }

    [Fact]
    public void A_JSON_scalar_line_filters_on_its_dotted_path_and_its_value()
    {
        var line = new DetailLine("OrderId: 42", [], LineKind.Json, Indent: 9, NodeId: null, Scope: "Properties.OrderId", Value: "42");

        Assert.Equal(new FieldPredicate("Properties.OrderId", "42"), FilterForm.FieldPredicateOf(line));
    }

    [Fact]
    public void A_line_without_a_value_filters_on_nothing_even_when_it_is_a_field_line()
    {
        var line = new DetailLine("trace: first", [], LineKind.Field, Indent: 7, NodeId: null, Scope: "trace", Value: null);

        Assert.Null(FilterForm.FieldPredicateOf(line));
    }

    [Theory]
    [InlineData(LineKind.Header)]
    [InlineData(LineKind.Message)]
    [InlineData(LineKind.Json)]
    [InlineData(LineKind.Raw)]
    [InlineData(LineKind.Fold)]
    [InlineData(LineKind.Notice)]
    public void Lines_that_are_not_fields_filter_on_nothing(LineKind kind)
    {
        var line = new DetailLine("text", [], kind, Indent: 0, NodeId: null, Scope: "message");

        Assert.Null(FilterForm.FieldPredicateOf(line));
    }

    [Fact]
    public void A_field_line_without_a_name_filters_on_nothing()
    {
        var line = new DetailLine("x", [], LineKind.Field, Indent: 0, NodeId: null, Scope: null);

        Assert.Null(FilterForm.FieldPredicateOf(line));
    }
}
