using Cfgedit.Core;
using Cfgedit.Core.Json;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Each validation rule of the JSON planner, with its exact user message.</summary>
public sealed class JsonEditValidationTests
{
    [Fact]
    public void A_new_member_with_an_existing_key_is_refused()
    {
        CfgeditException ex = Fails("{\"a\": 1}", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue("a", JsonValueKind.Number, "2")));

        Assert.Equal("a member named 'a' already exists", ex.Message);
    }

    [Fact]
    public void A_member_needs_a_key()
    {
        CfgeditException ex = Fails("{\"a\": 1}", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue(null, JsonValueKind.Number, "2")));

        Assert.Equal("A member needs a key", ex.Message);
    }

    [Fact]
    public void An_array_item_has_no_key()
    {
        CfgeditException ex = Fails("[1]", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue("k", JsonValueKind.Number, "2")));

        Assert.Equal("Items of an array have no key", ex.Message);
    }

    [Fact]
    public void An_invalid_number_is_refused_on_insert()
    {
        CfgeditException ex = Fails("[1]", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue(null, JsonValueKind.Number, "01")));

        Assert.Equal("'01' is not a valid number", ex.Message);
    }

    [Fact]
    public void A_number_needs_a_value_on_insert()
    {
        CfgeditException ex = Fails("[1]", Strict, new InsertNode(At(Item(0)), 1, new NewJsonValue(null, JsonValueKind.Number, null)));

        Assert.Equal("A number needs a value", ex.Message);
    }

    [Fact]
    public void Comments_are_refused_in_strict_json()
    {
        CfgeditException ex = Fails("{}", Strict, new InsertNode(At(Item(0)), 0, new NewJsonComment(" c", false)));

        Assert.Equal("comments are not allowed in JSON", ex.Message);
    }

    [Fact]
    public void A_line_comment_with_a_line_break_is_refused()
    {
        CfgeditException ex = Fails("{}", Jsonc, new InsertNode(At(Item(0)), 0, new NewJsonComment("a\nb", false)));

        Assert.Equal("a line comment cannot contain a line break", ex.Message);
    }

    [Fact]
    public void A_block_comment_with_the_closing_marker_is_refused()
    {
        CfgeditException ex = Fails("[1]", Jsonc, new InsertNode(At(Item(0)), 0, new NewJsonComment("a*/b", true)));

        Assert.Equal("a block comment cannot contain */", ex.Message);
    }

    [Fact]
    public void Only_comments_can_be_placed_at_the_document_level()
    {
        CfgeditException ex = Fails("[1]", Jsonc, new InsertNode(NodePath.Empty, 0, new NewJsonValue(null, JsonValueKind.Null, null)));

        Assert.Equal("Only comments can be placed at the document level", ex.Message);
    }

    [Fact]
    public void A_scalar_has_no_children()
    {
        CfgeditException ex = Fails("\"x\"", Strict, new InsertNode(RootValue, 0, new NewJsonValue(null, JsonValueKind.Null, null)));

        Assert.Equal("A scalar has no children", ex.Message);
    }

    [Fact]
    public void An_insert_index_past_the_children_is_refused()
    {
        CfgeditException ex = Fails("[1]", Strict, new InsertNode(At(Item(0)), 5, new NewJsonValue(null, JsonValueKind.Null, null)));

        Assert.Equal("Index 5 is outside the children, 0 to 1", ex.Message);
    }

    [Fact]
    public void JSON_has_no_attributes()
    {
        CfgeditException ex = Fails("[1]", Strict, new InsertAttribute(At(Item(0)), 0, "k", "v"));

        Assert.Equal("JSON values have no attributes", ex.Message);
    }

    [Fact]
    public void An_xml_node_cannot_be_inserted_into_json()
    {
        CfgeditException ex = Fails("[1]", Strict, new InsertNode(At(Item(0)), 0, new NewXmlComment("x")));

        Assert.Equal("This kind of node cannot be inserted into JSON", ex.Message);
    }

    [Fact]
    public void A_key_cannot_be_set_on_the_root_value()
    {
        CfgeditException ex = Fails("[1]", Strict, new SetPart(RootValue, PartKind.Key, -1, "k"));

        Assert.Equal("This part cannot be edited", ex.Message);
    }

    [Fact]
    public void A_missing_node_is_refused_for_a_move()
    {
        CfgeditException ex = Fails("[1]", Strict, new MoveNode(At(Item(0), Item(5)), 0));

        Assert.Equal("There is no node at /Item(0)/Item(5)", ex.Message);
    }

    [Fact]
    public void Changing_a_comment_into_a_block_with_the_closing_marker_is_refused_in_jsonc()
    {
        CfgeditException ex = Fails("/* a */ [1]", Jsonc, new SetPart(At(Comment(0)), PartKind.Comment, -1, "x */ y"));

        Assert.Equal("a block comment cannot contain */", ex.Message);
    }
}
