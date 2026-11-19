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

    private static FilterInput Empty => new(null, string.Empty, string.Empty, string.Empty, string.Empty, false, false, false, string.Empty);

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
    public void Predicate_lines_read_a_path_a_keyword_and_the_value_as_typed()
    {
        IReadOnlyList<FieldPredicate> predicates = FilterForm.ParsePredicates("user equals alice\n\n  code contains 42 \nname prefix a b  c\n   \n");

        Assert.Equal(
            [
                new FieldPredicate("user", FieldOperator.Equals, "alice"),
                new FieldPredicate("code", FieldOperator.Contains, "42 "),
                new FieldPredicate("name", FieldOperator.Prefix, "a b  c"),
            ],
            predicates);
    }

    [Fact]
    public void The_keyword_is_read_ignoring_case_and_the_value_starts_after_the_one_space_that_follows_it()
    {
        IReadOnlyList<FieldPredicate> predicates = FilterForm.ParsePredicates("a SUFFIX  x\nb Equals y");

        Assert.Equal([new FieldPredicate("a", FieldOperator.Suffix, " x"), new FieldPredicate("b", FieldOperator.Equals, "y")], predicates);
    }

    [Fact]
    public void Carriage_return_line_feeds_are_read_as_line_breaks()
    {
        IReadOnlyList<FieldPredicate> predicates = FilterForm.ParsePredicates("a equals 1\r\nb contains 2");

        Assert.Equal([new FieldPredicate("a", FieldOperator.Equals, "1"), new FieldPredicate("b", FieldOperator.Contains, "2")], predicates);
    }

    [Fact]
    public void An_empty_value_is_a_predicate_for_an_empty_field_when_the_keyword_is_followed_by_its_space()
    {
        IReadOnlyList<FieldPredicate> predicates = FilterForm.ParsePredicates("a equals ");

        Assert.Equal([new FieldPredicate("a", FieldOperator.Equals, string.Empty)], predicates);
    }

    [Theory]
    [InlineData("a equals 1\nnovalue", 2, "no keyword")]
    [InlineData("a equals 1\nuser sometimes x", 2, "\"sometimes\"")]
    [InlineData("a equals 1\n\nuser equals", 3, "no space after the keyword")]
    [InlineData("a = 1", 1, "\"=\"")]
    [InlineData("a prefix", 1, "no space after the keyword")]
    public void A_malformed_line_names_the_field_the_line_and_what_is_wrong(string text, int line, string problem)
    {
        FilterInputException error = Assert.Throws<FilterInputException>(() => FilterForm.ParsePredicates(text));

        Assert.Equal("Field predicates", error.Field);
        Assert.Contains($"Line {line}", error.Message, StringComparison.Ordinal);
        Assert.Contains(problem, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_keyword_lists_the_accepted_keywords()
    {
        FilterInputException error = Assert.Throws<FilterInputException>(() => FilterForm.ParsePredicates("a like b"));

        Assert.Contains("equals, prefix, suffix or contains", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Predicates_round_trip_through_the_editor_text()
    {
        FieldPredicate[] predicates =
        [
            new("user", FieldOperator.Equals, "alice"),
            new("logger", FieldOperator.Prefix, "Orders."),
            new("code", FieldOperator.Suffix, "9"),
            new("message", FieldOperator.Contains, string.Empty),
            new("note", FieldOperator.Contains, " spaced value "),
        ];

        string text = FilterForm.FormatPredicates(predicates);

        Assert.Equal("user equals alice\nlogger prefix Orders.\ncode suffix 9\nmessage contains \nnote contains  spaced value ", text);
        Assert.Equal(predicates, FilterForm.ParsePredicates(text));
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
            File = " app ",
            From = "2026-11-18 10:00",
            To = "2026-11-18 11:00",
            Text = "timeout ",
            CaseSensitive = true,
            Predicates = "Properties.OrderId equals 42",
        };

        FilterSpec spec = FilterForm.Build(input, Zone, Now);

        Assert.Equal(LogLevel.Warn, spec.MinLevel);
        Assert.Equal("app", spec.FileContains);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(2)), spec.From);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 11, 0, 0, TimeSpan.FromHours(2)), spec.To);
        Assert.Equal(new SearchQuery("timeout ", true, false, false), spec.Text);
        Assert.Equal([new FieldPredicate("Properties.OrderId", FieldOperator.Equals, "42")], spec.Fields);
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
            FileContains: "app",
            From: new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(2)),
            To: new DateTimeOffset(2026, 11, 18, 11, 30, 15, TimeSpan.FromHours(2)),
            Text: new SearchQuery("time.*out", false, true, true),
            Fields: [new FieldPredicate("user", FieldOperator.Equals, "alice"), new FieldPredicate("logger", FieldOperator.Contains, "Orders"), new FieldPredicate("code", FieldOperator.Equals, "42")]);
        var active = new EventFilter(spec);

        FilterInput shown = FilterForm.InputOf(active, Zone);
        var rebuilt = new EventFilter(FilterForm.Build(shown, Zone, Now));

        Assert.Equal(LogLevel.Error, shown.MinLevel);
        Assert.Equal("2026-11-18 10:00:00", shown.From);
        Assert.Equal("2026-11-18 11:30:15", shown.To);
        Assert.True(shown.WholeWord);
        Assert.True(shown.Regex);
        Assert.Equal("user equals alice\nlogger contains Orders\ncode equals 42", shown.Predicates);
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
        var predicate = new FieldPredicate("user", FieldOperator.Equals, "alice");

        EventFilter once = FilterForm.Extend(current, predicate);
        EventFilter twice = FilterForm.Extend(once, predicate);

        Assert.Equal([predicate], once.Fields);
        Assert.Equal(once.Fields, twice.Fields);
        Assert.Equal(LogLevel.Warn, twice.MinLevel);
        Assert.Equal("timeout", twice.Text!.Value.Text);
    }

    [Fact]
    public void Extending_a_filter_with_the_same_path_and_value_under_another_operator_adds_that_predicate_too()
    {
        EventFilter once = FilterForm.Extend(null, new FieldPredicate("user", FieldOperator.Equals, "alice"));

        EventFilter twice = FilterForm.Extend(once, new FieldPredicate("user", FieldOperator.Prefix, "alice"));

        Assert.Equal([new FieldPredicate("user", FieldOperator.Equals, "alice"), new FieldPredicate("user", FieldOperator.Prefix, "alice")], twice.Fields);
    }

    [Fact]
    public void Extending_no_filter_gives_a_filter_with_only_the_predicate()
    {
        EventFilter filter = FilterForm.Extend(null, new FieldPredicate("user", FieldOperator.Equals, "alice"));

        Assert.Equal([new FieldPredicate("user", FieldOperator.Equals, "alice")], filter.Fields);
        Assert.Null(filter.MinLevel);
        Assert.Null(filter.Text);
    }

    [Fact]
    public void Raising_the_minimum_level_walks_up_from_no_minimum_and_stops_at_fatal()
    {
        LogLevel?[] expected = [LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal, LogLevel.Fatal];
        var steps = new List<LogLevel?>();
        LogLevel? level = null;
        foreach (LogLevel? _ in expected)
        {
            level = FilterForm.StepMinLevel(level, 1);
            steps.Add(level);
        }

        Assert.Equal(expected, steps);
    }

    [Fact]
    public void Lowering_the_minimum_level_walks_down_from_fatal_and_ends_at_no_minimum()
    {
        LogLevel?[] expected = [LogLevel.Error, LogLevel.Warn, LogLevel.Info, LogLevel.Debug, null, null];
        var steps = new List<LogLevel?>();
        LogLevel? level = LogLevel.Fatal;
        foreach (LogLevel? _ in expected)
        {
            level = FilterForm.StepMinLevel(level, -1);
            steps.Add(level);
        }

        Assert.Equal(expected, steps);
    }

    [Theory]
    [InlineData(null, 1, LogLevel.Debug)]
    [InlineData(null, -1, null)]
    [InlineData(LogLevel.Trace, 1, LogLevel.Debug)]
    [InlineData(LogLevel.Trace, -1, null)]
    [InlineData(LogLevel.Debug, 1, LogLevel.Info)]
    [InlineData(LogLevel.Debug, -1, null)]
    [InlineData(LogLevel.Info, 1, LogLevel.Warn)]
    [InlineData(LogLevel.Info, -1, LogLevel.Debug)]
    [InlineData(LogLevel.Warn, 1, LogLevel.Error)]
    [InlineData(LogLevel.Warn, -1, LogLevel.Info)]
    [InlineData(LogLevel.Error, 1, LogLevel.Fatal)]
    [InlineData(LogLevel.Error, -1, LogLevel.Warn)]
    [InlineData(LogLevel.Fatal, 1, LogLevel.Fatal)]
    [InlineData(LogLevel.Fatal, -1, LogLevel.Error)]
    public void A_step_goes_to_the_neighbouring_level_of_the_steps_and_Trace_is_not_a_step(LogLevel? current, int direction, LogLevel? expected)
    {
        Assert.Equal(expected, FilterForm.StepMinLevel(current, direction));
    }

    [Fact]
    public void A_step_in_no_direction_is_an_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FilterForm.StepMinLevel(LogLevel.Info, 0));
    }

    [Fact]
    public void Changing_the_minimum_level_keeps_every_other_predicate_of_the_filter()
    {
        var from = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 11, 18, 11, 0, 0, TimeSpan.Zero);
        var current = new EventFilter(new FilterSpec(
            MinLevel: LogLevel.Warn,
            Levels: new HashSet<LogLevel> { LogLevel.Warn },
            FileContains: "app.log",
            From: from,
            To: to,
            Text: new SearchQuery("timeout", false, false, false),
            Fields: [new FieldPredicate("user", FieldOperator.Equals, "alice")]));

        EventFilter? changed = FilterForm.WithMinLevel(current, LogLevel.Error);

        Assert.NotNull(changed);
        Assert.Equal(LogLevel.Error, changed.MinLevel);
        Assert.Equal(new[] { LogLevel.Warn }, changed.Levels);
        Assert.Equal("app.log", changed.FileContains);
        Assert.Equal(from, changed.From);
        Assert.Equal(to, changed.To);
        Assert.Equal("timeout", changed.Text!.Value.Text);
        Assert.Equal(new[] { new FieldPredicate("user", FieldOperator.Equals, "alice") }, changed.Fields);
    }

    [Fact]
    public void A_minimum_level_on_no_filter_gives_a_filter_with_only_that_minimum()
    {
        EventFilter? filter = FilterForm.WithMinLevel(null, LogLevel.Info);

        Assert.NotNull(filter);
        Assert.Equal(LogLevel.Info, filter.MinLevel);
        Assert.Empty(filter.Fields);
        Assert.Null(filter.Text);
    }

    [Fact]
    public void Removing_the_minimum_level_of_a_filter_with_nothing_else_gives_no_filter()
    {
        var current = new EventFilter(new FilterSpec(MinLevel: LogLevel.Warn));

        Assert.Null(FilterForm.WithMinLevel(current, null));
    }

    [Fact]
    public void Removing_the_minimum_level_keeps_the_other_predicates()
    {
        var current = new EventFilter(new FilterSpec(MinLevel: LogLevel.Warn, FileContains: "app.log"));

        EventFilter? filter = FilterForm.WithMinLevel(current, null);

        Assert.NotNull(filter);
        Assert.Null(filter.MinLevel);
        Assert.Equal("app.log", filter.FileContains);
    }

    [Fact]
    public void A_field_line_filters_on_its_name_and_its_value()
    {
        var line = new DetailLine("user: alice", [], LineKind.Field, Indent: 6, NodeId: null, Scope: "user", Value: "alice");

        Assert.Equal(new FieldPredicate("user", FieldOperator.Equals, "alice"), FilterForm.FieldPredicateOf(line));
    }

    [Fact]
    public void A_field_line_keeps_a_value_with_a_line_break_as_it_is()
    {
        var line = new DetailLine("trace: one\ntwo", [], LineKind.Field, Indent: 7, NodeId: null, Scope: "trace", Value: "one\ntwo");

        Assert.Equal(new FieldPredicate("trace", FieldOperator.Equals, "one\ntwo"), FilterForm.FieldPredicateOf(line));
    }

    [Fact]
    public void A_JSON_scalar_line_filters_on_its_dotted_path_and_its_value()
    {
        var line = new DetailLine("OrderId: 42", [], LineKind.Json, Indent: 9, NodeId: null, Scope: "Properties.OrderId", Value: "42");

        Assert.Equal(new FieldPredicate("Properties.OrderId", FieldOperator.Equals, "42"), FilterForm.FieldPredicateOf(line));
    }

    [Fact]
    public void A_line_without_a_value_filters_on_nothing_even_when_it_is_a_field_line()
    {
        var line = new DetailLine("trace: first", [], LineKind.Field, Indent: 7, NodeId: null, Scope: "trace", Value: null);

        Assert.Null(FilterForm.FieldPredicateOf(line));
    }

    [Theory]
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
