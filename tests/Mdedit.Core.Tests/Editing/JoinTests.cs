using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>Backspace at a block's start and Delete at its end: the unwrapping of headings, items and quotes, and the joins of blocks.</summary>
public sealed class JoinTests
{
    [Fact]
    public void Backspace_in_a_heading_makes_it_a_paragraph()
    {
        Assert.Equal(new EditCase.Outcome("ab", 0), EditCase.Backspace("# ab", 2));
    }

    [Fact]
    public void Backspace_in_a_heading_inside_an_item_keeps_the_item()
    {
        Assert.Equal(new EditCase.Outcome("- ab", 2), EditCase.Backspace("- # ab", 4));
    }

    [Fact]
    public void Backspace_in_the_first_item_of_a_list_makes_its_text_a_paragraph_before_the_list()
    {
        Assert.Equal(new EditCase.Outcome("ab", 0), EditCase.Backspace("- ab", 2));
    }

    [Fact]
    public void Backspace_in_a_task_item_removes_the_item_marker_and_the_box()
    {
        Assert.Equal(new EditCase.Outcome("ab", 0), EditCase.Backspace("- [ ] ab", 6));
    }

    [Fact]
    public void Backspace_in_an_item_joins_its_text_to_the_previous_item_with_no_space()
    {
        Assert.Equal(new EditCase.Outcome("- ab", 3), EditCase.Backspace("- a\n- b", 6));
    }

    [Fact]
    public void Backspace_in_an_item_joins_to_the_last_text_block_of_the_previous_item()
    {
        // The previous item's last text is the nested "x"; "b" joins it.
        Assert.Equal(new EditCase.Outcome("- a\n  - xb", 9), EditCase.Backspace("- a\n  - x\n- b", 12));
    }

    [Fact]
    public void Backspace_in_a_nested_first_item_is_refused_because_it_would_change_the_structure()
    {
        Assert.Equal(EditMessages.CantEdit, EditCase.Refusal(ViewEdit.DeleteBackward(RenderDocument.Parse("- a\n  - b"), 8)));
    }

    [Fact]
    public void Backspace_in_the_first_leaf_of_a_quote_leaves_the_quote()
    {
        Assert.Equal(new EditCase.Outcome("a", 0), EditCase.Backspace("> a", 2));
    }

    [Fact]
    public void Backspace_in_a_quote_paragraph_after_a_paragraph_outside_the_quote_leaves_the_quote()
    {
        Assert.Equal(new EditCase.Outcome("a\n\nb", 3), EditCase.Backspace("a\n\n> b", 5));
    }

    [Fact]
    public void Backspace_at_the_start_of_a_later_paragraph_in_the_same_quote_joins_it_to_the_one_before()
    {
        Assert.Equal(new EditCase.Outcome("> ab", 3), EditCase.Backspace("> a\n>\n> b", 8));
    }

    [Fact]
    public void Backspace_at_the_start_of_a_paragraph_joins_it_to_the_paragraph_before_with_a_blank_line_between()
    {
        Assert.Equal(new EditCase.Outcome("ab", 1), EditCase.Backspace("a\n\nb", 3));
    }

    [Fact]
    public void Backspace_at_the_start_of_a_paragraph_after_a_heading_appends_to_the_heading()
    {
        Assert.Equal(new EditCase.Outcome("# ab", 3), EditCase.Backspace("# a\n\nb", 5));
    }

    [Fact]
    public void Backspace_at_the_start_of_a_paragraph_after_a_thematic_break_removes_the_break()
    {
        Assert.Equal(new EditCase.Outcome("b", 0), EditCase.Backspace("---\n\nb", 5));
    }

    [Fact]
    public void Backspace_at_the_start_of_a_paragraph_after_a_code_block_is_refused()
    {
        Assert.Equal(EditMessages.CantJoin, EditCase.Refusal(ViewEdit.DeleteBackward(RenderDocument.Parse("```\nab\n```\n\nb"), 12)));
    }

    [Fact]
    public void Backspace_at_the_start_of_a_paragraph_that_would_join_into_a_different_container_is_refused()
    {
        Assert.Equal(EditMessages.CantJoin, EditCase.Refusal(ViewEdit.DeleteBackward(RenderDocument.Parse("> a\n\nb"), 5)));
    }

    [Fact]
    public void Delete_at_the_end_of_a_paragraph_appends_the_next_paragraph_with_no_space()
    {
        Assert.Equal(new EditCase.Outcome("abcd", 2), EditCase.Delete("ab\n\ncd", 2));
    }

    [Fact]
    public void Delete_at_the_end_of_an_item_appends_the_next_item_and_its_marker_goes()
    {
        Assert.Equal(new EditCase.Outcome("- ab", 3), EditCase.Delete("- a\n- b", 3));
    }

    [Fact]
    public void Delete_at_the_end_of_a_paragraph_before_an_item_appends_the_item_text()
    {
        Assert.Equal(new EditCase.Outcome("ab", 1), EditCase.Delete("a\n\n- b", 1));
    }

    [Fact]
    public void Delete_at_the_end_of_a_paragraph_before_a_thematic_break_removes_the_break()
    {
        Assert.Equal(new EditCase.Outcome("a\n\nb", 1), EditCase.Delete("a\n\n---\n\nb", 1));
    }

    [Fact]
    public void Delete_at_the_end_of_a_quote_before_a_paragraph_outside_it_is_refused()
    {
        Assert.Equal(EditMessages.CantJoin, EditCase.Refusal(ViewEdit.DeleteForward(RenderDocument.Parse("> a\n\nb"), 3)));
    }

    [Fact]
    public void Delete_at_the_end_of_a_quote_paragraph_joins_the_next_one_of_the_same_quote()
    {
        Assert.Equal(new EditCase.Outcome("> ab", 3), EditCase.Delete("> a\n>\n> b", 3));
    }

    [Fact]
    public void Delete_of_a_block_that_would_join_into_a_thematic_break_of_another_container_is_refused()
    {
        Assert.Equal(EditMessages.CantJoin, EditCase.Refusal(ViewEdit.DeleteForward(RenderDocument.Parse("a\n\n> ---"), 1)));
    }
}
