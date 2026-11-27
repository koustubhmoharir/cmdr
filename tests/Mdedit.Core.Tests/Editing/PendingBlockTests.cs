using Mdedit.Core.Editing;

namespace Mdedit.Core.Tests.Editing;

/// <summary>Typing into a pending block that Enter holds: the pending's source and the typed character are written as one edit.</summary>
public sealed class PendingBlockTests
{
    [Fact]
    public void Typing_into_a_paragraph_held_below_writes_it_after_the_blank_line()
    {
        ViewEditResult.Pending held = EditCase.Held(EditCase.EnterAt("ab", 2));

        Assert.Equal(new EditCase.Outcome("ab\n\nx", 5), EditCase.Type("ab", 2, "x", held));
    }

    [Fact]
    public void Typing_into_an_item_held_below_writes_the_new_item()
    {
        ViewEditResult.Pending held = EditCase.Held(EditCase.EnterAt("- ab", 4));

        Assert.Equal(new EditCase.Outcome("- ab\n- x", 8), EditCase.Type("- ab", 4, "x", held));
    }

    [Fact]
    public void Typing_into_a_quote_paragraph_held_below_writes_it_in_the_quote()
    {
        ViewEditResult.Pending held = EditCase.Held(EditCase.EnterAt("> ab", 4));

        Assert.Equal(new EditCase.Outcome("> ab\n>\n> x", 10), EditCase.Type("> ab", 4, "x", held));
    }

    [Fact]
    public void Typing_into_a_paragraph_held_above_writes_it_before_the_block()
    {
        ViewEditResult.Pending held = EditCase.Held(EditCase.EnterAt("ab", 0));

        Assert.Equal(new EditCase.Outcome("x\n\nab", 1), EditCase.Type("ab", 0, "x", held));
    }

    [Fact]
    public void Typing_into_an_item_held_above_writes_the_new_item_before_the_item()
    {
        ViewEditResult.Pending held = EditCase.Held(EditCase.EnterAt("- ab", 2));

        Assert.Equal(new EditCase.Outcome("- x\n- ab", 3), EditCase.Type("- ab", 2, "x", held));
    }

    [Fact]
    public void Typing_into_a_paragraph_held_above_a_heading_writes_a_paragraph_before_the_heading()
    {
        ViewEditResult.Pending held = EditCase.Held(EditCase.EnterAt("# ab", 2));

        Assert.Equal(new EditCase.Outcome("x\n\n# ab", 1), EditCase.Type("# ab", 2, "x", held));
    }

    [Fact]
    public void A_markup_character_typed_into_a_held_paragraph_above_is_escaped_at_the_start_of_its_line()
    {
        ViewEditResult.Pending held = EditCase.Held(EditCase.EnterAt("ab", 0));

        Assert.Equal(new EditCase.Outcome("\\#\n\nab", 2), EditCase.Type("ab", 0, "#", held));
    }

    [Fact]
    public void Enter_on_a_held_item_ends_the_list_and_typing_then_writes_a_paragraph_after_it()
    {
        ViewEditResult.Pending item = EditCase.Held(EditCase.EnterAt("- a", 3));
        ViewEditResult.Pending paragraph = EditCase.Held(EditCase.EnterAt("- a", 3, item));

        Assert.Equal(new EditCase.Outcome("- a\n\nx", 6), EditCase.Type("- a", 3, "x", paragraph));
    }

    [Fact]
    public void A_held_paragraph_writes_the_typed_text_with_the_escape_of_a_line_start()
    {
        ViewEditResult.Pending held = EditCase.Held(EditCase.EnterAt("ab", 2));

        Assert.Equal(new EditCase.Outcome("ab\n\n\\-", 6), EditCase.Type("ab", 2, "-", held));
    }
}
