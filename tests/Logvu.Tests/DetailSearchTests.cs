using Logvu.Core.Detail;
using Logvu.Tui;
using Shared.Core;

namespace Logvu.Tests;

/// <summary>The matches of a pattern among the lines of a detail, in document order, for the moves of n and N.</summary>
public sealed class DetailSearchTests
{
    private static readonly SearchPattern Alpha = Pattern("alpha");

    // Line 0 has two matches, line 1 none, line 2 one.
    private static readonly IReadOnlyList<DetailLine> Lines =
    [
        Line("alpha beta alpha"),
        Line("gamma"),
        Line("delta alpha"),
    ];

    [Fact]
    public void First_is_the_first_match_of_the_document()
    {
        Assert.Equal(new DetailHit(0, 0, 5), DetailSearch.First(Lines, Alpha));
    }

    [Fact]
    public void Next_from_the_start_of_a_line_takes_its_first_match()
    {
        Assert.Equal(new DetailHit(0, 0, 5), DetailSearch.Next(Lines, Alpha, 0, -1));
    }

    [Fact]
    public void Next_from_a_match_is_strictly_after_it()
    {
        Assert.Equal(new DetailHit(0, 11, 5), DetailSearch.Next(Lines, Alpha, 0, 0));
        Assert.Equal(new DetailHit(2, 6, 5), DetailSearch.Next(Lines, Alpha, 0, 11));
    }

    [Fact]
    public void Next_skips_lines_without_matches()
    {
        Assert.Equal(new DetailHit(2, 6, 5), DetailSearch.Next(Lines, Alpha, 1, -1));
    }

    [Fact]
    public void Next_past_the_last_match_is_null()
    {
        Assert.Null(DetailSearch.Next(Lines, Alpha, 2, 6));
    }

    [Fact]
    public void Previous_from_a_match_is_the_match_before_it()
    {
        Assert.Equal(new DetailHit(0, 0, 5), DetailSearch.Previous(Lines, Alpha, 0, 11));
        Assert.Equal(new DetailHit(0, 11, 5), DetailSearch.Previous(Lines, Alpha, 2, 6));
    }

    [Fact]
    public void Previous_from_the_start_of_a_line_takes_the_last_match_of_the_lines_above()
    {
        Assert.Equal(new DetailHit(0, 11, 5), DetailSearch.Previous(Lines, Alpha, 1, -1));
    }

    [Fact]
    public void Previous_before_the_first_match_is_null()
    {
        Assert.Null(DetailSearch.Previous(Lines, Alpha, 0, 0));
        Assert.Null(DetailSearch.Previous(Lines, Alpha, 0, -1));
    }

    [Fact]
    public void A_document_without_matches_has_none()
    {
        Assert.Null(DetailSearch.First(Lines, Pattern("omega")));
    }

    private static SearchPattern Pattern(string text) =>
        SearchPattern.Create(new SearchQuery(text, CaseSensitive: false, WholeWord: false, Regex: false), out _)!;

    private static DetailLine Line(string text) => new(text, [], LineKind.Message, 0, null, null);
}
