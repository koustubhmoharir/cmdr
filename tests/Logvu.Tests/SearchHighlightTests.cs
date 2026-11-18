using Logvu.Tui;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tests;

/// <summary>The runs a text is drawn in for its matches: plain text between the matches, and each match in a run of its own.</summary>
public sealed class SearchHighlightTests
{
    // Matches "XYZ" at 3 and 9.
    private const string Text = "abcXYZdefXYZ";

    private static readonly SearchMatch[] Matches = [new SearchMatch(3, 3), new SearchMatch(9, 3)];

    [Fact]
    public void Without_matches_the_range_is_one_plain_run()
    {
        IReadOnlyList<SearchRun> runs = SearchHighlight.Split(Text, 0, Text.Length, null);

        Assert.Equal([new SearchRun(0, Text.Length, SearchRunKind.Plain)], runs);
    }

    [Fact]
    public void Text_between_matches_is_plain_and_each_match_is_a_run_of_its_own()
    {
        IReadOnlyList<SearchRun> runs = SearchHighlight.Split(Text, 0, Text.Length, Matches);

        Assert.Equal(
            [
                new SearchRun(0, 3, SearchRunKind.Plain),
                new SearchRun(3, 6, SearchRunKind.Match),
                new SearchRun(6, 9, SearchRunKind.Plain),
                new SearchRun(9, 12, SearchRunKind.Match),
            ],
            runs);
    }

    [Fact]
    public void The_active_match_is_a_run_of_the_active_kind()
    {
        IReadOnlyList<SearchRun> runs = SearchHighlight.Split(Text, 0, Text.Length, Matches, match => match.Start == 9);

        Assert.Equal(SearchRunKind.Match, runs[1].Kind);
        Assert.Equal(SearchRunKind.Active, runs[3].Kind);
    }

    [Fact]
    public void A_range_cuts_the_matches_at_its_edges_and_drops_the_text_outside_it()
    {
        IReadOnlyList<SearchRun> runs = SearchHighlight.Split(Text, 4, 10, Matches);

        Assert.Equal(
            [
                new SearchRun(4, 6, SearchRunKind.Match),
                new SearchRun(6, 9, SearchRunKind.Plain),
                new SearchRun(9, 10, SearchRunKind.Match),
            ],
            runs);
    }

    [Fact]
    public void A_match_is_widened_to_whole_grapheme_clusters()
    {
        // "e" followed by a combining acute accent is one cluster, so a match of the "e" alone covers both units of it.
        const string text = "éx";

        IReadOnlyList<SearchRun> runs = SearchHighlight.Split(text, 0, text.Length, [new SearchMatch(0, 1)]);

        Assert.Equal([new SearchRun(0, 2, SearchRunKind.Match), new SearchRun(2, 3, SearchRunKind.Plain)], runs);
    }

    [Fact]
    public void An_empty_range_has_no_runs()
    {
        Assert.Empty(SearchHighlight.Split(Text, 5, 5, Matches));
    }

    [Fact]
    public void Paint_gives_matches_the_match_colors_and_leaves_plain_text_alone()
    {
        var attribute = new Attribute(Terminal.Gui.Drawing.Color.White, Terminal.Gui.Drawing.Color.Black, TextStyle.Faint);

        Assert.Equal(SearchColors.Match(attribute), SearchHighlight.Paint(attribute, SearchRunKind.Match));
        Assert.Equal(SearchColors.Active(attribute), SearchHighlight.Paint(attribute, SearchRunKind.Active));
        Assert.Equal(attribute, SearchHighlight.Paint(attribute, SearchRunKind.Plain));
    }
}
