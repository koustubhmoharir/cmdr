using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Shared.Core;

namespace Cfgedit.Tests;

/// <summary>
/// The search highlights in the row layout: the segments of a match carry its highlight, cell by cell, through the glyphs and the
/// truncation of a value. Pure: the layout is checked without a view.
/// </summary>
public sealed class RowLayoutSearchTests
{
    /// <summary>The member of a JSON object document with one member. The document's top-level object is the first node below the root.</summary>
    private static DocNode JsonMember(string text) => TreeFixtures.Parse(text, "json").Root.Children[0].Children[0];

    private static PartMarks Marks(int active, params (int Start, int Length)[] ranges) =>
        new(ranges.Select(range => new SearchMatch(range.Start, range.Length)).ToArray(), active);

    /// <summary>The text and highlight of the segments of one part, in order.</summary>
    private static (string Text, CellHighlight Highlight)[] HighlightsOf(RowLines lines, int part) =>
        lines.Lines.SelectMany(line => line.Segments).Where(segment => segment.PartIndex == part)
            .Select(segment => (segment.Text, segment.Highlight)).ToArray();

    [Fact]
    public void A_match_and_the_current_match_are_marked_cell_by_cell_and_the_text_between_is_not()
    {
        // {"k": "ab ab"}: the value is part 1 of the member; the first "ab" is a match, the second is the current match.
        DocNode member = JsonMember("{\"k\": \"ab ab\"}");

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 0), false, 80, part => part == 1 ? Marks(1, (0, 2), (3, 2)) : null);

        Assert.Equal(
            new[] { ("ab", CellHighlight.Match), (" ", CellHighlight.None), ("ab", CellHighlight.Active) },
            HighlightsOf(lines, 1));
    }

    [Fact]
    public void A_match_after_a_control_glyph_is_marked_on_its_own_cells()
    {
        // The tab is shown as one glyph, and the match "web" starts after it: the marks follow the text, not the columns.
        DocNode element = TreeFixtures.Parse("<r>a\tweb</r>", "xml").Root.Children[0];

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80, part => part == 1 ? Marks(-1, (2, 3)) : null);

        Assert.Equal(
            new[] { ("a", CellHighlight.None), ("\x2192", CellHighlight.None), ("web", CellHighlight.Match) },
            HighlightsOf(lines, 1));
    }

    [Fact]
    public void A_match_in_a_truncated_value_is_marked_only_where_the_value_is_shown()
    {
        // The value is cut at the width: "abcd" is shown and "efghij" is not, so the match "cdefgh" is marked on "cd" only.
        DocNode member = JsonMember("{\"k\": \"abcdefghij\"}");

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 0), false, 10, part => part == 1 ? Marks(-1, (2, 6)) : null);

        Assert.Equal(new[] { ("cd", CellHighlight.Match) }, HighlightsOf(lines, 1).Where(segment => segment.Highlight != CellHighlight.None));
    }

    [Fact]
    public void Without_marks_no_cell_is_highlighted()
    {
        DocNode member = JsonMember("{\"k\": \"ab\"}");

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 0), false, 80);

        Assert.All(lines.Lines.SelectMany(line => line.Segments), segment => Assert.Equal(CellHighlight.None, segment.Highlight));
    }
}
