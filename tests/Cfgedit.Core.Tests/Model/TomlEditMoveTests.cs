using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Move on TOML: key/values with their comments within a section, multi-line values, items of inline arrays (one line and one per line,
/// trailing commas), inline table members, items of arrays of tables with their subtables, sibling header tables, the tables that cannot
/// move (dotted, implicit, spread), trailing comments, the last line without a break, and CRLF.
/// </summary>
public sealed class TomlEditMoveTests
{
    private static readonly IConfigFormat Toml = EditTestSupport.Toml;

    private const string Neighbour = "this cannot move: its neighbour is in another part of the file";

    private const string Spread = "this table is spread over the file and cannot move";

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    [Fact]
    public void A_key_value_moves_down_with_its_attached_and_trailing_comments()
    {
        string text = Edit("# about a\na = 1 # t\nb = 2 # u\nc = 3\n", Toml, new MoveNode(Path(Member("a")), 2));

        Assert.Equal("b = 2 # u\n# about a\na = 1 # t\nc = 3\n", text);
    }

    [Fact]
    public void A_key_value_moves_up_past_a_key_value_with_an_attached_comment()
    {
        string text = Edit("a = 1\n# about b\nb = 2\nc = 3\n", Toml, new MoveNode(Path(Member("c")), 0));

        Assert.Equal("c = 3\na = 1\n# about b\nb = 2\n", text);
    }

    [Fact]
    public void A_key_value_moves_to_the_end_of_its_section_and_keeps_the_blank_lines_in_place()
    {
        string text = Edit("a = 1\n\nb = 2\n\nc = 3\n", Toml, new MoveNode(Path(Member("a")), 2));

        Assert.Equal("b = 2\n\nc = 3\n\na = 1\n", text);
    }

    [Fact]
    public void A_key_value_cannot_move_past_a_table_header_into_another_section()
    {
        CfgeditException error = Fails("a = 1\n[t]\nb = 2\n", Toml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal(Neighbour, error.Message);
    }

    [Fact]
    public void A_table_cannot_move_past_a_key_value_of_the_root()
    {
        CfgeditException error = Fails("k = 1\n[t]\nx = 1\n", Toml, new MoveNode(Path(Member("t")), 0));

        Assert.Equal(Neighbour, error.Message);
    }

    [Fact]
    public void A_multi_line_value_moves_as_a_whole_line_block()
    {
        string text = Edit("a = [\n  1,\n  2,\n]\nb = 3\n", Toml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal("b = 3\na = [\n  1,\n  2,\n]\n", text);
    }

    [Fact]
    public void A_comment_row_moves_alone_past_the_entry_it_is_attached_to()
    {
        string text = Edit("# top\na = 1\n", Toml, new MoveNode(Path(Comment(0)), 1));

        Assert.Equal("a = 1\n# top\n", text);
    }

    [Fact]
    public void A_trailing_comment_cannot_move_on_its_own()
    {
        CfgeditException error = Fails("a = 1 # t\nb = 2\n", Toml, new MoveNode(Path(Comment(0)), 1));

        Assert.Equal("a trailing comment moves with its entry", error.Message);
    }

    [Fact]
    public void An_inline_array_item_moves_to_the_front_with_commas_fixed()
    {
        string text = Edit("a = [1, 2, 3]\n", Toml, new MoveNode(Path(Member("a"), Item(2)), 0));

        Assert.Equal("a = [3, 1, 2]\n", text);
    }

    [Fact]
    public void An_inline_array_with_a_trailing_comma_keeps_it_on_its_last_item()
    {
        string text = Edit("a = [1, 2,]\n", Toml, new MoveNode(Path(Member("a"), Item(0)), 1));

        Assert.Equal("a = [2, 1,]\n", text);
    }

    [Fact]
    public void A_one_per_line_array_item_moves_up_and_keeps_its_line_layout()
    {
        string text = Edit("a = [\n  1,\n  2,\n  3,\n]\n", Toml, new MoveNode(Path(Member("a"), Item(2)), 0));

        Assert.Equal("a = [\n  3,\n  1,\n  2,\n]\n", text);
    }

    [Fact]
    public void A_last_item_without_a_trailing_comma_gets_one_when_it_moves_into_the_middle()
    {
        string text = Edit("a = [\n  1,\n  2\n]\n", Toml, new MoveNode(Path(Member("a"), Item(0)), 1));

        Assert.Equal("a = [\n  2,\n  1\n]\n", text);
    }

    [Fact]
    public void A_comment_on_an_item_moves_with_the_item()
    {
        string text = Edit("a = [\n  1, # one\n  2,\n]\n", Toml, new MoveNode(Path(Member("a"), Item(0)), 1));

        Assert.Equal("a = [\n  2,\n  1, # one\n]\n", text);
    }

    [Fact]
    public void A_comment_row_inside_an_array_moves_alone()
    {
        string text = Edit("a = [\n  # c\n  1,\n  2,\n]\n", Toml, new MoveNode(Path(Member("a"), Comment(0)), 2));

        Assert.Equal("a = [\n  1,\n  2,\n  # c\n]\n", text);
    }

    [Fact]
    public void An_inline_table_member_moves_to_the_front_on_one_line()
    {
        string text = Edit("t = { a = 1, b = 2, c = 3 }\n", Toml, new MoveNode(Path(Member("t"), Member("c")), 0));

        Assert.Equal("t = { c = 3, a = 1, b = 2 }\n", text);
    }

    [Fact]
    public void An_inline_table_member_that_is_an_inline_table_moves_with_its_value()
    {
        string text = Edit("t = { a = { q = 1 }, b = 2 }\n", Toml, new MoveNode(Path(Member("t"), Member("b")), 0));

        Assert.Equal("t = { b = 2, a = { q = 1 } }\n", text);
    }

    [Fact]
    public void A_member_of_a_dotted_table_inside_an_inline_table_cannot_move_yet()
    {
        CfgeditException error = Fails("t = { a.x = 1, b = 2 }\n", Toml, new MoveNode(Path(Member("t"), Member("a"), Member("x")), 1));

        Assert.Equal("a member of a dotted table inside an inline table cannot move yet", error.Message);
    }

    [Fact]
    public void An_array_of_tables_item_moves_as_a_region_with_its_subtable()
    {
        string text = Edit("[[x]]\na = 1\n[x.sub]\nb = 2\n[[x]]\na = 3\n", Toml, new MoveNode(Path(Member("x"), Item(1)), 0));

        Assert.Equal("[[x]]\na = 3\n[[x]]\na = 1\n[x.sub]\nb = 2\n", text);
    }

    [Fact]
    public void An_array_of_tables_cannot_move_among_its_siblings_yet()
    {
        CfgeditException error = Fails("a = 1\n[[x]]\nb = 2\n", Toml, new MoveNode(Path(Member("x")), 0));

        Assert.Equal("an array of tables cannot move among its siblings yet", error.Message);
    }

    [Fact]
    public void A_comment_above_a_header_moves_with_its_table()
    {
        string text = Edit("[a]\nx = 1\n# about b\n[b]\ny = 2\n", Toml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal("# about b\n[b]\ny = 2\n[a]\nx = 1\n", text);
    }

    [Fact]
    public void An_array_of_tables_item_moves_with_the_comment_above_its_header()
    {
        string text = Edit("# first\n[[x]]\na = 1\n# second\n[[x]]\na = 2\n", Toml, new MoveNode(Path(Member("x"), Item(1)), 0));

        Assert.Equal("# second\n[[x]]\na = 2\n# first\n[[x]]\na = 1\n", text);
    }

    [Fact]
    public void A_member_of_a_dotted_table_moves_within_its_dotted_lines()
    {
        string text = Edit("a.x = 1\na.y = 2\n", Toml, new MoveNode(Path(Member("a"), Member("x")), 1));

        Assert.Equal("a.y = 2\na.x = 1\n", text);
    }

    [Fact]
    public void Contiguous_header_tables_swap_their_sections()
    {
        string text = Edit("[a]\nx = 1\n\n[b]\ny = 2\n", Toml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal("[b]\ny = 2\n\n[a]\nx = 1\n", text);
    }

    [Fact]
    public void A_header_table_moves_with_its_deeper_sections()
    {
        string text = Edit("[a]\nx = 1\n[a.c]\nz = 1\n[b]\ny = 2\n", Toml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal("[b]\ny = 2\n[a]\nx = 1\n[a.c]\nz = 1\n", text);
    }

    [Fact]
    public void A_header_table_whose_sections_are_spread_over_the_file_cannot_move()
    {
        CfgeditException error = Fails("[a]\nx = 1\n[b]\ny = 1\n[a.c]\nz = 2\n", Toml, new MoveNode(Path(Member("a")), 2));

        Assert.Equal(Spread, error.Message);
    }

    [Fact]
    public void A_dotted_table_cannot_move()
    {
        CfgeditException error = Fails("a.x = 1\nb = 2\n", Toml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal(Spread, error.Message);
    }

    [Fact]
    public void A_key_value_cannot_move_past_a_dotted_table()
    {
        CfgeditException error = Fails("a.x = 1\nb = 2\n", Toml, new MoveNode(Path(Member("b")), 0));

        Assert.Equal(Neighbour, error.Message);
    }

    [Fact]
    public void A_last_line_without_a_break_moves_down_and_the_file_still_ends_without_one()
    {
        string text = Edit("a = 1\nb = 2", Toml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal("b = 2\na = 1", text);
    }

    [Fact]
    public void A_last_line_without_a_break_moves_up_and_gets_one_while_the_file_keeps_none()
    {
        string text = Edit("a = 1\nb = 2", Toml, new MoveNode(Path(Member("b")), 0));

        Assert.Equal("b = 2\na = 1", text);
    }

    [Fact]
    public void A_key_value_moves_down_in_a_CRLF_document()
    {
        string text = Edit("a = 1\r\nb = 2\r\n", Toml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal("b = 2\r\na = 1\r\n", text);
    }

    [Fact]
    public void A_last_line_without_a_break_moves_up_in_a_CRLF_document()
    {
        string text = Edit("a = 1\r\nb = 2", Toml, new MoveNode(Path(Member("b")), 0));

        Assert.Equal("b = 2\r\na = 1", text);
    }

    [Fact]
    public void A_header_table_cannot_move_past_an_implicit_table()
    {
        CfgeditException error = Fails("[a.b]\nx = 1\n[c]\ny = 1\n", Toml, new MoveNode(Path(Member("c")), 0));

        Assert.Equal(Neighbour, error.Message);
    }

    [Fact]
    public void A_comment_cannot_move_onto_the_header_of_the_table_below_it()
    {
        CfgeditException error = Fails("# note\n\na = 1\n[t]\nx = 1\n", Toml, new MoveNode(Path(Comment(0)), 1));

        Assert.Equal("this cannot move there: a comment would then sit on the table header below it", error.Message);
    }

    [Fact]
    public void A_key_value_cannot_move_between_a_comment_and_the_entry_attached_to_it()
    {
        CfgeditException error = Fails("# c\na = 1\nb = 2\n", Toml, new MoveNode(Path(Member("b")), 1));

        Assert.Equal("this cannot move there: it would split an entry from its comments", error.Message);
    }

    [Fact]
    public void After_a_move_the_moved_node_keeps_the_focus_at_its_new_place()
    {
        (_, NodePath? item) = EditWithFocus("a = [1, 2, 3]\n", Toml, new MoveNode(Path(Member("a"), Item(2)), 0));
        (_, NodePath? comment) = EditWithFocus("# top\na = 1\n", Toml, new MoveNode(Path(Comment(0)), 1));

        Assert.Equal(Path(Member("a"), Item(0)), item);
        Assert.Equal(Path(Comment(0)), comment);
    }

    [Fact]
    public void A_move_to_the_place_it_already_has_changes_nothing()
    {
        const string original = "a = 1\nb = 2\n";

        Assert.Equal(original, Edit(original, Toml, new MoveNode(Path(Member("b")), 1)));
    }
}
