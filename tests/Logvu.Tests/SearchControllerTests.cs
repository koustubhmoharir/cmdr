using System.Reflection;
using System.Text.RegularExpressions;
using Logvu.Core.Detail;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Logvu.Tui;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using SearchRequest = Logvu.Tests.FakeTimelineSource.FindRequest;

namespace Logvu.Tests;

/// <summary>
/// The search through the controller: the keys that open the bar and reach the search through the window's own routing, the scan toward
/// older and newer events with its cancellation, and the search inside a detail.
/// </summary>
[Collection("Terminal GUI")]
public sealed class SearchControllerTests : IDisposable
{
    // Rows 3 and 6 match "needle"; every row's time is 10:00 plus its index in seconds.
    private const int Rows = 10;

    private readonly LogSet _set = new(0);
    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly FakeTimelineSource _source;
    private readonly PostQueue _posted = new();
    private readonly TimelineController _controller;
    private bool _disposed;

    public SearchControllerTests()
    {
        _app = Application.Create();
        _app.Init("ansi");
        _store = new LogStore(_set.Config);
        _source = new FakeTimelineSource(Enumerable.Range(0, Rows).Select(index => Row(index, Preview(index))));
        _controller = new TimelineController(
            _app,
            _store,
            new FixedDetailSource(DetailFixtures.Text(DetailFixtures.StackEvent)),
            _set.Config,
            _posted.Post,
            _source,
            FakeTimelineSource.CompareKeys);
        _controller.Start();
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count > 0, "the first rows");
        _controller.Model.Resize(3);
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 3, "three visible rows");
    }

    private SearchBar Bar => _controller.Search.Bar;

    [Fact]
    public void Slash_and_Ctrl_F_open_the_bar_with_the_focus_in_its_field_and_Esc_closes_it()
    {
        Assert.True(Send((Key)'/'));
        Assert.True(Bar.Visible);
        Assert.True(Bar.HasFieldFocus);

        Send(Key.Esc);
        Assert.False(Bar.Visible);
        Assert.True(_controller.Timeline.HasFocus);

        Assert.True(Send(Key.F.WithCtrl));
        Assert.True(Bar.Visible);
        Assert.True(Bar.HasFieldFocus);
    }

    [Fact]
    public void In_the_bar_n_f_and_slash_enter_text_and_run_no_command()
    {
        Send((Key)'/');

        Type("nf/");

        Assert.Equal("nf/", Bar.Query.Text);
        Assert.Empty(_source.Finds);
        Assert.Equal(TimelineMode.Follow, _controller.Model.Mode);
        Assert.Null(_controller.Detail);
        Assert.True(Bar.HasFieldFocus);
    }

    [Fact]
    public void Typing_highlights_the_visible_rows_and_starts_no_scan()
    {
        Send((Key)'/');

        Type("needle");

        Assert.NotNull(_controller.Timeline.Search);
        Assert.Equal("needle", _controller.Timeline.Search!.Query.Text);
        Assert.Empty(_source.Finds);
        Assert.False(_controller.Search.IsScanning);
    }

    [Fact]
    public void An_invalid_pattern_is_shown_in_the_bar_and_highlights_nothing_and_Enter_finds_nothing()
    {
        Send((Key)'/');
        Send(Key.R.WithAlt);

        Type("(");
        Send(Key.Enter);
        _posted.Pump();

        Assert.Equal("invalid pattern", Bar.Status);
        Assert.Null(_controller.Timeline.Search);
        Assert.Empty(_source.Finds);
        Assert.True(_controller.Timeline.HasFocus);
    }

    [Fact]
    public void Esc_clears_the_text_first_and_then_closes_the_bar_which_ends_the_highlights()
    {
        Send((Key)'/');
        Type("needle");

        Send(Key.Esc);

        Assert.True(Bar.Visible);
        Assert.Equal(string.Empty, Bar.Query.Text);
        Assert.Null(_controller.Timeline.Search);

        Send(Key.Esc);

        Assert.False(Bar.Visible);
        Assert.False(_controller.Search.IsOpen);
        Assert.Null(_controller.Timeline.Search);
        Assert.True(_controller.Timeline.HasFocus);
        Assert.DoesNotContain("n older", _controller.StatusHints, StringComparison.Ordinal);
    }

    [Fact]
    public void The_status_line_names_the_keys_of_the_matches_while_a_query_is_active()
    {
        Send((Key)'/');
        Type("needle");

        Assert.StartsWith("n older  N newer  ", _controller.StatusHints, StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_in_the_bar_focuses_the_timeline_and_searches_older_from_the_selected_row()
    {
        Send((Key)'/');
        Type("needle");
        RowKey selected = _controller.Model.SelectedRow!.Key;

        Send(Key.Enter);

        Assert.True(_controller.Timeline.HasFocus);
        SearchRequest request = Assert.Single(_source.Finds);
        Assert.Equal(SearchDirection.Older, request.Direction);
        Assert.Equal(selected, request.From);
        Assert.Equal("needle", request.Pattern.Query.Text);
    }

    [Fact]
    public void A_found_row_is_jumped_to_centred_and_its_time_is_shown()
    {
        Send((Key)'/');
        Type("needle");

        Send(Key.Enter);
        Settle();

        Assert.Equal(RowKeyOf(6), _controller.Model.SelectedRow!.Key);
        Assert.Equal(1, _controller.Model.SelectedScreenRow);
        Assert.Equal(TimelineMode.Paused, _controller.Model.Mode);
        Assert.Contains("Match 2026-11-18 10:00:06", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.Equal(RowKeyOf(6), _controller.Timeline.ActiveRow);
    }

    [Fact]
    public void n_and_F3_search_older_and_N_and_Shift_F3_search_newer_from_the_selected_row()
    {
        Send((Key)'/');
        Type("needle");
        _controller.Timeline.SetFocus();

        Send((Key)'n');
        Settle();
        Assert.Equal(RowKeyOf(6), _controller.Model.SelectedRow!.Key);

        Send(Key.F3);
        Settle();
        Assert.Equal(RowKeyOf(3), _controller.Model.SelectedRow!.Key);

        Send((Key)'N');
        Settle();
        Assert.Equal(RowKeyOf(6), _controller.Model.SelectedRow!.Key);

        Send(Key.F3.WithShift);
        Settle();
        Assert.Equal(RowKeyOf(6), _controller.Model.SelectedRow!.Key);

        Assert.Equal(
            [SearchDirection.Older, SearchDirection.Older, SearchDirection.Newer, SearchDirection.Newer],
            _source.Finds.Select(request => request.Direction));
        Assert.Equal(RowKeyOf(9), _source.Finds[0].From);
        Assert.Equal(RowKeyOf(6), _source.Finds[1].From);
        Assert.Contains("No more matches below", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void Shift_F3_and_N_in_the_bar_search_newer_and_F3_searches_older()
    {
        Send((Key)'/');
        Type("needle");

        Send(Key.F3.WithShift);
        Settle();
        Send(Key.F3);
        Settle();

        Assert.Equal(
            [SearchDirection.Newer, SearchDirection.Older],
            _source.Finds.Select(request => request.Direction));
        Assert.True(Bar.HasFieldFocus);
    }

    [Fact]
    public void A_search_that_reaches_the_start_of_history_says_so_and_keeps_the_selection()
    {
        Send((Key)'/');
        Type("needle 6");
        Send(Key.Enter);
        Settle();

        Send((Key)'n');
        Settle();

        Assert.Equal(RowKeyOf(6), _controller.Model.SelectedRow!.Key);
        Assert.Contains("No more matches above", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void A_search_that_reaches_the_end_of_history_says_so_and_keeps_the_selection()
    {
        Send((Key)'/');
        Type("needle 6");
        RowKey newest = _controller.Model.SelectedRow!.Key;

        Send(Key.F3.WithShift);
        Settle();

        Assert.Equal(newest, _controller.Model.SelectedRow!.Key);
        Assert.Contains("No more matches below", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void A_timed_out_search_says_the_pattern_timed_out_and_keeps_the_selection()
    {
        _source.HoldFinds = true;
        Send((Key)'/');
        Type("needle");
        RowKey selected = _controller.Model.SelectedRow!.Key;

        Send(Key.Enter);
        _source.Finds[^1].Complete(Logvu.Core.Store.SearchResult.TimedOut);
        Settle();

        Assert.Equal(selected, _controller.Model.SelectedRow!.Key);
        Assert.Contains("Search pattern timed out", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_search_cancels_the_first_and_the_first_late_result_is_ignored()
    {
        _source.HoldFinds = true;
        Send((Key)'/');
        Type("needle");
        RowKey selected = _controller.Model.SelectedRow!.Key;
        Send(Key.Enter);

        Send((Key)'n');

        SearchRequest first = _source.Finds[0];
        Assert.True(first.Token.IsCancellationRequested);
        first.Complete(Logvu.Core.Store.SearchResult.Found(Row(3, Preview(3))));
        _posted.Pump();
        Assert.Equal(selected, _controller.Model.SelectedRow!.Key);
        Assert.True(_controller.Search.IsScanning);

        _source.Finds[1].Complete(Logvu.Core.Store.SearchResult.Boundary);
        Settle();
        Assert.Equal(selected, _controller.Model.SelectedRow!.Key);
        Assert.Contains("No more matches above", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void Esc_in_the_timeline_cancels_the_scan_and_says_so()
    {
        _source.HoldFinds = true;
        Send((Key)'/');
        Type("needle");
        RowKey selected = _controller.Model.SelectedRow!.Key;
        Send(Key.Enter);
        Assert.True(_controller.Search.IsScanning);

        Send(Key.Esc);
        _source.Finds[0].Complete(Logvu.Core.Store.SearchResult.Found(Row(3, Preview(3))));
        Settle();

        Assert.True(_source.Finds[0].Token.IsCancellationRequested);
        Assert.False(_controller.Search.IsScanning);
        Assert.Equal(selected, _controller.Model.SelectedRow!.Key);
        Assert.Contains("Search cancelled", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void Progress_of_a_scan_is_shown_in_megabytes_after_the_delay_and_throttled()
    {
        long ms = 0;
        _controller.Search.ClockMs = () => ms;
        _source.HoldFinds = true;
        Send((Key)'/');
        Type("needle");
        Send(Key.Enter);
        SearchRequest request = _source.Finds[0];

        ms = SearchController.ProgressDelayMs;
        request.Progress!.Report(12_900_000);
        _posted.PumpUntil(() => _controller.StatusLeft.Contains("Searching… 12.3 MB", StringComparison.Ordinal), "the progress");

        // A second report within the interval of the first is not shown.
        ms += 50;
        request.Progress.Report(26_000_000);
        _posted.Pump();
        Assert.Contains("Searching… 12.3 MB", _controller.StatusLeft, StringComparison.Ordinal);

        request.Complete(Logvu.Core.Store.SearchResult.Boundary);
        Settle();
    }

    [Fact]
    public void A_report_before_the_delay_is_not_shown_and_the_previous_status_stays()
    {
        long ms = 0;
        _controller.Search.ClockMs = () => ms;
        Send((Key)'/');
        Type("needle 6");
        Send(Key.Enter);
        Settle();
        const string previous = "Match 2026-11-18 10:00:06";
        Assert.Contains(previous, _controller.StatusLeft, StringComparison.Ordinal);

        _source.HoldFinds = true;
        Send((Key)'n');
        SearchRequest request = _source.Finds[^1];

        ms = SearchController.ProgressDelayMs - 1;
        request.Progress!.Report(12_900_000);
        _posted.Pump();

        Assert.DoesNotContain("Searching", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.Contains(previous, _controller.StatusLeft, StringComparison.Ordinal);

        request.Complete(Logvu.Core.Store.SearchResult.Boundary);
        Settle();
    }

    [Fact]
    public void A_scan_that_ends_before_the_delay_goes_from_the_previous_status_to_its_outcome()
    {
        long ms = 0;
        _controller.Search.ClockMs = () => ms;
        Send((Key)'/');
        Type("needle 6");
        Send(Key.Enter);
        Settle();
        Assert.Contains("Match 2026-11-18 10:00:06", _controller.StatusLeft, StringComparison.Ordinal);

        _source.HoldFinds = true;
        Send((Key)'n');
        ms = SearchController.ProgressDelayMs - 1;
        Assert.DoesNotContain("Searching", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.Contains("Match 2026-11-18 10:00:06", _controller.StatusLeft, StringComparison.Ordinal);

        _source.Finds[^1].Complete(Logvu.Core.Store.SearchResult.Found(Row(3, Preview(3))));
        Settle();

        Assert.Contains("Match 2026-11-18 10:00:03", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.DoesNotContain("Searching", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void A_read_error_of_a_search_is_shown_after_its_outcome()
    {
        _source.HoldFinds = true;
        Send((Key)'/');
        Type("needle");
        Send(Key.Enter);

        _source.Finds[^1].Complete(WithErrors(
            Logvu.Core.Store.SearchResult.Boundary,
            new MemberReadError(1, "/var/log/app.log", "access denied")));
        Settle();

        Assert.Contains("No more matches above · app.log: access denied", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_a_detail_cancels_a_running_scan()
    {
        _source.HoldFinds = true;
        Send((Key)'/');
        Type("needle");
        Send(Key.Enter);
        Assert.True(_controller.Timeline.HasFocus);

        Send(Key.Enter);

        Assert.NotNull(_controller.Detail);
        Assert.False(_controller.Search.IsScanning);
        Assert.True(_source.Finds[0].Token.IsCancellationRequested);
    }

    [Fact]
    public void A_detail_opened_with_a_query_scrolls_to_its_first_match_and_reveals_it_in_a_fold()
    {
        Send((Key)'/');
        Type("ExecutionContext");
        _controller.Timeline.SetFocus();
        OpenDetail();

        DetailView detail = _controller.Detail!;
        Assert.NotNull(detail.ActiveHit);
        Assert.Equal(detail.Document!.FullIndex(detail.CursorLine), detail.ActiveHit!.Value.Line);
        Assert.Contains("ExecutionContext", CursorText(detail), StringComparison.Ordinal);
    }

    [Fact]
    public void Within_a_detail_n_and_N_move_between_the_matches_reveal_folded_ones_and_report_the_end()
    {
        Send((Key)'/');
        Type("ExecutionContext");
        _controller.Timeline.SetFocus();
        OpenDetail();
        DetailView detail = _controller.Detail!;

        // Folding every fold hides the first match: its line is not in the document any more.
        Send(Key.Z);
        Assert.DoesNotContain(detail.Document!.Lines, line => line.Text.Contains("RunInternal", StringComparison.Ordinal));

        // Nothing comes before the fold's marker, so N reports the start of the event and does not wrap.
        Send((Key)'N');
        Assert.Contains("No more matches in this event", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.DoesNotContain(detail.Document!.Lines, line => line.Text.Contains("RunInternal", StringComparison.Ordinal));

        // n reveals the match inside the collapsed fold and puts the cursor on it.
        Send((Key)'n');
        Assert.Contains("RunInternal", CursorText(detail), StringComparison.Ordinal);
        Assert.Contains(detail.Document.Lines, line => line.Text.Contains("RunInternal", StringComparison.Ordinal));

        // The search is case-insensitive, so the parameter "executionContext" is a match too: the line of RunInternal has three.
        Send((Key)'n');
        Assert.Contains("RunInternal", CursorText(detail), StringComparison.Ordinal);
        Send((Key)'n');
        Assert.Contains("RunInternal", CursorText(detail), StringComparison.Ordinal);

        // Then the line of Run, which has three matches of its own: the cursor stays on that line for all of them.
        Send((Key)'n');
        string runLine = CursorText(detail);
        Assert.Contains("ExecutionContext.Run(", runLine, StringComparison.Ordinal);
        Send((Key)'n');
        Assert.Equal(runLine, CursorText(detail));
        Send((Key)'n');
        Assert.Equal(runLine, CursorText(detail));

        // The last match is passed: the end of the event is reported and the cursor stays.
        Send((Key)'n');
        Assert.Contains("No more matches in this event", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.Equal(runLine, CursorText(detail));
    }

    [Fact]
    public void Esc_in_the_detail_returns_to_the_timeline_and_the_query_stays_active()
    {
        Send((Key)'/');
        Type("needle");
        _controller.Timeline.SetFocus();
        OpenDetail();

        Send(Key.Esc);

        Assert.Null(_controller.Detail);
        Assert.Same(_controller.Timeline, _controller.Window.Content);
        Assert.True(Bar.Visible);
        Assert.True(_controller.Search.IsOpen);
        Assert.NotNull(_controller.Timeline.Search);
        Assert.StartsWith("n older  N newer  ", _controller.StatusHints, StringComparison.Ordinal);
    }

    [Fact]
    public void Slash_with_a_one_line_selection_in_the_detail_seeds_the_bar_and_makes_it_the_current_match()
    {
        OpenDetail();
        DetailView detail = _controller.Detail!;
        SelectInDetail(detail, line: 4, from: 9, to: 19);
        Assert.Equal("Run failed", detail.SelectionSeed()!.Value.Text);

        Send((Key)'/');

        Assert.Equal("Run failed", Bar.Query.Text);
        Assert.True(Bar.HasFieldFocus);
        Assert.Equal(new DetailHit(4, 9, 10), detail.ActiveHit);
        Assert.Equal("message: Run failed", CursorText(detail));
    }

    [Fact]
    public void Slash_with_a_selection_over_lines_leaves_the_query_as_it_was()
    {
        OpenDetail();
        DetailView detail = _controller.Detail!;
        Assert.True(detail.HandleKey(Key.CursorDown.WithShift));

        Send((Key)'/');

        Assert.Equal(string.Empty, Bar.Query.Text);
        Assert.Null(detail.ActiveHit);
    }

    [Fact]
    public void With_the_regex_option_on_the_seeded_text_is_escaped_and_keeps_the_option()
    {
        Send((Key)'/');
        Send(Key.R.WithAlt);
        Send(Key.Esc);
        Assert.False(Bar.Visible);

        OpenDetail();
        DetailView detail = _controller.Detail!;
        const string text = "Execute(Int32";
        int line = 6;
        int from = detail.Document!.Lines[line].Text.IndexOf(text, StringComparison.Ordinal);
        // The drag ends on the last character of the text, which the selection includes.
        SelectInDetail(detail, line, from, from + text.Length - 1);

        Send((Key)'/');

        Assert.Equal(Regex.Escape(text), Bar.Query.Text);
        Assert.True(Bar.Query.Regex);
        Assert.Equal(line, detail.CursorLine);
        Assert.Contains("Execute(Int32", CursorText(detail), StringComparison.Ordinal);
    }

    /// <summary>Selects the characters from <paramref name="from"/> to <paramref name="to"/> of a visible line of the detail, with the mouse.</summary>
    [Fact]
    public void Slash_with_a_selection_in_the_timeline_seeds_the_query_and_starts_no_scan()
    {
        // The newest row, r9 at 10:00:09, is the bottom one of the three rows: its first six characters are its hour, its minute and the colon.
        _controller.Timeline.Viewport = new System.Drawing.Rectangle(0, 0, 60, 4);
        _controller.Timeline.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(1, 3) });
        _controller.Timeline.HandleMouse(new Mouse { Flags = MouseFlags.PositionReport, Position = new(6, 3) });
        _controller.Timeline.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new(6, 3) });

        Assert.True(Send((Key)'/'));

        Assert.True(Bar.Visible);
        Assert.Equal("10:00:", Bar.Query.Text);
        Assert.Empty(_source.Finds);
        Assert.False(_controller.Search.IsScanning);
    }

    private void SelectInDetail(DetailView detail, int line, int from, int to)
    {
        // The detail is not in a running window here, so it gets its size directly; its rows are then measured at that width.
        detail.Frame = new System.Drawing.Rectangle(0, 0, 80, 30);
        detail.Layout(new System.Drawing.Size(80, 30));
        int width = Math.Max(1, detail.Viewport.Width - DetailView.Gutter);
        int row = Enumerable.Range(0, line).Sum(i => new DetailWrap(detail.Document!.Lines[i], width).RowCount);
        Assert.True(detail.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(DetailView.Gutter + from, row) }));
        Assert.True(detail.HandleMouse(new Mouse { Flags = MouseFlags.PositionReport, Position = new(DetailView.Gutter + to, row) }));
        Assert.True(detail.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new(DetailView.Gutter + to, row) }));
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

    /// <summary>Runs the posted continuations until the scan and the timeline's loads are done.</summary>
    private void Settle() => _posted.PumpUntil(
        () => !_controller.Search.IsScanning && !_controller.Model.IsLoading,
        "the search to finish");

    /// <summary>Opens the detail of the selected row on the timeline, and waits until it is loaded.</summary>
    private void OpenDetail()
    {
        Send(Key.Enter);
        _posted.PumpUntil(() => _controller.Detail?.Document is not null, "the detail");
    }

    private static string CursorText(DetailView detail) => detail.Document!.Lines[detail.CursorLine].Text;

    private RowKey RowKeyOf(int index) => Row(index, Preview(index)).Key;

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

    /// <summary>
    /// A search result with read errors. <c>WithErrors</c> is internal to Logvu.Core, which does not expose its internals to these tests, so
    /// it is called by reflection.
    /// </summary>
    private static Logvu.Core.Store.SearchResult WithErrors(Logvu.Core.Store.SearchResult result, params MemberReadError[] errors) =>
        (Logvu.Core.Store.SearchResult)typeof(Logvu.Core.Store.SearchResult)
            .GetMethod("WithErrors", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(result, [errors.ToList()])!;

    /// <summary>A detail source that answers every read with the same detail.</summary>
    private sealed class FixedDetailSource(EventDetail detail) : IDetailSource
    {
        public Task<EventDetail> ReadDetailAsync(RowKey key, CancellationToken ct) => Task.FromResult(detail);
    }
}
