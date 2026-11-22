using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Duplicate on TOML: key/values with their comments, tables made by headers (contiguous, and spread over the file), array-of-tables items,
/// inline array items and inline table members, and the refusals. A copy's key gets the <c>-copy</c> suffix, as in JSON.
/// </summary>
public sealed class TomlEditDuplicateTests
{
    private static readonly IConfigFormat Toml = EditTestSupport.Toml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    [Fact]
    public void A_key_value_is_copied_with_its_comments_after_its_group_under_a_copy_name()
    {
        string text = Edit("# about a\na = 1 # t\nb = 2\n", Toml, new DuplicateNode(Path(Member("a"))));

        Assert.Equal("# about a\na = 1 # t\n# about a\na-copy = 1 # t\nb = 2\n", text);
    }

    [Fact]
    public void A_copy_takes_the_first_free_copy_name()
    {
        string text = Edit("a = 1\na-copy = 5\n", Toml, new DuplicateNode(Path(Member("a"))));

        // The copy goes right after its own key/value, and it takes the first copy name that is free.
        Assert.Equal("a = 1\na-copy2 = 1\na-copy = 5\n", text);
    }

    [Fact]
    public void A_key_value_on_the_last_line_without_a_break_gets_the_break_before_its_copy()
    {
        string text = Edit("a = 1", Toml, new DuplicateNode(Path(Member("a"))));

        Assert.Equal("a = 1\na-copy = 1", text);
    }

    [Fact]
    public void A_dotted_leaf_keeps_its_prefix_and_gets_the_copy_name_on_its_last_segment()
    {
        string text = Edit("a.b = 1\n", Toml, new DuplicateNode(Path(Member("a"), Member("b"))));

        Assert.Equal("a.b = 1\na.b-copy = 1\n", text);
    }

    [Fact]
    public void A_key_value_in_a_CRLF_document_is_copied_with_CRLF()
    {
        string text = Edit("a = 1\r\nb = 2\r\n", Toml, new DuplicateNode(Path(Member("a"))));

        Assert.Equal("a = 1\r\na-copy = 1\r\nb = 2\r\n", text);
    }

    [Fact]
    public void A_contiguous_table_is_copied_after_its_last_section_with_the_header_renamed()
    {
        string text = Edit("[t]\nx = 1\n[t.u]\ny = 2\n[z]\n", Toml, new DuplicateNode(Path(Member("t"))));

        Assert.Equal("[t]\nx = 1\n[t.u]\ny = 2\n[t-copy]\nx = 1\n[t-copy.u]\ny = 2\n[z]\n", text);
    }

    [Fact]
    public void A_nested_table_copy_renames_its_own_segment_in_every_header_below_it()
    {
        string text = Edit("[a.b]\nx = 1\n[a.b.c]\ny = 2\n", Toml, new DuplicateNode(Path(Member("a"), Member("b"))));

        Assert.Equal("[a.b]\nx = 1\n[a.b.c]\ny = 2\n[a.b-copy]\nx = 1\n[a.b-copy.c]\ny = 2\n", text);
    }

    [Fact]
    public void A_table_whose_sections_are_spread_over_the_file_is_refused()
    {
        CfgeditException error = Fails("[t]\nx = 1\n[u]\n[t.v]\ny = 2\n", Toml, new DuplicateNode(Path(Member("t"))));

        Assert.Equal("this table is spread over the file and cannot be duplicated", error.Message);
    }

    [Fact]
    public void A_table_defined_by_dotted_keys_is_refused()
    {
        CfgeditException error = Fails("a.x = 1\n", Toml, new DuplicateNode(Path(Member("a"))));

        Assert.Equal("a table defined by dotted keys cannot be duplicated", error.Message);
    }

    [Fact]
    public void An_array_of_tables_item_is_copied_as_its_section_after_itself()
    {
        string text = Edit("[[x]]\na = 1\n[[x]]\na = 2\n", Toml, new DuplicateNode(Path(Member("x"), Item(0))));

        Assert.Equal("[[x]]\na = 1\n[[x]]\na = 1\n[[x]]\na = 2\n", text);
    }

    [Fact]
    public void An_array_of_tables_item_copies_its_subtables_with_it()
    {
        string text = Edit("[[x]]\na = 1\n[x.s]\nb = 2\n", Toml, new DuplicateNode(Path(Member("x"), Item(0))));

        Assert.Equal("[[x]]\na = 1\n[x.s]\nb = 2\n[[x]]\na = 1\n[x.s]\nb = 2\n", text);
    }

    [Fact]
    public void A_whole_array_of_tables_is_refused()
    {
        CfgeditException error = Fails("[[x]]\na = 1\n", Toml, new DuplicateNode(Path(Member("x"))));

        Assert.Equal("an array of tables cannot be duplicated; duplicate one of its items", error.Message);
    }

    [Fact]
    public void An_inline_array_item_is_copied_after_itself_with_the_comma_style_of_the_array()
    {
        Assert.Equal("a = [1, 1, 2]\n", Edit("a = [1, 2]\n", Toml, new DuplicateNode(Path(Member("a"), Item(0)))));
        Assert.Equal("a = [1, 2, 2]\n", Edit("a = [1, 2]\n", Toml, new DuplicateNode(Path(Member("a"), Item(1)))));
    }

    [Fact]
    public void A_one_per_line_array_item_is_copied_as_its_line_and_the_last_item_gets_a_comma()
    {
        string text = Edit("a = [\n  1,\n  2\n]\n", Toml, new DuplicateNode(Path(Member("a"), Item(1))));

        Assert.Equal("a = [\n  1,\n  2,\n  2\n]\n", text);
    }

    [Fact]
    public void An_inline_table_member_is_copied_after_itself_with_its_key_renamed()
    {
        Assert.Equal("t = { a = 1, a-copy = 1, b = 2 }\n", Edit("t = { a = 1, b = 2 }\n", Toml, new DuplicateNode(Path(Member("t"), Member("a")))));
        Assert.Equal("t = { a = 1, b = 2, b-copy = 2 }\n", Edit("t = { a = 1, b = 2 }\n", Toml, new DuplicateNode(Path(Member("t"), Member("b")))));
    }

    [Fact]
    public void A_comment_line_is_copied_as_it_is()
    {
        string text = Edit("a = 1\n# c\nb = 2\n", Toml, new DuplicateNode(Path(Comment(0))));

        Assert.Equal("a = 1\n# c\n# c\nb = 2\n", text);
    }

    [Fact]
    public void A_trailing_comment_is_refused_because_it_is_copied_with_its_entry()
    {
        CfgeditException error = Fails("a = 1 # t\n", Toml, new DuplicateNode(Path(Comment(0))));

        Assert.Equal("a trailing comment is duplicated with its entry", error.Message);
    }

    [Fact]
    public void A_comment_inside_an_array_is_refused()
    {
        CfgeditException error = Fails("a = [\n  # c\n  1,\n]\n", Toml, new DuplicateNode(Path(Member("a"), Comment(0))));

        Assert.Equal("a comment inside an array cannot be duplicated", error.Message);
    }

    [Fact]
    public void The_root_cannot_be_duplicated()
    {
        CfgeditException error = Fails("a = 1\n", Toml, new DuplicateNode(Path()));

        Assert.Equal("the root table cannot be duplicated", error.Message);
    }

    [Fact]
    public void The_focus_after_a_duplicate_is_the_copy()
    {
        (string _, NodePath? focus) = EditWithFocus("a = 1\n", Toml, new DuplicateNode(Path(Member("a"))));

        Assert.Equal(Path(Member("a-copy")), focus);
    }
}
