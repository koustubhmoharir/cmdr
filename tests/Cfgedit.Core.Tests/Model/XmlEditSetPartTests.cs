using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Setting the parts of XML nodes: names, attribute values, text, comments and processing instructions.</summary>
public sealed class XmlEditSetPartTests
{
    // The field shadows the Xml namespace of this test assembly, which the static import would otherwise lose to.
    private static readonly IConfigFormat Xml = EditTestSupport.Xml;
    private static readonly NodePath R = At(Element("r"));

    [Fact]
    public void Renaming_an_element_renames_its_end_tag_too()
    {
        string text = Edit("<r><b><c/></b></r>", Xml, new SetPart(At(Element("r"), Element("b")), PartKind.EndName, -1, "d"));

        Assert.Equal("<r><d><c/></d></r>", text);
    }

    [Fact]
    public void Renaming_a_text_only_element_renames_its_end_tag_too()
    {
        string text = Edit("<r><b>x</b></r>", Xml, new SetPart(At(Element("r"), Element("b")), PartKind.ElementName, -1, "c"));

        Assert.Equal("<r><c>x</c></r>", text);
    }

    [Fact]
    public void Renaming_to_the_same_name_changes_nothing()
    {
        string source = "<r><b/></r>";

        Assert.Equal(source, Edit(source, Xml, new SetPart(At(Element("r"), Element("b")), PartKind.ElementName, -1, "b")));
    }

    [Fact]
    public void An_element_name_with_a_declared_prefix_is_accepted()
    {
        string text = Edit("<r xmlns:p=\"u\"><a/></r>", Xml, new SetPart(At(Element("r"), Element("a")), PartKind.ElementName, -1, "p:a"));

        Assert.Equal("<r xmlns:p=\"u\"><p:a/></r>", text);
    }

    [Fact]
    public void An_element_name_with_an_undeclared_prefix_is_refused()
    {
        CfgeditException ex = Fails("<r/>", Xml, new SetPart(R, PartKind.ElementName, -1, "p:r"));

        Assert.Equal("the namespace prefix 'p' is not declared", ex.Message);
    }

    [Fact]
    public void An_invalid_element_name_is_refused()
    {
        CfgeditException ex = Fails("<r/>", Xml, new SetPart(R, PartKind.ElementName, -1, "1x"));

        Assert.Equal("'1x' is not a valid XML name", ex.Message);
    }

    [Fact]
    public void Attribute_values_escape_with_the_attribute_quote_in_use()
    {
        Assert.Equal("<r a=\"1\" b='x&amp;y'/>", Edit("<r a=\"1\" b='2'/>", Xml, new SetPart(R, PartKind.AttributeValue, 1, "x&y")));
        Assert.Equal("<r a='it&apos;s \"q\"'/>", Edit("<r a='1'/>", Xml, new SetPart(R, PartKind.AttributeValue, 0, "it's \"q\"")));
        Assert.Equal("<r a=\"it's &quot;q&quot;\"/>", Edit("<r a=\"1\"/>", Xml, new SetPart(R, PartKind.AttributeValue, 0, "it's \"q\"")));
    }

    [Fact]
    public void Tab_and_line_break_in_an_attribute_value_are_written_as_references()
    {
        string text = Edit("<r a=\"1\"/>", Xml, new SetPart(R, PartKind.AttributeValue, 0, "x\ty\nz"));

        Assert.Equal("<r a=\"x&#9;y&#10;z\"/>", text);
    }

    [Fact]
    public void Renaming_an_attribute_keeps_its_value_and_position()
    {
        string text = Edit("<r a=\"1\" b=\"2\"/>", Xml, new SetPart(R, PartKind.AttributeName, 1, "c"));

        Assert.Equal("<r a=\"1\" c=\"2\"/>", text);
    }

    [Fact]
    public void An_attribute_name_that_duplicates_a_sibling_is_refused()
    {
        CfgeditException ex = Fails("<r a=\"1\" b=\"2\"/>", Xml, new SetPart(R, PartKind.AttributeName, 1, "a"));

        Assert.Equal("an attribute named 'a' already exists", ex.Message);
    }

    [Fact]
    public void An_attribute_value_with_a_character_xml_cannot_hold_is_refused()
    {
        CfgeditException ex = Fails("<r a=\"1\"/>", Xml, new SetPart(R, PartKind.AttributeValue, 0, "x" + (char)1));

        Assert.Equal("the value contains U+0001, which XML 1.0 cannot hold", ex.Message);
    }

    [Fact]
    public void Text_of_a_mixed_text_node_escapes_ampersand_and_less_than()
    {
        string text = Edit("<r>a<b/></r>", Xml, new SetPart(At(Element("r"), new TextStep(0)), PartKind.Text, -1, "x&y<"));

        Assert.Equal("<r>x&amp;y&lt;<b/></r>", text);
    }

    [Fact]
    public void Text_with_a_greater_than_after_two_brackets_escapes_it()
    {
        string text = Edit("<r>a<b/></r>", Xml, new SetPart(At(Element("r"), new TextStep(0)), PartKind.Text, -1, "]]>"));

        Assert.Equal("<r>]]&gt;<b/></r>", text);
    }

    [Fact]
    public void Text_line_breaks_take_the_document_newline()
    {
        string text = Edit("<r>\r\n  <a>x</a>\r\n</r>", Xml, new SetPart(At(Element("r"), Element("a")), PartKind.Text, -1, "p\nq"));

        Assert.Equal("<r>\r\n  <a>p\r\nq</a>\r\n</r>", text);
    }

    [Fact]
    public void A_carriage_return_in_text_is_written_as_a_reference()
    {
        string text = Edit("<r><a>x</a></r>", Xml, new SetPart(At(Element("r"), Element("a")), PartKind.Text, -1, "p\rq"));

        Assert.Equal("<r><a>p&#13;q</a></r>", text);
    }

    [Fact]
    public void Setting_a_text_only_element_to_empty_gives_an_empty_pair()
    {
        string text = Edit("<r><a>x</a></r>", Xml, new SetPart(At(Element("r"), Element("a")), PartKind.Text, -1, string.Empty));

        Assert.Equal("<r><a></a></r>", text);
    }

    [Fact]
    public void Setting_the_text_of_an_empty_element_gives_it_text()
    {
        string text = Edit("<r><a/></r>", Xml, new SetPart(At(Element("r"), Element("a")), PartKind.Text, -1, "t"));

        Assert.Equal("<r><a>t</a></r>", text);
    }

    [Fact]
    public void Setting_an_empty_element_to_empty_text_changes_nothing()
    {
        string source = "<r><a/></r>";

        Assert.Equal(source, Edit(source, Xml, new SetPart(At(Element("r"), Element("a")), PartKind.Text, -1, string.Empty)));
    }

    [Fact]
    public void Whitespace_is_allowed_as_the_text_of_a_text_only_element()
    {
        string text = Edit("<r><a>x</a></r>", Xml, new SetPart(At(Element("r"), Element("a")), PartKind.Text, -1, " "));

        Assert.Equal("<r><a> </a></r>", text);
    }

    [Fact]
    public void Text_that_is_only_a_line_break_is_refused_because_it_would_make_a_container()
    {
        CfgeditException ex = Fails("<r><a>x</a></r>", Xml, new SetPart(At(Element("r"), Element("a")), PartKind.Text, -1, "\n"));

        Assert.Equal("text cannot be only whitespace with a line break, which would make the element a container", ex.Message);
    }

    [Fact]
    public void Whitespace_only_text_in_mixed_content_is_refused()
    {
        CfgeditException ex = Fails("<r>a<b/></r>", Xml, new SetPart(At(Element("r"), new TextStep(0)), PartKind.Text, -1, "  "));

        Assert.Equal("text cannot be only whitespace; delete it instead", ex.Message);
    }

    [Fact]
    public void Empty_text_in_mixed_content_deletes_the_node()
    {
        string text = Edit("<r>a<b/></r>", Xml, new SetPart(At(Element("r"), new TextStep(0)), PartKind.Text, -1, string.Empty));

        Assert.Equal("<r><b/></r>", text);
    }

    [Fact]
    public void Mixed_content_text_changes_in_place()
    {
        string text = Edit("<r>a<b/></r>", Xml, new SetPart(At(Element("r"), new TextStep(0)), PartKind.Text, -1, "b"));

        Assert.Equal("<r>b<b/></r>", text);
    }

    [Fact]
    public void CDATA_text_is_written_as_is_when_it_has_no_terminator()
    {
        string text = Edit("<r><![CDATA[abc]]></r>", Xml, new SetPart(At(Element("r"), new TextStep(0)), PartKind.Text, -1, "x<y"));

        Assert.Equal("<r><![CDATA[x<y]]></r>", text);
    }

    [Fact]
    public void CDATA_text_with_a_terminator_is_split_into_two_sections()
    {
        string text = Edit("<r><![CDATA[abc]]></r>", Xml, new SetPart(At(Element("r"), new TextStep(0)), PartKind.Text, -1, "x]]>y"));

        Assert.Equal("<r><![CDATA[x]]]]><![CDATA[>y]]></r>", text);
    }

    [Fact]
    public void CDATA_text_with_a_carriage_return_is_refused()
    {
        CfgeditException ex = Fails("<r><![CDATA[abc]]></r>", Xml, new SetPart(At(Element("r"), new TextStep(0)), PartKind.Text, -1, "x\ry"));

        Assert.Equal("CDATA cannot hold a carriage return", ex.Message);
    }

    [Fact]
    public void Empty_CDATA_text_deletes_the_section()
    {
        string text = Edit("<r><![CDATA[abc]]></r>", Xml, new SetPart(At(Element("r"), new TextStep(0)), PartKind.Text, -1, string.Empty));

        Assert.Equal("<r></r>", text);
    }

    [Fact]
    public void Setting_a_comment_changes_its_content()
    {
        string text = Edit("<r><!--c--></r>", Xml, new SetPart(At(Element("r"), new CommentStep(0)), PartKind.Comment, -1, "d e"));

        Assert.Equal("<r><!--d e--></r>", text);
    }

    [Fact]
    public void A_comment_with_a_double_dash_or_trailing_dash_is_refused()
    {
        CfgeditException dash = Fails("<r><!--c--></r>", Xml, new SetPart(At(Element("r"), new CommentStep(0)), PartKind.Comment, -1, "a--b"));
        CfgeditException trailing = Fails("<r><!--c--></r>", Xml, new SetPart(At(Element("r"), new CommentStep(0)), PartKind.Comment, -1, "c-"));

        Assert.Equal("a comment cannot contain -- or end with -", dash.Message);
        Assert.Equal("a comment cannot contain -- or end with -", trailing.Message);
    }

    [Fact]
    public void A_comment_with_a_carriage_return_is_refused_because_XML_would_read_it_as_a_line_break()
    {
        CfgeditException ex = Fails("<r><!--c--></r>", Xml, new SetPart(At(Element("r"), new CommentStep(0)), PartKind.Comment, -1, "a\rb"));

        Assert.Equal("a comment cannot contain a carriage return", ex.Message);
    }

    [Fact]
    public void Setting_a_processing_instruction_target_renames_it()
    {
        string text = Edit("<r><?p q?></r>", Xml, new SetPart(At(Element("r"), new PIStep(0)), PartKind.PITarget, -1, "z"));

        Assert.Equal("<r><?z q?></r>", text);
    }

    [Fact]
    public void A_processing_instruction_target_must_be_a_name_and_not_xml_in_any_case()
    {
        CfgeditException bad = Fails("<r><?p q?></r>", Xml, new SetPart(At(Element("r"), new PIStep(0)), PartKind.PITarget, -1, "1x"));
        CfgeditException reserved = Fails("<r><?p q?></r>", Xml, new SetPart(At(Element("r"), new PIStep(0)), PartKind.PITarget, -1, "XmL"));

        Assert.Equal("'1x' is not a valid processing instruction target", bad.Message);
        Assert.Equal("a processing instruction target cannot be 'xml'", reserved.Message);
    }

    [Fact]
    public void Processing_instruction_data_replaces_the_data_and_keeps_the_separator()
    {
        string text = Edit("<r><?p q?></r>", Xml, new SetPart(At(Element("r"), new PIStep(0)), PartKind.PIData, -1, "w"));

        Assert.Equal("<r><?p w?></r>", text);
    }

    [Fact]
    public void Empty_processing_instruction_data_removes_the_separator_too()
    {
        string text = Edit("<r><?p q?></r>", Xml, new SetPart(At(Element("r"), new PIStep(0)), PartKind.PIData, -1, string.Empty));

        Assert.Equal("<r><?p?></r>", text);
    }

    [Fact]
    public void Data_given_to_a_processing_instruction_without_data_gets_a_separator()
    {
        string text = Edit("<r><?p?></r>", Xml, new SetPart(At(Element("r"), new PIStep(0)), PartKind.PIData, -1, "w"));

        Assert.Equal("<r><?p w?></r>", text);
    }

    [Fact]
    public void Processing_instruction_data_that_would_end_the_instruction_is_refused()
    {
        CfgeditException ex = Fails("<r><?p q?></r>", Xml, new SetPart(At(Element("r"), new PIStep(0)), PartKind.PIData, -1, "a?>b"));

        Assert.Equal("a processing instruction's data cannot contain ?>", ex.Message);
    }

    [Fact]
    public void Processing_instruction_data_with_leading_whitespace_is_refused_because_XML_would_drop_it()
    {
        CfgeditException ex = Fails("<r><?p q?></r>", Xml, new SetPart(At(Element("r"), new PIStep(0)), PartKind.PIData, -1, " a"));

        Assert.Equal("a processing instruction's data cannot start with whitespace", ex.Message);
    }

    [Fact]
    public void Setting_a_declaration_or_doctype_part_is_refused()
    {
        CfgeditException ex = Fails("<?xml version=\"1.0\"?><r/>", Xml, new SetPart(At(new DeclarationStep()), PartKind.ElementName, -1, "x"));

        Assert.Equal("this part cannot be edited", ex.Message);
    }

    [Fact]
    public void Changing_a_namespace_declaration_value_is_accepted_when_the_prefix_is_still_bound()
    {
        string text = Edit("<r xmlns:p=\"u\"><p:a/></r>", Xml, new SetPart(R, PartKind.AttributeValue, 0, "v"));

        Assert.Equal("<r xmlns:p=\"v\"><p:a/></r>", text);
    }

    [Fact]
    public void Setting_a_part_to_its_current_value_changes_nothing()
    {
        const string source = "<r a=\"1\"><!--c--></r>";

        Assert.Equal(source, Edit(source, Xml, new SetPart(R, PartKind.AttributeValue, 0, "1")));
        Assert.Equal(source, Edit(source, Xml, new SetPart(At(Element("r"), new CommentStep(0)), PartKind.Comment, -1, "c")));
    }
}
