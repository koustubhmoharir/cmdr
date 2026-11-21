using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>ConfigDocument.ShapeLike: the shape of a sibling, with empty values, inserted at a position.</summary>
public sealed class SiblingShapeTests
{
    private static readonly IConfigFormat XmlFormat = Formats.FromName("xml")!;
    private static readonly NodePath XmlRoot = At(Element("root"));

    [Fact]
    public void A_JSON_object_item_is_copied_with_its_keys_and_empty_values()
    {
        ConfigDocument doc = ModelTestSupport.Parse("""[{"a": 1, "b": "x"}]""", Strict);

        ShapedInsert shaped = doc.ShapeLike(At(Item(0), Item(0)), RootValue, doc.InsertionIndexAfter(At(Item(0), Item(0))));

        Assert.Equal("""[{"a": 1, "b": "x"}, {"a": 0, "b": ""}]""", Apply(doc, shaped));
    }

    [Fact]
    public void A_JSON_scalar_item_is_copied_as_an_empty_value_of_its_kind()
    {
        ConfigDocument doc = ModelTestSupport.Parse("""["a", 1, true]""", Strict);

        Assert.Equal("""["a", 1, true, false]""", Apply(doc, doc.ShapeLike(At(Item(0), Item(2)), RootValue, 3)));
        Assert.Equal("""["", "a", 1, true]""", Apply(doc, doc.ShapeLike(At(Item(0), Item(0)), RootValue, 0)));
    }

    [Fact]
    public void A_JSON_member_has_a_key_so_its_shape_is_not_copied()
    {
        ConfigDocument doc = ModelTestSupport.Parse("""{"a": 1}""", Strict);

        ArgumentException ex = Assert.Throws<ArgumentException>(() => doc.ShapeLike(At(Item(0), Member("a")), RootValue, 1));

        Assert.Contains("key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_XML_element_is_copied_with_attribute_names_empty_values_and_child_element_names()
    {
        ConfigDocument doc = ModelTestSupport.Parse("<root><add key=\"a\" value=\"b\"><x k=\"1\"/></add></root>", XmlFormat);
        NodePath add = XmlRoot.Append(Element("add"));

        ShapedInsert shaped = doc.ShapeLike(add, XmlRoot, doc.InsertionIndexAfter(add));

        // The copied child is written by the formatter with the line breaks of the document's own layout.
        Assert.Equal(
            "<root><add key=\"a\" value=\"b\"><x k=\"1\"/></add><add key=\"\" value=\"\">\n  <x />\n</add></root>",
            Apply(doc, shaped));
    }

    [Fact]
    public void A_text_only_element_is_copied_as_an_empty_element_because_empty_text_is_not_kept()
    {
        ConfigDocument doc = ModelTestSupport.Parse("<root><note>hello</note></root>", XmlFormat);
        NodePath note = XmlRoot.Append(Element("note"));

        ShapedInsert shaped = doc.ShapeLike(note, XmlRoot, doc.InsertionIndexBefore(note));

        Assert.Equal("<root><note /><note>hello</note></root>", Apply(doc, shaped));
    }

    [Fact]
    public void The_inserted_node_path_names_the_new_node_in_the_edited_document()
    {
        ConfigDocument doc = ModelTestSupport.Parse("<root><add key=\"a\"/></root>", XmlFormat);
        NodePath add = XmlRoot.Append(Element("add"));

        ShapedInsert shaped = doc.ShapeLike(add, XmlRoot, doc.InsertionIndexAfter(add));
        ConfigDocument after = doc.Apply(shaped.Edit).Document;

        Assert.Equal("add", ((ElementStep)shaped.Node.Steps[^1]).Name);
        Assert.Equal(shaped.Node, after.Resolve(shaped.Node)!.Path);
        Assert.Equal("<root><add key=\"a\"/><add key=\"\"/></root>", Apply(doc, shaped));
    }

    [Fact]
    public void A_comment_has_no_shape_to_copy()
    {
        ConfigDocument doc = ModelTestSupport.Parse("<root><!-- note --></root>", XmlFormat);
        NodePath comment = XmlRoot.Append(Comment(0));

        Assert.Throws<ArgumentException>(() => doc.ShapeLike(comment, XmlRoot, 1));
    }

    private static string Apply(ConfigDocument doc, ShapedInsert shaped) =>
        System.Text.Encoding.UTF8.GetString(doc.Apply(shaped.Edit).Document.Source.Encode());
}
