using Cfgedit.Core.Model;
using Cfgedit.Tui;

namespace Cfgedit.Tests;

public sealed class PathTextTests
{
    private const string Configuration =
        "<configuration><appSettings><add key=\"a\"/><add key=\"b\"/></appSettings><system.web debug=\"x\"/></configuration>";

    [Fact]
    public void Json_paths_use_dots_for_identifiers_and_quotes_otherwise()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"a b\": {\"c\": [1, 2]}, \"x\": 1}", "json");

        Assert.Equal("$", PathText.Format(doc, doc.Root.Children[0], null));
        Assert.Equal("$[\"a b\"]", Path(doc, new ItemStep(0), new MemberStep("a b", 0)));
        Assert.Equal("$[\"a b\"].c", Path(doc, new ItemStep(0), new MemberStep("a b", 0), new MemberStep("c", 0)));
        Assert.Equal("$[\"a b\"].c[1]", Path(doc, new ItemStep(0), new MemberStep("a b", 0), new MemberStep("c", 0), new ItemStep(1)));
        Assert.Equal("$.x", Path(doc, new ItemStep(0), new MemberStep("x", 0)));
    }

    [Fact]
    public void Json_duplicate_keys_after_the_first_carry_their_one_based_occurrence()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"port\": 1, \"port\": 2, \"a b\": 3, \"a b\": 4}", "json");

        Assert.Equal("$.port", Path(doc, new ItemStep(0), new MemberStep("port", 0)));
        Assert.Equal("$.port#2", Path(doc, new ItemStep(0), new MemberStep("port", 1)));
        Assert.Equal("$[\"a b\"]#2", Path(doc, new ItemStep(0), new MemberStep("a b", 1)));
    }

    [Fact]
    public void Json_key_with_a_quote_inside_is_escaped()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"a\\\"b\": 1}", "json");

        Assert.Equal("$[\"a\\\"b\"]", Path(doc, new ItemStep(0), new MemberStep("a\"b", 0)));
    }

    [Fact]
    public void Json_identifier_may_start_with_dollar_and_contain_underscores_and_digits()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"$_x1\": 1}", "json");

        Assert.Equal("$.$_x1", Path(doc, new ItemStep(0), new MemberStep("$_x1", 0)));
    }

    [Fact]
    public void Json_key_starting_with_a_digit_or_empty_is_quoted()
    {
        ConfigDocument digit = TreeFixtures.Parse("{\"1a\": 1}", "json");
        ConfigDocument empty = TreeFixtures.Parse("{\"\": 1}", "json");

        Assert.Equal("$[\"1a\"]", Path(digit, new ItemStep(0), new MemberStep("1a", 0)));
        Assert.Equal("$[\"\"]", Path(empty, new ItemStep(0), new MemberStep("", 0)));
    }

    [Fact]
    public void Json_key_with_a_backslash_is_escaped()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"a\\\\b\": 1}", "json");

        Assert.Equal("$[\"a\\\\b\"]", Path(doc, new ItemStep(0), new MemberStep("a\\b", 0)));
    }

    [Fact]
    public void Json_key_with_a_control_character_uses_a_unicode_escape()
    {
        // The JSON text holds the escape \u0001, written without a \u sequence in the source.
        ConfigDocument doc = TreeFixtures.Parse("{\"\\" + "u0001\": 1}", "json");

        Assert.Equal("$[\"\\" + "u0001\"]", Path(doc, new ItemStep(0), new MemberStep("\x0001", 0)));
    }

    [Fact]
    public void Json_comments_are_numbered_per_parent_from_one()
    {
        ConfigDocument root = TreeFixtures.Parse("// c\n{}", "jsonc");
        ConfigDocument inner = TreeFixtures.Parse("{ // one\n \"a\": 1, // two\n \"b\": 2 }", "jsonc");

        Assert.Equal("$/#comment[1]", Path(root, new CommentStep(0)));
        Assert.Equal("$/#comment[1]", Path(inner, new ItemStep(0), new CommentStep(0)));
        Assert.Equal("$/#comment[2]", Path(inner, new ItemStep(0), new CommentStep(1)));
    }

    [Fact]
    public void Xml_paths_use_element_names_and_ordinals_only_for_same_name_siblings()
    {
        ConfigDocument doc = TreeFixtures.Parse(Configuration, "xml");

        Assert.Equal("/", PathText.Format(doc, doc.Root, null));
        Assert.Equal("/configuration", XPath(doc, new ElementStep("configuration", 0)));
        Assert.Equal("/configuration/appSettings", XPath(doc, new ElementStep("configuration", 0), new ElementStep("appSettings", 0)));
        Assert.Equal(
            "/configuration/appSettings/add[1]",
            XPath(doc, new ElementStep("configuration", 0), new ElementStep("appSettings", 0), new ElementStep("add", 0)));
        Assert.Equal(
            "/configuration/appSettings/add[2]",
            XPath(doc, new ElementStep("configuration", 0), new ElementStep("appSettings", 0), new ElementStep("add", 1)));
        Assert.Equal("/configuration/system.web", XPath(doc, new ElementStep("configuration", 0), new ElementStep("system.web", 0)));
    }

    [Fact]
    public void Xml_attribute_part_appends_the_attribute_name()
    {
        ConfigDocument doc = TreeFixtures.Parse(Configuration, "xml");
        DocNode add = TreeFixtures.Find(
            doc,
            new ElementStep("configuration", 0),
            new ElementStep("appSettings", 0),
            new ElementStep("add", 1));
        DocPart value = add.Parts.Single(part => part.Kind == PartKind.AttributeValue);
        DocPart name = add.Parts.Single(part => part.Kind == PartKind.AttributeName);

        Assert.Equal("/configuration/appSettings/add[2]/@key", PathText.Format(doc, add, value));
        Assert.Equal("/configuration/appSettings/add[2]/@key", PathText.Format(doc, add, name));
        Assert.Equal("/configuration/appSettings/add[2]", PathText.Format(doc, add, null));
    }

    [Fact]
    public void Xml_element_ordinals_follow_the_order_of_same_name_siblings()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r><a/><b/><a/></r>", "xml");

        Assert.Equal("/r/a[1]", XPath(doc, new ElementStep("r", 0), new ElementStep("a", 0)));
        Assert.Equal("/r/a[2]", XPath(doc, new ElementStep("r", 0), new ElementStep("a", 1)));
        Assert.Equal("/r/b", XPath(doc, new ElementStep("r", 0), new ElementStep("b", 0)));
    }

    [Fact]
    public void Xml_text_and_cdata_nodes_use_text_with_a_one_based_index()
    {
        ConfigDocument mixed = TreeFixtures.Parse("<r>t<x/>u</r>", "xml");
        ConfigDocument cdata = TreeFixtures.Parse("<r><![CDATA[x]]></r>", "xml");

        Assert.Equal("/r/text()[1]", XPath(mixed, new ElementStep("r", 0), new TextStep(0)));
        Assert.Equal("/r/text()[2]", XPath(mixed, new ElementStep("r", 0), new TextStep(1)));
        Assert.Equal("/r/text()[1]", XPath(cdata, new ElementStep("r", 0), new TextStep(0)));
    }

    [Fact]
    public void Xml_comments_are_numbered_among_the_parent_comments()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r><!--a--><x/><!--b--></r>", "xml");

        Assert.Equal("/r/comment()[2]", XPath(doc, new ElementStep("r", 0), new CommentStep(1)));
    }

    [Fact]
    public void Xml_processing_instruction_names_its_target()
    {
        ConfigDocument doc = TreeFixtures.Parse("<?p d?><r/>", "xml");

        Assert.Equal("/processing-instruction(p)", XPath(doc, new PIStep(0)));
    }

    [Fact]
    public void Xml_declaration_and_doctype_have_their_own_steps()
    {
        ConfigDocument doc = TreeFixtures.Parse("<?xml version=\"1.0\"?><!DOCTYPE r><r/>", "xml");

        Assert.Equal("/?xml", XPath(doc, new DeclarationStep()));
        Assert.Equal("/!DOCTYPE", XPath(doc, new DoctypeStep()));
    }

    [Fact]
    public void Xml_text_part_of_a_text_only_element_is_the_element_path_with_kind_text()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r>v</r>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("r", 0));
        DocPart text = element.Parts.Single(part => part.Kind == PartKind.Text);

        Assert.Equal("/r", PathText.Format(doc, element, text));
        Assert.Equal("text", PathText.KindName(element, text));
    }

    [Fact]
    public void Xml_end_name_part_formats_like_its_element()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r><c/></r>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("r", 0));
        DocPart endName = element.EndParts.Single();

        Assert.Equal("/r", PathText.Format(doc, element, endName));
        Assert.Equal("element", PathText.KindName(element, endName));
    }

    [Fact]
    public void Kind_names_for_json_nodes_and_parts()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"s\": \"x\", \"n\": 1, \"t\": true, \"z\": null, \"o\": {}, \"c\": []}", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        Assert.Equal("object", PathText.KindName(root, null));
        Assert.Equal("string", PathText.KindName(Member(doc, "s"), Member(doc, "s").Parts[1]));
        Assert.Equal("number", PathText.KindName(Member(doc, "n"), null));
        Assert.Equal("bool", PathText.KindName(Member(doc, "t"), null));
        Assert.Equal("null", PathText.KindName(Member(doc, "z"), null));
        Assert.Equal("object", PathText.KindName(Member(doc, "o"), null));
        Assert.Equal("array", PathText.KindName(Member(doc, "c"), null));
        Assert.Equal("key", PathText.KindName(Member(doc, "s"), Member(doc, "s").Parts[0]));
        Assert.Equal("object", PathText.KindName(Member(doc, "o"), Member(doc, "o").Parts[1]));
    }

    [Fact]
    public void Kind_names_for_json_items_and_comments()
    {
        ConfigDocument doc = TreeFixtures.Parse("// c\n[7]", "jsonc");
        DocNode item = TreeFixtures.Find(doc, new ItemStep(0), new ItemStep(0));
        DocNode comment = TreeFixtures.Find(doc, new CommentStep(0));

        Assert.Equal("index", PathText.KindName(item, item.Parts[0]));
        Assert.Equal("number", PathText.KindName(item, item.Parts[1]));
        Assert.Equal("comment", PathText.KindName(comment, null));
    }

    [Fact]
    public void Kind_names_for_xml_nodes_and_parts()
    {
        ConfigDocument doc = TreeFixtures.Parse(
            "<?xml version=\"1.0\"?><!DOCTYPE r><r a=\"1\"><![CDATA[x]]><!--c--><?p d?>t</r>",
            "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("r", 0));

        Assert.Equal("element", PathText.KindName(element, null));
        Assert.Equal("attribute", PathText.KindName(element, element.Parts[1]));
        Assert.Equal("attribute", PathText.KindName(element, element.Parts[2]));
        Assert.Equal("CDATA", PathText.KindName(TreeFixtures.Find(doc, new ElementStep("r", 0), new TextStep(0)), null));
        Assert.Equal("comment", PathText.KindName(TreeFixtures.Find(doc, new ElementStep("r", 0), new CommentStep(0)), null));
        Assert.Equal("PI", PathText.KindName(TreeFixtures.Find(doc, new ElementStep("r", 0), new PIStep(0)), null));
        Assert.Equal("text", PathText.KindName(TreeFixtures.Find(doc, new ElementStep("r", 0), new TextStep(1)), null));
        Assert.Equal("declaration", PathText.KindName(TreeFixtures.Find(doc, new DeclarationStep()), null));
        Assert.Equal("doctype", PathText.KindName(TreeFixtures.Find(doc, new DoctypeStep()), null));
    }

    private static DocNode Member(ConfigDocument doc, string key)
    {
        return TreeFixtures.Find(doc, new ItemStep(0), new MemberStep(key, 0));
    }

    private static string Path(ConfigDocument doc, params PathStep[] steps)
    {
        return PathText.Format(doc, TreeFixtures.Find(doc, steps), null);
    }

    private static string XPath(ConfigDocument doc, params PathStep[] steps)
    {
        return PathText.Format(doc, TreeFixtures.Find(doc, steps), null);
    }
}
