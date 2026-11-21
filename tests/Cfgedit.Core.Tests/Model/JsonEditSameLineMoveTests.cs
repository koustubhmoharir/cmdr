using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Moves between neighbours that share a line: only the moved text and the separating spaces move, and no line break is
/// removed or added where the moved text and its target are on one line.
/// </summary>
public sealed class JsonEditSameLineMoveTests
{
    [Fact]
    public void A_block_comment_moves_up_in_front_of_its_sibling_comment_and_the_line_break_stays()
    {
        string text = Move("{\n  \"a\": 1, /*t1*/ /*t2*/\n  \"b\": 2\n}", Jsonc, At(Item(0), Comment(1)), -1);

        Assert.Equal("{\n  \"a\": 1, /*t2*/ /*t1*/\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_line_comment_moves_up_in_front_of_a_block_comment_and_the_block_comment_takes_the_next_line()
    {
        // A line comment must end its line, so the comment it moves past goes to the line below it. That break is required.
        string text = Move("{\n  \"a\": 1, /*t1*/ // t2\n  \"b\": 2\n}", Jsonc, At(Item(0), Comment(1)), -1);

        Assert.Equal("{\n  \"a\": 1, // t2\n  /*t1*/\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_block_comment_moves_down_past_a_line_comment_and_takes_the_next_line()
    {
        string text = Move("{\n  \"a\": 1, /*t1*/ // t2\n  \"b\": 2\n}", Jsonc, At(Item(0), Comment(0)), 1);

        Assert.Equal("{\n  \"a\": 1, // t2\n  /*t1*/\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_block_comment_moves_up_in_front_of_a_comment_in_a_single_line_array()
    {
        string text = Move("[1, /*x*/ /*y*/ 2]", Jsonc, At(Item(0), Comment(1)), -1);

        Assert.Equal("[1, /*y*/ /*x*/ 2]", text);
    }

    [Fact]
    public void A_block_comment_moves_to_the_end_of_a_single_line_array_after_an_item()
    {
        // The array's items are 1 and 2, so the end is index 3 counting the comment that stays behind.
        string text = Edit("[1, /*x*/ /*y*/ 2]", Jsonc, new MoveNode(At(Item(0), Comment(0)), 3));

        Assert.Equal("[1, /*y*/ 2 /*x*/]", text);
    }

    [Fact]
    public void A_block_comment_moves_down_past_a_comment_in_a_multi_line_array_and_the_line_break_stays()
    {
        string text = Move("[\n  1, /*x*/ /*y*/ 2\n]", Jsonc, At(Item(0), Comment(0)), 1);

        Assert.Equal("[\n  1, /*y*/ /*x*/ 2\n]", text);
    }

    [Fact]
    public void A_member_with_a_trailing_comment_moves_above_a_neighbour_on_another_line_with_the_comment()
    {
        string text = Move("{\n  \"x\": 0,\n  \"a\": 1, /*t*/\n  \"b\": 2\n}", Jsonc, At(Item(0), Member("a")), -1);

        Assert.Equal("{\n  \"a\": 1, /*t*/\n  \"x\": 0,\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_member_with_a_trailing_comment_moves_below_a_neighbour_on_another_line_with_the_comment()
    {
        string text = Move("{\n  \"x\": 0,\n  \"a\": 1, /*t*/\n  \"b\": 2\n}", Jsonc, At(Item(0), Member("a")), 1);

        Assert.Equal("{\n  \"x\": 0,\n  \"b\": 2,\n  \"a\": 1 /*t*/\n}", text);
    }

    [Fact]
    public void A_member_with_a_trailing_comment_moves_in_front_of_a_neighbour_on_its_line_and_the_line_break_stays()
    {
        string text = Move("{\n  \"x\": 0, \"a\": 1, /*t*/\n  \"b\": 2\n}", Jsonc, At(Item(0), Member("a")), -1);

        Assert.Equal("{\n  \"a\": 1, /*t*/ \"x\": 0,\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_block_comment_moves_down_past_its_sibling_comment_on_the_same_line()
    {
        string text = Move("{\n  \"a\": 1, /*t1*/ /*t2*/\n  \"b\": 2\n}", Jsonc, At(Item(0), Comment(0)), 1);

        Assert.Equal("{\n  \"a\": 1, /*t2*/ /*t1*/\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_block_comment_moves_down_past_an_item_in_a_single_line_array_to_the_array_item_before_it()
    {
        string text = Move("[1, /*x*/ /*y*/ 2]", Jsonc, At(Item(0), Comment(0)), 1);

        Assert.Equal("[1, /*y*/ /*x*/ 2]", text);
    }

    [Fact]
    public void A_member_moves_down_past_a_neighbour_on_its_line_and_the_line_break_stays()
    {
        string text = Move("{\n  \"a\": 1, \"x\": 0,\n  \"b\": 2\n}", Jsonc, At(Item(0), Member("a")), 1);

        Assert.Equal("{\n  \"x\": 0, \"a\": 1,\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_member_with_a_trailing_comment_moves_down_past_a_neighbour_on_its_line_with_the_comment()
    {
        string text = Move("{\n  \"a\": 1, /*t*/ \"x\": 0,\n  \"b\": 2\n}", Jsonc, At(Item(0), Member("a")), 1);

        Assert.Equal("{\n  \"x\": 0, \"a\": 1, /*t*/\n  \"b\": 2\n}", text);
    }

    /// <summary>The move that <see cref="ConfigDocument.MoveBy"/> returns, applied to the text.</summary>
    private static string Move(string text, IConfigFormat format, NodePath node, int delta)
    {
        MoveNode move = Assert.IsType<MoveNode>(ModelTestSupport.Parse(text, format).MoveBy(node, delta));
        return Edit(text, format, move);
    }
}
