using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tests;

/// <summary>
/// Searching the details strip: the origin of each Find, the wrap, scrolling to a match (including one on a wrapped row), the counts
/// of the status, and the highlights in the draw. The rows of the group used here are the mode line, the working directory line,
/// then the description lines, each one row at the width of <see cref="Wide"/>.
/// </summary>
[Collection("Terminal GUI")]
public sealed class DetailsSearchTests
{
    private const int Wide = 200;
    private const string Hit = "HIT";

    /// <summary>The description lines of most tests: logical lines 2 to 8 of the group.</summary>
    private static readonly string[] Lines = { "HIT a HIT b", "c", "d HIT", "e", "f", "g HIT", "h" };

    /// <summary>One cell of a frame: its grapheme and its attribute as the driver holds it.</summary>
    private readonly record struct Painted(string Grapheme, Attribute? Attribute);

    /// <summary>The pattern for a case-sensitive literal query.</summary>
    private static SearchPattern Literal(string text)
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(text, CaseSensitive: true, WholeWord: false, Regex: false), out string? error);
        Assert.Null(error);
        return Assert.IsType<SearchPattern>(pattern);
    }

    /// <summary>A group with the description, shown by name in the tests.</summary>
    private static GroupSpec Group(string name, string description) => new() { Name = name, Description = description };

    /// <summary>The description of the group of <see cref="Lines"/>.</summary>
    private static string Described() => string.Join("\n", Lines);

    /// <summary>The row of the group with the given name, as the list shows it.</summary>
    private static ListRow GroupRowNamed(TempSession temp, string name) =>
        temp.Session.BuildRows().OfType<GroupRow>().Single(row => DetailsText.Title(row) == name);

    /// <summary>
    /// A details view of the given size that shows the first group, with the session and the application it needs. Disposing it
    /// disposes all three.
    /// </summary>
    private sealed class Shown : IDisposable
    {
        private readonly TempSession _temp;
        private readonly IApplication _app;
        private readonly MainController _controller;

        private Shown(TempSession temp, IApplication app, MainController controller, DetailsView details)
        {
            _temp = temp;
            _app = app;
            _controller = controller;
            Details = details;
        }

        /// <summary>The view under test.</summary>
        public DetailsView Details { get; }

        /// <summary>Opens the groups and shows the first one in a view of the given size, laid out.</summary>
        public static Shown Open(int width, int height, params GroupSpec[] groups)
        {
            TempSession temp = TempSession.Open(groups);
            IApplication app = Application.Create();
            app.Init("ansi");
            var controller = new MainController(app, temp.Session);
            controller.Build();
            var details = new DetailsView(controller) { X = 0, Y = 0, Width = width, Height = height };
            var shown = new Shown(temp, app, controller, details);
            shown.ShowGroup(groups[0].Name);
            return shown;
        }

        /// <summary>Shows the group with the given name and lays the view out again.</summary>
        public void ShowGroup(string name)
        {
            Details.Show(GroupRowNamed(_temp, name), Path.GetFullPath(_temp.Session.ConfigDir));
            Details.Layout();
        }

        /// <summary>Disposes the controller, the application and the session.</summary>
        public void Dispose()
        {
            _controller.Dispose();
            _app.Dispose();
            _temp.Dispose();
        }
    }

    /// <summary>Without an active match, the first Find takes the first match in the top row onward, the next ones follow in order, and the one past the end wraps.</summary>
    [Fact]
    public void Forward_find_without_an_active_match_starts_at_the_top_row_and_walks_the_matches_in_order()
    {
        using Shown shown = Shown.Open(Wide, 3, Group("g", Described()));
        DetailsView details = shown.Details;
        Assert.Equal(9, details.Scroll.Total);
        details.SetSearch(Literal(Hit));

        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: true));
        Assert.Equal(new SearchStatus(4, 1), details.SearchStatus());
        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: false));
        Assert.Equal(new SearchStatus(4, 2), details.SearchStatus());
        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: false));
        Assert.Equal(new SearchStatus(4, 3), details.SearchStatus());
        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: false));
        Assert.Equal(new SearchStatus(4, 4), details.SearchStatus());

        Assert.Equal(SearchResult.Wrapped, details.Find(backward: false, inclusive: false));
        Assert.Equal(new SearchStatus(4, 1), details.SearchStatus());
        Assert.Equal(2, details.Scroll.Offset);
    }

    /// <summary>Without an active match, a forward search starts at the top visible row, so matches above a scrolled view are skipped.</summary>
    [Fact]
    public void Forward_find_without_an_active_match_starts_at_the_top_visible_row_when_scrolled()
    {
        using Shown shown = Shown.Open(Wide, 3, Group("g", Described()));
        DetailsView details = shown.Details;
        details.SetSearch(Literal(Hit));
        details.Find(backward: false, inclusive: true);
        details.Find(backward: false, inclusive: false);
        details.Find(backward: false, inclusive: false);
        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: false));
        Assert.Equal(5, details.Scroll.Offset);

        // Clearing the search drops the active match but keeps the scroll position: the top row is now row 5.
        details.SetSearch(null);
        details.SetSearch(Literal(Hit));
        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: true));
        Assert.Equal(new SearchStatus(4, 4), details.SearchStatus());
        Assert.Equal(5, details.Scroll.Offset);
    }

    /// <summary>Without an active match, a backward search starts at the end of the last visible row, and wraps to the last match.</summary>
    [Fact]
    public void Backward_find_without_an_active_match_starts_at_the_last_visible_row_and_wraps()
    {
        using Shown shown = Shown.Open(Wide, 3, Group("g", Described()));
        DetailsView details = shown.Details;
        details.SetSearch(Literal(Hit));

        // Rows 0 to 2 are shown, so the last visible row is "HIT a HIT b", and the search starts after its end.
        Assert.Equal(SearchResult.Found, details.Find(backward: true, inclusive: false));
        Assert.Equal(new SearchStatus(4, 2), details.SearchStatus());
        Assert.Equal(SearchResult.Found, details.Find(backward: true, inclusive: false));
        Assert.Equal(new SearchStatus(4, 1), details.SearchStatus());

        // Before the first match the search wraps to the last one, which is below the view.
        Assert.Equal(SearchResult.Wrapped, details.Find(backward: true, inclusive: false));
        Assert.Equal(new SearchStatus(4, 4), details.SearchStatus());
        Assert.Equal(5, details.Scroll.Offset);
    }

    /// <summary>Without an active match, a backward search from a scrolled view starts at the end of its last visible row.</summary>
    [Fact]
    public void Backward_find_without_an_active_match_starts_at_the_end_of_the_last_visible_row_when_scrolled()
    {
        using Shown shown = Shown.Open(Wide, 3, Group("g", Described()));
        DetailsView details = shown.Details;
        details.SetSearch(Literal(Hit));
        details.Find(backward: false, inclusive: true);
        details.Find(backward: false, inclusive: false);
        details.Find(backward: false, inclusive: false);
        details.Find(backward: false, inclusive: false);
        Assert.Equal(5, details.Scroll.Offset);

        // Rows 5 to 7 are shown, and the match on row 7 is the last one before the end of that row.
        details.SetSearch(null);
        details.SetSearch(Literal(Hit));
        Assert.Equal(SearchResult.Found, details.Find(backward: true, inclusive: false));
        Assert.Equal(new SearchStatus(4, 4), details.SearchStatus());
        Assert.Equal(5, details.Scroll.Offset);
    }

    /// <summary>An inclusive search keeps the active match when a longer query still matches it at its start; a non-inclusive one moves on.</summary>
    [Fact]
    public void Inclusive_find_keeps_the_active_match_when_a_longer_query_still_matches_it()
    {
        using Shown shown = Shown.Open(Wide, 3, Group("g", Described()));
        DetailsView details = shown.Details;
        details.SetSearch(Literal("HI"));
        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: true));
        Assert.Equal(new SearchStatus(4, 1), details.SearchStatus());

        details.SetSearch(Literal(Hit));
        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: true));
        Assert.Equal(new SearchStatus(4, 1), details.SearchStatus());

        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: false));
        Assert.Equal(new SearchStatus(4, 2), details.SearchStatus());
    }

    /// <summary>A match below the view scrolls so that its row is the last visible one.</summary>
    [Fact]
    public void A_match_below_the_view_scrolls_so_its_row_is_the_last_visible_row()
    {
        using Shown shown = Shown.Open(Wide, 3, Group("g", Described()));
        DetailsView details = shown.Details;
        details.SetSearch(Literal(Hit));
        details.Find(backward: false, inclusive: true);
        details.Find(backward: false, inclusive: false);
        Assert.Equal(0, details.Scroll.Offset);

        // The match on row 4 is below rows 0 to 2, so row 4 becomes the last visible row.
        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: false));
        Assert.Equal(2, details.Scroll.Offset);
        Assert.Equal(new SearchStatus(4, 3), details.SearchStatus());
    }

    /// <summary>A match above the view scrolls so that its row is the top visible one.</summary>
    [Fact]
    public void A_match_above_the_view_scrolls_so_its_row_is_the_top_row()
    {
        using Shown shown = Shown.Open(Wide, 3, Group("g", Described()));
        DetailsView details = shown.Details;
        details.SetSearch(Literal(Hit));
        details.Find(backward: false, inclusive: true);
        details.Find(backward: false, inclusive: false);
        details.Find(backward: false, inclusive: false);
        details.Find(backward: false, inclusive: false);
        Assert.Equal(5, details.Scroll.Offset);

        // The match on row 4 is above rows 5 to 7, so row 4 becomes the top row.
        Assert.Equal(SearchResult.Found, details.Find(backward: true, inclusive: false));
        Assert.Equal(4, details.Scroll.Offset);
        Assert.Equal(new SearchStatus(4, 3), details.SearchStatus());
    }

    /// <summary>A match that is already shown does not scroll the view.</summary>
    [Fact]
    public void A_match_that_is_already_shown_does_not_scroll()
    {
        using Shown shown = Shown.Open(Wide, 3, Group("g", Described()));
        DetailsView details = shown.Details;
        details.SetSearch(Literal(Hit));
        details.Find(backward: false, inclusive: true);
        details.Find(backward: false, inclusive: false);
        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: false));
        Assert.Equal(2, details.Scroll.Offset);

        // The match on row 2 is the top visible row, so the view stays where it is.
        Assert.Equal(SearchResult.Found, details.Find(backward: true, inclusive: false));
        Assert.Equal(2, details.Scroll.Offset);
        Assert.Equal(new SearchStatus(4, 2), details.SearchStatus());
    }

    /// <summary>A match that starts on the second wrapped row of a long line scrolls to that row, not to the line's first row.</summary>
    [Fact]
    public void A_match_on_the_second_row_of_a_wrapped_line_scrolls_to_that_row()
    {
        // The long line wraps at 200 columns into two rows: the match is on the second, which is row 3 of the rows of the group.
        string description = new string('.', 290) + " HIT" + "\nx";
        using Shown shown = Shown.Open(Wide, 3, Group("g", description));
        DetailsView details = shown.Details;
        Assert.Equal(5, details.Scroll.Total);
        details.SetSearch(Literal(Hit));

        Assert.Equal(SearchResult.Found, details.Find(backward: false, inclusive: true));
        Assert.Equal(new SearchStatus(1, 1), details.SearchStatus());
        Assert.Equal(1, details.Scroll.Offset);
    }

    /// <summary>A search with no match returns NotFound, changes nothing in the view, and leaves the status at zero.</summary>
    [Fact]
    public void Find_without_a_match_returns_not_found_and_changes_nothing()
    {
        using Shown shown = Shown.Open(Wide, 3, Group("g", Described()));
        DetailsView details = shown.Details;
        details.SetSearch(Literal(Hit));
        details.Find(backward: false, inclusive: true);
        details.Find(backward: false, inclusive: false);
        details.Find(backward: false, inclusive: false);
        Assert.Equal(2, details.Scroll.Offset);

        details.SetSearch(Literal("NOPE"));
        Assert.Equal(SearchResult.NotFound, details.Find(backward: false, inclusive: true));
        Assert.Equal(SearchResult.NotFound, details.Find(backward: true, inclusive: false));
        Assert.Equal(2, details.Scroll.Offset);
        Assert.Equal(new SearchStatus(0, 0), details.SearchStatus());
    }

    /// <summary>The status counts every match and numbers the active one. It has no active number after the search ends, or after another item is shown.</summary>
    [Fact]
    public void Status_counts_the_matches_and_numbers_the_active_one_until_the_search_ends_or_the_item_changes()
    {
        using Shown shown = Shown.Open(Wide, 3, Group("g", Described()), Group("other", "HIT once"));
        DetailsView details = shown.Details;
        details.SetSearch(Literal(Hit));
        Assert.Equal(new SearchStatus(4, 0), details.SearchStatus());

        details.Find(backward: false, inclusive: true);
        Assert.Equal(new SearchStatus(4, 1), details.SearchStatus());

        details.SetSearch(null);
        Assert.Equal(new SearchStatus(0, 0), details.SearchStatus());

        details.SetSearch(Literal(Hit));
        details.Find(backward: false, inclusive: true);
        shown.ShowGroup("other");
        Assert.Equal(new SearchStatus(1, 0), details.SearchStatus());
    }

    /// <summary>Matched characters take the match color, the active match takes the active color, and every other character keeps its attribute.</summary>
    [Fact]
    public void Matched_characters_take_the_match_color_and_the_active_match_the_active_color()
    {
        IReadOnlyList<Painted[][]> frames = Frames(
            Wide,
            6,
            "HIT xy HIT",
            _ => { },
            view =>
            {
                view.SetSearch(Literal(Hit));
                view.Find(backward: false, inclusive: true);
            });

        int row = Array.FindIndex(frames[0], cells => Text(cells).StartsWith("HIT xy", StringComparison.Ordinal));
        Assert.True(row >= 0);

        // The first match is the active one (columns 0 to 2); the second is an ordinary match (columns 7 to 9).
        var active = new HashSet<(int Row, int Column)>();
        var matched = new HashSet<(int Row, int Column)>();
        for (int column = 0; column < 3; column++)
        {
            active.Add((row, column));
        }

        for (int column = 7; column < 10; column++)
        {
            matched.Add((row, column));
        }

        AssertHighlights(frames[0], frames[1], active, matched);
    }

    /// <summary>Clearing the search removes every highlight, so the frame is the one drawn before the search.</summary>
    [Fact]
    public void No_character_is_highlighted_after_the_search_ends()
    {
        IReadOnlyList<Painted[][]> frames = Frames(
            Wide,
            6,
            "HIT xy HIT",
            _ => { },
            view =>
            {
                view.SetSearch(Literal(Hit));
                view.Find(backward: false, inclusive: true);
            },
            view => view.SetSearch(null));

        for (int row = 0; row < 6; row++)
        {
            for (int column = 0; column < Wide; column++)
            {
                Assert.Equal(frames[0][row][column], frames[2][row][column]);
            }
        }
    }

    /// <summary>A match that wraps over a row break is highlighted on the cells of both rows.</summary>
    [Fact]
    public void A_match_across_a_wrap_point_is_highlighted_on_both_rows()
    {
        // At six columns "HELLOWORLD" wraps to "HELLOW" and "ORLD". The match "LOWO" covers "LOW" on the first row and "O" on the second.
        IReadOnlyList<Painted[][]> frames = Frames(
            6,
            20,
            "HELLOWORLD",
            _ => { },
            view =>
            {
                view.SetSearch(Literal("LOWO"));
                view.Find(backward: false, inclusive: true);
            });

        int first = Array.FindIndex(frames[0], cells => Text(cells).TrimEnd() == "HELLOW");
        int second = Array.FindIndex(frames[0], cells => Text(cells).TrimEnd() == "ORLD");
        Assert.True(first >= 0 && second == first + 1);

        var active = new HashSet<(int Row, int Column)> { (first, 3), (first, 4), (first, 5), (second, 0) };
        AssertHighlights(frames[0], frames[1], active, new HashSet<(int Row, int Column)>());
    }

    /// <summary>The text of a row of cells.</summary>
    private static string Text(Painted[] cells) => string.Concat(cells.Select(cell => cell.Grapheme));

    /// <summary>
    /// Checks each cell of the highlighted frame against the plain one: the same grapheme, the active color on the cells of
    /// <paramref name="active"/>, the match color on those of <paramref name="matched"/>, and the plain attribute on the rest.
    /// </summary>
    private static void AssertHighlights(
        Painted[][] plain,
        Painted[][] highlighted,
        IReadOnlySet<(int Row, int Column)> active,
        IReadOnlySet<(int Row, int Column)> matched)
    {
        for (int row = 0; row < plain.Length; row++)
        {
            for (int column = 0; column < plain[row].Length; column++)
            {
                Painted before = plain[row][column];
                Painted after = highlighted[row][column];
                Assert.Equal(before.Grapheme, after.Grapheme);

                Attribute? expected = before.Attribute;
                if (active.Contains((row, column)))
                {
                    expected = SearchColors.Active(before.Attribute!.Value);
                }
                else if (matched.Contains((row, column)))
                {
                    expected = SearchColors.Match(before.Attribute!.Value);
                }

                Assert.Equal(expected, after.Attribute);
            }
        }
    }

    /// <summary>
    /// Draws the group with the description in a view of <paramref name="width"/> × <paramref name="height"/>, one frame per step. Each
    /// step runs before its frame. Returns each frame's cells, row by row.
    /// </summary>
    private static IReadOnlyList<Painted[][]> Frames(int width, int height, string description, params Action<DetailsView>[] steps)
    {
        using TempSession temp = TempSession.Open(Group("g", description));
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);

        var frames = new List<Painted[][]>();
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        var details = new DetailsView(controller) { X = 0, Y = 0, Width = width, Height = height };
        details.Show(GroupRowNamed(temp, "g"), Path.GetFullPath(temp.Session.ConfigDir));

        var window = new Window { X = 0, Y = 0, Width = width, Height = height, BorderStyle = LineStyle.None };
        window.Add(details);
        int frame = 0;
        app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            steps[frame](details);
            app.LayoutAndDraw();
            var driver = app.Driver!;
            var rows = new Painted[height][];
            for (int r = 0; r < height; r++)
            {
                rows[r] = new Painted[width];
                for (int c = 0; c < width; c++)
                {
                    rows[r][c] = new Painted(driver.Contents![r, c].Grapheme, driver.Contents[r, c].Attribute);
                }
            }

            frames.Add(rows);
            frame++;
            if (frame == steps.Length)
            {
                app.RequestStop(window);
                return false;
            }

            return true;
        });
        app.Run(window);
        return frames;
    }
}
