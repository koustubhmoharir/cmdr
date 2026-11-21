using Cfgedit.Core.Model;

namespace Cfgedit.Core.Tests.Model;

/// <summary>The logical tree: documents that differ only in layout are equal, and a change of content is not.</summary>
public sealed class LogicalNodeTests
{
    private static readonly IConfigFormat StrictJson = Formats.FromName("json")!;
    private static readonly IConfigFormat Jsonc = Formats.FromName("jsonc")!;
    private static readonly IConfigFormat Json5 = Formats.FromName("json5")!;
    private static readonly IConfigFormat Xml = Formats.FromName("xml")!;

    private static LogicalNode Logical(string text, IConfigFormat format)
    {
        ConfigDocument document = ModelTestSupport.Parse(text, format);
        return format.ToLogical(document);
    }

    // ----- Equal when only the layout differs -----

    [Fact]
    public void Re_indented_json_is_equal()
    {
        LogicalNode compact = Logical("{\"a\":{\"b\":[1,2]},\"c\":\"x\"}", StrictJson);
        LogicalNode layout = Logical("{\n  \"a\": {\n    \"b\": [\n      1,\n      2\n    ]\n  },\n  \"c\": \"x\"\n}", StrictJson);

        Assert.Equal(compact, layout);
        Assert.Equal(compact.GetHashCode(), layout.GetHashCode());
    }

    [Fact]
    public void Json5_quoting_and_trailing_commas_do_not_change_the_logical_tree()
    {
        LogicalNode plain = Logical("{\"a\": \"x\", \"b\": 1}", Json5);
        LogicalNode json5 = Logical("{ a: 'x', b: 1, }", Json5);

        Assert.Equal(plain, json5);
    }

    [Fact]
    public void A_self_closing_element_equals_an_empty_pair()
    {
        LogicalNode self = Logical("<r><a/></r>", Xml);
        LogicalNode pair = Logical("<r><a></a></r>", Xml);

        Assert.Equal(self, pair);
        Assert.Equal(self.GetHashCode(), pair.GetHashCode());
    }

    [Fact]
    public void An_empty_container_written_across_lines_equals_a_self_closing_element()
    {
        LogicalNode self = Logical("<r><a/></r>", Xml);
        LogicalNode lines = Logical("<r><a>\n  </a></r>", Xml);

        Assert.Equal(self, lines);
        Assert.Equal(self.GetHashCode(), lines.GetHashCode());
    }

    [Fact]
    public void Text_is_compared_by_its_decoded_value()
    {
        LogicalNode plain = Logical("<r><a>x&amp;y</a></r>", Xml);
        LogicalNode written = Logical("<r><a>x&#38;y</a></r>", Xml);

        Assert.Equal(plain, written);
    }

    [Fact]
    public void Attributes_split_across_lines_are_equal_to_one_line()
    {
        LogicalNode one = Logical("<e a=\"1\" b=\"2\"/>", Xml);
        LogicalNode lines = Logical("<e a=\"1\"\n   b=\"2\"/>", Xml);

        Assert.Equal(one, lines);
    }

    [Fact]
    public void Whitespace_between_elements_is_not_logical_content()
    {
        LogicalNode compact = Logical("<r><a/><b>x</b></r>", Xml);
        LogicalNode indented = Logical("<r>\n  <a/>\n  <b>x</b>\n</r>\n", Xml);

        Assert.Equal(compact, indented);
    }

    // ----- Different when the content differs -----

    [Theory]
    [InlineData("{\"a\": \"x\"}", "{\"a\": \"y\"}")]
    [InlineData("{\"a\": 1}", "{\"b\": 1}")]
    [InlineData("{\"a\": 1, \"b\": 2}", "{\"b\": 2, \"a\": 1}")]
    [InlineData("{\"a\": 1}", "{\"a\": 1.0}")]
    [InlineData("[1, 2]", "[1, 2, 3]")]
    [InlineData("{\"a\": 1}", "{\"a\": 1 // one\n}")]
    [InlineData("{\"a\": 1 // one\n}", "{\"a\": 1 // two\n}")]
    public void Json_values_keys_order_numbers_and_comments_all_count(string first, string second)
    {
        LogicalNode a = Logical(first, Jsonc);
        LogicalNode b = Logical(second, Jsonc);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Json_numbers_compare_by_their_spelling()
    {
        Assert.NotEqual(Logical("[1.0]", StrictJson), Logical("[1]", StrictJson));
    }

    [Theory]
    [InlineData("<r><a>x</a></r>", "<r><a>y</a></r>")]
    [InlineData("<r><a/></r>", "<r><b/></r>")]
    [InlineData("<e a=\"1\" b=\"2\"/>", "<e b=\"2\" a=\"1\"/>")]
    [InlineData("<e a=\"1\"/>", "<e a=\"2\"/>")]
    [InlineData("<r><!--x--></r>", "<r><!--y--></r>")]
    [InlineData("<r><a/></r>", "<r><a/><a/></r>")]
    public void Xml_names_text_attribute_order_and_comments_all_count(string first, string second)
    {
        LogicalNode a = Logical(first, Xml);
        LogicalNode b = Logical(second, Xml);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void A_text_only_element_with_a_space_is_not_equal_to_an_empty_one()
    {
        // The space is the element's text, so the text child differs.
        Assert.NotEqual(Logical("<r><a/></r>", Xml), Logical("<r><a> </a></r>", Xml));
    }

    [Fact]
    public void Cdata_and_plain_text_are_different_logical_kinds()
    {
        // A decision: the logical tree keeps CDATA as its own kind, so an edit that changes how text is written is visible.
        Assert.NotEqual(Logical("<r><![CDATA[x]]></r>", Xml), Logical("<r>x</r>", Xml));
    }

    // ----- Shape and text -----

    [Fact]
    public void The_dump_of_a_json_tree_is_readable()
    {
        LogicalNode node = Logical("{\"a\": [1]}", StrictJson);

        string expected = """
            Document
              Object
                Array name="a"
                  Scalar scalar=Number value="1"
            """;
        Assert.Equal(expected, node.ToString());
    }

    [Fact]
    public void The_dump_of_an_xml_tree_shows_names_attributes_and_values()
    {
        LogicalNode node = Logical("<r k=\"v\"><a>x</a></r>", Xml);

        string expected = """
            Document
              Element name="r" @k="v"
                Element name="a"
                  Text value="x"
            """;
        Assert.Equal(expected, node.ToString());
    }

    [Fact]
    public void Equals_is_false_for_null_and_for_other_types()
    {
        LogicalNode node = Logical("[1]", StrictJson);

        Assert.False(node.Equals(null));
        Assert.True(node.Equals((object)Logical("[1]", StrictJson)));
        Assert.False(node.Equals("[1]"));
    }

    [Fact]
    public void A_document_with_a_declaration_and_doctype_keeps_them_in_the_logical_tree()
    {
        LogicalNode node = Logical("<?xml version=\"1.0\" standalone=\"yes\"?><!DOCTYPE r><r/>", Xml);

        string expected = """
            Document
              Declaration @version="1.0" @standalone="yes"
              Doctype name="r"
              Element name="r"
            """;
        Assert.Equal(expected, node.ToString());
    }
}
