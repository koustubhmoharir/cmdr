using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>DeleteNode on JSON: first, middle and last entries, the only entry, attached comments and commas.</summary>
public sealed class JsonEditDeleteTests
{
    [Fact]
    public void The_first_member_of_a_multi_line_object_is_deleted_with_its_whole_line()
    {
        string text = Edit("{\n  \"a\": 1,\n  \"b\": 2\n}", Strict, new DeleteNode(At(Item(0), Member("a"))));

        Assert.Equal("{\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_middle_member_of_a_multi_line_object_is_deleted()
    {
        string text = Edit("{\n  \"a\": 1,\n  \"b\": 2,\n  \"c\": 3\n}", Strict, new DeleteNode(At(Item(0), Member("b"))));

        Assert.Equal("{\n  \"a\": 1,\n  \"c\": 3\n}", text);
    }

    [Fact]
    public void The_last_member_loses_the_comma_of_the_member_before_it()
    {
        string text = Edit("{\n  \"a\": 1,\n  \"b\": 2\n}", Strict, new DeleteNode(At(Item(0), Member("b"))));

        Assert.Equal("{\n  \"a\": 1\n}", text);
    }

    [Fact]
    public void The_last_member_of_a_trailing_comma_object_keeps_the_trailing_comma_style()
    {
        string text = Edit("{\n  \"a\": 1,\n  \"b\": 2,\n}", Jsonc, new DeleteNode(At(Item(0), Member("b"))));

        Assert.Equal("{\n  \"a\": 1,\n}", text);
    }

    [Fact]
    public void The_only_member_of_a_multi_line_object_leaves_an_empty_object_on_the_opening_line()
    {
        string text = Edit("{\n  \"a\": 1\n}", Strict, new DeleteNode(At(Item(0), Member("a"))));

        Assert.Equal("{}", text);
    }

    [Fact]
    public void The_only_member_of_a_single_line_object_leaves_an_empty_object()
    {
        string text = Edit("{\"a\": 1}", Strict, new DeleteNode(At(Item(0), Member("a"))));

        Assert.Equal("{}", text);
    }

    [Fact]
    public void The_last_member_of_a_single_line_object_takes_the_comma_before_it()
    {
        string text = Edit("{\"a\": 1, \"b\": 2}", Strict, new DeleteNode(At(Item(0), Member("b"))));

        Assert.Equal("{\"a\": 1}", text);
    }

    [Fact]
    public void The_last_member_of_a_single_line_trailing_comma_object_keeps_the_previous_comma()
    {
        string text = Edit("{\"a\": 1, \"b\": 2,}", Jsonc, new DeleteNode(At(Item(0), Member("b"))));

        Assert.Equal("{\"a\": 1,}", text);
    }

    [Fact]
    public void The_first_member_of_a_single_line_object_takes_the_separator_after_it()
    {
        string text = Edit("{\"a\": 1, \"b\": 2}", Strict, new DeleteNode(At(Item(0), Member("a"))));

        Assert.Equal("{\"b\": 2}", text);
    }

    [Fact]
    public void Attached_comments_above_and_on_the_line_of_a_member_are_deleted_with_it()
    {
        string text = Edit(
            "{\n  // about a\n  \"a\": 1, // trailing a\n  \"b\": 2\n}",
            Jsonc,
            new DeleteNode(At(Item(0), Member("a"))));

        Assert.Equal("{\n  \"b\": 2\n}", text);
    }

    [Fact]
    public void A_comment_separated_from_a_member_by_a_blank_line_is_not_attached_and_is_kept()
    {
        string text = Edit("{\n  // header\n\n  \"a\": 1\n}", Jsonc, new DeleteNode(At(Item(0), Member("a"))));

        Assert.Equal("{\n  // header\n\n}", text);
    }

    [Fact]
    public void A_document_level_comment_is_deleted_with_its_line()
    {
        string text = Edit("// c\n[1]", Jsonc, new DeleteNode(At(Comment(0))));

        Assert.Equal("[1]", text);
    }

    [Fact]
    public void A_middle_item_of_a_multi_line_array_is_deleted()
    {
        string text = Edit("[\n  1,\n  2,\n  3\n]", Strict, new DeleteNode(At(Item(0), Item(1))));

        Assert.Equal("[\n  1,\n  3\n]", text);
    }

    [Fact]
    public void A_middle_item_of_a_single_line_array_is_deleted_with_its_separator()
    {
        string text = Edit("[1, 2, 3]", Strict, new DeleteNode(At(Item(0), Item(1))));

        Assert.Equal("[1, 3]", text);
    }

    [Fact]
    public void The_only_item_of_a_multi_line_array_leaves_an_empty_array()
    {
        string text = Edit("[\n  1\n]", Strict, new DeleteNode(At(Item(0), Item(0))));

        Assert.Equal("[]", text);
    }

    [Fact]
    public void The_only_member_of_an_object_that_holds_a_container_is_removed_with_its_contents()
    {
        string text = Edit("{\n  \"a\": {\n    \"x\": 1\n  }\n}", Strict, new DeleteNode(At(Item(0), Member("a"))));

        Assert.Equal("{}", text);
    }

    [Fact]
    public void The_root_value_cannot_be_deleted()
    {
        CfgeditException ex = Fails("[1]", Strict, new DeleteNode(RootValue));

        Assert.Equal("The root value cannot be deleted", ex.Message);
    }

    [Fact]
    public void Deleting_moves_focus_to_the_next_sibling_then_the_previous_then_the_parent()
    {
        (string first, NodePath? nextFocus) = EditWithFocus("{\"a\": 1, \"b\": 2}", Strict, new DeleteNode(At(Item(0), Member("a"))));
        (string last, NodePath? previousFocus) = EditWithFocus("{\"a\": 1, \"b\": 2}", Strict, new DeleteNode(At(Item(0), Member("b"))));
        (string only, NodePath? parentFocus) = EditWithFocus("{\"a\": 1}", Strict, new DeleteNode(At(Item(0), Member("a"))));

        Assert.Equal("{\"b\": 2}", first);
        Assert.Equal(At(Item(0), Member("b")), nextFocus);
        Assert.Equal("{\"a\": 1}", last);
        Assert.Equal(At(Item(0), Member("a")), previousFocus);
        Assert.Equal("{}", only);
        Assert.Equal(At(Item(0)), parentFocus);
    }
}
