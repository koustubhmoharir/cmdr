using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>
/// Removing a block's last character: the block goes with its separator and a pending block of the same shape is held where it was, which typing
/// writes back.
/// </summary>
public sealed class LastCharacterTests
{
    [Fact]
    public void Backspace_of_the_last_character_of_a_last_block_removes_it_and_holds_a_paragraph_below_the_previous_one()
    {
        ViewEditResult.Replacement edit = Edit("a\n\nb", ViewEdit.DeleteBackward(RenderDocument.Parse("a\n\nb"), 4));

        Assert.Equal(new EditCase.Outcome("a", 1), EditCase.Apply("a\n\nb", edit));
        Assert.Equal(new ViewEditResult.Pending(1, "\n\n", PendingPlacement.Below, string.Empty), edit.Then);
    }

    [Fact]
    public void Delete_of_the_last_character_of_the_first_block_removes_it_and_holds_a_paragraph_above_the_next_one()
    {
        ViewEditResult.Replacement edit = Edit("a\n\nb", ViewEdit.DeleteForward(RenderDocument.Parse("a\n\nb"), 0));

        Assert.Equal(new EditCase.Outcome("b", 0), EditCase.Apply("a\n\nb", edit));
        Assert.Equal(new ViewEditResult.Pending(0, string.Empty, PendingPlacement.Above, string.Empty, "\n\n"), edit.Then);
    }

    [Fact]
    public void Deleting_the_last_character_of_a_heading_holds_a_heading_marker_above_the_next_block()
    {
        ViewEditResult.Replacement edit = Edit("# a\n\nb", ViewEdit.DeleteForward(RenderDocument.Parse("# a\n\nb"), 2));

        Assert.Equal(new EditCase.Outcome("b", 0), EditCase.Apply("# a\n\nb", edit));
        Assert.Equal(new ViewEditResult.Pending(0, "# ", PendingPlacement.Above, string.Empty, "\n\n"), edit.Then);
    }

    [Fact]
    public void Deleting_the_last_character_of_an_item_holds_an_item_above_the_next_one_with_its_marker_in_the_tail()
    {
        ViewEditResult.Replacement edit = Edit("- a\n- b", ViewEdit.DeleteForward(RenderDocument.Parse("- a\n- b"), 2));

        Assert.Equal(new EditCase.Outcome("- b", 2), EditCase.Apply("- a\n- b", edit));
        Assert.Equal(new ViewEditResult.Pending(2, string.Empty, PendingPlacement.Above, "• ", "\n- "), edit.Then);
    }

    [Fact]
    public void Deleting_the_last_character_of_a_last_item_holds_an_item_below_the_previous_text()
    {
        ViewEditResult.Replacement edit = Edit("x\n\n- a", ViewEdit.DeleteBackward(RenderDocument.Parse("x\n\n- a"), 6));

        Assert.Equal(new EditCase.Outcome("x", 1), EditCase.Apply("x\n\n- a", edit));
        Assert.Equal(new ViewEditResult.Pending(1, "\n\n- ", PendingPlacement.Below, "• "), edit.Then);
    }

    [Fact]
    public void Deleting_the_last_character_of_the_only_heading_holds_its_marker_inline_at_the_start()
    {
        ViewEditResult.Replacement edit = Edit("# a", ViewEdit.DeleteForward(RenderDocument.Parse("# a"), 2));

        Assert.Equal(new EditCase.Outcome(string.Empty, 0), EditCase.Apply("# a", edit));
        Assert.Equal(new ViewEditResult.Pending(0, "# ", PendingPlacement.Inline, "# "), edit.Then);
    }

    [Fact]
    public void Deleting_the_last_character_of_the_only_item_holds_its_marker_and_bullet()
    {
        ViewEditResult.Replacement edit = Edit("- a", ViewEdit.DeleteForward(RenderDocument.Parse("- a"), 2));

        Assert.Equal(new ViewEditResult.Pending(0, "- ", PendingPlacement.Inline, "• "), edit.Then);
    }

    [Fact]
    public void Deleting_the_last_character_of_the_only_quote_paragraph_holds_its_bar()
    {
        ViewEditResult.Replacement edit = Edit("> a", ViewEdit.DeleteForward(RenderDocument.Parse("> a"), 2));

        Assert.Equal(new ViewEditResult.Pending(0, "> ", PendingPlacement.Inline, "│ "), edit.Then);
    }

    [Fact]
    public void Deleting_the_only_block_keeps_the_blank_lines_around_it()
    {
        ViewEditResult.Replacement edit = Edit("\n\na", ViewEdit.DeleteForward(RenderDocument.Parse("\n\na"), 2));

        Assert.Equal(new EditCase.Outcome("\n\n", 2), EditCase.Apply("\n\na", edit));
    }

    [Fact]
    public void Typing_into_the_held_marker_of_an_empty_document_writes_the_block_back()
    {
        Assert.Equal(new EditCase.Outcome("# x", 3), EditCase.Type(string.Empty, 0, "x", new ViewEditResult.Pending(0, "# ", PendingPlacement.Inline, "# ")));
    }

    [Fact]
    public void Typing_into_a_held_item_marker_of_an_empty_document_writes_the_item()
    {
        Assert.Equal(new EditCase.Outcome("- x", 3), EditCase.Type(string.Empty, 0, "x", new ViewEditResult.Pending(0, "- ", PendingPlacement.Inline, "• ")));
    }

    [Fact]
    public void Typing_a_plain_character_into_an_empty_document_writes_a_paragraph()
    {
        Assert.Equal(new EditCase.Outcome("a", 1), EditCase.Type(string.Empty, 0, "a", new ViewEditResult.Pending(0, string.Empty, PendingPlacement.Inline, string.Empty)));
    }

    [Fact]
    public void A_markup_character_typed_into_a_held_heading_marker_is_escaped()
    {
        Assert.Equal(new EditCase.Outcome("# \\#", 4), EditCase.Type(string.Empty, 0, "#", new ViewEditResult.Pending(0, "# ", PendingPlacement.Inline, "# ")));
    }

    [Fact]
    public void A_space_typed_into_an_empty_document_with_a_held_marker_leaves_the_pending_unchanged()
    {
        var held = new ViewEditResult.Pending(0, "# ", PendingPlacement.Inline, "# ");

        Assert.Equal(held, EditCase.Held(ViewEdit.Insert(RenderDocument.Parse(string.Empty), 0, " ", held)));
    }

    [Fact]
    public void Typing_below_a_held_paragraph_writes_the_block_back_after_its_separator()
    {
        Assert.Equal(new EditCase.Outcome("a\n\nx", 4), EditCase.Type("a", 1, "x", new ViewEditResult.Pending(1, "\n\n", PendingPlacement.Below, string.Empty)));
    }

    [Fact]
    public void Typing_below_a_held_item_writes_the_item_with_its_marker()
    {
        Assert.Equal(new EditCase.Outcome("x\n\n- y", 6), EditCase.Type("x", 1, "y", new ViewEditResult.Pending(1, "\n\n- ", PendingPlacement.Below, "• ")));
    }

    [Fact]
    public void Typing_above_a_held_item_writes_the_item_before_the_next_one()
    {
        Assert.Equal(new EditCase.Outcome("- x\n- b", 3), EditCase.Type("- b", 2, "x", new ViewEditResult.Pending(2, string.Empty, PendingPlacement.Above, "• ", "\n- ")));
    }

    [Fact]
    public void Typing_above_a_held_heading_marker_writes_a_heading_and_keeps_the_next_block()
    {
        Assert.Equal(new EditCase.Outcome("# x\n\nb", 3), EditCase.Type("b", 0, "x", new ViewEditResult.Pending(0, "# ", PendingPlacement.Above, string.Empty, "\n\n")));
    }

    private static ViewEditResult.Replacement Edit(string source, ViewEditResult result) => EditCase.Edit(source, result);
}
