namespace Mdedit.Core.Tests.Editing;

/// <summary>
/// A selection whose end is no caret stop (the offset just before a closing marker, as a double-click on a styled word leaves it) is resolved by
/// the blocks and cells it covers: the covered span goes with its markers, and the spaces around it are the ones the source had. Typing, Paste and
/// Enter over the selection share that path, and so do selections across blocks.
/// </summary>
public sealed class SelectionEndTests
{
    [Fact]
    public void Deleting_a_double_clicked_strong_word_removes_it_with_its_markers()
    {
        // "bold" is 4..7 and its closing "**" is 8..9, so the end 8 is just after the "d". The span is emptied, so its markers go with it.
        Assert.Equal(new EditCase.Outcome("a  b", 2), EditCase.Replace("a **bold** b", 4, 8, string.Empty));
    }

    [Fact]
    public void Typing_over_a_double_clicked_strong_word_writes_the_text_where_the_selection_started()
    {
        Assert.Equal(new EditCase.Outcome("a x b", 3), EditCase.Replace("a **bold** b", 4, 8, "x"));
    }

    [Fact]
    public void Deleting_a_double_clicked_emphasis_word_removes_it_with_its_markers()
    {
        // "it" is 3..4 and its closing "*" is 5, so the end 5 is just after the "t".
        Assert.Equal(new EditCase.Outcome("a  b", 2), EditCase.Replace("a *it* b", 3, 5, string.Empty));
        Assert.Equal(new EditCase.Outcome("a x b", 3), EditCase.Replace("a *it* b", 3, 5, "x"));
    }

    [Fact]
    public void Deleting_a_double_clicked_link_word_removes_the_link_with_its_destination()
    {
        // "go" is 3..4; the link's "]" is at 5, so the end 5 is just after the "o". The link has no text left, so its destination goes too.
        Assert.Equal(new EditCase.Outcome("a  b", 2), EditCase.Replace("a [go](u) b", 3, 5, string.Empty));
        Assert.Equal(new EditCase.Outcome("a x b", 3), EditCase.Replace("a [go](u) b", 3, 5, "x"));
    }

    [Fact]
    public void Deleting_a_double_clicked_code_word_removes_it_with_its_backticks()
    {
        // "co" is 3..4 and the closing backtick is at 5.
        Assert.Equal(new EditCase.Outcome("a  b", 2), EditCase.Replace("a `co` b", 3, 5, string.Empty));
        Assert.Equal(new EditCase.Outcome("a x b", 3), EditCase.Replace("a `co` b", 3, 5, "x"));
    }

    [Fact]
    public void A_selection_ending_just_before_a_closing_marker_mid_paragraph_keeps_the_rest_of_the_span()
    {
        // "old" is 5..7 and the closing "**" is at 8: the "b" stays in the span, so only the three characters go and the markers stay.
        Assert.Equal(new EditCase.Outcome("x **b** y z", 5), EditCase.Replace("x **bold** y z", 5, 8, string.Empty));
    }

    [Fact]
    public void Paste_over_a_double_clicked_strong_word_inserts_where_the_selection_started()
    {
        Assert.Equal(new EditCase.Outcome("a Z b", 3), EditCase.Paste("a **bold** b", 4, 8, "Z"));
    }

    [Fact]
    public void Enter_over_a_double_clicked_strong_word_splits_the_paragraph_where_the_selection_started()
    {
        // The selection leaves "a  b" with the caret between the two spaces. The split writes no spaces at its edges, so "a" and "b" are two blocks.
        Assert.Equal(new EditCase.Outcome("a\n\nb", 3), EditCase.EnterOver("a **bold** b", 4, 8));
    }

    [Fact]
    public void A_cross_block_selection_ending_just_before_a_closing_marker_joins_the_blocks_without_the_markers()
    {
        // The first block keeps "p" before the selection's "1" at 1; the last block keeps " b" after the closing "**" at 12, which is not a stop,
        // so its text is kept from the first cell after it (the space at 14).
        Assert.Equal(new EditCase.Outcome("p b", 1), EditCase.Replace("p1\n\na **bold** b", 1, 12, string.Empty));
    }

    [Fact]
    public void A_cross_block_selection_typed_over_ending_just_before_a_closing_marker_writes_the_text_at_its_start()
    {
        Assert.Equal(new EditCase.Outcome("px b", 2), EditCase.Replace("p1\n\na **bold** b", 1, 12, "x"));
    }
}
