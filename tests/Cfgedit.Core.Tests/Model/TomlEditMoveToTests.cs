using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// MoveNodeTo and NestBy on TOML: key/values into a table (before its sub-tables, after its key/values), into and out of inline tables and
/// inline arrays, items between arrays, keys (kept, renamed, dropped, required), header tables and arrays of tables moving with their
/// headers renamed, multi-line values, attached comments, the refusals (dotted keys, implicit tables, a header table into an array, duplicate
/// keys), focus, and the nest rules at the edges of the root table.
/// </summary>
public sealed class TomlEditMoveToTests
{
    private static readonly IConfigFormat Toml = EditTestSupport.Toml;

    private static NodePath Root => At(Item(0));

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    [Fact]
    public void A_root_key_value_moves_into_the_first_table_at_its_start()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "a = 1\n[t]\nx = 1\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 0, null));

        Assert.Equal("[t]\na = 1\nx = 1\n", text);
        Assert.Equal(Path(Member("t"), Member("a")), focus);
    }

    [Fact]
    public void A_key_value_moves_into_the_table_before_its_sub_tables_and_after_its_key_values()
    {
        // Index 2 is after the sub-table [t.s], so the key/value goes after the last key/value of [t] instead.
        string text = Edit(
            "a = 0\n[t]\nx = 1\n[t.s]\ny = 2\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 2, null));

        Assert.Equal("[t]\nx = 1\na = 0\n[t.s]\ny = 2\n", text);
    }

    [Fact]
    public void A_first_key_value_of_a_table_moves_out_to_the_root_before_the_first_header()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "k = 1\n[t]\na = 2\nb = 3\n",
            Toml,
            new MoveNodeTo(Path(Member("t"), Member("a")), Root, 1, null));

        Assert.Equal("k = 1\na = 2\n[t]\nb = 3\n", text);
        Assert.Equal(Path(Member("a")), focus);
    }

    [Fact]
    public void A_key_value_moves_into_the_root_at_the_top_when_it_has_no_key_values()
    {
        string text = Edit("[t]\na = 2\n[u]\n", Toml, new MoveNodeTo(Path(Member("t"), Member("a")), Root, 0, null));

        Assert.Equal("a = 2\n[t]\n[u]\n", text);
    }

    [Fact]
    public void A_key_value_moves_into_an_inline_table_as_its_last_member()
    {
        string text = Edit(
            "a = 1\nt = { x = 2 }\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 1, null));

        Assert.Equal("t = { x = 2, a = 1 }\n", text);
    }

    [Fact]
    public void An_inline_member_moves_out_to_the_root_after_its_table()
    {
        string text = Edit(
            "t = { x = 2, a = 1 }\n",
            Toml,
            new MoveNodeTo(Path(Member("t"), Member("a")), Root, 1, null));

        Assert.Equal("t = { x = 2 }\na = 1\n", text);
    }

    [Fact]
    public void An_item_moves_between_inline_arrays_and_takes_the_first_place()
    {
        string text = Edit(
            "a = [1, 2]\nb = [3]\n",
            Toml,
            new MoveNodeTo(Path(Member("a"), Item(1)), Path(Member("b")), 0, null));

        Assert.Equal("a = [1]\nb = [2, 3]\n", text);
    }

    [Fact]
    public void A_key_value_moves_into_an_inline_array_and_its_key_is_dropped()
    {
        string text = Edit(
            "x = 1\na = [5]\n",
            Toml,
            new MoveNodeTo(Path(Member("x")), Path(Member("a")), 0, null));

        Assert.Equal("a = [1, 5]\n", text);
    }

    [Fact]
    public void An_item_moves_into_an_inline_array_with_a_key_refused()
    {
        CfgeditException error = Fails(
            "x = 1\na = [5]\n",
            Toml,
            new MoveNodeTo(Path(Member("x")), Path(Member("a")), 0, "k"));

        Assert.Equal("an item of an array has no key", error.Message);
    }

    [Fact]
    public void An_array_item_moves_into_an_inline_table_with_a_new_key()
    {
        string text = Edit(
            "a = [1, 2]\nt = { x = 0 }\n",
            Toml,
            new MoveNodeTo(Path(Member("a"), Item(0)), Path(Member("t")), 1, "k"));

        Assert.Equal("a = [2]\nt = { x = 0, k = 1 }\n", text);
    }

    [Fact]
    public void An_array_item_moves_into_a_table_and_needs_a_key()
    {
        CfgeditException error = Fails(
            "a = [1, 2]\n[t]\n",
            Toml,
            new MoveNodeTo(Path(Member("a"), Item(0)), Path(Member("t")), 0, null));

        Assert.Equal("a key is needed for an item moving into a table", error.Message);
    }

    [Fact]
    public void A_key_value_moves_into_a_table_with_its_key_renamed()
    {
        string text = Edit(
            "a = 1\n[t]\nx = 2\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 1, "b"));

        Assert.Equal("[t]\nx = 2\nb = 1\n", text);
    }

    [Fact]
    public void A_key_that_already_exists_in_the_destination_is_refused()
    {
        CfgeditException error = Fails(
            "a = 1\n[t]\na = 2\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 0, null));

        Assert.Equal("a key named 'a' already exists here", error.Message);
    }

    [Fact]
    public void A_multi_line_string_keeps_its_text_when_it_moves()
    {
        string text = Edit(
            "s = \"\"\"\nline\n\"\"\"\n[t]\n",
            Toml,
            new MoveNodeTo(Path(Member("s")), Path(Member("t")), 0, null));

        Assert.Equal("[t]\ns = \"\"\"\nline\n\"\"\"\n", text);
    }

    [Fact]
    public void A_multi_line_array_keeps_its_lines_when_it_moves_into_a_table()
    {
        string text = Edit(
            "a = [\n  1,\n  2,\n]\n[t]\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 0, null));

        Assert.Equal("[t]\na = [\n  1,\n  2,\n]\n", text);
    }

    [Fact]
    public void A_key_value_moves_with_its_attached_comment_above_and_its_trailing_comment()
    {
        string text = Edit(
            "# about a\na = 1 # t\n[t]\nb = 2\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 1, null));

        Assert.Equal("[t]\nb = 2\n# about a\na = 1 # t\n", text);
    }

    [Fact]
    public void A_key_value_with_a_trailing_comment_cannot_move_into_an_inline_table()
    {
        CfgeditException error = Fails(
            "a = 1 # t\nt = { x = 2 }\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 1, null));

        Assert.Equal("a comment on an entry cannot move into an array or inline table yet", error.Message);
    }

    [Fact]
    public void A_header_table_moves_into_another_table_with_its_sub_tables_renamed()
    {
        string text = Edit(
            "[a]\nx = 1\n[a.b]\ny = 2\n[t]\nz = 3\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 1, null));

        Assert.Equal("[t]\nz = 3\n[t.a]\nx = 1\n[t.a.b]\ny = 2\n", text);
    }

    [Fact]
    public void A_header_table_moves_under_a_new_key_and_its_sub_tables_follow()
    {
        string text = Edit(
            "[a]\nx = 1\n[a.b]\ny = 2\n[t]\nz = 3\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 1, "k"));

        Assert.Equal("[t]\nz = 3\n[t.k]\nx = 1\n[t.k.b]\ny = 2\n", text);
    }

    [Fact]
    public void A_header_table_moves_before_a_sub_table_of_the_destination_at_that_index()
    {
        // Index 0 is the key/value x of [t], so the section goes after it, before the sub-table [t.s].
        string text = Edit(
            "[a]\n[t]\nx = 1\n[t.s]\ny = 1\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 0, null));

        Assert.Equal("[t]\nx = 1\n[t.a]\n[t.s]\ny = 1\n", text);
    }

    [Fact]
    public void A_header_table_moves_out_to_the_root_and_its_header_loses_the_old_prefix()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "[t]\nx = 1\n[t.s]\ny = 1\n",
            Toml,
            new MoveNodeTo(Path(Member("t"), Member("s")), Root, 1, null));

        Assert.Equal("[t]\nx = 1\n[s]\ny = 1\n", text);
        Assert.Equal(Path(Member("s")), focus);
    }

    [Fact]
    public void An_item_of_an_array_of_tables_moves_into_a_table_under_the_array_name()
    {
        string text = Edit(
            "[[x]]\nn = 1\n[[x]]\nn = 2\n[t]\n",
            Toml,
            new MoveNodeTo(Path(Member("x"), Item(1)), Path(Member("t")), 0, null));

        Assert.Equal("[[x]]\nn = 1\n[t]\n[[t.x]]\nn = 2\n", text);
    }

    [Fact]
    public void A_whole_array_of_tables_moves_with_all_its_items_into_a_table()
    {
        string text = Edit(
            "k = 1\n[[x]]\nn = 1\n[t]\nq = 2\n",
            Toml,
            new MoveNodeTo(Path(Member("x")), Path(Member("t")), 0, null));

        Assert.Equal("k = 1\n[t]\nq = 2\n[[t.x]]\nn = 1\n", text);
    }

    [Fact]
    public void A_header_table_cannot_move_into_an_inline_array()
    {
        CfgeditException error = Fails(
            "t = [1, 2]\n[a]\nx = 1\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 0, "k"));

        Assert.Equal("a table with its own header can only move into a table with a header, or into the root", error.Message);
    }

    [Fact]
    public void A_table_with_its_own_header_cannot_move_into_an_item_of_an_array_of_tables()
    {
        CfgeditException error = Fails(
            "[[x]]\nn = 1\n[t]\nq = 2\n",
            Toml,
            new MoveNodeTo(Path(Member("t")), Path(Member("x"), Item(0)), 0, null));

        Assert.Equal("a table with its own header cannot move into an item of an array of tables yet", error.Message);
    }

    [Fact]
    public void A_table_defined_by_dotted_keys_cannot_move()
    {
        CfgeditException error = Fails(
            "a.b = 1\n[t]\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 0, null));

        Assert.Equal("a table defined by dotted keys cannot move; move its keys instead", error.Message);
    }

    [Fact]
    public void A_dotted_key_value_cannot_move_to_another_table()
    {
        CfgeditException error = Fails(
            "a.b = 1\n[t]\n",
            Toml,
            new MoveNodeTo(Path(Member("a"), Member("b")), Path(Member("t")), 0, null));

        Assert.Equal("a value under dotted keys cannot move to another table yet; move the table that defines it instead", error.Message);
    }

    [Fact]
    public void A_table_defined_only_by_its_sub_tables_cannot_move()
    {
        CfgeditException error = Fails(
            "[a.b]\nx = 1\n[t]\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 0, null));

        Assert.Equal("a table defined only by its sub-tables cannot move yet", error.Message);
    }

    [Fact]
    public void Nothing_moves_into_a_table_defined_only_by_its_sub_tables()
    {
        CfgeditException error = Fails(
            "a = 1\n[x.y]\nz = 2\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("x")), 0, null));

        Assert.Equal("a table defined by dotted keys, or only by its sub-tables, cannot take a moved node yet", error.Message);
    }

    [Fact]
    public void A_section_moved_into_a_table_keeps_one_blank_line_between_sections()
    {
        string text = Edit(
            "[a]\nx = 1\n\n[t]\nz = 3\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 1, null));

        Assert.Equal("[t]\nz = 3\n\n[t.a]\nx = 1\n", text);
    }

    [Fact]
    public void NestBy_down_from_a_header_table_enters_the_next_table_after_its_key_values()
    {
        const string original = "[a]\nx = 1\n[t]\nz = 3\n";
        NestStep? step = ModelTestSupport.Parse(original, Toml).NestBy(Path(Member("a")), 1);

        Assert.Equal(new MoveNodeTo(Path(Member("a")), Path(Member("t")), 0, null), step!.Op);
        Assert.Equal("[t]\nz = 3\n[t.a]\nx = 1\n", Edit(original, Toml, step.Op));
    }

    [Fact]
    public void A_comment_inside_a_moved_value_takes_its_new_place_in_the_comment_order()
    {
        // The comment inside the array moves with it, after the comment of [t]: the verification of the edit checks the order.
        string text = Edit(
            "# before\na = [ # inner\n  1,\n]\n[t]\n# in t\nb = 2\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 1, null));

        // Index 1 is the key/value b, so the value goes before b's attached comment line.
        Assert.Equal("[t]\n# before\na = [ # inner\n  1,\n]\n# in t\nb = 2\n", text);
    }

    [Fact]
    public void A_multi_line_array_moves_into_an_inline_table_as_a_value()
    {
        string text = Edit(
            "a = [\n  1,\n  2,\n]\nt = { x = 0 }\n",
            Toml,
            new MoveNodeTo(Path(Member("a")), Path(Member("t")), 1, null));

        Assert.Equal("t = { x = 0, a = [\n  1,\n  2,\n] }\n", text);
    }

    [Fact]
    public void A_multi_line_string_moves_into_an_inline_table_with_its_text_kept()
    {
        string text = Edit(
            "s = \"\"\"\nline\n\"\"\"\nt = { x = 0 }\n",
            Toml,
            new MoveNodeTo(Path(Member("s")), Path(Member("t")), 1, null));

        Assert.Equal("t = { x = 0, s = \"\"\"\nline\n\"\"\" }\n", text);
    }

    [Fact]
    public void A_key_value_moves_into_an_item_of_an_array_of_tables_as_one_of_its_keys()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "k = 1\n[[x]]\nn = 1\n",
            Toml,
            new MoveNodeTo(Path(Member("k")), Path(Member("x"), Item(0)), 1, null));

        Assert.Equal("[[x]]\nn = 1\nk = 1\n", text);
        Assert.Equal(Path(Member("x"), Item(0), Member("k")), focus);
    }

    [Fact]
    public void A_key_value_moves_into_a_table_with_crlf_line_breaks()
    {
        string text = Edit("a = 1\r\n[t]\r\nx = 1\r\n", Toml, new MoveNodeTo(Path(Member("a")), Path(Member("t")), 0, null));

        Assert.Equal("[t]\r\na = 1\r\nx = 1\r\n", text);
    }

    [Fact]
    public void An_item_moved_into_an_inline_array_gets_its_focus_at_its_index()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "x = 1\na = [5, 6]\n",
            Toml,
            new MoveNodeTo(Path(Member("x")), Path(Member("a")), 1, null));

        Assert.Equal("a = [5, 1, 6]\n", text);
        Assert.Equal(Path(Member("a"), Item(1)), focus);
    }

    [Fact]
    public void NestBy_down_from_the_last_root_key_value_enters_the_first_table()
    {
        NestStep? step = ModelTestSupport.Parse("a = 1\nb = 2\n[t]\nx = 1\n", Toml).NestBy(Path(Member("b")), 1);

        Assert.Equal(new MoveNodeTo(Path(Member("b")), Path(Member("t")), 0, null), step!.Op);
        Assert.False(step.NeedsKey);
    }

    [Fact]
    public void NestBy_up_from_the_first_key_value_of_a_table_leaves_it_for_the_root()
    {
        NestStep? step = ModelTestSupport.Parse("k = 1\n[t]\na = 2\n", Toml).NestBy(Path(Member("t"), Member("a")), -1);

        Assert.Equal(new MoveNodeTo(Path(Member("t"), Member("a")), Root, 1, null), step!.Op);
        Assert.False(step.NeedsKey);
    }

    [Fact]
    public void NestBy_at_the_root_edge_gives_nothing()
    {
        Assert.Null(ModelTestSupport.Parse("a = 1\nb = 2\n", Toml).NestBy(Path(Member("a")), -1));
        Assert.Null(ModelTestSupport.Parse("a = 1\nb = 2\n", Toml).NestBy(Path(Member("b")), 1));
    }

    [Fact]
    public void NestBy_up_from_an_array_of_tables_item_moves_it_as_a_plain_move()
    {
        NestStep? step = ModelTestSupport.Parse("[[x]]\nn = 1\n[[x]]\nn = 2\n", Toml).NestBy(Path(Member("x"), Item(1)), -1);

        Assert.IsType<MoveNode>(step!.Op);
    }

    [Fact]
    public void NestBy_of_a_table_into_a_static_array_neighbour_is_a_plain_move()
    {
        NestStep? step = ModelTestSupport.Parse("x = [1]\n[t]\na = 1\n", Toml).NestBy(Path(Member("t")), -1);

        Assert.IsType<MoveNode>(step!.Op);
    }
}
