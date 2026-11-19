using Logvu.Core.Detail;
using Logvu.Core.Filter;
using Logvu.Core.Reading;
using Logvu.Core.Parsing;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Logvu.Tui;
using Shared.Core;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Logvu.Tests;

/// <summary>
/// The filter through the controller: <c>f</c> opens the editor only in the timeline, Apply and Cancel, the search and the status texts, <c>F</c>
/// clears, and <c>f</c> in the detail adds a field predicate and returns to the timeline with it applied.
/// </summary>
[Collection("Terminal GUI")]
public sealed class FilterControllerTests : IDisposable
{
    // Rows 3 and 6 match "needle"; every row's time is 10:00 plus its index in seconds.
    private const int Rows = 10;

    // A log4net web event with named fields: version, user, sourceAddress and localAddress, then the message.
    // A Serilog event with a nested number and an array of strings.
    private const string JsonEvent =
        "{\"Timestamp\":\"2026-11-18T10:20:31.007Z\",\"Level\":\"Error\",\"RenderedMessage\":\"Failed\","
        + "\"Properties\":{\"OrderId\":42,\"Tags\":[\"alpha\",\"beta\"]}}";

    private const string WebEvent =
        "ERROR 2026-11-18 10:20:31,007 v.2.1 u.alice 10.0.0.1 10.0.0.2 [7] SheetKraftWeb.Startup - Anonymous";

    private readonly LogSet _set = new(0);
    private readonly SwitchableDetailSource _details = new(DetailFixtures.Loaded(WebEvent, BuiltInSchemes.Log4NetWeb));
    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly FakeTimelineSource _source;
    private readonly PostQueue _posted = new();
    private readonly List<EventFilter?> _editorInputs = [];
    private readonly TimelineController _controller;
    private FilterEdit? _nextEdit;
    private bool _disposed;

    public FilterControllerTests()
    {
        _app = Application.Create();
        _app.Init("ansi");
        _store = new LogStore(_set.Config);
        _source = new FakeTimelineSource(Enumerable.Range(0, Rows).Select(index => Row(index, Preview(index))));
        _controller = new TimelineController(
            _app,
            _store,
            _details,
            _set.Config,
            _posted.Post,
            _source,
            FakeTimelineSource.CompareKeys,
            current =>
            {
                _editorInputs.Add(current);
                return _nextEdit;
            });
        _controller.Start();
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count > 0, "the first rows");
        _controller.Model.Resize(3);
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 3, "three visible rows");
    }

    [Fact]
    public void f_opens_the_filter_editor_from_the_timeline()
    {
        Assert.True(Send((Key)'f'));

        Assert.Single(_editorInputs);
        Assert.Null(_editorInputs[0]);
    }

    [Fact]
    public void f_typed_in_the_search_bar_is_text_and_does_not_open_the_editor()
    {
        Send(Key.F.WithCtrl);
        Assert.True(_controller.Search.Bar.HasFieldFocus);

        Send((Key)'f');

        Assert.Empty(_editorInputs);
        Assert.Equal("f", _controller.Search.Bar.Query.Text);
    }

    [Fact]
    public void Apply_sets_the_filter_on_the_timeline_and_the_status_shows_it()
    {
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("needle", false, false, false)));

        Send((Key)'f');
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 2, "the matching rows");

        Assert.NotNull(_controller.Filter);
        Assert.Equal(new[] { "needle 3", "needle 6" }, _controller.Model.VisibleRows.Select(row => row.Preview));
        Assert.StartsWith("FOLLOW · Filter: text ~ needle", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_of_an_empty_editor_removes_the_filter()
    {
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("needle", false, false, false)));
        Send((Key)'f');
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 2, "the matching rows");
        _nextEdit = Edit(null);

        Send((Key)'f');
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Any(row => row.Preview == "row 9"), "every row again");

        Assert.Null(_controller.Filter);
        Assert.DoesNotContain("Filter:", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancel_keeps_the_filter_that_was_active()
    {
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("needle", false, false, false)));
        Send((Key)'f');
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 2, "the matching rows");
        EventFilter active = _controller.Filter!;
        _nextEdit = null;

        Send((Key)'f');

        Assert.Same(active, _controller.Filter);
        Assert.Equal(2, _editorInputs.Count);
        Assert.Same(active, _editorInputs[1]);
    }

    [Fact]
    public void The_editor_is_shown_over_the_active_filter()
    {
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("needle", false, false, false)));
        Send((Key)'f');
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 2, "the matching rows");

        Send((Key)'f');

        Assert.Same(_controller.Filter, _editorInputs[1]);
    }

    [Fact]
    public void A_search_started_with_a_filter_passes_it_to_the_history()
    {
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("row", false, false, false)));
        Send((Key)'f');
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 3, "the matching rows");

        Send((Key)'/');
        Type("needle");
        Send(Key.Enter);
        _posted.Pump();

        FakeTimelineSource.FindRequest find = Assert.Single(_source.Finds);
        Assert.Same(_controller.Filter, find.Filter);
    }

    [Fact]
    public void A_search_started_without_a_filter_passes_none()
    {
        Send((Key)'/');
        Type("needle");
        Send(Key.Enter);
        _posted.Pump();

        FakeTimelineSource.FindRequest find = Assert.Single(_source.Finds);
        Assert.Null(find.Filter);
    }

    [Fact]
    public void F_clears_the_filter_and_says_so()
    {
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("needle", false, false, false)));
        Send((Key)'f');
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 2, "the matching rows");

        Assert.True(Send((Key)'F'));
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Any(row => row.Preview == "row 9"), "every row again");

        Assert.Null(_controller.Filter);
        Assert.Contains("Filter cleared", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.DoesNotContain("Filter:", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void Plus_and_equals_raise_the_minimum_level_one_step_and_the_filter_shows_it_without_a_notice()
    {
        Assert.True(Send((Key)'='));
        _posted.Pump();
        Assert.Equal(LogLevel.Debug, _controller.Filter!.MinLevel);
        Assert.Contains("Filter: level ≥ DEBUG", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.DoesNotContain("Min level", _controller.StatusLeft, StringComparison.Ordinal);

        Assert.True(Send((Key)'+'));
        _posted.Pump();
        Assert.Equal(LogLevel.Info, _controller.Filter!.MinLevel);
        Assert.Contains("Filter: level ≥ INFO", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.DoesNotContain("Min level", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void Minus_and_underscore_lower_the_minimum_level_and_clear_the_filter_when_nothing_is_left()
    {
        _nextEdit = Edit(new FilterSpec(MinLevel: LogLevel.Info));
        Send((Key)'f');
        Assert.Equal(LogLevel.Info, _controller.Filter!.MinLevel);

        Assert.True(Send((Key)'_'));
        _posted.Pump();
        Assert.Equal(LogLevel.Debug, _controller.Filter!.MinLevel);

        Assert.True(Send((Key)'-'));
        _posted.Pump();
        Assert.Null(_controller.Filter);
        Assert.Contains("Min level: any", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.DoesNotContain("Filter:", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void The_level_keys_keep_the_other_predicates_of_the_filter()
    {
        _nextEdit = Edit(new FilterSpec(MinLevel: LogLevel.Info, Text: new SearchQuery("needle", false, false, false)));
        Send((Key)'f');

        Send((Key)'=');
        _posted.Pump();

        EventFilter filter = _controller.Filter!;
        Assert.Equal(LogLevel.Warn, filter.MinLevel);
        Assert.Equal("needle", filter.Text!.Value.Text);
    }

    [Fact]
    public void At_the_ends_the_filter_is_not_changed_and_no_minimum_still_says_so()
    {
        _nextEdit = Edit(new FilterSpec(MinLevel: LogLevel.Fatal));
        Send((Key)'f');
        EventFilter fatal = _controller.Filter!;

        Assert.True(Send((Key)'='));

        Assert.Same(fatal, _controller.Filter);
        Assert.Contains("Filter: level ≥ FATAL", _controller.StatusLeft, StringComparison.Ordinal);

        Send((Key)'F');
        Assert.Null(_controller.Filter);

        Assert.True(Send((Key)'-'));
        Assert.Null(_controller.Filter);
        Assert.Contains("Min level: any", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void The_level_keys_do_nothing_to_the_filter_while_the_detail_is_open()
    {
        _controller.Timeline.HandleKey(Key.Enter);
        _posted.PumpUntil(() => _controller.Detail?.Document is not null, "the detail");

        Send((Key)'=');
        Send((Key)'-');

        Assert.NotNull(_controller.Detail);
        Assert.Null(_controller.Filter);
        Assert.DoesNotContain("Min level", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void The_level_keys_typed_in_the_search_bar_are_text_and_do_not_step_the_level()
    {
        Send(Key.F.WithCtrl);
        Assert.True(_controller.Search.Bar.HasFieldFocus);

        Type("=+-_");

        Assert.Empty(_editorInputs);
        Assert.Null(_controller.Filter);
        Assert.Equal("=+-_", _controller.Search.Bar.Query.Text);
    }

    [Fact]
    public void A_filter_with_no_matches_shows_no_matching_events_once_the_scan_completes()
    {
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("zzz", false, false, false)));

        Send((Key)'f');
        _posted.PumpUntil(() => _controller.Model.NoMatches, "the scan to complete");

        Assert.Contains("No matching events", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scan_in_progress_shows_its_megabytes_and_not_no_matches()
    {
        HoldEveryRead();
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("needle", false, false, false)));

        Send((Key)'f');

        Assert.Contains("Scanning… 0.0 MB", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.DoesNotContain("No matching events", _controller.StatusLeft, StringComparison.Ordinal);
        ReleaseEveryRead();
    }

    [Fact]
    public void A_timed_out_filter_pattern_is_reported_on_the_status_line()
    {
        HoldEveryRead();
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("needle", false, false, false)));
        Send((Key)'f');

        _source.CompleteOldestAbandoned(ReadKind.Newest);
        _posted.PumpUntil(() => _controller.Model.FilterTimedOut, "the abandoned page");

        Assert.Contains("Filter pattern timed out", _controller.StatusLeft, StringComparison.Ordinal);
        ReleaseEveryRead();
    }

    [Fact]
    public void Paused_with_arrivals_the_window_has_not_reached_shows_new_events_without_a_count()
    {
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("needle", false, false, false)));
        Send((Key)'f');
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 2, "the matching rows");
        _controller.Model.Pause();

        // A reload in progress has not placed the window yet, so an arrival during it cannot be counted. Row 0 does not match the
        // filter, so it is not loaded and the jump reloads; a loaded row would be selected in place.
        HoldEveryRead();
        _controller.Model.JumpTo(new RowKey(1, 0));
        _controller.Model.Arrive([Row(Rows, "needle 10")], []);

        Assert.True(_controller.Model.HasUnseenArrivals);
        Assert.StartsWith("PAUSED · new events", _controller.StatusLeft, StringComparison.Ordinal);
        ReleaseEveryRead();
    }

    [Fact]
    public void Paused_with_arrivals_the_window_has_reached_shows_the_count()
    {
        _controller.Model.Pause();

        _controller.Model.Arrive([Row(Rows, "row 10")], []);

        Assert.False(_controller.Model.HasUnseenArrivals);
        Assert.Equal(1, _controller.Model.NewCount);
        Assert.StartsWith("PAUSED · 1 new", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void f_on_a_field_line_of_the_detail_adds_the_predicate_closes_the_detail_and_applies_it()
    {
        _controller.Timeline.HandleKey(Key.Enter);
        _posted.PumpUntil(() => _controller.Detail?.Document is not null, "the detail");
        MoveCursorTo(_controller.Detail!, line => line.Kind == LineKind.Field && line.Scope == "user");

        Assert.True(Send((Key)'f'));

        Assert.Null(_controller.Detail);
        Assert.Same(_controller.Timeline, _controller.Window.Content);
        Assert.Equal(new[] { new FieldPredicate("user", FieldOperator.Equals, "alice") }, _controller.Filter!.Fields);
        Assert.StartsWith("PAUSED", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.Contains("Filter: user equals alice", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void f_on_a_nested_JSON_scalar_adds_its_dotted_path_and_value()
    {
        _details.Detail = DetailFixtures.Json(JsonEvent);
        _controller.Timeline.HandleKey(Key.Enter);
        _posted.PumpUntil(() => _controller.Detail?.Document is not null, "the detail");
        MoveCursorTo(_controller.Detail!, line => line.Scope == "Properties.OrderId");

        Assert.True(Send((Key)'f'));

        Assert.Null(_controller.Detail);
        Assert.Equal(new[] { new FieldPredicate("Properties.OrderId", FieldOperator.Equals, "42") }, _controller.Filter!.Fields);
        Assert.Contains("Filter: Properties.OrderId equals 42", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void f_on_an_array_element_of_the_detail_adds_its_indexed_path_and_its_string_value()
    {
        _details.Detail = DetailFixtures.Json(JsonEvent);
        _controller.Timeline.HandleKey(Key.Enter);
        _posted.PumpUntil(() => _controller.Detail?.Document is not null, "the detail");
        // The array is expanded by default, so its second element is visible.
        MoveCursorTo(_controller.Detail!, line => line.Scope == "Properties.Tags[1]");

        Assert.True(Send((Key)'f'));

        Assert.Null(_controller.Detail);
        Assert.Equal(new[] { new FieldPredicate("Properties.Tags[1]", FieldOperator.Equals, "beta") }, _controller.Filter!.Fields);
    }

    [Fact]
    public void f_on_a_message_line_of_the_detail_keeps_the_detail_open_and_says_what_it_filters_on()
    {
        _controller.Timeline.HandleKey(Key.Enter);
        _posted.PumpUntil(() => _controller.Detail?.Document is not null, "the detail");
        MoveCursorTo(_controller.Detail!, line => line.Kind == LineKind.Message);

        Send((Key)'f');

        Assert.NotNull(_controller.Detail);
        Assert.Null(_controller.Filter);
        Assert.Contains("f filters on a field line", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void f_on_the_message_line_of_the_detail_filters_on_nothing()
    {
        _controller.Timeline.HandleKey(Key.Enter);
        _posted.PumpUntil(() => _controller.Detail?.Document is not null, "the detail");
        MoveCursorTo(_controller.Detail!, line => line.Kind == LineKind.Message);

        Send((Key)'f');

        Assert.NotNull(_controller.Detail);
        Assert.Null(_controller.Filter);
    }

    [Fact]
    public void The_empty_body_says_no_matching_events_when_a_filter_matches_nothing_and_waits_again_once_cleared()
    {
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("zzz", false, false, false)));
        Send((Key)'f');
        _posted.PumpUntil(() => _controller.Model.NoMatches, "the scan to complete");

        Assert.Equal("No matching events", _controller.Timeline.Placeholder);
        Assert.Contains("No matching events", _controller.StatusLeft, StringComparison.Ordinal);

        Send((Key)'F');

        Assert.Equal("Waiting for events…", _controller.Timeline.Placeholder);
    }

    [Fact]
    public void The_empty_body_says_scanning_while_a_filtered_scan_runs_and_waits_for_events_without_a_filter()
    {
        Assert.Equal("Waiting for events…", _controller.Timeline.Placeholder);
        HoldEveryRead();
        _nextEdit = Edit(new FilterSpec(Text: new SearchQuery("needle", false, false, false)));

        Send((Key)'f');

        Assert.Equal("Scanning…", _controller.Timeline.Placeholder);
        ReleaseEveryRead();
        Assert.NotEqual("Scanning…", _controller.Timeline.Placeholder);
    }

    [Fact]
    public void The_timeline_hints_name_the_filter_and_level_keys()
    {
        Assert.Equal("f filter  -/+ level  Space pause/follow  c copy event  End newest  ^Q quit", TimelineController.Hints);
        Assert.Equal("f filter  -/+ level  Space pause/follow  c copy selection  End newest  ^Q quit", TimelineController.SelectionHints);
        Assert.Equal("f filter on value  Enter toggle  z folds  r raw  c copy line  ←/Esc back", TimelineController.DetailHints);
        Assert.Equal("f filter on value  Enter toggle  z folds  r raw  c copy selection  ←/Esc back", TimelineController.DetailSelectionHints);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.Dispose();
        _store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _app.Dispose();
        _set.Dispose();
    }

    /// <summary>Sends a key through the window's routing: the focused view first, then the window. Returns whether it was handled.</summary>
    private bool Send(Key key) => _controller.Window.NewKeyDownEvent(key);

    /// <summary>Sends each character of the text as a key, as typing does.</summary>
    private void Type(string text)
    {
        foreach (char ch in text)
        {
            Send((Key)ch);
        }
    }

    /// <summary>Holds every read of the timeline until the test releases it, so that the model stays loading.</summary>
    private void HoldEveryRead()
    {
        _source.Held.Add(ReadKind.Newest);
        _source.Held.Add(ReadKind.Older);
        _source.Held.Add(ReadKind.Newer);
    }

    /// <summary>Completes the held reads and lets their continuations run, so that nothing is left loading.</summary>
    private void ReleaseEveryRead()
    {
        foreach (ReadKind kind in new[] { ReadKind.Newest, ReadKind.Older, ReadKind.Newer })
        {
            while (_source.Waiting(kind) > 0)
            {
                _source.CompleteOldest(kind);
            }
        }

        _source.Held.Clear();
        _posted.PumpUntil(() => !_controller.Model.IsLoading, "the reads to finish");
    }

    /// <summary>Moves the cursor of the detail down until it is on a line that matches.</summary>
    private static void MoveCursorTo(DetailView detail, Func<DetailLine, bool> matches)
    {
        int index = detail.Document!.Lines.ToList().FindIndex(line => matches(line));
        string lines = string.Join("\n", detail.Document!.Lines.Select(line => $"{line.Kind} node={line.NodeId} scope={line.Scope} value={line.Value} text={line.Text}"));
        Assert.True(index >= 0, "no line of the detail matches:\n" + lines);
        for (int step = 0; detail.CursorLine != index && step < 1000; step++)
        {
            detail.HandleKey(Key.CursorDown);
        }

        Assert.Equal(index, detail.CursorLine);
    }

    /// <summary>The edit that applies a filter of the spec, or no filter for <c>null</c>.</summary>
    private static FilterEdit Edit(FilterSpec? spec) => new(spec is null ? null : new EventFilter(spec));

    private static string Preview(int index) => index is 3 or 6 ? $"needle {index}" : $"row {index}";

    private static EventRow Row(int index, string preview) => new(
        new RowKey(1, index * 100L),
        new EventExtent(0, 0, 0, Provisional: false, Orphan: false),
        new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero).AddSeconds(index),
        null,
        LogLevel.Unknown,
        null,
        preview,
        IsRaw: false,
        Orphan: false,
        Provisional: false);

    /// <summary>A detail source that answers every read with its current detail, which a test may replace.</summary>
    private sealed class SwitchableDetailSource(EventDetail detail) : IDetailSource
    {
        /// <summary>The detail every read answers with.</summary>
        public EventDetail Detail { get; set; } = detail;

        public Task<EventDetail> ReadDetailAsync(RowKey key, CancellationToken ct) => Task.FromResult(Detail);
    }
}
