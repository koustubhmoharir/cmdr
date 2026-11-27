using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>Backspace and Delete across the lines of one block: hard breaks, soft breaks, code lines, and selections across lines.</summary>
public sealed class LineTests
{
    [Fact]
    public void Backspace_after_a_backslash_hard_break_removes_the_break_and_joins_the_lines()
    {
        Assert.Equal(new EditCase.Outcome("ab", 1), EditCase.Backspace("a\\\nb", 3));
    }

    [Fact]
    public void Backspace_after_a_trailing_space_hard_break_removes_the_break_and_joins_the_lines()
    {
        Assert.Equal(new EditCase.Outcome("ab", 1), EditCase.Backspace("a  \nb", 4));
    }

    [Fact]
    public void Delete_at_the_end_of_a_line_before_a_backslash_hard_break_joins_the_lines()
    {
        Assert.Equal(new EditCase.Outcome("ab", 1), EditCase.Delete("a\\\nb", 1));
    }

    [Fact]
    public void Backspace_in_a_quote_after_a_hard_break_removes_the_next_line_bar_too()
    {
        Assert.Equal(new EditCase.Outcome("> ab", 3), EditCase.Backspace("> a\\\n> b", 7));
    }

    [Fact]
    public void Backspace_after_a_soft_break_in_a_quote_removes_the_break_with_the_bar_of_the_next_line()
    {
        // The soft break is a space in the rendering; removing it removes the next line's "> ", so the words join.
        Assert.Equal(new EditCase.Outcome("> ab", 3), EditCase.Backspace("> a\n> b", 6));
    }

    [Fact]
    public void Delete_on_a_soft_break_in_a_quote_removes_the_break_with_the_bar_of_the_next_line()
    {
        Assert.Equal(new EditCase.Outcome("> ab", 3), EditCase.Delete("> a\n> b", 3));
    }

    [Fact]
    public void Backspace_after_a_soft_break_in_a_list_item_removes_the_break_with_the_indentation()
    {
        Assert.Equal(new EditCase.Outcome("- ab", 3), EditCase.Backspace("- a\n  b", 6));
    }

    [Fact]
    public void Backspace_at_the_start_of_a_code_line_joins_it_to_the_line_before()
    {
        Assert.Equal(new EditCase.Outcome("```\nabcd\n```", 6), EditCase.Backspace("```\nab\ncd\n```", 7));
    }

    [Fact]
    public void Delete_at_the_end_of_a_code_line_joins_it_to_the_next_line()
    {
        Assert.Equal(new EditCase.Outcome("```\nabcd\n```", 6), EditCase.Delete("```\nab\ncd\n```", 6));
    }

    [Fact]
    public void Selecting_across_a_hard_break_and_deleting_joins_what_is_left()
    {
        Assert.Equal(new EditCase.Outcome("ad", 1), EditCase.Replace("ab\\\ncd", 1, 5, ""));
    }

    [Fact]
    public void Typing_over_a_selection_across_a_hard_break_writes_the_text_where_it_started()
    {
        Assert.Equal(new EditCase.Outcome("aXd", 2), EditCase.Replace("ab\\\ncd", 1, 5, "X"));
    }

    [Fact]
    public void Deleting_a_selection_across_two_code_lines_removes_the_break_between_them()
    {
        Assert.Equal(new EditCase.Outcome("```\nad\n```", 5), EditCase.Replace("```\nab\ncd\n```", 5, 8, ""));
    }

    [Fact]
    public void Deleting_a_selection_that_is_a_soft_break_removes_it_and_the_next_lines_prefix()
    {
        Assert.Equal(new EditCase.Outcome("ab", 1), EditCase.Replace("a\nb", 1, 2, ""));
    }

    [Fact]
    public void Deleting_a_selection_across_a_hard_break_where_a_whole_block_goes_removes_the_block_and_holds_a_pending_below()
    {
        ViewEditResult.Replacement edit = EditCase.Edit("p\n\nx\\\ny", ViewEdit.ReplaceSelection(RenderDocument.Parse("p\n\nx\\\ny"), 3, 7, ""));

        Assert.Equal(new EditCase.Outcome("p", 1), EditCase.Apply("p\n\nx\\\ny", edit));
        Assert.Equal(new ViewEditResult.Pending(1, "\n\n", PendingPlacement.Below, string.Empty), edit.Then);
    }
}
