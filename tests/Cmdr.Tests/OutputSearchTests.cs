using System.Reflection;
using Cmdr.Tui;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Cmdr.Tests;

/// <summary>
/// Finding matches in the output view: the origin of each Find, the wrap, pausing and scrolling to the match, leaving the view
/// unchanged when nothing matches, and the counts of the status.
/// </summary>
[Collection("Terminal GUI")]
public sealed class OutputSearchTests
{
    private static SearchPattern Literal(string text)
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(text, true, false, false), out string? error);
        Assert.Null(error);
        return Assert.IsType<SearchPattern>(pattern);
    }

    [Fact]
    public void Forward_find_without_an_active_match_starts_at_the_top_row_and_walks_the_matches_in_order()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 20, 3, "a1 a2", "b", "a3 a4");
        h.View.SetSearch(Literal("a"));

        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: true));
        Assert.Equal(new SearchStatus(4, 1), h.View.SearchStatus());
        Assert.Equal(0L, h.CursorLine);

        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: false));
        Assert.Equal(new SearchStatus(4, 2), h.View.SearchStatus());
        Assert.Equal(0L, h.CursorLine);

        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: false));
        Assert.Equal(new SearchStatus(4, 3), h.View.SearchStatus());
        Assert.Equal(2L, h.CursorLine);

        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: false));
        Assert.Equal(new SearchStatus(4, 4), h.View.SearchStatus());
        Assert.Equal(2L, h.CursorLine);

        // Past the last match the search wraps to the first one.
        Assert.Equal(SearchResult.Wrapped, h.View.Find(backward: false, inclusive: false));
        Assert.Equal(new SearchStatus(4, 1), h.View.SearchStatus());
        Assert.Equal(0L, h.CursorLine);
    }

    [Fact]
    public void Forward_find_without_an_active_match_takes_the_first_match_at_or_after_the_top_row_of_a_scrolled_view()
    {
        using var app = Application.Create();
        app.Init("ansi");

        // Two rows of view over four lines: the tail shows lines 2 and 3, so the matches on lines 0 and 1 are above the view.
        using OutputViewHarness h = OutputViewHarness.Create(app, 20, 2, "a", "b", "a", "a");
        Assert.Equal(new ScrollPosition(2, 0), h.EffectiveTop);
        h.View.SetSearch(Literal("a"));

        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: true));

        Assert.Equal(2L, h.CursorLine);
        Assert.Equal(new SearchStatus(3, 2), h.View.SearchStatus());
    }

    [Fact]
    public void Backward_find_without_an_active_match_starts_at_the_last_row_in_view_and_walks_back_in_order()
    {
        using var app = Application.Create();
        app.Init("ansi");

        // Home shows lines 0 and 1. The matches on lines 2 and 3 are below the view and are not reached first.
        using OutputViewHarness h = OutputViewHarness.Create(app, 20, 2, "a a", "a b", "a", "a");
        h.Home();
        h.View.SetSearch(Literal("a"));

        Assert.Equal(SearchResult.Found, h.View.Find(backward: true, inclusive: false));
        Assert.Equal(1L, h.CursorLine);
        Assert.Equal(new SearchStatus(5, 3), h.View.SearchStatus());

        Assert.Equal(SearchResult.Found, h.View.Find(backward: true, inclusive: false));
        Assert.Equal(0L, h.CursorLine);
        Assert.Equal(new SearchStatus(5, 2), h.View.SearchStatus());

        Assert.Equal(SearchResult.Found, h.View.Find(backward: true, inclusive: false));
        Assert.Equal(0L, h.CursorLine);
        Assert.Equal(new SearchStatus(5, 1), h.View.SearchStatus());

        // Before the first match the search wraps to the last match of the output.
        Assert.Equal(SearchResult.Wrapped, h.View.Find(backward: true, inclusive: false));
        Assert.Equal(3L, h.CursorLine);
        Assert.Equal(new SearchStatus(5, 5), h.View.SearchStatus());
    }

    [Fact]
    public void Inclusive_find_keeps_the_active_match_when_the_longer_query_still_matches_there()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 20, 3, "ab", "ab cd", "x");
        h.View.SetSearch(Literal("a"));
        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: true));
        Assert.Equal(0L, h.CursorLine);

        // The query grows while it is typed: "ab" still matches at the active match, so typing keeps it there.
        h.View.SetSearch(Literal("ab"));
        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: true));
        Assert.Equal(0L, h.CursorLine);
        Assert.Equal(new SearchStatus(2, 1), h.View.SearchStatus());

        // Without inclusion the search moves on to the next match.
        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: false));
        Assert.Equal(1L, h.CursorLine);
        Assert.Equal(new SearchStatus(2, 2), h.View.SearchStatus());
    }

    [Fact]
    public void Find_on_a_match_in_a_buffer_taller_than_the_view_pauses_and_scrolls_the_match_into_view()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 20, 3, "a", "b", "c", "d", "hit", "f");
        h.Home();
        h.View.SetSearch(Literal("hit"));

        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: true));

        Assert.False(h.Following);
        Assert.Equal(4L, h.CursorLine);
        // The match is brought in at the bottom of the view, which then shows lines 2 to 4.
        Assert.Equal(new ScrollPosition(2, 0), h.Top);
        Assert.Equal(new ScrollPosition(2, 0), h.EffectiveTop);
    }

    [Fact]
    public void Find_on_a_match_already_in_view_pauses_following_without_scrolling()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 20, 3, "a", "b", "c", "d", "hit", "f");
        Assert.True(h.Following);
        Assert.Equal(new ScrollPosition(3, 0), h.EffectiveTop);
        h.View.SetSearch(Literal("hit"));

        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: true));

        Assert.False(h.Following);
        Assert.Equal(4L, h.CursorLine);
        Assert.Equal(new ScrollPosition(3, 0), h.Top);
        Assert.Equal(new ScrollPosition(3, 0), h.EffectiveTop);
    }

    [Fact]
    public void A_match_on_a_late_row_of_a_line_taller_than_the_view_puts_that_row_at_the_top()
    {
        using var app = Application.Create();
        app.Init("ansi");

        // One line of 40 cells in 4 columns is 10 rows, so the match 'm' at cell 20 is on row 5 of a 3-row view.
        using OutputViewHarness h = OutputViewHarness.Create(app, 4, 3, new string('a', 20) + "m" + new string('b', 19));
        h.Home();
        h.View.SetSearch(Literal("m"));

        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: true));

        Assert.False(h.Following);
        Assert.Equal(0L, h.CursorLine);
        Assert.Equal(new ScrollPosition(0, 5), h.Top);
        Assert.Equal(new ScrollPosition(0, 5), h.EffectiveTop);
    }

    [Fact]
    public void Find_with_no_match_returns_not_found_and_leaves_following_top_and_cursor_unchanged()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 20, 3, "a", "b", "c", "d", "e", "f");
        h.View.SetSearch(Literal("zz"));

        Assert.Equal(SearchResult.NotFound, h.View.Find(backward: false, inclusive: true));
        Assert.Equal(SearchResult.NotFound, h.View.Find(backward: true, inclusive: false));

        Assert.True(h.Following);
        Assert.Equal(new ScrollPosition(3, 0), h.EffectiveTop);
        Assert.Equal(5L, h.CursorLine);

        h.Home();
        Assert.Equal(SearchResult.NotFound, h.View.Find(backward: false, inclusive: true));
        Assert.Equal(SearchResult.NotFound, h.View.Find(backward: true, inclusive: false));

        Assert.False(h.Following);
        Assert.Equal(new ScrollPosition(0, 0), h.Top);
        Assert.Equal(0L, h.CursorLine);
    }

    [Fact]
    public void Find_without_a_pattern_or_without_lines_returns_not_found()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness empty = OutputViewHarness.Create(app, 20, 3);
        empty.View.SetSearch(Literal("a"));
        Assert.Equal(SearchResult.NotFound, empty.View.Find(backward: false, inclusive: true));
        Assert.Equal(new SearchStatus(0, 0), empty.View.SearchStatus());

        using OutputViewHarness h = OutputViewHarness.Create(app, 20, 3, "a");
        Assert.Equal(SearchResult.NotFound, h.View.Find(backward: false, inclusive: true));
    }

    [Fact]
    public void Find_moves_onto_a_partial_line_and_counts_it()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 20, 3, "a");
        h.Output.WriteStdOut("ab");
        h.View.SetSearch(Literal("a"));

        Assert.Equal(new SearchStatus(2, 0), h.View.SearchStatus());
        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: true));
        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: false));

        Assert.Equal(1L, h.CursorLine);
        Assert.Equal(new SearchStatus(2, 2), h.View.SearchStatus());
    }

    [Fact]
    public void Status_counts_the_matches_of_all_lines_and_numbers_the_active_one_within_its_line()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 20, 3, "ab ab", "x", "ab");
        h.View.SetSearch(Literal("ab"));
        Assert.Equal(new SearchStatus(3, 0), h.View.SearchStatus());

        // The second match of the first line is number 2, and the match on the last line is number 3.
        h.View.Find(backward: false, inclusive: true);
        Assert.Equal(new SearchStatus(3, 1), h.View.SearchStatus());
        h.View.Find(backward: false, inclusive: false);
        Assert.Equal(new SearchStatus(3, 2), h.View.SearchStatus());
        h.View.Find(backward: false, inclusive: false);
        Assert.Equal(new SearchStatus(3, 3), h.View.SearchStatus());

        // Output that arrives later is counted, and the active match keeps its number.
        h.Output.WriteSystem("ab");
        Assert.Equal(new SearchStatus(4, 3), h.View.SearchStatus());
    }

    [Fact]
    public void Status_has_no_active_number_after_the_search_ends_or_another_entry_is_shown()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 20, 3, "ab", "ab");
        h.View.SetSearch(Literal("ab"));
        h.View.Find(backward: false, inclusive: true);
        Assert.Equal(1, h.View.SearchStatus().Active);

        h.View.SetSearch(null);
        Assert.Equal(new SearchStatus(0, 0), h.View.SearchStatus());

        h.View.SetSearch(Literal("ab"));
        Assert.Equal(new SearchStatus(2, 0), h.View.SearchStatus());
        h.View.Find(backward: false, inclusive: true);
        Assert.Equal(1, h.View.SearchStatus().Active);

        h.View.Show(h.OtherEntry);
        Assert.Equal(0, h.View.SearchStatus().Active);
    }

    /// <summary>Selects from one cell of the view to another, with the view's own mouse handling (press, drag, release). The end cell is included.</summary>
    private static void Select(OutputViewHarness h, int fromX, int fromY, int toX, int toY)
    {
        h.Press(fromX, fromY);
        h.Drag(toX, toY);
        h.Release(toX, toY);
    }

    private static bool KeyDown(OutputView view, Key key) =>
        (bool)typeof(OutputView).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, new object[] { key })!;

    [Fact]
    public void Seed_is_the_selected_text_with_its_line_and_offset_within_one_line()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 40, 6, "hit one", "x hit two", "y");

        // The cells 2 to 4 of line 1: "hit".
        Select(h, 2, 1, 4, 1);

        Assert.True(h.View.HasSelection);
        Assert.Equal(new SearchSeed("hit", 1L, 2), h.View.SelectionSeed());
    }

    [Fact]
    public void Seed_is_null_without_a_selection_and_after_a_press_without_a_drag()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 40, 6, "hit one", "x hit two", "y");
        Assert.Null(h.View.SelectionSeed());

        Select(h, 2, 1, 2, 1);

        Assert.False(h.View.HasSelection);
        Assert.Null(h.View.SelectionSeed());
    }

    [Fact]
    public void Seed_is_null_for_a_selection_across_lines()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 40, 6, "hit one", "x hit two", "y");

        Select(h, 2, 0, 1, 1);

        Assert.True(h.View.HasSelection);
        Assert.Null(h.View.SelectionSeed());
    }

    [Fact]
    public void Seed_is_null_for_a_line_selection_within_one_line()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 40, 6, "hit one", "x hit two");

        // Shift+Up then Shift+Down from the cursor line (the last line) selects that line in line mode.
        Assert.True(KeyDown(h.View, Key.CursorUp.WithShift));
        Assert.True(KeyDown(h.View, Key.CursorDown.WithShift));

        Assert.NotNull(h.Selection);
        Assert.Null(h.View.SelectionSeed());
    }

    [Fact]
    public void Seed_is_limited_to_200_characters()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 300, 3, new string('a', 250));

        Select(h, 0, 0, 199, 0);
        Assert.Equal(200, h.View.SelectionSeed()!.Value.Text.Length);

        Select(h, 0, 0, 200, 0);
        Assert.Null(h.View.SelectionSeed());
    }

    /// <summary>The selected occurrence is the first match even when an earlier one is visible above it.</summary>
    [Fact]
    public void Activating_the_seed_makes_the_selected_occurrence_the_first_find_below_an_earlier_one()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 40, 6, "hit one", "x hit two", "y");
        Select(h, 2, 1, 4, 1);
        SearchSeed? seed = h.View.SelectionSeed();
        Assert.Equal(new SearchSeed("hit", 1L, 2), seed);

        h.View.SetSearch(Literal("hit"));
        h.View.ActivateSeed(seed!.Value);

        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: true));
        Assert.Equal(new SearchStatus(2, 2), h.View.SearchStatus());
        Assert.Equal(1L, h.CursorLine);
        Assert.True(h.View.HasSelection);
    }

    /// <summary>When the selected text is not a match (a part of a word under Whole word), the search goes on from its start.</summary>
    [Fact]
    public void Activating_a_seed_that_is_not_a_match_continues_the_search_forward_from_its_start()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, 40, 6, "hit one", "x hit two", "hit three");
        Select(h, 3, 1, 4, 1);
        SearchSeed? seed = h.View.SelectionSeed();
        Assert.Equal(new SearchSeed("it", 1L, 3), seed);

        SearchPattern? wholeWord = SearchPattern.Create(new SearchQuery("hit", CaseSensitive: true, WholeWord: true, Regex: false), out _);
        h.View.SetSearch(wholeWord);
        h.View.ActivateSeed(seed!.Value);

        Assert.Equal(SearchResult.Found, h.View.Find(backward: false, inclusive: true));
        Assert.Equal(new SearchStatus(3, 3), h.View.SearchStatus());
        Assert.Equal(2L, h.CursorLine);
    }
}
