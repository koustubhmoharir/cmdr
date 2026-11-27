using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>Backspace and Delete inside a block: whole cells, entities and escapes, emptied containers, and the refusals at a block's edges.</summary>
public sealed class DeleteTests
{
    [Fact]
    public void Backspace_removes_the_character_before_the_caret()
    {
        Assert.Equal(new EditCase.Outcome("b", 0), EditCase.Backspace("ab", 1));
    }

    [Fact]
    public void Backspace_at_the_end_removes_the_last_character()
    {
        Assert.Equal(new EditCase.Outcome("a", 1), EditCase.Backspace("ab", 2));
    }

    [Fact]
    public void Backspace_removes_an_entity_whole()
    {
        Assert.Equal(new EditCase.Outcome("ab", 1), EditCase.Backspace("a&amp;b", 6));
    }

    [Fact]
    public void Backspace_removes_an_escape_with_its_backslash()
    {
        Assert.Equal(new EditCase.Outcome("ab", 1), EditCase.Backspace("a\\*b", 3));
    }

    [Fact]
    public void Backspace_inside_emphasis_keeps_the_delimiters()
    {
        Assert.Equal(new EditCase.Outcome("*ac*", 2), EditCase.Backspace("*abc*", 3));
    }

    [Fact]
    public void Backspace_of_the_last_character_of_emphasis_removes_the_emphasis_with_its_delimiters()
    {
        Assert.Equal(new EditCase.Outcome("x", 1), EditCase.Backspace("x *a*", 4));
    }

    [Fact]
    public void Deleting_the_only_character_of_a_block_is_refused_until_M6b_because_the_block_would_go()
    {
        Assert.Equal(EditMessages.BlockEmptied, EditCase.Refusal(ViewEdit.DeleteBackward(RenderDocument.Parse("*a*"), 2)));
    }

    [Fact]
    public void Backspace_of_the_last_character_of_emphasis_in_the_middle_of_a_line_removes_both_delimiters()
    {
        Assert.Equal(new EditCase.Outcome("xy", 1), EditCase.Backspace("x*a*y", 4));
    }

    [Fact]
    public void Backspace_of_the_last_character_of_link_text_removes_the_link_and_its_destination()
    {
        Assert.Equal(new EditCase.Outcome("x", 1), EditCase.Backspace("x [a](u)", 4));
    }

    [Fact]
    public void Backspace_of_the_last_character_of_a_link_at_the_end_of_a_block_leaves_the_text_after_it()
    {
        Assert.Equal(new EditCase.Outcome("z", 0), EditCase.Backspace("[a](u)z", 6));
    }

    [Fact]
    public void Backspace_in_a_code_block_removes_one_character()
    {
        Assert.Equal(new EditCase.Outcome("```\nb\n```", 4), EditCase.Backspace("```\nab\n```", 5));
    }

    [Fact]
    public void Backspace_at_the_start_of_a_block_is_refused_until_M6b()
    {
        Assert.Equal(EditMessages.BackspaceAtStart, EditCase.Refusal(ViewEdit.DeleteBackward(RenderDocument.Parse("ab"), 0)));
    }

    [Fact]
    public void Delete_removes_the_character_at_the_caret()
    {
        Assert.Equal(new EditCase.Outcome("b", 0), EditCase.Delete("ab", 0));
    }

    [Fact]
    public void Delete_at_the_end_of_a_block_is_refused_until_M6b()
    {
        Assert.Equal(EditMessages.DeleteAtEnd, EditCase.Refusal(ViewEdit.DeleteForward(RenderDocument.Parse("ab"), 2)));
    }

    [Fact]
    public void Delete_on_an_escape_removes_the_escape_with_its_backslash()
    {
        Assert.Equal(new EditCase.Outcome("ab", 1), EditCase.Delete("a\\*b", 2));
    }

    [Fact]
    public void Delete_of_the_only_character_of_emphasis_removes_it_and_the_space_that_would_lead_the_text()
    {
        Assert.Equal(new EditCase.Outcome("b", 0), EditCase.Delete("*a* b", 1));
    }

    [Fact]
    public void Delete_in_a_code_block_removes_one_character()
    {
        Assert.Equal(new EditCase.Outcome("```\nb\n```", 4), EditCase.Delete("```\nab\n```", 4));
    }

    [Fact]
    public void Deleting_the_first_letter_of_a_paragraph_also_drops_the_space_that_would_lead_the_text()
    {
        // Markdown strips a leading space, so " y" shows as "y"; the edit writes "y", which shows the same.
        Assert.Equal(new EditCase.Outcome("y", 0), EditCase.Delete("x y", 0));
    }

    [Fact]
    public void Backspace_after_the_last_word_drops_the_space_that_would_trail_the_text()
    {
        Assert.Equal(new EditCase.Outcome("a", 1), EditCase.Backspace("a b", 3));
    }

    [Fact]
    public void Backspace_in_a_table_cell_is_refused_until_M6c()
    {
        const string source = "| a | b |\n|---|---|\n| 1 | 2 |\n";
        int cell = source.IndexOf('2');

        Assert.Equal(EditMessages.Table, EditCase.Refusal(ViewEdit.DeleteBackward(RenderDocument.Parse(source), cell + 1)));
    }
}
