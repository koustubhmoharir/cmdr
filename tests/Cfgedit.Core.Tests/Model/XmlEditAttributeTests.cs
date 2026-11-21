using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>Inserting, deleting and moving attributes, with their layout and their namespace declarations.</summary>
public sealed class XmlEditAttributeTests
{
    // The field shadows the Xml namespace of this test assembly, which the static import would otherwise lose to.
    private static readonly IConfigFormat Xml = EditTestSupport.Xml;
    private static readonly NodePath R = At(Element("r"));

    [Fact]
    public void An_attribute_is_inserted_at_its_index_after_one_space()
    {
        Assert.Equal("<r a=\"1\" b=\"2\"/>", Edit("<r a=\"1\"/>", Xml, new InsertAttribute(R, 1, "b", "2")));
    }

    [Fact]
    public void An_attribute_inserted_at_the_front_takes_the_gap_of_the_attribute_it_precedes()
    {
        Assert.Equal("<r b=\"2\" a=\"1\"/>", Edit("<r a=\"1\"/>", Xml, new InsertAttribute(R, 0, "b", "2")));
    }

    [Fact]
    public void The_first_attribute_of_an_element_goes_after_its_name_with_one_space()
    {
        Assert.Equal("<r a=\"1\"/>", Edit("<r/>", Xml, new InsertAttribute(R, 0, "a", "1")));
        Assert.Equal("<r a=\"1\" />", Edit("<r />", Xml, new InsertAttribute(R, 0, "a", "1")));
    }

    [Fact]
    public void A_new_attribute_on_a_line_of_its_own_is_aligned_with_the_others()
    {
        string text = Edit("<r\n  a=\"1\"\n  b=\"2\"/>", Xml, new InsertAttribute(R, 2, "c", "3"));

        Assert.Equal("<r\n  a=\"1\"\n  b=\"2\"\n  c=\"3\"/>", text);
    }

    [Fact]
    public void A_new_attribute_at_the_front_of_aligned_attributes_takes_their_gap()
    {
        string text = Edit("<r\n  a=\"1\"/>", Xml, new InsertAttribute(R, 0, "b", "2"));

        Assert.Equal("<r\n  b=\"2\"\n  a=\"1\"/>", text);
    }

    [Fact]
    public void Alignment_follows_the_column_of_an_attribute_that_is_not_at_the_start_of_its_line()
    {
        string text = Edit("<item a=\"1\"\n     b=\"2\"/>", Xml, new InsertAttribute(At(Element("item")), 2, "c", "3"));

        Assert.Equal("<item a=\"1\"\n     b=\"2\"\n     c=\"3\"/>", text);
    }

    [Fact]
    public void A_new_attribute_quote_is_the_quote_of_the_elements_first_attribute()
    {
        string text = Edit("<r a='1'/>", Xml, new InsertAttribute(R, 1, "b", "x\"y"));

        Assert.Equal("<r a='1' b='x\"y'/>", text);
    }

    [Fact]
    public void A_new_attribute_quote_is_the_document_majority_when_the_element_has_none()
    {
        string text = Edit("<r><a x='1'/></r>", Xml, new InsertAttribute(R, 0, "y", "2"));

        Assert.Equal("<r y='2'><a x='1'/></r>", text);
    }

    [Fact]
    public void Deleting_an_attribute_on_its_own_line_removes_the_whole_line()
    {
        string text = Edit("<r\n  a=\"1\"\n  b=\"2\"\n/>", Xml, new DeleteAttribute(R, 0));

        Assert.Equal("<r\n  b=\"2\"\n/>", text);
    }

    [Fact]
    public void Deleting_the_last_attribute_removes_the_whitespace_before_it()
    {
        string text = Edit("<r\n  a=\"1\"\n  b=\"2\"/>", Xml, new DeleteAttribute(R, 1));

        Assert.Equal("<r\n  a=\"1\"/>", text);
    }

    [Fact]
    public void Deleting_the_first_attribute_on_a_line_removes_the_space_after_the_name()
    {
        string text = Edit("<r a=\"1\" b=\"2\"/>", Xml, new DeleteAttribute(R, 0));

        Assert.Equal("<r b=\"2\"/>", text);
    }

    [Fact]
    public void Deleting_a_middle_attribute_removes_it_and_the_space_before_it()
    {
        string text = Edit("<r a=\"1\" b=\"2\" c=\"3\"/>", Xml, new DeleteAttribute(R, 1));

        Assert.Equal("<r a=\"1\" c=\"3\"/>", text);
    }

    [Fact]
    public void Moving_an_attribute_swaps_text_and_keeps_the_whitespace_of_each_position()
    {
        string text = Edit("<r a=\"1\" b=\"2\" c=\"3\"/>", Xml, new MoveAttribute(R, 0, 2));

        Assert.Equal("<r b=\"2\" c=\"3\" a=\"1\"/>", text);
    }

    [Fact]
    public void Moving_an_attribute_on_separate_lines_keeps_the_layout()
    {
        string text = Edit("<r\n  a=\"1\"\n  b=\"2\"/>", Xml, new MoveAttribute(R, 0, 1));

        Assert.Equal("<r\n  b=\"2\"\n  a=\"1\"/>", text);
    }

    [Fact]
    public void Moving_an_attribute_to_its_own_position_changes_nothing()
    {
        const string source = "<r a=\"1\" b=\"2\"/>";

        Assert.Equal(source, Edit(source, Xml, new MoveAttribute(R, 0, 0)));
    }

    [Fact]
    public void A_new_index_past_the_end_is_clamped_for_an_attribute()
    {
        string text = Edit("<r a=\"1\" b=\"2\" c=\"3\"/>", Xml, new MoveAttribute(R, 0, 99));

        Assert.Equal("<r b=\"2\" c=\"3\" a=\"1\"/>", text);
    }

    [Fact]
    public void A_duplicate_attribute_name_is_refused()
    {
        CfgeditException ex = Fails("<r a=\"1\"/>", Xml, new InsertAttribute(R, 0, "a", "2"));

        Assert.Equal("an attribute named 'a' already exists", ex.Message);
    }

    [Fact]
    public void An_invalid_attribute_name_is_refused()
    {
        CfgeditException ex = Fails("<r/>", Xml, new InsertAttribute(R, 0, "1a", "2"));

        Assert.Equal("'1a' is not a valid XML name", ex.Message);
    }

    [Fact]
    public void An_attribute_value_with_a_character_xml_cannot_hold_is_refused()
    {
        CfgeditException ex = Fails("<r/>", Xml, new InsertAttribute(R, 0, "a", "x" + (char)1));

        Assert.Equal("the value contains U+0001, which XML 1.0 cannot hold", ex.Message);
    }

    [Fact]
    public void Attributes_belong_to_elements_only()
    {
        CfgeditException insert = Fails("<r>a<b/></r>", Xml, new InsertAttribute(At(Element("r"), new TextStep(0)), 0, "a", "1"));
        CfgeditException delete = Fails("<r>a<b/></r>", Xml, new DeleteAttribute(At(Element("r"), new TextStep(0)), 0));

        Assert.Equal("Only an element has attributes", insert.Message);
        Assert.Equal("Only an element has attributes", delete.Message);
    }

    [Fact]
    public void An_attribute_index_out_of_range_is_refused()
    {
        CfgeditException delete = Fails("<r a=\"1\"/>", Xml, new DeleteAttribute(R, 3));
        CfgeditException insert = Fails("<r a=\"1\"/>", Xml, new InsertAttribute(R, 2, "b", "2"));

        Assert.Equal("There is no attribute at index 3", delete.Message);
        Assert.Equal("Index 2 is outside the attributes, 0 to 1", insert.Message);
    }

    [Fact]
    public void An_attribute_with_an_undeclared_prefix_is_refused()
    {
        CfgeditException ex = Fails("<r/>", Xml, new InsertAttribute(R, 0, "q:a", "1"));

        Assert.Equal("the namespace prefix 'q' is not declared", ex.Message);
    }

    [Fact]
    public void An_attribute_may_use_a_prefix_the_element_itself_declares()
    {
        string text = Edit("<r xmlns:p=\"u\"/>", Xml, new InsertAttribute(R, 0, "p:a", "1"));

        Assert.Equal("<r p:a=\"1\" xmlns:p=\"u\"/>", text);
    }

    [Fact]
    public void Deleting_a_namespace_declaration_still_in_use_is_refused()
    {
        CfgeditException ex = Fails("<r xmlns:p=\"u\"><p:a/></r>", Xml, new DeleteAttribute(R, 0));

        Assert.Equal("the prefix 'p' is still used in its scope", ex.Message);
    }

    [Fact]
    public void Deleting_a_namespace_declaration_that_is_not_used_is_allowed()
    {
        string text = Edit("<r xmlns:p=\"u\"><a/></r>", Xml, new DeleteAttribute(R, 0));

        Assert.Equal("<r><a/></r>", text);
    }

    [Fact]
    public void A_descendant_that_declares_the_prefix_again_does_not_count_as_a_use()
    {
        string text = Edit("<r xmlns:p=\"u\"><s xmlns:p=\"v\"><p:a/></s></r>", Xml, new DeleteAttribute(R, 0));

        Assert.Equal("<r><s xmlns:p=\"v\"><p:a/></s></r>", text);
    }

    [Fact]
    public void Renaming_a_namespace_declaration_still_in_use_is_refused()
    {
        CfgeditException ex = Fails("<r xmlns:p=\"u\"><p:a/></r>", Xml, new SetPart(R, PartKind.AttributeName, 0, "xmlns:q"));

        Assert.Equal("the prefix 'p' is still used in its scope", ex.Message);
    }

    [Fact]
    public void Renaming_a_namespace_declaration_that_is_not_used_is_allowed()
    {
        string text = Edit("<r xmlns:p=\"u\"><a/></r>", Xml, new SetPart(R, PartKind.AttributeName, 0, "xmlns:q"));

        Assert.Equal("<r xmlns:q=\"u\"><a/></r>", text);
    }

    [Fact]
    public void An_attribute_renamed_to_an_existing_name_is_refused()
    {
        CfgeditException ex = Fails("<r a=\"1\" b=\"2\"/>", Xml, new SetPart(R, PartKind.AttributeName, 1, "a"));

        Assert.Equal("an attribute named 'a' already exists", ex.Message);
    }
}
