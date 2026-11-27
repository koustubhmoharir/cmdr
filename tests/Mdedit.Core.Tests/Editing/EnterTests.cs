using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>Enter: the split of a block, the pending blocks at its start and end, the line break in code, and the ending of a list or a quote.</summary>
public sealed class EnterTests
{
    [Fact]
    public void Enter_in_the_middle_of_a_paragraph_splits_it_with_a_blank_line()
    {
        Assert.Equal(new EditCase.Outcome("a\n\nb", 3), EditCase.Enter("ab", 1));
    }

    [Fact]
    public void Enter_in_a_heading_splits_it_into_two_headings_of_the_same_level()
    {
        // "# a" + blank line + "# " + "b": the caret is before the "b", at 7.
        Assert.Equal(new EditCase.Outcome("# a\n\n# b", 7), EditCase.Enter("# ab", 3));
    }

    [Fact]
    public void Enter_in_a_quote_splits_it_with_a_blank_quote_line_and_the_bar_on_the_new_line()
    {
        // "> a", then the blank quote line ">", then "> " and "b".
        Assert.Equal(new EditCase.Outcome("> a\n>\n> b", 8), EditCase.Enter("> ab", 3));
    }

    [Fact]
    public void Enter_in_a_tight_list_item_makes_a_new_item_with_one_line_break()
    {
        Assert.Equal(new EditCase.Outcome("- a\n- b", 6), EditCase.Enter("- ab", 3));
    }

    [Fact]
    public void Enter_in_a_loose_list_item_makes_a_new_item_after_a_blank_line()
    {
        Assert.Equal(new EditCase.Outcome("- a\n\n- b\n\n- c", 12), EditCase.Enter("- a\n\n- bc", 8));
    }

    [Fact]
    public void Enter_in_a_tight_list_keeps_the_list_tight_with_the_new_item_in_it()
    {
        Assert.Equal(new EditCase.Outcome("- a\n- b\n- c", 10), EditCase.Enter("- a\n- bc", 7));
    }

    [Fact]
    public void Enter_in_an_ordered_item_numbers_the_new_item_one_after_the_old_with_its_delimiter()
    {
        Assert.Equal(new EditCase.Outcome("1. a\n2. b", 8), EditCase.Enter("1. ab", 4));
        Assert.Equal(new EditCase.Outcome("3) a\n4) b", 8), EditCase.Enter("3) ab", 4));
    }

    [Fact]
    public void Enter_in_a_task_item_makes_an_unchecked_task_item()
    {
        Assert.Equal(new EditCase.Outcome("- [ ] a\n- [ ] b", 14), EditCase.Enter("- [ ] ab", 7));
    }

    [Fact]
    public void Enter_in_a_nested_item_splits_the_nested_item_and_keeps_the_outer_indent()
    {
        // "- a", "  - b", then the new "  - " item and "c".
        Assert.Equal(new EditCase.Outcome("- a\n  - b\n  - c", 14), EditCase.Enter("- a\n  - bc", 9));
    }

    [Fact]
    public void Enter_in_a_code_block_writes_a_line_break_at_the_caret()
    {
        Assert.Equal(new EditCase.Outcome("```\na\nb\n```", 6), EditCase.Enter("```\nab\n```", 5));
    }

    [Fact]
    public void Enter_at_the_end_of_a_code_block_writes_an_empty_line_inside_the_block()
    {
        Assert.Equal(new EditCase.Outcome("```\nab\n\n```", 7), EditCase.Enter("```\nab\n```", 6));
    }

    [Fact]
    public void Enter_in_an_indented_code_block_writes_the_indentation_too()
    {
        Assert.Equal(new EditCase.Outcome("    a\n    b\n    cd", 10), EditCase.Enter("    ab\n    cd", 5));
    }

    [Fact]
    public void Enter_in_a_code_block_inside_a_quote_writes_the_bar_on_the_new_line()
    {
        Assert.Equal(new EditCase.Outcome("> ```\n> a\n> b\n> ```", 12), EditCase.Enter("> ```\n> ab\n> ```", 9));
    }

    [Fact]
    public void Enter_at_the_end_of_a_paragraph_holds_a_paragraph_below_and_writes_nothing()
    {
        ViewEditResult.Pending held = EditCase.Held(EditCase.EnterAt("ab", 2));

        Assert.Equal(new ViewEditResult.Pending(2, "\n\n", PendingPlacement.Below, string.Empty), held);
    }

    [Fact]
    public void Enter_at_the_end_of_a_heading_holds_a_paragraph_below()
    {
        Assert.Equal(new ViewEditResult.Pending(4, "\n\n", PendingPlacement.Below, string.Empty), EditCase.Held(EditCase.EnterAt("# ab", 4)));
    }

    [Fact]
    public void Enter_at_the_end_of_a_quote_paragraph_holds_a_quoted_paragraph_below_with_the_bar_shown()
    {
        Assert.Equal(new ViewEditResult.Pending(4, "\n>\n> ", PendingPlacement.Below, "│ "), EditCase.Held(EditCase.EnterAt("> ab", 4)));
    }

    [Fact]
    public void Enter_at_the_end_of_a_bullet_item_holds_a_new_item_below_with_its_bullet_shown()
    {
        Assert.Equal(new ViewEditResult.Pending(4, "\n- ", PendingPlacement.Below, "• "), EditCase.Held(EditCase.EnterAt("- ab", 4)));
    }

    [Fact]
    public void Enter_at_the_end_of_an_ordered_item_holds_the_next_number_below()
    {
        Assert.Equal(new ViewEditResult.Pending(4, "\n2. ", PendingPlacement.Below, "2. "), EditCase.Held(EditCase.EnterAt("1. a", 4)));
    }

    [Fact]
    public void Enter_at_the_end_of_a_task_item_holds_an_unchecked_task_item_below()
    {
        Assert.Equal(new ViewEditResult.Pending(7, "\n- [ ] ", PendingPlacement.Below, "• [ ] "), EditCase.Held(EditCase.EnterAt("- [ ] a", 7)));
    }

    [Fact]
    public void Enter_at_the_end_of_a_loose_item_holds_the_new_item_after_a_blank_line()
    {
        Assert.Equal(new ViewEditResult.Pending(8, "\n\n- ", PendingPlacement.Below, "• "), EditCase.Held(EditCase.EnterAt("- a\n\n- b", 8)));
    }

    [Fact]
    public void Enter_at_the_end_of_a_nested_last_item_holds_a_nested_item_below_with_the_outer_indent()
    {
        Assert.Equal(new ViewEditResult.Pending(9, "\n  - ", PendingPlacement.Below, "  ◦ "), EditCase.Held(EditCase.EnterAt("- a\n  - b", 9)));
    }

    [Fact]
    public void Enter_at_the_start_of_a_paragraph_holds_a_paragraph_above_with_its_tail()
    {
        Assert.Equal(new ViewEditResult.Pending(0, string.Empty, PendingPlacement.Above, string.Empty, "\n\n"), EditCase.Held(EditCase.EnterAt("ab", 0)));
    }

    [Fact]
    public void Enter_at_the_start_of_a_bullet_item_holds_a_new_item_above_with_the_items_marker_in_the_tail()
    {
        Assert.Equal(new ViewEditResult.Pending(2, string.Empty, PendingPlacement.Above, "• ", "\n- "), EditCase.Held(EditCase.EnterAt("- ab", 2)));
    }

    [Fact]
    public void Enter_at_the_start_of_an_ordered_item_holds_an_item_above_with_the_same_number()
    {
        Assert.Equal(new ViewEditResult.Pending(3, string.Empty, PendingPlacement.Above, "3. ", "\n3. "), EditCase.Held(EditCase.EnterAt("3. ab", 3)));
    }

    [Fact]
    public void Enter_at_the_start_of_a_loose_item_holds_the_item_above_after_a_blank_line()
    {
        Assert.Equal(new ViewEditResult.Pending(7, string.Empty, PendingPlacement.Above, "• ", "\n\n- "), EditCase.Held(EditCase.EnterAt("- a\n\n- b", 7)));
    }

    [Fact]
    public void Enter_at_the_start_of_a_heading_holds_a_paragraph_above_not_a_heading()
    {
        Assert.Equal(new ViewEditResult.Pending(2, string.Empty, PendingPlacement.Above, string.Empty, "\n\n"), EditCase.Held(EditCase.EnterAt("# ab", 2)));
    }

    [Fact]
    public void Enter_at_the_start_of_a_quote_paragraph_holds_a_quoted_paragraph_above()
    {
        Assert.Equal(new ViewEditResult.Pending(2, string.Empty, PendingPlacement.Above, "│ ", "\n>\n> "), EditCase.Held(EditCase.EnterAt("> ab", 2)));
    }

    [Fact]
    public void Enter_on_a_pending_item_ends_the_list_with_a_paragraph_after_it()
    {
        var item = new ViewEditResult.Pending(3, "\n- ", PendingPlacement.Below, "• ");

        Assert.Equal(new ViewEditResult.Pending(3, "\n\n", PendingPlacement.Below, string.Empty), EditCase.Held(EditCase.EnterAt("- a", 3, item)));
    }

    [Fact]
    public void Enter_on_a_pending_item_of_a_nested_list_ends_it_with_a_paragraph_in_the_parent_item()
    {
        var item = new ViewEditResult.Pending(9, "\n  - ", PendingPlacement.Below, "  ◦ ");

        Assert.Equal(new ViewEditResult.Pending(9, "\n\n  ", PendingPlacement.Below, "  "), EditCase.Held(EditCase.EnterAt("- a\n  - b", 9, item)));
    }

    [Fact]
    public void Enter_on_a_pending_paragraph_in_a_quote_ends_the_quote()
    {
        var paragraph = new ViewEditResult.Pending(4, "\n>\n> ", PendingPlacement.Below, "│ ");

        Assert.Equal(new ViewEditResult.Pending(4, "\n\n", PendingPlacement.Below, string.Empty), EditCase.Held(EditCase.EnterAt("> ab", 4, paragraph)));
    }

    [Fact]
    public void Enter_on_a_pending_paragraph_outside_any_container_changes_nothing()
    {
        var paragraph = new ViewEditResult.Pending(2, "\n\n", PendingPlacement.Below, string.Empty);

        Assert.Equal(paragraph, EditCase.Held(EditCase.EnterAt("ab", 2, paragraph)));
    }

    [Fact]
    public void A_held_inline_space_is_dropped_and_Enter_acts_at_the_caret()
    {
        Assert.Equal(new EditCase.Outcome("a\n\nb", 3), EditCase.Enter("ab", 1, new ViewEditResult.Pending(1, " ")));
    }

    [Fact]
    public void Enter_over_a_selection_removes_it_and_splits_where_it_started()
    {
        // "b " is removed, leaving "acd"; Enter then splits it between the "a" and the "cd".
        Assert.Equal(new EditCase.Outcome("a\n\ncd", 3), EditCase.EnterOver("ab cd", 1, 3));
    }

    [Fact]
    public void Enter_over_the_last_text_of_a_block_removes_the_block_and_holds_a_pending_below_the_previous_one()
    {
        ViewEditResult.Replacement edit = EditCase.Edit("a\n\nb", ViewEdit.EnterOver(RenderDocument.Parse("a\n\nb"), 3, 4));

        Assert.Equal("a", string.Concat("a\n\nb".AsSpan(0, edit.Start), edit.Text, "a\n\nb".AsSpan(edit.Start + edit.Length)));
        Assert.Equal(new ViewEditResult.Pending(1, "\n\n", PendingPlacement.Below, string.Empty), edit.Then);
    }

    [Fact]
    public void Enter_at_a_place_inside_a_line_after_a_hard_break_is_refused()
    {
        Assert.Equal(EditMessages.CantEdit, EditCase.Refusal(EditCase.EnterAt("a\\\nb", 3)));
    }

    [Fact]
    public void Enter_in_a_table_cell_is_refused_until_M6c()
    {
        const string source = "| a | b |\n|---|---|\n| 1 | 2 |\n";

        Assert.Equal(EditMessages.Table, EditCase.Refusal(EditCase.EnterAt(source, source.IndexOf('1'))));
    }
}
