using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>
/// A split whose new block would start with line-start syntax escapes it: the text is written as it would be typed there, so the split keeps
/// the text it has.
/// </summary>
public sealed class LineStartEscapeTests
{
    [Fact]
    public void A_split_before_a_bullet_marker_in_a_paragraph_escapes_the_dash()
    {
        // "Use " then the blank line, then "\- for bullets": the dash is the first text cell of the new paragraph, at 7.
        Assert.Equal(new EditCase.Outcome("Use\n\n\\- for bullets", 6), EditCase.Enter("Use - for bullets", 4));
    }

    [Fact]
    public void A_split_before_a_plus_escapes_the_plus()
    {
        Assert.Equal(new EditCase.Outcome("a\n\n\\+ b", 4), EditCase.Enter("a + b", 2));
    }

    [Fact]
    public void A_split_before_an_equals_sign_escapes_it()
    {
        Assert.Equal(new EditCase.Outcome("a\n\n\\= b", 4), EditCase.Enter("a = b", 2));
    }

    [Fact]
    public void A_split_before_a_hash_escapes_it()
    {
        Assert.Equal(new EditCase.Outcome("a\n\n\\# b", 4), EditCase.Enter("a # b", 2));
    }

    [Fact]
    public void A_split_before_a_quote_marker_escapes_it()
    {
        Assert.Equal(new EditCase.Outcome("a\n\n\\> b", 4), EditCase.Enter("a > b", 2));
    }

    [Fact]
    public void A_split_before_an_ordered_number_escapes_its_dot_so_the_digits_stay_text()
    {
        // The digits are text; the "." after them is what would make an ordered item, so it is the escaped character (at 3 of the new text).
        Assert.Equal(new EditCase.Outcome("a\n\n3\\. b", 3), EditCase.Enter("a 3. b", 2));
    }

    [Fact]
    public void A_split_before_a_number_with_a_parenthesis_escapes_the_parenthesis()
    {
        Assert.Equal(new EditCase.Outcome("a\n\n3\\) b", 3), EditCase.Enter("a 3) b", 2));
    }

    [Fact]
    public void A_split_in_a_quote_escapes_the_marker_after_the_bar()
    {
        // The new line is "> " then "\- b"; the blank quote line between them is ">".
        Assert.Equal(new EditCase.Outcome("> a\n>\n> \\- b", 9), EditCase.Enter("> a - b", 4));
    }

    [Fact]
    public void A_split_of_an_item_escapes_the_dash_after_the_new_items_marker()
    {
        // The new item's text starts with "-", which after "- " would be a nested item.
        Assert.Equal(new EditCase.Outcome("- a\n- \\- b", 7), EditCase.Enter("- a - b", 4));
    }

    [Fact]
    public void A_marker_that_is_escaped_already_is_not_escaped_twice()
    {
        Assert.Equal(new EditCase.Outcome("a\n\n\\- b", 4), EditCase.Enter("a \\- b", 3));
    }

    [Fact]
    public void A_split_before_plain_text_escapes_nothing()
    {
        Assert.Equal(new EditCase.Outcome("a\n\nb", 3), EditCase.Enter("a b", 2));
    }
}

/// <summary>A typed space into a pending block: the pending is returned unchanged, with no message.</summary>
public sealed class PendingSpaceTests
{
    [Fact]
    public void A_space_typed_into_a_paragraph_held_below_returns_the_same_pending()
    {
        var held = EditCase.Held(EditCase.EnterAt("ab", 2));

        Assert.Equal(held, EditCase.Held(ViewEdit.Insert(Doc("ab"), 2, " ", held)));
    }

    [Fact]
    public void A_space_typed_into_an_item_held_below_returns_the_same_pending()
    {
        var held = EditCase.Held(EditCase.EnterAt("- ab", 4));

        Assert.Equal(held, EditCase.Held(ViewEdit.Insert(Doc("- ab"), 4, " ", held)));
    }

    [Fact]
    public void A_space_typed_into_a_paragraph_held_above_returns_the_same_pending()
    {
        var held = EditCase.Held(EditCase.EnterAt("ab", 0));

        Assert.Equal(held, EditCase.Held(ViewEdit.Insert(Doc("ab"), 0, " ", held)));
    }

    [Fact]
    public void A_space_typed_into_a_pending_block_writes_nothing_and_the_next_character_writes_the_block()
    {
        var held = EditCase.Held(EditCase.EnterAt("ab", 2));
        ViewEditResult.Pending after = EditCase.Held(ViewEdit.Insert(Doc("ab"), 2, " ", held));

        Assert.Equal(new EditCase.Outcome("ab\n\nx", 5), EditCase.Type("ab", 2, "x", after));
    }

    private static Mdedit.Core.Render.RenderDocument Doc(string text) => Mdedit.Core.Render.RenderDocument.Parse(text);
}
