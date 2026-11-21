using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// <see cref="ConfigDocument.InsertionIndexBefore"/> and <see cref="ConfigDocument.InsertionIndexAfter"/>, for JSON and XML: the
/// index is the one an insertion needs to go before the node's attached leading comments, or after its trailing comment.
/// </summary>
public sealed class XmlEditInsertionIndexTests
{
    // The fields shadow the format names of this test assembly, which the static import would otherwise lose to.
    private static readonly IConfigFormat Xml = EditTestSupport.Xml;
    private static readonly IConfigFormat Jsonc = EditTestSupport.Jsonc;

    // children of the array: lead(0), item 0 (1), trail(2), item 1 (3); the leading and trailing comments are attached to item 0
    private const string JsonArray = "[\n  // lead\n  1, // trail\n  2\n]";

    [Fact]
    public void In_JSON_a_node_with_attached_comments_is_inserted_before_its_leading_comment_and_after_its_trailing_one()
    {
        ConfigDocument document = ModelTestSupport.Parse(JsonArray, Jsonc);
        NodePath first = At(Item(0), Item(0));
        NodePath second = At(Item(0), Item(1));

        Assert.Equal(0, document.InsertionIndexBefore(first));
        Assert.Equal(3, document.InsertionIndexAfter(first));
        Assert.Equal(3, document.InsertionIndexBefore(second));
        Assert.Equal(4, document.InsertionIndexAfter(second));
    }

    [Fact]
    public void In_JSON_a_comment_is_its_own_insertion_point()
    {
        ConfigDocument document = ModelTestSupport.Parse(JsonArray, Jsonc);
        NodePath lead = At(Item(0), Comment(0));

        Assert.Equal(0, document.InsertionIndexBefore(lead));
        Assert.Equal(1, document.InsertionIndexAfter(lead));
    }

    [Fact]
    public void In_XML_a_node_with_attached_comments_gets_the_same_indices()
    {
        ConfigDocument document = ModelTestSupport.Parse("<r>\n  <!--lead-->\n  <a/> <!--trail-->\n  <b/>\n</r>", Xml);
        NodePath a = At(Element("r"), Element("a"));
        NodePath b = At(Element("r"), Element("b"));

        Assert.Equal(0, document.InsertionIndexBefore(a));
        Assert.Equal(3, document.InsertionIndexAfter(a));
        Assert.Equal(3, document.InsertionIndexBefore(b));
        Assert.Equal(4, document.InsertionIndexAfter(b));
    }

    [Fact]
    public void In_XML_a_comment_on_the_same_line_after_an_element_trails_that_element()
    {
        // children: a(0), comment(1), b(2). The comment is a trailing comment of a, so b's insertion index is 2.
        ConfigDocument document = ModelTestSupport.Parse("<r><a/><!--c--><b/></r>", Xml);

        Assert.Equal(0, document.InsertionIndexBefore(At(Element("r"), Element("a"))));
        Assert.Equal(2, document.InsertionIndexAfter(At(Element("r"), Element("a"))));
        Assert.Equal(2, document.InsertionIndexBefore(At(Element("r"), Element("b"))));
    }

    [Fact]
    public void In_XML_the_prolog_comments_and_the_root_have_their_own_indices()
    {
        ConfigDocument document = ModelTestSupport.Parse("<!--p-->\n<r/>\n<!--e-->", Xml);

        Assert.Equal(0, document.InsertionIndexBefore(At(new CommentStep(0))));
        Assert.Equal(1, document.InsertionIndexAfter(At(new CommentStep(0))));
        Assert.Equal(1, document.InsertionIndexBefore(At(Element("r"))));
        Assert.Equal(2, document.InsertionIndexAfter(At(Element("r"))));
    }

    [Fact]
    public void An_insertion_at_the_index_goes_before_the_attached_comment()
    {
        ConfigDocument document = ModelTestSupport.Parse("<r>\n  <!--lead-->\n  <a/>\n</r>", Xml);
        int index = document.InsertionIndexBefore(At(Element("r"), Element("a")));

        string text = document.Apply(new InsertNode(At(Element("r")), index, new NewXmlElement("x", [], null))).Document.Source.Text;

        Assert.Equal("<r>\n  <x/>\n  <!--lead-->\n  <a/>\n</r>", text);
    }

    [Fact]
    public void A_path_with_no_node_is_refused_as_an_argument_error()
    {
        ConfigDocument document = ModelTestSupport.Parse("<r/>", Xml);

        Assert.Throws<ArgumentException>(() => document.InsertionIndexBefore(At(Element("zz"))));
        Assert.Throws<ArgumentException>(() => document.InsertionIndexAfter(At(Element("zz"))));
    }

    [Fact]
    public void The_document_node_has_no_insertion_index()
    {
        ConfigDocument document = ModelTestSupport.Parse("<r/>", Xml);

        Assert.Throws<ArgumentException>(() => document.InsertionIndexBefore(NodePath.Empty));
    }
}
