using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Deleting, moving and duplicating XML nodes, with their attached comments and the layout they leave.</summary>
public sealed class XmlEditDeleteMoveDuplicateTests
{
    // The field shadows the Xml namespace of this test assembly, which the static import would otherwise lose to.
    private static readonly IConfigFormat Xml = EditTestSupport.Xml;
    private static readonly NodePath R = At(Element("r"));

    [Fact]
    public void Deleting_the_first_of_several_lines_removes_its_whole_line()
    {
        string text = Edit("<r>\n  <a/>\n  <b/>\n</r>", Xml, new DeleteNode(At(Element("r"), Element("a"))));

        Assert.Equal("<r>\n  <b/>\n</r>", text);
    }

    [Fact]
    public void Deleting_the_last_of_several_lines_removes_its_whole_line()
    {
        string text = Edit("<r>\n  <a/>\n  <b/>\n</r>", Xml, new DeleteNode(At(Element("r"), Element("b"))));

        Assert.Equal("<r>\n  <a/>\n</r>", text);
    }

    [Fact]
    public void Deleting_the_only_child_leaves_a_whitespace_only_container()
    {
        string text = Edit("<r>\n  <a/>\n</r>", Xml, new DeleteNode(At(Element("r"), Element("a"))));

        Assert.Equal("<r>\n</r>", text);
    }

    [Fact]
    public void Deleting_an_element_on_a_line_with_others_removes_it_and_the_space_after_it()
    {
        string text = Edit("<r><a/> <b/></r>", Xml, new DeleteNode(At(Element("r"), Element("a"))));

        Assert.Equal("<r><b/></r>", text);
    }

    [Fact]
    public void Deleting_the_last_element_on_a_line_removes_the_space_before_it()
    {
        string text = Edit("<r><a/> <b/></r>", Xml, new DeleteNode(At(Element("r"), Element("b"))));

        Assert.Equal("<r><a/></r>", text);
    }

    [Fact]
    public void A_leading_comment_is_deleted_with_its_element()
    {
        string text = Edit("<r>\n  <!--c-->\n  <a/>\n  <b/>\n</r>", Xml, new DeleteNode(At(Element("r"), Element("a"))));

        Assert.Equal("<r>\n  <b/>\n</r>", text);
    }

    [Fact]
    public void A_trailing_comment_on_the_same_line_is_deleted_with_its_element()
    {
        string text = Edit("<r>\n  <a/> <!--t-->\n  <b/>\n</r>", Xml, new DeleteNode(At(Element("r"), Element("a"))));

        Assert.Equal("<r>\n  <b/>\n</r>", text);
    }

    [Fact]
    public void A_comment_deleted_alone_leaves_its_element()
    {
        string text = Edit("<r>\n  <!--c-->\n  <a/>\n</r>", Xml, new DeleteNode(At(Element("r"), new CommentStep(0))));

        Assert.Equal("<r>\n  <a/>\n</r>", text);
    }

    [Fact]
    public void In_mixed_content_a_delete_removes_only_the_node_span()
    {
        string text = Edit("<r>a<b/>c<d/></r>", Xml, new DeleteNode(At(Element("r"), Element("d"))));

        Assert.Equal("<r>a<b/>c</r>", text);
    }

    [Fact]
    public void In_mixed_content_removing_a_node_between_two_text_runs_is_refused_because_they_would_join()
    {
        CfgeditException ex = Fails("<r>a<b/>c</r>", Xml, new DeleteNode(At(Element("r"), Element("b"))));

        Assert.Equal("removing this would join two pieces of text", ex.Message);
    }

    [Fact]
    public void Removing_a_CDATA_section_between_text_runs_is_refused_for_the_same_reason()
    {
        // Text(1) is the CDATA section: text and CDATA share the text index.
        CfgeditException ex = Fails("<r>a <![CDATA[x]]> c</r>", Xml, new DeleteNode(At(Element("r"), new TextStep(1))));

        Assert.Equal("removing this would join two pieces of text", ex.Message);
    }

    [Fact]
    public void Deleting_between_whitespace_separated_siblings_joins_nothing()
    {
        string text = Edit("<r>\n  <a/>\n  <b/>\n  <c/>\n</r>", Xml, new DeleteNode(At(Element("r"), Element("b"))));

        Assert.Equal("<r>\n  <a/>\n  <c/>\n</r>", text);
    }

    [Fact]
    public void The_root_element_cannot_be_deleted()
    {
        CfgeditException ex = Fails("<r/>", Xml, new DeleteNode(R));

        Assert.Equal("The root element cannot be deleted", ex.Message);
    }

    [Fact]
    public void A_declaration_or_doctype_cannot_be_deleted()
    {
        CfgeditException declaration = Fails("<?xml version=\"1.0\"?><r/>", Xml, new DeleteNode(At(new DeclarationStep())));
        CfgeditException doctype = Fails("<!DOCTYPE r><r/>", Xml, new DeleteNode(At(new DoctypeStep())));

        Assert.Equal("This node cannot be deleted", declaration.Message);
        Assert.Equal("This node cannot be deleted", doctype.Message);
    }

    [Fact]
    public void A_prolog_comment_on_its_own_line_is_deleted_with_its_line()
    {
        string text = Edit("<!--p-->\n<r/>", Xml, new DeleteNode(At(new CommentStep(0))));

        Assert.Equal("<r/>", text);
    }

    [Fact]
    public void An_epilog_comment_on_its_own_line_is_deleted_with_its_line()
    {
        string text = Edit("<r/>\n<!--e-->", Xml, new DeleteNode(At(new CommentStep(0))));

        Assert.Equal("<r/>\n", text);
    }

    [Fact]
    public void Moving_a_node_to_the_front_puts_it_on_a_line_of_its_own()
    {
        string text = Edit("<r>\n  <a/>\n  <b/>\n  <c/>\n</r>", Xml, new MoveNode(At(Element("r"), Element("c")), 0));

        Assert.Equal("<r>\n  <c/>\n  <a/>\n  <b/>\n</r>", text);
    }

    [Fact]
    public void Moving_a_node_past_the_last_puts_it_after_the_last_line()
    {
        string text = Edit("<r>\n  <a/>\n  <b/>\n</r>", Xml, new MoveNode(At(Element("r"), Element("a")), 1));

        Assert.Equal("<r>\n  <b/>\n  <a/>\n</r>", text);
    }

    [Fact]
    public void A_new_index_past_the_end_is_clamped()
    {
        string text = Edit("<r>\n  <a/>\n  <b/>\n</r>", Xml, new MoveNode(At(Element("r"), Element("a")), 99));

        Assert.Equal("<r>\n  <b/>\n  <a/>\n</r>", text);
    }

    [Fact]
    public void Moving_a_node_to_its_own_position_changes_nothing()
    {
        const string source = "<r>\n  <a/>\n  <b/>\n</r>";

        Assert.Equal(source, Edit(source, Xml, new MoveNode(At(Element("r"), Element("a")), 0)));
    }

    [Fact]
    public void Moving_a_node_in_mixed_content_places_it_with_no_added_whitespace()
    {
        string text = Edit("<r>x<a/>y<b/></r>", Xml, new MoveNode(At(Element("r"), Element("b")), 0));

        Assert.Equal("<r><b/>x<a/>y</r>", text);
    }

    [Fact]
    public void A_leading_comment_moves_with_its_element()
    {
        string text = Edit("<r>\n  <a/>\n  <!--c-->\n  <b/>\n</r>", Xml, new MoveNode(At(Element("r"), Element("b")), 0));

        Assert.Equal("<r>\n  <!--c-->\n  <b/>\n  <a/>\n</r>", text);
    }

    [Fact]
    public void Moving_a_prolog_comment_into_the_epilog_puts_it_after_the_root()
    {
        string text = Edit("<!--p-->\n<r/>", Xml, new MoveNode(At(new CommentStep(0)), 1));

        Assert.Equal("<r/>\n<!--p-->", text);
    }

    [Fact]
    public void The_root_element_cannot_be_moved()
    {
        CfgeditException ex = Fails("<r/>", Xml, new MoveNode(R, 0));

        Assert.Equal("The root element cannot be moved", ex.Message);
    }

    [Fact]
    public void A_text_node_cannot_be_moved()
    {
        CfgeditException ex = Fails("<r>a<b/></r>", Xml, new MoveNode(At(Element("r"), new TextStep(0)), 0));

        Assert.Equal("A text node cannot be moved", ex.Message);
    }

    [Fact]
    public void An_element_is_duplicated_on_its_own_line_after_the_original()
    {
        string text = Edit("<r>\n  <a/>\n</r>", Xml, new DuplicateNode(At(Element("r"), Element("a"))));

        Assert.Equal("<r>\n  <a/>\n  <a/>\n</r>", text);
    }

    [Fact]
    public void Duplicating_copies_the_exact_source_including_its_attached_comment()
    {
        string text = Edit("<r>\n  <a/> <!--t-->\n  <b/>\n</r>", Xml, new DuplicateNode(At(Element("r"), Element("a"))));

        Assert.Equal("<r>\n  <a/> <!--t-->\n  <a/> <!--t-->\n  <b/>\n</r>", text);
    }

    [Fact]
    public void Duplicating_on_one_line_stays_on_that_line()
    {
        string text = Edit("<r><a/></r>", Xml, new DuplicateNode(At(Element("r"), Element("a"))));

        Assert.Equal("<r><a/><a/></r>", text);
    }

    [Fact]
    public void Duplicating_in_mixed_content_adds_no_whitespace()
    {
        string text = Edit("<r>a<c/>b</r>", Xml, new DuplicateNode(At(Element("r"), Element("c"))));

        Assert.Equal("<r>a<c/><c/>b</r>", text);
    }

    [Fact]
    public void A_text_node_cannot_be_duplicated()
    {
        CfgeditException ex = Fails("<r>a<b/></r>", Xml, new DuplicateNode(At(Element("r"), new TextStep(0))));

        Assert.Equal("A text node cannot be duplicated", ex.Message);
    }

    [Fact]
    public void The_root_element_cannot_be_duplicated()
    {
        CfgeditException ex = Fails("<r/>", Xml, new DuplicateNode(R));

        Assert.Equal("The root element cannot be duplicated", ex.Message);
    }
}
