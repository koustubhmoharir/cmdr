using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>SetPart on TOML: scalar values by kind and string form, keys (scalars, dotted keys, tables and arrays of tables), and comments.</summary>
public sealed class TomlEditSetPartTests
{
    private static readonly IConfigFormat Toml = EditTestSupport.Toml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    // ----- String values keep their form when they can -----

    [Fact]
    public void A_basic_string_stays_basic()
    {
        string text = Edit("s = \"a\"\n", Toml, new SetPart(Path(Member("s")), PartKind.Value, -1, "b c"));

        Assert.Equal("s = \"b c\"\n", text);
    }

    [Fact]
    public void A_literal_string_stays_literal_when_it_can_hold_the_value()
    {
        string text = Edit("s = 'a'\n", Toml, new SetPart(Path(Member("s")), PartKind.Value, -1, "x\\y"));

        Assert.Equal("s = 'x\\y'\n", text);
    }

    [Fact]
    public void A_literal_string_falls_back_to_basic_for_an_apostrophe()
    {
        string text = Edit("s = 'a'\n", Toml, new SetPart(Path(Member("s")), PartKind.Value, -1, "it's"));

        Assert.Equal("s = \"it's\"\n", text);
    }

    [Fact]
    public void A_basic_string_escapes_quotation_marks()
    {
        string text = Edit("s = \"a\"\n", Toml, new SetPart(Path(Member("s")), PartKind.Value, -1, "q\"t"));

        Assert.Equal("s = \"q\\\"t\"\n", text);
    }

    [Fact]
    public void A_multi_line_basic_string_stays_multi_line_basic()
    {
        string text = Edit("s = \"\"\"\nabc\n\"\"\"\n", Toml, new SetPart(Path(Member("s")), PartKind.Value, -1, "x\ny"));

        Assert.Equal("s = \"\"\"\nx\ny\"\"\"\n", text);
    }

    [Fact]
    public void A_multi_line_literal_string_stays_multi_line_literal()
    {
        string text = Edit("s = '''\nabc\n'''\n", Toml, new SetPart(Path(Member("s")), PartKind.Value, -1, "x"));

        Assert.Equal("s = '''\nx'''\n", text);
    }

    [Fact]
    public void A_multi_line_literal_string_falls_back_to_basic_for_three_apostrophes()
    {
        string text = Edit("s = '''\nabc\n'''\n", Toml, new SetPart(Path(Member("s")), PartKind.Value, -1, "a'''b"));

        Assert.Equal("s = \"a'''b\"\n", text);
    }

    [Fact]
    public void A_literal_string_takes_a_line_break_as_a_multi_line_basic_string()
    {
        string text = Edit("s = 'a'\n", Toml, new SetPart(Path(Member("s")), PartKind.Value, -1, "x\ny"));

        Assert.Equal("s = \"\"\"\nx\ny\"\"\"\n", text);
    }

    [Fact]
    public void A_string_edit_in_a_CRLF_document_keeps_the_line_breaks()
    {
        string text = Edit("s = \"a\"\r\nt = 1\r\n", Toml, new SetPart(Path(Member("s")), PartKind.Value, -1, "b"));

        Assert.Equal("s = \"b\"\r\nt = 1\r\n", text);
    }

    // ----- Numbers, date-times and booleans are written as typed -----

    [Fact]
    public void An_integer_is_written_as_typed()
    {
        string text = Edit("i = 1\n", Toml, new SetPart(Path(Member("i")), PartKind.Value, -1, "0xFF"));

        Assert.Equal("i = 0xFF\n", text);
    }

    [Fact]
    public void An_invalid_integer_is_refused()
    {
        CfgeditException error = Fails("i = 1\n", Toml, new SetPart(Path(Member("i")), PartKind.Value, -1, "1.5"));

        Assert.Equal("'1.5' is not a valid integer", error.Message);
    }

    [Fact]
    public void A_float_is_written_as_typed()
    {
        string text = Edit("f = 1.5\n", Toml, new SetPart(Path(Member("f")), PartKind.Value, -1, "6.02e23"));

        Assert.Equal("f = 6.02e23\n", text);
    }

    [Fact]
    public void An_invalid_float_is_refused()
    {
        CfgeditException error = Fails("f = 1.5\n", Toml, new SetPart(Path(Member("f")), PartKind.Value, -1, "1."));

        Assert.Equal("'1.' is not a valid float", error.Message);
    }

    [Fact]
    public void A_local_date_becomes_an_offset_date_time_when_the_text_says_so()
    {
        string text = Edit("d = 1979-05-27\n", Toml, new SetPart(Path(Member("d")), PartKind.Value, -1, "1979-05-27T07:32:00Z"));

        Assert.Equal("d = 1979-05-27T07:32:00Z\n", text);
    }

    [Fact]
    public void An_invalid_date_time_is_refused()
    {
        CfgeditException error = Fails("d = 1979-05-27\n", Toml, new SetPart(Path(Member("d")), PartKind.Value, -1, "1979-13-01"));

        Assert.Equal("'1979-13-01' is not a valid date-time", error.Message);
    }

    [Fact]
    public void A_boolean_is_set_to_true_or_false()
    {
        string text = Edit("b = true\n", Toml, new SetPart(Path(Member("b")), PartKind.Value, -1, "false"));

        Assert.Equal("b = false\n", text);
    }

    [Fact]
    public void A_boolean_other_than_true_or_false_is_refused()
    {
        CfgeditException error = Fails("b = true\n", Toml, new SetPart(Path(Member("b")), PartKind.Value, -1, "yes"));

        Assert.Equal("'yes' is not a valid boolean (true or false)", error.Message);
    }

    // ----- Values inside inline arrays and inline tables -----

    [Fact]
    public void A_scalar_in_an_inline_array_is_edited_in_place()
    {
        string text = Edit("a = [1, 2]\n", Toml, new SetPart(Path(Member("a"), Item(1)), PartKind.Value, -1, "3"));

        Assert.Equal("a = [1, 3]\n", text);
    }

    [Fact]
    public void A_string_in_an_inline_table_is_edited_in_place()
    {
        string text = Edit("t = { x = 1, y = \"s\" }\n", Toml, new SetPart(Path(Member("t"), Member("y")), PartKind.Value, -1, "z"));

        Assert.Equal("t = { x = 1, y = \"z\" }\n", text);
    }

    // ----- Keys -----

    [Fact]
    public void A_scalar_key_is_renamed()
    {
        string text = Edit("a = 1\nb = 2\n", Toml, new SetPart(Path(Member("a")), PartKind.Key, -1, "c"));

        Assert.Equal("c = 1\nb = 2\n", text);
    }

    [Fact]
    public void The_last_part_of_a_dotted_key_is_renamed_and_the_rest_stays()
    {
        string text = Edit("x.a = 1\n", Toml, new SetPart(Path(Member("x"), Member("a")), PartKind.Key, -1, "b"));

        Assert.Equal("x.b = 1\n", text);
    }

    [Fact]
    public void A_table_spread_over_a_dotted_key_and_a_deeper_header_is_renamed_in_every_piece()
    {
        const string text = "a.c = 3\n[a.b]\ny = 2\n";

        string renamed = Edit(text, Toml, new SetPart(Path(Member("a")), PartKind.Key, -1, "q"));

        Assert.Equal("q.c = 3\n[q.b]\ny = 2\n", renamed);
    }

    [Fact]
    public void A_table_with_its_own_header_and_a_deeper_header_is_renamed_in_both()
    {
        const string text = "[a]\nx = 1\n[a.b]\ny = 2\n";

        string renamed = Edit(text, Toml, new SetPart(Path(Member("a")), PartKind.Key, -1, "q"));

        Assert.Equal("[q]\nx = 1\n[q.b]\ny = 2\n", renamed);
    }

    [Fact]
    public void A_quoted_header_is_renamed_bare_when_the_name_allows()
    {
        string text = Edit("[\"a\"]\nx = 1\n", Toml, new SetPart(Path(Member("a")), PartKind.Key, -1, "b"));

        Assert.Equal("[b]\nx = 1\n", text);
    }

    [Fact]
    public void An_implicit_table_is_renamed_in_the_deeper_header_that_made_it()
    {
        string text = Edit("[a.b]\ny = 1\n", Toml, new SetPart(Path(Member("a")), PartKind.Key, -1, "q"));

        Assert.Equal("[q.b]\ny = 1\n", text);
    }

    [Fact]
    public void A_rename_focuses_the_renamed_node_under_its_parent()
    {
        (string text, NodePath? focus) = EditWithFocus("a = 1\n", Toml, new SetPart(Path(Member("a")), PartKind.Key, -1, "c"));

        Assert.Equal("c = 1\n", text);
        Assert.Equal(Path(Member("c")), focus);
    }

    [Fact]
    public void An_array_of_tables_is_renamed_in_every_header_and_deeper_header()
    {
        const string text = "[[fruit]]\nname = \"apple\"\n[fruit.physical]\ncolor = \"red\"\n[[fruit]]\nname = \"banana\"\n";

        string renamed = Edit(text, Toml, new SetPart(Path(Member("fruit")), PartKind.Key, -1, "item"));

        Assert.Equal("[[item]]\nname = \"apple\"\n[item.physical]\ncolor = \"red\"\n[[item]]\nname = \"banana\"\n", renamed);
    }

    [Fact]
    public void A_sub_table_of_one_array_item_is_renamed_without_the_others()
    {
        const string text = "[[fruit]]\nname = \"apple\"\n[fruit.physical]\ncolor = \"red\"\n[[fruit]]\nname = \"banana\"\n[fruit.physical]\ncolor = \"yellow\"\n";

        string renamed = Edit(text, Toml, new SetPart(Path(Member("fruit"), Item(1), Member("physical")), PartKind.Key, -1, "body"));

        Assert.Equal("[[fruit]]\nname = \"apple\"\n[fruit.physical]\ncolor = \"red\"\n[[fruit]]\nname = \"banana\"\n[fruit.body]\ncolor = \"yellow\"\n", renamed);
    }

    [Fact]
    public void A_key_that_needs_quotes_is_written_basic_quoted()
    {
        string text = Edit("a = 1\n", Toml, new SetPart(Path(Member("a")), PartKind.Key, -1, "my key"));

        Assert.Equal("\"my key\" = 1\n", text);
    }

    [Fact]
    public void A_rename_onto_an_existing_sibling_is_refused()
    {
        CfgeditException error = Fails("a = 1\nb = 2\n", Toml, new SetPart(Path(Member("a")), PartKind.Key, -1, "b"));

        Assert.Equal("a key named 'b' already exists here", error.Message);
    }

    [Fact]
    public void An_array_item_key_is_not_editable()
    {
        CfgeditException error = Fails("a = [1]\n", Toml, new SetPart(Path(Member("a"), Item(0)), PartKind.Key, -1, "b"));

        Assert.Equal("this part cannot be edited", error.Message);
    }

    // ----- Comments -----

    [Fact]
    public void A_comment_line_is_rewritten_after_its_hash()
    {
        string text = Edit("# old\na = 1\n", Toml, new SetPart(Path(Comment(0)), PartKind.Comment, -1, " new"));

        Assert.Equal("# new\na = 1\n", text);
    }

    [Fact]
    public void A_trailing_comment_is_rewritten_and_the_line_keeps_its_value()
    {
        string text = Edit("a = 1 # old\n", Toml, new SetPart(Path(Comment(0)), PartKind.Comment, -1, "fresh"));

        Assert.Equal("a = 1 #fresh\n", text);
    }

    [Fact]
    public void A_comment_inside_an_array_is_rewritten()
    {
        string text = Edit("a = [\n  1, # one\n  2\n]\n", Toml, new SetPart(Path(Member("a"), Comment(0)), PartKind.Comment, -1, " uno"));

        Assert.Equal("a = [\n  1, # uno\n  2\n]\n", text);
    }

    [Fact]
    public void A_comment_with_a_line_break_is_refused()
    {
        CfgeditException error = Fails("# old\na = 1\n", Toml, new SetPart(Path(Comment(0)), PartKind.Comment, -1, "x\ny"));

        Assert.Equal("a comment cannot contain a line break", error.Message);
    }

    [Fact]
    public void A_comment_with_a_control_character_is_refused_but_a_tab_is_kept()
    {
        CfgeditException error = Fails("# old\na = 1\n", Toml, new SetPart(Path(Comment(0)), PartKind.Comment, -1, "x\u0001"));

        Assert.Equal("a comment cannot contain control characters other than tab", error.Message);

        string text = Edit("# old\na = 1\n", Toml, new SetPart(Path(Comment(0)), PartKind.Comment, -1, "x\ty"));
        Assert.Equal("#x\ty\na = 1\n", text);
    }
}
