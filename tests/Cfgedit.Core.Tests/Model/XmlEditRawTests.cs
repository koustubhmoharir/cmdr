using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Raw-mode parts (source text with a custom entity reference), the logical wildcard that verifies them, and the reparse errors
/// that are the user's rather than cfgedit's.
/// </summary>
public sealed class XmlEditRawTests
{
    // The field shadows the Xml namespace of this test assembly, which the static import would otherwise lose to.
    private static readonly IConfigFormat Xml = EditTestSupport.Xml;
    private const string Doctype = "<!DOCTYPE r [<!ENTITY e \"v\">]>\n";

    [Fact]
    public void A_raw_text_part_takes_the_user_source_as_is()
    {
        string text = Edit(Doctype + "<r>a &e;</r>", Xml, new SetPart(At(Element("r")), PartKind.Text, -1, "b &e;"));

        Assert.Equal(Doctype + "<r>b &e;</r>", text);
    }

    [Fact]
    public void A_raw_text_part_that_is_not_well_formed_is_refused_as_the_users_error()
    {
        CfgeditException ex = Fails(Doctype + "<r>a &e;</r>", Xml, new SetPart(At(Element("r")), PartKind.Text, -1, "b &nope;"));

        Assert.Equal("this change would make the document invalid: Reference to undeclared entity 'nope'.", ex.Message);
        Assert.Equal(2, ex.Line);
        // The column is where XmlReader reports the reference: the entity name after the ampersand.
        Assert.Equal(7, ex.Column);
    }

    [Fact]
    public void A_raw_text_part_with_a_less_than_sign_is_refused_because_it_would_be_markup()
    {
        CfgeditException ex = Fails(Doctype + "<r>a &e;</r>", Xml, new SetPart(At(Element("r")), PartKind.Text, -1, "<x/>"));

        Assert.Equal("text cannot contain '<'; markup is not text", ex.Message);
    }

    [Fact]
    public void A_raw_text_node_in_mixed_content_with_markup_is_refused_for_the_same_reason()
    {
        CfgeditException ex = Fails(Doctype + "<r>a &e;<b/></r>", Xml, new SetPart(At(Element("r"), new TextStep(0)), PartKind.Text, -1, "x<y/>"));

        Assert.Equal("text cannot contain '<'; markup is not text", ex.Message);
    }

    [Fact]
    public void A_raw_attribute_value_takes_the_user_source_as_is()
    {
        string text = Edit(Doctype + "<r a=\"&e;\"/>", Xml, new SetPart(At(Element("r")), PartKind.AttributeValue, 0, "x &e;"));

        Assert.Equal(Doctype + "<r a=\"x &e;\"/>", text);
    }

    [Fact]
    public void A_raw_attribute_value_that_closes_its_quote_is_refused_as_the_users_error()
    {
        CfgeditException ex = Fails(Doctype + "<r a=\"&e;\"/>", Xml, new SetPart(At(Element("r")), PartKind.AttributeValue, 0, "x\" y"));

        Assert.Equal("this change would make the document invalid: '\"' is an unexpected token. The expected token is '='.", ex.Message);
    }

    [Fact]
    public void A_raw_text_node_in_mixed_content_that_is_only_whitespace_is_refused_because_it_would_be_formatting()
    {
        CfgeditException ex = Fails(Doctype + "<r>a &e;<b/></r>", Xml, new SetPart(At(Element("r"), new TextStep(0)), PartKind.Text, -1, " "));

        Assert.Equal("text cannot be only whitespace; delete it instead", ex.Message);
    }

    [Fact]
    public void A_raw_text_part_of_a_text_only_element_may_be_whitespace()
    {
        string text = Edit(Doctype + "<r>a &e;</r>", Xml, new SetPart(At(Element("r")), PartKind.Text, -1, " "));

        Assert.Equal(Doctype + "<r> </r>", text);
    }

    [Fact]
    public void An_empty_raw_text_part_removes_the_text_as_any_text_does()
    {
        string text = Edit(Doctype + "<r>a &e;</r>", Xml, new SetPart(At(Element("r")), PartKind.Text, -1, string.Empty));

        Assert.Equal(Doctype + "<r></r>", text);
    }

    [Fact]
    public void A_reparse_error_is_the_users_for_XML_and_never_for_JSON()
    {
        var error = new CfgeditException("reference to an undeclared entity");

        Assert.True(Xml.IsUserError(error));
        Assert.False(Strict.IsUserError(error));
    }

    [Fact]
    public void An_internal_parse_mismatch_is_cfgedits_bug_and_not_the_users()
    {
        var error = new CfgeditException("internal parse mismatch: element name (please report this)");

        Assert.False(Xml.IsUserError(error));
    }

    [Fact]
    public void A_wildcard_value_matches_any_value_but_not_another_kind_or_name()
    {
        var wildcard = new LogicalNode(LogicalKind.Text, null, null, "anything", [], [], anyValue: true);

        Assert.True(LogicalNode.Matches(wildcard, new LogicalNode(LogicalKind.Text, null, null, "other", [], [])));
        Assert.False(LogicalNode.Matches(wildcard, new LogicalNode(LogicalKind.CData, null, null, "other", [], [])));
        Assert.False(LogicalNode.Matches(
            new LogicalNode(LogicalKind.Element, "a", null, null, [], [wildcard]),
            new LogicalNode(LogicalKind.Element, "b", null, null, [], [new LogicalNode(LogicalKind.Text, null, null, "x", [], [])])));
    }

    [Fact]
    public void A_wildcard_attribute_value_matches_any_value_and_equality_stays_exact()
    {
        var expected = new LogicalNode(LogicalKind.Element, "r", null, null, [new LogicalAttribute("a", "x", AnyValue: true)], []);
        var actual = new LogicalNode(LogicalKind.Element, "r", null, null, [new LogicalAttribute("a", "y")], []);

        Assert.True(LogicalNode.Matches(expected, actual));
        Assert.False(expected.Equals(actual));
    }

    [Fact]
    public void Without_a_wildcard_matches_is_the_same_as_equality()
    {
        var one = new LogicalNode(LogicalKind.Text, null, null, "x", [], []);
        var two = new LogicalNode(LogicalKind.Text, null, null, "y", [], []);

        Assert.Equal(one.Equals(two), LogicalNode.Matches(one, two));
        Assert.Equal(one.Equals(one), LogicalNode.Matches(one, one));
    }
}
