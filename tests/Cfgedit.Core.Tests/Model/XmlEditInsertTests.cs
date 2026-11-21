using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Inserting nodes: layout of element content, mixed content, empty and text-only elements, and the prolog and epilog.</summary>
public sealed class XmlEditInsertTests
{
    // The field shadows the Xml namespace of this test assembly, which the static import would otherwise lose to.
    private static readonly IConfigFormat Xml = EditTestSupport.Xml;
    private static readonly NodePath R = At(Element("r"));

    [Fact]
    public void An_element_goes_into_an_empty_root_on_its_own_line_with_a_spaced_self_closing_tag()
    {
        string text = Edit("<r/>", Xml, new InsertNode(R, 0, new NewXmlElement("a", [], null)));

        Assert.Equal("<r>\n  <a />\n</r>", text);
    }

    [Fact]
    public void Lines_in_a_CRLF_document_are_CRLF()
    {
        string text = Edit("<r/>\r\n", Xml, new InsertNode(R, 0, new NewXmlElement("a", [], null)));

        Assert.Equal("<r>\r\n  <a />\r\n</r>\r\n", text);
    }

    [Fact]
    public void A_new_child_at_the_end_copies_the_indentation_and_self_closing_style_of_its_siblings()
    {
        string text = Edit("<r>\r\n  <a/>\r\n</r>", Xml, new InsertNode(R, 1, new NewXmlElement("b", [("x", "1")], null)));

        Assert.Equal("<r>\r\n  <a/>\r\n  <b x=\"1\"/>\r\n</r>", text);
    }

    [Fact]
    public void A_new_child_before_the_first_takes_the_self_closing_style_of_the_next_sibling()
    {
        string text = Edit("<r>\n  <a/>\n</r>", Xml, new InsertNode(R, 0, new NewXmlElement("b", [], null)));

        Assert.Equal("<r>\n  <b/>\n  <a/>\n</r>", text);
    }

    [Fact]
    public void The_self_closing_style_is_copied_from_a_space_before_the_slash()
    {
        string text = Edit("<r><a x='1' /></r>", Xml, new InsertNode(R, 1, new NewXmlElement("b", [("y", "2")], null)));

        Assert.Equal("<r><a x='1' /><b y='2' /></r>", text);
    }

    [Fact]
    public void A_new_attribute_quote_is_the_document_majority_when_the_element_has_none()
    {
        string text = Edit("<r><a x='1'/></r>", Xml, new InsertNode(R, 1, new NewXmlElement("b", [("y", "2")], null)));

        Assert.Equal("<r><a x='1'/><b y='2'/></r>", text);
    }

    [Fact]
    public void Tab_indentation_is_copied()
    {
        string text = Edit("<r>\n\t<a/>\n</r>", Xml, new InsertNode(R, 1, new NewXmlElement("x", [], null)));

        Assert.Equal("<r>\n\t<a/>\n\t<x/>\n</r>", text);
    }

    [Fact]
    public void A_four_space_unit_is_inferred_and_used_for_a_new_level()
    {
        string text = Edit("<r>\n    <a>\n        <b/>\n    </a>\n</r>", Xml, new InsertNode(At(Element("r"), Element("a")), 1, new NewXmlElement("c", [], null)));

        Assert.Equal("<r>\n    <a>\n        <b/>\n        <c/>\n    </a>\n</r>", text);
    }

    [Fact]
    public void An_empty_element_takes_its_first_child_one_unit_deeper_with_a_closing_tag_at_its_own_indentation()
    {
        string text = Edit("<r>\n    <a/>\n</r>", Xml, new InsertNode(At(Element("r"), Element("a")), 0, new NewXmlElement("c", [], null)));

        Assert.Equal("<r>\n    <a>\n        <c />\n    </a>\n</r>", text);
    }

    [Fact]
    public void A_pair_with_no_content_becomes_a_container_with_its_child_on_a_line_of_its_own()
    {
        string text = Edit("<a></a>", Xml, new InsertNode(At(Element("a")), 0, new NewXmlElement("x", [], null)));

        Assert.Equal("<a>\n  <x />\n</a>", text);
    }

    [Fact]
    public void A_container_written_across_lines_gets_its_child_in_the_same_way()
    {
        string text = Edit("<a>\n</a>", Xml, new InsertNode(At(Element("a")), 0, new NewXmlElement("x", [], null)));

        Assert.Equal("<a>\n  <x />\n</a>", text);
    }

    [Fact]
    public void Whitespace_inside_an_element_with_no_content_is_replaced_by_the_new_content()
    {
        string text = Edit("<a> </a>", Xml, new InsertNode(At(Element("a")), 0, new NewXmlElement("x", [], null)));

        Assert.Equal("<a>\n  <x />\n</a>", text);
    }

    [Fact]
    public void A_nested_empty_container_is_indented_one_unit_deeper_than_its_own_line()
    {
        string text = Edit("<r>\n  <a>\n  </a>\n</r>", Xml, new InsertNode(At(Element("r"), Element("a")), 0, new NewXmlElement("x", [], null)));

        Assert.Equal("<r>\n  <a>\n    <x />\n  </a>\n</r>", text);
    }

    [Fact]
    public void A_child_inserted_into_a_text_only_element_goes_after_its_text_as_mixed_content()
    {
        string text = Edit("<a>x</a>", Xml, new InsertNode(At(Element("a")), 0, new NewXmlElement("b", [], null)));

        Assert.Equal("<a>x<b /></a>", text);
    }

    [Fact]
    public void Before_a_child_in_mixed_content_no_whitespace_is_added()
    {
        string text = Edit("<r>a<c/>b</r>", Xml, new InsertNode(R, 1, new NewXmlElement("x", [], null)));

        Assert.Equal("<r>a<x/><c/>b</r>", text);
    }

    [Fact]
    public void After_the_last_child_in_mixed_content_no_whitespace_is_added()
    {
        string text = Edit("<r>a <c/></r>", Xml, new InsertNode(R, 2, new NewXmlElement("x", [], null)));

        Assert.Equal("<r>a <c/><x/></r>", text);
    }

    [Fact]
    public void A_child_between_two_elements_on_one_line_stays_on_that_line()
    {
        string text = Edit("<r><a/><c/></r>", Xml, new InsertNode(R, 1, new NewXmlElement("x", [], null)));

        Assert.Equal("<r><a/><x/><c/></r>", text);
    }

    [Fact]
    public void An_element_with_text_writes_its_attribute_and_text_escaped()
    {
        string text = Edit("<r/>", Xml, new InsertNode(R, 0, new NewXmlElement("a", [("k", "v&")], "t<")));

        Assert.Equal("<r>\n  <a k=\"v&amp;\">t&lt;</a>\n</r>", text);
    }

    [Fact]
    public void Text_that_is_only_a_line_break_is_refused_for_a_new_element()
    {
        CfgeditException ex = Fails("<r/>", Xml, new InsertNode(R, 0, new NewXmlElement("a", [], "\n")));

        Assert.Equal("text cannot be only whitespace with a line break, which would make the element a container", ex.Message);
    }

    [Fact]
    public void Text_goes_into_an_empty_element_as_its_content()
    {
        string text = Edit("<r/>", Xml, new InsertNode(R, 0, new NewXmlText("hi & bye", false)));

        Assert.Equal("<r>hi &amp; bye</r>", text);
    }

    [Fact]
    public void Text_goes_between_two_elements_with_no_whitespace_between_them()
    {
        string text = Edit("<r><a/><b/></r>", Xml, new InsertNode(R, 1, new NewXmlText("t", false)));

        Assert.Equal("<r><a/>t<b/></r>", text);
    }

    [Fact]
    public void Text_next_to_existing_text_is_refused_because_it_would_merge_with_it()
    {
        CfgeditException ex = Fails("<a>x<b/></a>", Xml, new InsertNode(At(Element("a")), 0, new NewXmlText("t", false)));

        Assert.Equal("text cannot sit next to other text", ex.Message);
    }

    [Fact]
    public void Text_next_to_whitespace_between_elements_is_refused_because_it_would_merge_with_it()
    {
        CfgeditException ex = Fails("<r>\n  <a/>\n</r>", Xml, new InsertNode(R, 1, new NewXmlText("t", false)));

        Assert.Equal("text cannot sit next to other text", ex.Message);
    }

    [Fact]
    public void Whitespace_only_text_is_refused_because_it_would_be_formatting()
    {
        CfgeditException ex = Fails("<r/>", Xml, new InsertNode(R, 0, new NewXmlText("  ", false)));

        Assert.Equal("text cannot be only whitespace; it would be formatting, not a node", ex.Message);
    }

    [Fact]
    public void Empty_text_is_refused()
    {
        CfgeditException ex = Fails("<r/>", Xml, new InsertNode(R, 0, new NewXmlText(string.Empty, false)));

        Assert.Equal("text cannot be empty", ex.Message);
    }

    [Fact]
    public void A_CDATA_section_goes_on_its_own_line_in_element_content()
    {
        string text = Edit("<r>\n  <a/>\n</r>", Xml, new InsertNode(R, 1, new NewXmlText("x]]>y", true)));

        Assert.Equal("<r>\n  <a/>\n  <![CDATA[x]]]]><![CDATA[>y]]>\n</r>", text);
    }

    [Fact]
    public void A_CDATA_section_with_a_carriage_return_is_refused()
    {
        CfgeditException ex = Fails("<r/>", Xml, new InsertNode(R, 0, new NewXmlText("a\rb", true)));

        Assert.Equal("CDATA cannot hold a carriage return", ex.Message);
    }

    [Fact]
    public void A_comment_goes_on_its_own_line_with_the_indentation_of_its_siblings()
    {
        string text = Edit("<r>\n  <a/>\n</r>", Xml, new InsertNode(R, 0, new NewXmlComment(" c ")));

        Assert.Equal("<r>\n  <!-- c -->\n  <a/>\n</r>", text);
    }

    [Fact]
    public void A_comment_in_an_empty_element_makes_it_a_container()
    {
        string text = Edit("<a/>", Xml, new InsertNode(At(Element("a")), 0, new NewXmlComment("c")));

        Assert.Equal("<a>\n  <!--c-->\n</a>", text);
    }

    [Fact]
    public void A_comment_inserted_into_text_only_content_goes_after_the_text()
    {
        string text = Edit("<r>x</r>", Xml, new InsertNode(R, 0, new NewXmlComment("c")));

        Assert.Equal("<r>x<!--c--></r>", text);
    }

    [Fact]
    public void A_comment_with_a_double_dash_is_refused()
    {
        CfgeditException ex = Fails("<r/>", Xml, new InsertNode(R, 0, new NewXmlComment("a--b")));

        Assert.Equal("a comment cannot contain -- or end with -", ex.Message);
    }

    [Fact]
    public void A_child_index_past_the_end_is_refused()
    {
        CfgeditException ex = Fails("<r/>", Xml, new InsertNode(R, 1, new NewXmlElement("a", [], null)));

        Assert.Equal("Index 1 is outside the children, 0 to 0", ex.Message);
    }

    [Fact]
    public void A_child_cannot_be_inserted_into_a_comment()
    {
        CfgeditException ex = Fails("<r><!--c--></r>", Xml, new InsertNode(At(Element("r"), new CommentStep(0)), 0, new NewXmlComment("d")));

        Assert.Equal("This node cannot have children", ex.Message);
    }

    [Fact]
    public void A_prefixed_element_needs_its_prefix_declared_in_scope_or_on_itself()
    {
        CfgeditException undeclared = Fails("<r/>", Xml, new InsertNode(R, 0, new NewXmlElement("p:a", [], null)));
        string declared = Edit("<r/>", Xml, new InsertNode(R, 0, new NewXmlElement("p:a", [("xmlns:p", "u")], null)));

        Assert.Equal("the namespace prefix 'p' is not declared", undeclared.Message);
        Assert.Equal("<r>\n  <p:a xmlns:p=\"u\" />\n</r>", declared);
    }

    [Fact]
    public void A_new_element_with_an_invalid_name_or_attribute_is_refused()
    {
        CfgeditException name = Fails("<r/>", Xml, new InsertNode(R, 0, new NewXmlElement("1a", [], null)));
        CfgeditException duplicate = Fails("<r/>", Xml, new InsertNode(R, 0, new NewXmlElement("a", [("k", "1"), ("k", "2")], null)));

        Assert.Equal("'1a' is not a valid XML name", name.Message);
        Assert.Equal("an attribute named 'k' already exists", duplicate.Message);
    }

    [Fact]
    public void Only_a_comment_can_go_at_the_document_level()
    {
        CfgeditException ex = Fails("<r/>", Xml, new InsertNode(NodePath.Empty, 0, new NewXmlElement("a", [], null)));

        Assert.Equal("Only comments can be placed at the document level", ex.Message);
    }

    [Fact]
    public void A_comment_cannot_go_before_the_XML_declaration()
    {
        CfgeditException ex = Fails("<?xml version=\"1.0\"?>\n<r/>", Xml, new InsertNode(NodePath.Empty, 0, new NewXmlComment("c")));

        Assert.Equal("a comment cannot come before the XML declaration", ex.Message);
    }

    [Fact]
    public void A_prolog_comment_goes_before_the_root_on_its_own_line()
    {
        string text = Edit("<?xml version=\"1.0\"?>\n<r/>", Xml, new InsertNode(NodePath.Empty, 1, new NewXmlComment("c")));

        Assert.Equal("<?xml version=\"1.0\"?>\n<!--c-->\n<r/>", text);
    }

    [Fact]
    public void An_epilog_comment_goes_after_the_root_on_its_own_line()
    {
        string text = Edit("<r/>\n", Xml, new InsertNode(NodePath.Empty, 1, new NewXmlComment("c")));

        Assert.Equal("<r/>\n<!--c-->\n", text);
    }

    [Fact]
    public void A_document_comment_on_the_same_line_as_the_root_is_separated_by_a_space()
    {
        string text = Edit("<?xml version=\"1.0\"?><r/>", Xml, new InsertNode(NodePath.Empty, 1, new NewXmlComment("c")));

        Assert.Equal("<?xml version=\"1.0\"?><!--c--> <r/>", text);
    }
}
