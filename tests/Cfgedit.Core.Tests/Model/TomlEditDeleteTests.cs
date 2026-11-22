using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Delete on TOML: key/value lines with their comments, inline array and inline table members with their commas, tables and arrays of tables spread over pieces, comments, and the refusals.</summary>
public sealed class TomlEditDeleteTests
{
    private static readonly IConfigFormat Toml = EditTestSupport.Toml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    // ----- Key/value lines -----

    [Fact]
    public void A_key_value_takes_its_attached_comment_above_and_its_trailing_comment()
    {
        string text = Edit("# about a\na = 1 # trailing\nb = 2\n", Toml, new DeleteNode(Path(Member("a"))));

        Assert.Equal("b = 2\n", text);
    }

    [Fact]
    public void A_comment_separated_by_a_blank_line_stays_when_the_key_value_goes()
    {
        string text = Edit("# standalone\n\na = 1\n", Toml, new DeleteNode(Path(Member("a"))));

        Assert.Equal("# standalone\n\n", text);
    }

    [Fact]
    public void A_multi_line_array_value_goes_with_all_its_lines()
    {
        string text = Edit("a = [\n  1,\n  2,\n]\nb = 0\n", Toml, new DeleteNode(Path(Member("a"))));

        Assert.Equal("b = 0\n", text);
    }

    [Fact]
    public void A_multi_line_string_value_goes_with_all_its_lines()
    {
        string text = Edit("s = \"\"\"\nabc\n\"\"\"\nt = 1\n", Toml, new DeleteNode(Path(Member("s"))));

        Assert.Equal("t = 1\n", text);
    }

    [Fact]
    public void A_key_value_in_a_CRLF_document_goes_with_its_line_break()
    {
        string text = Edit("k = 1\r\na = 2\r\nb = 3\r\n", Toml, new DeleteNode(Path(Member("a"))));

        Assert.Equal("k = 1\r\nb = 3\r\n", text);
    }

    [Fact]
    public void A_dotted_key_leaf_goes_and_its_sibling_stays()
    {
        string text = Edit("a.b = 1\na.c = 2\n", Toml, new DeleteNode(Path(Member("a"), Member("b"))));

        Assert.Equal("a.c = 2\n", text);
    }

    [Fact]
    public void The_last_dotted_key_goes_with_the_tables_it_alone_defined()
    {
        string text = Edit("a.b = 1\nk = 0\n", Toml, new DeleteNode(Path(Member("a"), Member("b"))));

        Assert.Equal("k = 0\n", text);
    }

    [Fact]
    public void Deleting_a_key_value_focuses_the_next_sibling()
    {
        (string text, NodePath? focus) = EditWithFocus("k = 1\na = 2\nb = 3\n", Toml, new DeleteNode(Path(Member("a"))));

        Assert.Equal("k = 1\nb = 3\n", text);
        Assert.Equal(Path(Member("b")), focus);
    }

    // ----- Inline arrays -----

    [Fact]
    public void A_one_line_array_item_goes_with_its_comma_and_the_next_item_moves_up()
    {
        string text = Edit("a = [1, 2, 3]\n", Toml, new DeleteNode(Path(Member("a"), Item(1))));

        Assert.Equal("a = [1, 3]\n", text);
    }

    [Fact]
    public void The_last_one_line_array_item_goes_with_the_comma_before_it()
    {
        string text = Edit("a = [1, 2, 3]\n", Toml, new DeleteNode(Path(Member("a"), Item(2))));

        Assert.Equal("a = [1, 2]\n", text);
    }

    [Fact]
    public void A_multi_line_item_with_a_trailing_comma_goes_with_its_line()
    {
        string text = Edit("a = [\n  1,\n  2,\n]\n", Toml, new DeleteNode(Path(Member("a"), Item(1))));

        Assert.Equal("a = [\n  1,\n]\n", text);
    }

    [Fact]
    public void A_last_multi_line_item_without_a_comma_leaves_the_comma_of_the_item_before_it()
    {
        string text = Edit("a = [\n  1,\n  2\n]\n", Toml, new DeleteNode(Path(Member("a"), Item(1))));

        Assert.Equal("a = [\n  1,\n]\n", text);
    }

    [Fact]
    public void A_nested_array_item_goes_with_its_comma()
    {
        string text = Edit("a = [[1, 2], [3]]\n", Toml, new DeleteNode(Path(Member("a"), Item(0))));

        Assert.Equal("a = [[3]]\n", text);
    }

    [Fact]
    public void An_inline_table_item_of_an_array_goes_with_its_comma()
    {
        string text = Edit("a = [{x = 1}, {x = 2}]\n", Toml, new DeleteNode(Path(Member("a"), Item(0))));

        Assert.Equal("a = [{x = 2}]\n", text);
    }

    [Fact]
    public void A_comment_on_the_line_of_an_array_item_goes_with_the_item()
    {
        string text = Edit("a = [\n  1, # one\n  2\n]\n", Toml, new DeleteNode(Path(Member("a"), Item(0))));

        Assert.Equal("a = [\n  2\n]\n", text);
    }

    [Fact]
    public void A_comment_line_above_an_array_item_is_a_row_of_the_array_and_stays_when_the_item_goes()
    {
        string text = Edit("a = [\n  # c\n  1,\n  2,\n]\n", Toml, new DeleteNode(Path(Member("a"), Item(0))));

        Assert.Equal("a = [\n  # c\n  2,\n]\n", text);
    }

    // ----- Inline tables -----

    [Fact]
    public void A_first_inline_member_goes_with_its_comma()
    {
        string text = Edit("t = { x = 1, y = 2 }\n", Toml, new DeleteNode(Path(Member("t"), Member("x"))));

        Assert.Equal("t = { y = 2 }\n", text);
    }

    [Fact]
    public void A_last_inline_member_goes_with_the_comma_before_it()
    {
        string text = Edit("t = { x = 1, y = 2 }\n", Toml, new DeleteNode(Path(Member("t"), Member("y"))));

        Assert.Equal("t = { x = 1 }\n", text);
    }

    // ----- Tables and arrays of tables -----

    [Fact]
    public void A_table_spread_over_a_header_and_a_deeper_header_goes_with_all_its_pieces()
    {
        string text = Edit("[a]\nx = 1\n[a.b]\ny = 2\n[c]\nz = 3\n", Toml, new DeleteNode(Path(Member("a"))));

        Assert.Equal("[c]\nz = 3\n", text);
    }

    [Fact]
    public void A_table_spread_over_dotted_keys_and_a_deeper_header_goes_with_all_its_pieces()
    {
        string text = Edit("k = 0\na.c = 3\n[a.b]\ny = 2\n", Toml, new DeleteNode(Path(Member("a"))));

        Assert.Equal("k = 0\n", text);
    }

    [Fact]
    public void A_header_takes_the_comment_lines_attached_above_it()
    {
        string text = Edit("k = 0\n# doc\n[a]\nx = 1\n", Toml, new DeleteNode(Path(Member("a"))));

        Assert.Equal("k = 0\n", text);
    }

    [Fact]
    public void An_array_of_tables_item_goes_with_its_subtable_section()
    {
        const string text = "[[fruit]]\nname = \"apple\"\n[fruit.physical]\ncolor = \"red\"\n[[fruit]]\nname = \"banana\"\n";

        string deleted = Edit(text, Toml, new DeleteNode(Path(Member("fruit"), Item(0))));

        Assert.Equal("[[fruit]]\nname = \"banana\"\n", deleted);
    }

    [Fact]
    public void A_whole_array_of_tables_goes_with_every_item()
    {
        string text = Edit("k = 0\n[[fruit]]\nname = \"apple\"\n[[fruit]]\nname = \"banana\"\n", Toml, new DeleteNode(Path(Member("fruit"))));

        Assert.Equal("k = 0\n", text);
    }

    [Fact]
    public void The_last_array_of_tables_item_goes_and_the_array_goes_with_it()
    {
        string text = Edit("k = 0\n[[fruit]]\nname = \"a\"\n", Toml, new DeleteNode(Path(Member("fruit"), Item(0))));

        Assert.Equal("k = 0\n", text);
    }

    [Fact]
    public void A_section_followed_directly_by_another_table_goes_without_touching_the_next_one()
    {
        string text = Edit("[a]\nx = 1\n[b]\ny = 2\n", Toml, new DeleteNode(Path(Member("a"))));

        Assert.Equal("[b]\ny = 2\n", text);
    }

    [Fact]
    public void An_inline_member_of_an_array_item_goes_with_its_comma()
    {
        string text = Edit("a = [{ x = 1, y = 2 }]\n", Toml, new DeleteNode(Path(Member("a"), Item(0), Member("x"))));

        Assert.Equal("a = [{ y = 2 }]\n", text);
    }

    [Fact]
    public void A_multi_line_array_with_comments_goes_with_all_of_them()
    {
        string text = Edit("a = [\n  # c\n  1, # t\n  2\n]\nb = 1\n", Toml, new DeleteNode(Path(Member("a"))));

        Assert.Equal("b = 1\n", text);
    }

    [Fact]
    public void The_trailing_comment_of_a_header_goes_with_the_spaces_before_it()
    {
        string text = Edit("[a] # hdr\nx = 1\n", Toml, new DeleteNode(Path(Member("a"), Comment(0))));

        Assert.Equal("[a]\nx = 1\n", text);
    }

    [Fact]
    public void A_table_that_only_the_deleted_table_kept_alive_goes_too()
    {
        string text = Edit("k = 0\n[a.b.c]\nx = 1\n", Toml, new DeleteNode(Path(Member("a"), Member("b"), Member("c"))));

        Assert.Equal("k = 0\n", text);
    }

    [Fact]
    public void Deleting_a_table_focuses_the_nearest_surviving_ancestor_when_the_parents_go_too()
    {
        (string text, NodePath? focus) = EditWithFocus("k = 0\n[a.b.c]\nx = 1\n", Toml, new DeleteNode(Path(Member("a"), Member("b"), Member("c"))));

        Assert.Equal("k = 0\n", text);
        Assert.Equal(Path(), focus);
    }

    // ----- Comments -----

    [Fact]
    public void A_comment_line_goes_with_its_line()
    {
        string text = Edit("# a\nk = 1\n", Toml, new DeleteNode(Path(Comment(0))));

        Assert.Equal("k = 1\n", text);
    }

    [Fact]
    public void A_trailing_comment_goes_with_the_spaces_before_it_and_keeps_the_line()
    {
        string text = Edit("k = 1 # t\nj = 2\n", Toml, new DeleteNode(Path(Comment(0))));

        Assert.Equal("k = 1\nj = 2\n", text);
    }

    [Fact]
    public void A_trailing_comment_in_an_array_goes_and_keeps_the_comma()
    {
        string text = Edit("a = [\n  1, # one\n  2\n]\n", Toml, new DeleteNode(Path(Member("a"), Comment(0))));

        Assert.Equal("a = [\n  1,\n  2\n]\n", text);
    }

    [Fact]
    public void A_comment_alone_in_an_array_goes_with_its_line()
    {
        string text = Edit("a = [\n  # first\n  1\n]\n", Toml, new DeleteNode(Path(Member("a"), Comment(0))));

        Assert.Equal("a = [\n  1\n]\n", text);
    }

    // ----- Refusals and the other operations -----

    [Fact]
    public void The_root_table_cannot_be_deleted()
    {
        CfgeditException error = Fails("a = 1\n", Toml, new DeleteNode(Path()));

        Assert.Equal("the root table cannot be deleted", error.Message);
    }

    [Fact]
    public void A_JSON_node_cannot_be_inserted_into_TOML()
    {
        CfgeditException error = Fails("a = 1\n", Toml, new InsertNode(Path(), 0, new NewJsonComment("x", false)));

        Assert.Equal("this kind of node cannot be inserted into TOML", error.Message);
    }

    [Fact]
    public void The_root_cannot_be_duplicated_in_TOML()
    {
        CfgeditException error = Fails("a = 1\n", Toml, new DuplicateNode(Path()));

        Assert.Equal("the root table cannot be duplicated", error.Message);
    }

    [Fact]
    public void A_JSON_type_change_is_refused_on_TOML()
    {
        CfgeditException error = Fails("a = 1\n", Toml, new ChangeJsonType(Path(Member("a")), Cfgedit.Core.Model.JsonValueKind.String, "x"));

        Assert.Equal("use the TOML type change", error.Message);
    }
}
