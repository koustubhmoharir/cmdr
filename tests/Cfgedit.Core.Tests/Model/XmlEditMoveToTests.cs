using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>MoveNodeTo and NestBy on XML: moves into another element, layout, verbatim lines, namespaces, focus and the nest rules.</summary>
public sealed class XmlEditMoveToTests
{
    // The field shadows the Xml namespace of this test assembly, which the static import would otherwise lose to.
    private static readonly IConfigFormat Xml = EditTestSupport.Xml;
    private static readonly NodePath R = At(Element("r"));

    [Fact]
    public void An_element_moves_up_into_the_element_above_as_its_last_child()
    {
        string text = Edit(
            "<r>\n  <a>\n    <x/>\n  </a>\n  <b/>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("b")), At(Element("r"), Element("a")), 1, null));

        Assert.Equal("<r>\n  <a>\n    <x/>\n    <b/>\n  </a>\n</r>", text);
    }

    [Fact]
    public void An_element_moves_down_into_the_element_below_as_its_first_child()
    {
        string text = Edit(
            "<r>\n  <a/>\n  <b>\n    <x/>\n  </b>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("a")), At(Element("r"), Element("b")), 0, null));

        Assert.Equal("<r>\n  <b>\n    <a/>\n    <x/>\n  </b>\n</r>", text);
    }

    [Fact]
    public void An_element_moves_out_of_its_parent_before_the_parent()
    {
        string text = Edit(
            "<r>\n  <p>\n    <a/>\n  </p>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("p"), Element("a")), R, 0, null));

        Assert.Equal("<r>\n  <a/>\n  <p>\n  </p>\n</r>", text);
    }

    [Fact]
    public void An_element_moves_out_of_its_parent_after_the_parent()
    {
        string text = Edit(
            "<r>\n  <p>\n    <a/>\n  </p>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("p"), Element("a")), R, 1, null));

        Assert.Equal("<r>\n  <p>\n  </p>\n  <a/>\n</r>", text);
    }

    [Fact]
    public void An_element_moves_into_a_self_closing_element_which_gets_content()
    {
        string text = Edit(
            "<r>\n  <a/>\n  <b/>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("b")), At(Element("r"), Element("a")), 0, null));

        Assert.Equal("<r>\n  <a>\n    <b/>\n  </a>\n</r>", text);
    }

    [Fact]
    public void An_element_moves_into_an_empty_start_and_end_pair_which_gets_content()
    {
        string text = Edit(
            "<r>\n  <a></a>\n  <b/>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("b")), At(Element("r"), Element("a")), 0, null));

        Assert.Equal("<r>\n  <a>\n    <b/>\n  </a>\n</r>", text);
    }

    [Fact]
    public void An_element_moves_into_a_text_only_element_after_its_text()
    {
        string text = Edit(
            "<r>\n  <a>x</a>\n  <b/>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("b")), At(Element("r"), Element("a")), 0, null));

        Assert.Equal("<r>\n  <a>x<b/></a>\n</r>", text);
    }

    [Fact]
    public void Moving_the_only_child_leaves_its_parent_with_no_children_as_a_delete_would()
    {
        string text = Edit(
            "<r>\n  <a>\n    <b/>\n  </a>\n  <c/>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("a"), Element("b")), At(Element("r"), Element("c")), 0, null));

        Assert.Equal("<r>\n  <a>\n  </a>\n  <c>\n    <b/>\n  </c>\n</r>", text);
    }

    [Fact]
    public void A_comment_moves_alone_into_an_element_as_its_last_child()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "<r>\n  <b>\n    <x/>\n  </b>\n  <!--c-->\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), new CommentStep(0)), At(Element("r"), Element("b")), 1, null));

        Assert.Equal("<r>\n  <b>\n    <x/>\n    <!--c-->\n  </b>\n</r>", text);
        Assert.Equal(At(Element("r"), Element("b"), new CommentStep(0)), focus);
    }

    [Fact]
    public void A_processing_instruction_moves_into_an_empty_element()
    {
        string text = Edit(
            "<r>\n  <a/>\n  <?pi x?>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), new PIStep(0)), At(Element("r"), Element("a")), 0, null));

        Assert.Equal("<r>\n  <a>\n    <?pi x?>\n  </a>\n</r>", text);
    }

    [Fact]
    public void A_text_node_moves_into_an_empty_element_and_is_removed_from_its_parent()
    {
        string text = Edit(
            "<r><a/>hi<b/></r>",
            Xml,
            new MoveNodeTo(At(Element("r"), new TextStep(0)), At(Element("r"), Element("a")), 0, null));

        Assert.Equal("<r><a>hi</a><b/></r>", text);
    }

    [Fact]
    public void A_text_node_is_refused_next_to_other_text_in_its_new_element()
    {
        CfgeditException ex = Fails(
            "<r><a>y</a>x</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), new TextStep(0)), At(Element("r"), Element("a")), 0, null));

        Assert.Equal("text cannot sit next to other text", ex.Message);
    }

    [Fact]
    public void A_deeper_element_is_re_indented_and_its_verbatim_lines_stay()
    {
        // The attribute value and the comment span lines, so the lines that begin inside them keep their text.
        string text = Edit(
            "<r>\n  <a>\n    <p k=\"v\n    w\">\n      <!--c\n      d-->\n      <q/>\n    </p>\n  </a>\n  <box>\n    <deep>\n      <k/>\n    </deep>\n  </box>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("a"), Element("p")), At(Element("r"), Element("box"), Element("deep")), 1, null));

        Assert.Equal(
            "<r>\n  <a>\n  </a>\n  <box>\n    <deep>\n      <k/>\n      <p k=\"v\n    w\">\n        <!--c\n      d-->\n        <q/>\n      </p>\n    </deep>\n  </box>\n</r>",
            text);
    }

    [Fact]
    public void A_shallower_element_is_re_indented_and_its_verbatim_lines_stay()
    {
        string text = Edit(
            "<r>\n  <a>\n    <x/>\n  </a>\n  <box>\n    <deep>\n      <p k=\"v\n    w\">\n        <!--c\n      d-->\n        <q/>\n      </p>\n    </deep>\n  </box>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("box"), Element("deep"), Element("p")), At(Element("r"), Element("a")), 1, null));

        Assert.Equal(
            "<r>\n  <a>\n    <x/>\n    <p k=\"v\n    w\">\n      <!--c\n      d-->\n      <q/>\n    </p>\n  </a>\n  <box>\n    <deep>\n    </deep>\n  </box>\n</r>",
            text);
    }

    [Fact]
    public void A_move_in_a_crlf_document_keeps_its_line_breaks()
    {
        string text = Edit(
            "<r>\r\n  <a/>\r\n  <b/>\r\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("b")), At(Element("r"), Element("a")), 0, null));

        Assert.Equal("<r>\r\n  <a>\r\n    <b/>\r\n  </a>\r\n</r>", text);
    }

    [Fact]
    public void A_key_is_refused()
    {
        CfgeditException ex = Fails(
            "<r>\n  <a/>\n  <b/>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("b")), At(Element("r"), Element("a")), 0, "k"));

        Assert.Equal("XML nodes have no key", ex.Message);
    }

    [Fact]
    public void A_parent_that_is_not_an_element_is_refused()
    {
        CfgeditException ex = Fails(
            "<r><!--c--><a/></r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("a")), At(Element("r"), new CommentStep(0)), 0, null));

        Assert.Equal("The new parent must be an XML element", ex.Message);
    }

    [Fact]
    public void A_node_cannot_move_into_its_own_descendant()
    {
        CfgeditException ex = Fails(
            "<r>\n  <a>\n    <b/>\n  </a>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("a")), At(Element("r"), Element("a"), Element("b")), 0, null));

        Assert.Equal("A node cannot be moved into itself", ex.Message);
    }

    [Fact]
    public void The_root_element_cannot_move()
    {
        CfgeditException ex = Fails(
            "<r>\n  <a/>\n</r>",
            Xml,
            new MoveNodeTo(R, At(Element("r"), Element("a")), 0, null));

        Assert.Equal("The root element cannot be moved", ex.Message);
    }

    [Fact]
    public void A_prefix_declared_outside_the_moved_element_is_refused_where_the_new_place_resolves_it_differently()
    {
        CfgeditException ex = Fails(
            "<r>\n  <p xmlns:x=\"urn:a\">\n    <x:e/>\n  </p>\n  <q/>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("p"), Element("x:e")), At(Element("r"), Element("q")), 0, null));

        Assert.Equal(
            "the element uses namespace prefix 'x' declared outside it; moving it would change its namespace",
            ex.Message);
    }

    [Fact]
    public void A_prefix_that_resolves_to_the_same_uri_at_the_new_place_is_allowed()
    {
        string text = Edit(
            "<r xmlns:x=\"urn:a\">\n  <p>\n    <x:e/>\n  </p>\n  <q/>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("p"), Element("x:e")), At(Element("r"), Element("q")), 0, null));

        Assert.Equal("<r xmlns:x=\"urn:a\">\n  <p>\n  </p>\n  <q>\n    <x:e/>\n  </q>\n</r>", text);
    }

    [Fact]
    public void A_prefix_declared_inside_the_moved_element_is_allowed()
    {
        string text = Edit(
            "<r>\n  <p>\n    <n xmlns:x=\"urn:a\">\n      <x:e/>\n    </n>\n  </p>\n  <q/>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("p"), Element("n")), At(Element("r"), Element("q")), 0, null));

        Assert.Equal("<r>\n  <p>\n  </p>\n  <q>\n    <n xmlns:x=\"urn:a\">\n      <x:e/>\n    </n>\n  </q>\n</r>", text);
    }

    [Fact]
    public void A_default_namespace_declared_outside_the_moved_element_is_refused_where_the_new_place_has_none()
    {
        CfgeditException ex = Fails(
            "<r>\n  <p xmlns=\"urn:d\">\n    <c/>\n  </p>\n  <q/>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("p"), Element("c")), At(Element("r"), Element("q")), 0, null));

        Assert.Equal(
            "the element uses the default namespace declared outside it; moving it would change its namespace",
            ex.Message);
    }

    [Fact]
    public void A_default_namespace_that_resolves_the_same_at_the_new_place_is_allowed()
    {
        string text = Edit(
            "<r xmlns=\"urn:d\">\n  <p>\n    <c/>\n  </p>\n  <q/>\n</r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("p"), Element("c")), At(Element("r"), Element("q")), 0, null));

        Assert.Equal("<r xmlns=\"urn:d\">\n  <p>\n  </p>\n  <q>\n    <c/>\n  </q>\n</r>", text);
    }

    [Fact]
    public void Focus_follows_an_element_moved_into_a_sibling_with_the_same_name()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "<r><b/><x><b/></x></r>",
            Xml,
            new MoveNodeTo(At(Element("r"), Element("b")), At(Element("r"), Element("x")), 1, null));

        Assert.Equal("<r><x><b/><b/></x></r>", text);
        Assert.Equal(At(Element("r"), Element("x"), Element("b", 1)), focus);
    }

    [Fact]
    public void Nest_up_into_an_element_neighbour_enters_it_at_its_end()
    {
        NestStep? step = ModelTestSupport.Parse("<r>\n  <a>\n    <x/>\n  </a>\n  <b/>\n</r>", Xml)
            .NestBy(At(Element("r"), Element("b")), -1);

        Assert.Equal(new MoveNodeTo(At(Element("r"), Element("b")), At(Element("r"), Element("a")), 1, null), step!.Op);
        Assert.False(step.NeedsKey);
    }

    [Fact]
    public void Nest_down_into_an_element_neighbour_enters_it_at_its_start()
    {
        NestStep? step = ModelTestSupport.Parse("<r>\n  <a/>\n  <b>\n    <x/>\n  </b>\n</r>", Xml)
            .NestBy(At(Element("r"), Element("a")), 1);

        Assert.Equal(new MoveNodeTo(At(Element("r"), Element("a")), At(Element("r"), Element("b")), 0, null), step!.Op);
    }

    [Fact]
    public void Nest_past_a_text_neighbour_is_the_ordinary_move()
    {
        NestStep? step = ModelTestSupport.Parse("<r><a/>hi<b/></r>", Xml).NestBy(At(Element("r"), Element("b")), -1);

        Assert.IsType<MoveNode>(step!.Op);
    }

    [Fact]
    public void Nest_out_of_the_parent_goes_just_before_it()
    {
        NestStep? step = ModelTestSupport.Parse("<r>\n  <p>\n    <a/>\n    <z/>\n  </p>\n</r>", Xml)
            .NestBy(At(Element("r"), Element("p"), Element("a")), -1);

        Assert.Equal(new MoveNodeTo(At(Element("r"), Element("p"), Element("a")), R, 0, null), step!.Op);
    }

    [Fact]
    public void The_first_child_of_the_root_element_cannot_nest_out_of_it()
    {
        Assert.Null(ModelTestSupport.Parse("<r><a/></r>", Xml).NestBy(At(Element("r"), Element("a")), -1));
    }
}
