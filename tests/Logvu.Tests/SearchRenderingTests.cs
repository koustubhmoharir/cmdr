using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Logvu.Tui;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tests;

/// <summary>
/// The search's highlights as drawn: the matches of the timeline's source and message cells, the active row, and the matches of a detail,
/// including the active match, and a match that wraps across rows.
/// </summary>
[Collection("Terminal GUI")]
public sealed class SearchRenderingTests
{
    private static readonly Color BrightYellow = new(ColorName16.BrightYellow);

    [Fact]
    public void Matches_in_the_message_and_source_cells_are_drawn_in_the_match_colors()
    {
        TimelineView view = Timeline(search: "needle", "row zero", "needle here", "row two");

        TerminalScreen screen = Draw(view)[0];

        Attribute match = screen.At("needle");
        Assert.Equal(SearchColors.Match(screen.Normal), match);
        Assert.Equal(screen.Normal, screen.At("row zero"));
    }

    [Fact]
    public void The_source_column_is_searched_as_drawn()
    {
        TimelineView view = Timeline(search: "Logvu", "row zero", "row one", "row two");

        TerminalScreen screen = Draw(view)[0];

        Assert.Equal(SearchColors.Match(screen.Normal), screen.At("Logvu"));
    }

    [Fact]
    public void The_time_and_level_cells_are_not_searched()
    {
        TimelineView view = Timeline(search: "10:00", "row zero", "row one", "row two");

        TerminalScreen screen = Draw(view)[0];

        Assert.DoesNotContain(screen.Cells.SelectMany(row => row), cell => cell.Attribute.Background == BrightYellow);
    }

    [Fact]
    public void The_matches_of_the_active_row_are_drawn_in_the_active_colors_and_the_others_in_the_match_colors()
    {
        TimelineView view = Timeline(search: "needle", "needle zero", "needle one", "row two");
        view.ActiveRow = new RowKey(1, 100);

        TerminalScreen screen = Draw(view)[0];

        Assert.Equal(SearchColors.Active(screen.Normal), screen.At("needle one"));
        Assert.Equal(SearchColors.Match(screen.Normal), screen.At("needle zero"));
    }

    [Fact]
    public void A_match_on_the_selected_row_is_drawn_in_the_match_colors_so_it_stays_legible()
    {
        TimelineView view = Timeline(search: "two", "row zero", "row one", "row two");

        TerminalScreen screen = Draw(view)[0];

        Attribute match = screen.At("two");
        Assert.Equal(BrightYellow, match.Background);
        Assert.Equal(new Color(ColorName16.Black), match.Foreground);
    }

    [Fact]
    public void Every_match_of_a_detail_is_drawn_in_the_match_colors_even_across_wrapped_rows()
    {
        string message = string.Join(' ', Enumerable.Repeat("alpha", 12));
        var view = new DetailView();
        view.Load(DetailFixtures.Text("INFO 2026-11-18 10:20:31,007 [Worker 3] SheetKraft.Engine.Runner - " + message), DetailFixtures.Config());
        view.Search = Pattern("alpha");

        List<TerminalScreen> screens = TerminalHarness.Run(view, 40, 30);

        int highlighted = screens[0].Cells.Sum(row => row.Count(cell => cell.Attribute.Background == BrightYellow));
        Assert.Equal(12 * "alpha".Length, highlighted);
    }

    [Fact]
    public void The_active_match_of_a_detail_is_drawn_in_the_active_colors()
    {
        var view = new DetailView();
        view.Load(DetailFixtures.Text(DetailFixtures.StackEvent), DetailFixtures.Config());
        SearchPattern pattern = Pattern("Runner");
        view.Search = pattern;
        DetailHit first = DetailSearch.First(view.Document!.FullLines, pattern)!.Value;

        List<TerminalScreen> screens = TerminalHarness.Run(view, 100, 30, () => view.ShowHit(first));

        // Screen 0 is the first draw, with the first match drawn in the match colors; screen 1 after the hit was shown.
        Assert.Equal(SearchColors.Match(screens[0].Normal), screens[0].At("Runner"));
        Assert.Equal(SearchColors.Active(screens[1].Normal).Background, screens[1].At("Runner").Background);
    }

    /// <summary>A timeline with the given messages as its rows, drawn in a 60 by 5 view with the search's pattern.</summary>
    private static TimelineView Timeline(string search, params string[] previews)
    {
        var source = new FakeTimelineSource(previews.Select((preview, index) => Row(index, preview)));
        var model = new TimelineModel(source, action => action(), FakeTimelineSource.CompareKeys);
        model.Start(previews.Length);
        return new TimelineView(model, TimeZoneInfo.Utc) { Search = Pattern(search) };
    }

    private static List<TerminalScreen> Draw(TimelineView view) => TerminalHarness.Run(view, 60, 5);

    private static SearchPattern Pattern(string text) =>
        SearchPattern.Create(new SearchQuery(text, CaseSensitive: false, WholeWord: false, Regex: false), out _)!;

    private static EventRow Row(int index, string preview) => new(
        new RowKey(1, index * 100L),
        new EventExtent(0, 0, 0, Provisional: false, Orphan: false),
        new DateTimeOffset(2026, 11, 18, 10, 0, index, TimeSpan.Zero),
        null,
        LogLevel.Unknown,
        null,
        "Logvu.Test",
        preview,
        IsRaw: false,
        Orphan: false,
        Provisional: false);
}
