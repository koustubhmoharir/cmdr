using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Duplicate on YAML: block entries and items with their comments and nested content, entries that share a line with a dash, flow entries and
/// items, anchors (removed from the copy, kept in an alias), comments, and the refusals. Every expected text is written out by hand.
/// </summary>
public sealed class YamlEditDuplicateTests
{
    private static readonly IConfigFormat Yaml = EditTestSupport.Yaml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    private static DuplicateNode Duplicate(params PathStep[] steps) => new(Path(steps));

    // ----- Block entries and items -----

    [Fact]
    public void A_mapping_entry_is_copied_after_itself_under_a_copy_name()
    {
        Assert.Equal("a: 1\na2: 1\nb: 2\n", Edit("a: 1\nb: 2\n", Yaml, Duplicate(Member("a"))));
    }

    [Fact]
    public void A_copy_takes_the_first_copy_name_that_is_free()
    {
        Assert.Equal("a: 1\na3: 1\na2: 5\n", Edit("a: 1\na2: 5\n", Yaml, Duplicate(Member("a"))));
    }

    [Fact]
    public void A_copy_takes_its_attached_comment_and_trailing_comment_with_it()
    {
        Assert.Equal(
            "# about a\na: 1 # t\n# about a\na2: 1 # t\nb: 2\n",
            Edit("# about a\na: 1 # t\nb: 2\n", Yaml, Duplicate(Member("a"))));
    }

    [Fact]
    public void A_nested_entry_is_copied_with_all_its_lines()
    {
        Assert.Equal("a:\n  x: 1\na2:\n  x: 1\nb: 2\n", Edit("a:\n  x: 1\nb: 2\n", Yaml, Duplicate(Member("a"))));
    }

    [Fact]
    public void A_block_sequence_item_is_copied_after_itself()
    {
        Assert.Equal("- a\n- a\n- b\n", Edit("- a\n- b\n", Yaml, Duplicate(Item(0))));
    }

    [Fact]
    public void An_item_under_a_key_is_copied_at_the_dash_column()
    {
        Assert.Equal("list:\n  - a\n  - a\n  - b\n", Edit("list:\n  - a\n  - b\n", Yaml, Duplicate(Member("list"), Item(0))));
    }

    [Fact]
    public void An_entry_that_shares_its_line_with_a_dash_is_copied_as_a_line_of_its_own_at_its_column()
    {
        Assert.Equal("- a: 1\n  a2: 1\n  b: 2\n", Edit("- a: 1\n  b: 2\n", Yaml, Duplicate(Item(0), Member("a"))));
    }

    [Fact]
    public void An_item_that_shares_a_line_with_an_outer_dash_is_copied_as_a_line_of_its_own()
    {
        Assert.Equal("- - a\n  - a\n  - b\n", Edit("- - a\n  - b\n", Yaml, Duplicate(Item(0), Item(0))));
    }

    [Fact]
    public void A_copy_at_the_end_of_a_file_with_no_final_break_gets_the_break_first()
    {
        Assert.Equal("a: 1\na2: 1",Edit("a: 1", Yaml, Duplicate(Member("a"))));
    }

    [Fact]
    public void A_copy_in_a_CRLF_document_keeps_its_line_breaks()
    {
        Assert.Equal("a: 1\r\na2: 1\r\nb: 2\r\n", Edit("a: 1\r\nb: 2\r\n", Yaml, Duplicate(Member("a"))));
    }

    // ----- Anchors and aliases -----

    [Fact]
    public void An_anchor_is_removed_from_the_copy_so_that_it_is_not_defined_twice()
    {
        Assert.Equal("a: &x 1\na2: 1\nb: 2\n", Edit("a: &x 1\nb: 2\n", Yaml, Duplicate(Member("a"))));
    }

    [Fact]
    public void An_anchor_inside_a_copied_sequence_item_is_removed_and_the_item_keeps_its_value()
    {
        Assert.Equal(
            "list:\n  - &i a\n  - a\n  - b\n",
            Edit("list:\n  - &i a\n  - b\n", Yaml, Duplicate(Member("list"), Item(0))));
    }

    [Fact]
    public void An_alias_in_a_copy_stays_and_still_refers_to_the_original()
    {
        Assert.Equal("a: &x 1\nb: *x\nb2: *x\n", Edit("a: &x 1\nb: *x\n", Yaml, Duplicate(Member("b"))));
    }

    [Fact]
    public void An_anchored_key_loses_its_anchor_in_the_copy()
    {
        Assert.Equal("&k key: 1\nkey2: 1\n", Edit("&k key: 1\n", Yaml, Duplicate(Member("key"))));
    }

    // ----- Flow entries and items -----

    [Fact]
    public void A_flow_entry_is_copied_after_its_comma()
    {
        Assert.Equal("{a: 1, a2: 1, b: 2}\n", Edit("{a: 1, b: 2}\n", Yaml, Duplicate(Member("a"))));
    }

    [Fact]
    public void A_flow_item_is_copied_after_its_comma()
    {
        Assert.Equal("[1, 1, 2]\n", Edit("[1, 2]\n", Yaml, Duplicate(Item(0))));
    }

    [Fact]
    public void The_last_flow_item_is_copied_after_it_with_a_comma_before_the_copy()
    {
        Assert.Equal("[1, 2, 2]\n", Edit("[1, 2]\n", Yaml, Duplicate(Item(1))));
    }

    [Fact]
    public void An_item_on_its_own_line_is_copied_as_a_line_and_the_last_item_gets_the_comma()
    {
        Assert.Equal("[\n  1,\n  2,\n  2\n]\n", Edit("[\n  1,\n  2\n]\n", Yaml, Duplicate(Item(1))));
    }

    [Fact]
    public void An_anchor_inside_a_copied_flow_entry_is_removed()
    {
        Assert.Equal("[&i 1, 1, 2]\n", Edit("[&i 1, 2]\n", Yaml, Duplicate(Item(0))));
    }

    // ----- Comments -----

    [Fact]
    public void A_comment_line_is_copied_after_itself()
    {
        Assert.Equal("# c\n# c\na: 1\n", Edit("# c\na: 1\n", Yaml, new DuplicateNode(Path(Comment(0)))));
    }

    [Fact]
    public void A_trailing_comment_is_copied_as_a_line_of_its_own_after_its_line()
    {
        Assert.Equal("a: 1 # t\n# t\n", Edit("a: 1 # t\n", Yaml, new DuplicateNode(Path(Comment(0)))));
    }

    // ----- Refusals -----

    [Fact]
    public void After_a_duplicate_the_copy_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("a: 1\nb: 2\n", Yaml, Duplicate(Member("a")));

        Assert.Equal(Path(Member("a2")), focus);
    }

    [Fact]
    public void A_root_value_cannot_be_duplicated()
    {
        CfgeditException ex = Fails("a: 1\n", Yaml, new DuplicateNode(Path()));

        Assert.Equal("the root value cannot be duplicated", ex.Message);
    }

    [Fact]
    public void A_document_cannot_be_duplicated()
    {
        CfgeditException ex = Fails("a: 1\n---\nb: 2\n", Yaml, new DuplicateNode(At(Item(0))));

        Assert.Equal("a document cannot be duplicated", ex.Message);
    }

}
