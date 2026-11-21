using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// The logical application on XML trees, for each XML operation shape. These test <see cref="LogicalEdit"/> directly on the
/// logical tree of a parsed document, with no planning and no text.
/// </summary>
public sealed class LogicalEditXmlTests
{
    private static string Apply(string xml, EditOp op)
    {
        ConfigDocument document = ModelTestSupport.Parse(xml, EditTestSupport.Xml);
        return LogicalEdit.Apply(document, EditTestSupport.Xml.ToLogical(document), op).ToString();
    }

    private static readonly NodePath R = At(Element("r"));

    [Fact]
    public void Setting_an_element_name_renames_the_element()
    {
        string logical = Apply("<r><b/></r>", new SetPart(At(Element("r"), Element("b")), PartKind.ElementName, -1, "c"));

        Assert.Equal("Document\n  Element name=\"r\"\n    Element name=\"c\"", logical);
    }

    [Fact]
    public void Setting_an_end_name_renames_the_element_too()
    {
        string logical = Apply("<r><b></b></r>", new SetPart(At(Element("r"), Element("b")), PartKind.EndName, -1, "c"));

        Assert.Equal("Document\n  Element name=\"r\"\n    Element name=\"c\"", logical);
    }

    [Fact]
    public void Setting_an_attribute_value_changes_that_attribute_only()
    {
        string logical = Apply("<r a=\"1\"/>", new SetPart(R, PartKind.AttributeValue, 0, "2"));

        Assert.Equal("Document\n  Element name=\"r\" @a=\"2\"", logical);
    }

    [Fact]
    public void Setting_an_attribute_name_renames_it_and_keeps_its_value()
    {
        string logical = Apply("<r a=\"1\"/>", new SetPart(R, PartKind.AttributeName, 0, "b"));

        Assert.Equal("Document\n  Element name=\"r\" @b=\"1\"", logical);
    }

    [Fact]
    public void Setting_the_text_of_a_text_only_element_to_empty_removes_its_text_child()
    {
        string logical = Apply("<r><a>x</a></r>", new SetPart(At(Element("r"), Element("a")), PartKind.Text, -1, string.Empty));

        Assert.Equal("Document\n  Element name=\"r\"\n    Element name=\"a\"", logical);
    }

    [Fact]
    public void Setting_the_text_of_an_empty_element_adds_a_text_child()
    {
        string logical = Apply("<r><a/></r>", new SetPart(At(Element("r"), Element("a")), PartKind.Text, -1, "t"));

        Assert.Equal("Document\n  Element name=\"r\"\n    Element name=\"a\"\n      Text value=\"t\"", logical);
    }

    [Fact]
    public void Setting_the_text_of_a_text_node_in_mixed_content_changes_its_value()
    {
        // In mixed content the text is a node of its own; a text-only element shows its text as a part instead.
        string logical = Apply("<r>x<b/></r>", new SetPart(At(Element("r"), Text(0)), PartKind.Text, -1, "y"));

        Assert.Equal("Document\n  Element name=\"r\"\n    Text value=\"y\"\n    Element name=\"b\"", logical);
    }

    [Fact]
    public void Setting_a_comment_changes_its_value()
    {
        string logical = Apply("<r><!--c--></r>", new SetPart(At(Element("r"), Comment(0)), PartKind.Comment, -1, "d"));

        Assert.Equal("Document\n  Element name=\"r\"\n    Comment value=\"d\"", logical);
    }

    [Fact]
    public void Setting_a_processing_instruction_target_renames_it()
    {
        string logical = Apply("<r><?p q?></r>", new SetPart(At(Element("r"), Pi(0)), PartKind.PITarget, -1, "z"));

        Assert.Equal("Document\n  Element name=\"r\"\n    ProcessingInstruction name=\"z\" value=\"q\"", logical);
    }

    [Fact]
    public void Setting_processing_instruction_data_changes_its_value()
    {
        string logical = Apply("<r><?p q?></r>", new SetPart(At(Element("r"), Pi(0)), PartKind.PIData, -1, "w"));

        Assert.Equal("Document\n  Element name=\"r\"\n    ProcessingInstruction name=\"p\" value=\"w\"", logical);
    }

    [Fact]
    public void Inserting_an_attribute_adds_it_at_its_index()
    {
        string logical = Apply("<r a=\"1\"/>", new InsertAttribute(R, 1, "b", "2"));

        Assert.Equal("Document\n  Element name=\"r\" @a=\"1\" @b=\"2\"", logical);
    }

    [Fact]
    public void Deleting_an_attribute_removes_it()
    {
        string logical = Apply("<r a=\"1\" b=\"2\"/>", new DeleteAttribute(R, 0));

        Assert.Equal("Document\n  Element name=\"r\" @b=\"2\"", logical);
    }

    [Fact]
    public void Moving_an_attribute_places_it_by_its_index_after_removal()
    {
        string logical = Apply("<r a=\"1\" b=\"2\"/>", new MoveAttribute(R, 0, 1));

        Assert.Equal("Document\n  Element name=\"r\" @b=\"2\" @a=\"1\"", logical);
    }

    [Fact]
    public void Inserting_an_element_with_attributes_and_text_adds_it_at_the_index()
    {
        string logical = Apply(
            "<r><b/></r>",
            new InsertNode(R, 1, new NewXmlElement("c", [("k", "v")], "t")));

        Assert.Equal("Document\n  Element name=\"r\"\n    Element name=\"b\"\n    Element name=\"c\" @k=\"v\"\n      Text value=\"t\"", logical);
    }

    [Fact]
    public void Inserting_a_cdata_section_into_an_empty_element()
    {
        string logical = Apply("<r/>", new InsertNode(R, 0, new NewXmlText("x", true)));

        Assert.Equal("Document\n  Element name=\"r\"\n    CData value=\"x\"", logical);
    }

    [Fact]
    public void Inserting_a_comment_at_the_start_of_an_element()
    {
        string logical = Apply("<r><b/></r>", new InsertNode(R, 0, new NewXmlComment("c")));

        Assert.Equal("Document\n  Element name=\"r\"\n    Comment value=\"c\"\n    Element name=\"b\"", logical);
    }

    [Fact]
    public void Deleting_an_element_removes_its_attached_comment_too()
    {
        string logical = Apply("<r>\n  <!-- about a -->\n  <a/>\n  <b/>\n</r>", new DeleteNode(At(Element("r"), Element("a"))));

        Assert.Equal("Document\n  Element name=\"r\"\n    Element name=\"b\"", logical);
    }

    [Fact]
    public void Moving_an_element_places_it_at_the_new_index()
    {
        string logical = Apply("<r><a/><b/></r>", new MoveNode(At(Element("r"), Element("a")), 1));

        Assert.Equal("Document\n  Element name=\"r\"\n    Element name=\"b\"\n    Element name=\"a\"", logical);
    }

    [Fact]
    public void The_logical_tree_is_not_changed_by_applying_an_operation()
    {
        ConfigDocument document = ModelTestSupport.Parse("<r a=\"1\"/>", EditTestSupport.Xml);
        LogicalNode before = EditTestSupport.Xml.ToLogical(document);
        string dumpBefore = before.ToString();

        LogicalEdit.Apply(document, before, new SetPart(R, PartKind.AttributeValue, 0, "2"));

        Assert.Equal(dumpBefore, before.ToString());
    }

    private static PathStep Text(int index) => new TextStep(index);

    private static PathStep Pi(int index) => new PIStep(index);
}
