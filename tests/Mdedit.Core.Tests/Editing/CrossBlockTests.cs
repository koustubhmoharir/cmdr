using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>
/// A selection across blocks: the blocks wholly inside it and their separators go; the first and last blocks are joined into the first one's kind
/// and containers; a block the selection covers wholly is removed; a code block, HTML or raw block or table is removed only whole.
/// </summary>
public sealed class CrossBlockTests
{
    [Fact]
    public void Deleting_from_the_end_of_one_paragraph_to_the_start_of_the_next_joins_them()
    {
        // "a" and "b" are joined; the blank line between them, which the selection covers, goes.
        Assert.Equal(new EditCase.Outcome("ab", 1), EditCase.Replace("a\n\nb", 1, 3, string.Empty));
    }

    [Fact]
    public void Deleting_from_a_heading_into_a_paragraph_keeps_the_heading_kind()
    {
        // The "b" of the heading and the blank line go; "cd" joins the heading "# a", which stays a heading.
        Assert.Equal(new EditCase.Outcome("# acd", 3), EditCase.Replace("# ab\n\ncd", 3, 6, string.Empty));
    }

    [Fact]
    public void Deleting_from_a_paragraph_into_a_list_item_keeps_the_paragraph_kind()
    {
        // "item" loses its marker and joins the paragraph "pa", which has no containers, so the result is a paragraph.
        Assert.Equal(new EditCase.Outcome("paitem", 2), EditCase.Replace("para\n\n- item", 2, 8, string.Empty));
    }

    [Fact]
    public void A_block_the_selection_covers_wholly_is_removed_with_its_separator()
    {
        // The selection runs from the end of "a" to the end of "b": "b" goes with the blank line before it, and "c" keeps its own.
        Assert.Equal(new EditCase.Outcome("a\n\nc", 1), EditCase.Replace("a\n\nb\n\nc", 1, 4, string.Empty));
    }

    [Fact]
    public void Blocks_between_two_partly_selected_ends_are_removed_with_their_separators()
    {
        // "a" and "b" are covered wholly; the blocks "p" and "q" around them keep their separators, so two blank lines are left between them.
        Assert.Equal(new EditCase.Outcome("p\n\n\n\nq", 1), EditCase.Replace("p\n\na\n\nb\n\nq", 3, 7, string.Empty));
    }

    [Fact]
    public void A_selection_ending_inside_a_code_block_is_refused()
    {
        // The selection ends at the first "c" of the code, which it does not cover; the code block is removed only whole.
        Assert.Equal(EditMessages.WholeOnly, EditCase.Refusal(ViewEdit.ReplaceSelection(RenderDocument.Parse("a\n\n```\ncode\n```"), 1, 7, string.Empty)));
    }

    [Fact]
    public void A_selection_ending_inside_a_table_is_refused()
    {
        // The selection ends at the header's "a" at 5 of "p\n\n| a |\n|---|\n| 1 |".
        Assert.Equal(EditMessages.WholeOnly, EditCase.Refusal(ViewEdit.ReplaceSelection(RenderDocument.Parse("p\n\n| a |\n|---|\n| 1 |"), 1, 5, string.Empty)));
    }

    [Fact]
    public void A_code_block_the_selection_covers_wholly_is_removed()
    {
        // The code block runs from 3 to 15 with its fences; its text ends at 11. The selection ends at 11, so it covers the block.
        Assert.Equal(new EditCase.Outcome("a\n\nb", 1), EditCase.Replace("a\n\n```\ncode\n```\n\nb", 1, 11, string.Empty));
    }

    [Fact]
    public void A_table_the_selection_covers_wholly_at_the_end_of_a_selection_is_removed()
    {
        // The table runs from 3 to 32 (without its line ending); its last cell's text ends at 30, where the selection ends.
        Assert.Equal(new EditCase.Outcome("p\n", 1), EditCase.Replace("p\n\n| a | b |\n|---|---|\n| 1 | 2 |\n", 1, 30, string.Empty));
    }

    [Fact]
    public void Typed_text_replaces_a_selection_across_blocks_where_it_started()
    {
        Assert.Equal(new EditCase.Outcome("axb", 2), EditCase.Replace("a\n\nb", 1, 3, "x"));
    }

    [Fact]
    public void Selecting_every_block_removes_them_all_and_holds_the_only_block_pending()
    {
        // With no block left, the removal holds an empty inline pending at the line's start, as removing the only block does.
        ViewEditResult.Replacement edit = EditCase.Edit("a\n\nb", ViewEdit.ReplaceSelection(RenderDocument.Parse("a\n\nb"), 0, 4, string.Empty));

        Assert.Equal(string.Empty, edit.Text);
        Assert.Equal(4, edit.Length);
        Assert.NotNull(edit.Then);
        Assert.Equal(0, edit.Then.Offset);
        Assert.Equal(string.Empty, edit.Then.SourceText);
    }

    [Fact]
    public void A_selection_across_blocks_that_leaves_a_block_before_it_puts_the_caret_at_its_end()
    {
        // "p" stays with the blank line before the removed blocks, and the caret goes to its end, at 1.
        Assert.Equal(new EditCase.Outcome("p\n\n", 1), EditCase.Replace("p\n\na\n\nb", 3, 7, string.Empty));
    }
}
