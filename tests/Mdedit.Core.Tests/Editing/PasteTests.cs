using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>
/// Paste: text written as source at the caret or over a selection, with the line endings normalised, the container prefix on the lines after the
/// first, code raw, a table cell's one line, the pending blocks, and the verification by the blocks around the paste.
/// </summary>
public sealed class PasteTests
{
    [Fact]
    public void Pasting_inline_text_writes_it_at_the_caret()
    {
        Assert.Equal(new EditCase.Outcome("axyb", 3), EditCase.Paste("ab", 1, 1, "xy"));
    }

    [Fact]
    public void Pasting_text_with_a_blank_line_splits_the_paragraph()
    {
        // "x", a blank line and "y": the paragraph "ab" becomes "ax" and "yb", and the caret is after the "y" at 5.
        Assert.Equal(new EditCase.Outcome("ax\n\nyb", 5), EditCase.Paste("ab", 1, 1, "x\n\ny"));
    }

    [Fact]
    public void Pasting_into_a_quote_gives_each_new_line_the_quote_bar()
    {
        // The caret is after the "a" at 3 in "> ab"; the second line takes the bar, so the quote holds both lines.
        Assert.Equal(new EditCase.Outcome("> ax\n> yb", 8), EditCase.Paste("> ab", 3, 3, "x\ny"));
    }

    [Fact]
    public void Pasting_into_a_list_item_indents_each_new_line_under_the_marker()
    {
        // The item's text starts at 2; the second line takes the two blanks of a bullet's marker, so it stays in the item.
        Assert.Equal(new EditCase.Outcome("- ax\n  yb", 8), EditCase.Paste("- ab", 3, 3, "x\ny"));
    }

    [Fact]
    public void Pasting_into_a_code_block_writes_the_text_raw()
    {
        // The caret is after the "a" at 5; the code block has no prefix, so the text is written as it is.
        Assert.Equal(new EditCase.Outcome("```\nax\nyb\n```", 8), EditCase.Paste("```\nab\n```", 5, 5, "x\ny"));
    }

    [Fact]
    public void Pasting_a_line_into_a_table_cell_writes_it_into_the_cell()
    {
        // The "x|y" is written with its pipe escaped, so the cell stays one cell; the caret is after the "y" at 27.
        Assert.Equal(new EditCase.Outcome("| a | b |\n|---|---|\n| 1x\\|y | 22 |\n", 27), EditCase.Paste("| a | b |\n|---|---|\n| 1 | 22 |\n", 23, 23, "x|y"));
    }

    [Theory]
    [InlineData("x\n")]
    [InlineData("x\r\n")]
    public void A_line_break_ending_a_paste_into_a_table_cell_is_dropped(string pasted)
    {
        Assert.Equal(new EditCase.Outcome("| a | b |\n|---|---|\n| 1x | 22 |\n", 24), EditCase.Paste("| a | b |\n|---|---|\n| 1 | 22 |\n", 23, 23, pasted));
    }

    [Fact]
    public void Pasting_several_lines_into_a_table_cell_is_refused()
    {
        Assert.Equal(EditMessages.CellOneLine, EditCase.Refusal(ViewEdit.Paste(RenderDocument.Parse("| a |\n|---|\n| 1 |\n"), 14, 14, "x\ny")));
    }

    [Fact]
    public void Pasting_over_a_selection_removes_the_selection_first()
    {
        // "b " is removed, then "X" is pasted where it was: "a" + "X" + "cd".
        Assert.Equal(new EditCase.Outcome("aXcd", 2), EditCase.Paste("ab cd", 1, 3, "X"));
    }

    [Theory]
    [InlineData("x\r\ny")]
    [InlineData("x\ry")]
    [InlineData("x\ny")]
    public void Pasted_line_breaks_become_the_document_line_ending(string pasted)
    {
        // The document has CRLF, so the pasted break of any kind is written as CRLF.
        Assert.Equal(new EditCase.Outcome("ax\r\nyb\r\n\r\ncd", 5), EditCase.Paste("ab\r\n\r\ncd", 1, 1, pasted));
    }

    [Fact]
    public void Pasting_into_an_empty_document_writes_the_blocks()
    {
        Assert.Equal(new EditCase.Outcome("a\n\nb", 4), EditCase.Paste(string.Empty, 0, 0, "a\n\nb"));
    }

    [Fact]
    public void Pasting_at_a_pending_block_below_writes_its_source_in_front_of_the_text()
    {
        // Enter at the end of "a" holds a pending paragraph below it, whose source is a blank line; the paste follows it.
        ViewEditResult.Pending below = EditCase.Held(EditCase.EnterAt("a", 1));

        Assert.Equal(new EditCase.Outcome("a\n\nx\ny", 6), EditCase.Paste("a", 1, 1, "x\ny", below));
    }

    [Fact]
    public void Pasting_at_a_pending_block_above_writes_the_text_before_its_tail()
    {
        // Enter at the start of "b" holds a pending block above it: its tail is the blank line that separates the two blocks.
        ViewEditResult.Pending above = EditCase.Held(EditCase.EnterAt("a\n\nb", 3));

        Assert.Equal(new EditCase.Outcome("a\n\nx\ny\n\nb", 6), EditCase.Paste("a\n\nb", 3, 3, "x\ny", above));
    }

    [Fact]
    public void Pasting_after_a_held_space_writes_the_space_first()
    {
        Assert.Equal(new EditCase.Outcome("a xb", 3), EditCase.Paste("ab", 1, 1, "x", new ViewEditResult.Pending(1, " ")));
    }

    [Fact]
    public void Pasted_markdown_is_written_as_source_and_renders_as_its_markup()
    {
        // The asterisks go in as written, and the "x" between them renders strong, which the verification does not judge.
        Assert.Equal(new EditCase.Outcome("a**x**b", 6), EditCase.Paste("ab", 1, 1, "**x**"));
        RenderDocument rendered = RenderDocument.Parse("a**x**b");
        Assert.Contains(rendered.Blocks[0].Runs[0].Cells, cell => cell.Text == "x" && (cell.Style.Inline & InlineStyle.Strong) != 0);
    }

    [Fact]
    public void Pasting_a_fence_that_would_swallow_the_blocks_after_it_is_refused()
    {
        // The opened fence would take "b" into the code block, so the block after the paste changes and the paste is refused.
        Assert.Equal(EditMessages.CantEdit, EditCase.Refusal(ViewEdit.Paste(RenderDocument.Parse("a\n\nb"), 1, 1, "\n```")));
    }

    [Fact]
    public void Empty_text_does_nothing_without_a_message()
    {
        Assert.Equal(string.Empty, EditCase.Refusal(ViewEdit.Paste(RenderDocument.Parse("ab"), 1, 1, string.Empty)));
    }
}
