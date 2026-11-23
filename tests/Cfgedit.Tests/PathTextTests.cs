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
    public void The_root_marker_has_the_root_path_and_the_kind_root()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"x\": 1}", "json");
        DocNode root = doc.Root.Children[0];

        Assert.Equal("$", PathText.Format(doc, root, root.Parts[0]));
        Assert.Equal("root", PathText.KindName(root, root.Parts[0]));
        Assert.Equal("object", PathText.KindName(root, root.Parts[1]));
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
    public void Xml_empty_element_summary_is_the_element_path_with_kind_element()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r><c/></r>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("r", 0), new ElementStep("c", 0));
        DocPart summary = element.Parts.Single(part => part.Kind == PartKind.Summary);

        Assert.Equal("/r/c", PathText.Format(doc, element, summary));
        Assert.Equal("element", PathText.KindName(element, summary));
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

    // ----- YAML -----

    [Fact]
    public void Yaml_paths_are_dotted_keys_with_brackets_for_items_and_dollar_for_the_root()
    {
        ConfigDocument doc = TreeFixtures.Parse("servers:\n  - port: 80\n  - port: 81\nname: web\n", "yaml");

        Assert.Equal("$", Path(doc, new ItemStep(0)));
        Assert.Equal("servers", Path(doc, new ItemStep(0), new MemberStep("servers", 0)));
        Assert.Equal("servers[1]", Path(doc, new ItemStep(0), new MemberStep("servers", 0), new ItemStep(1)));
        Assert.Equal(
            "servers[1].port",
            Path(doc, new ItemStep(0), new MemberStep("servers", 0), new ItemStep(1), new MemberStep("port", 0)));
        Assert.Equal("name", Path(doc, new ItemStep(0), new MemberStep("name", 0)));
    }

    [Fact]
    public void Yaml_keys_that_are_not_plain_are_quoted_and_a_key_that_reads_as_a_number_or_boolean_is_quoted_too()
    {
        ConfigDocument doc = TreeFixtures.Parse("\"true\": 1\n\"1\": 2\n\"#x\": 3\nplain: 4\n", "yaml");

        Assert.Equal("\"true\"", Path(doc, new ItemStep(0), new MemberStep("true", 0)));
        Assert.Equal("\"1\"", Path(doc, new ItemStep(0), new MemberStep("1", 0)));
        Assert.Equal("\"#x\"", Path(doc, new ItemStep(0), new MemberStep("#x", 0)));
        Assert.Equal("plain", Path(doc, new ItemStep(0), new MemberStep("plain", 0)));
    }

    [Fact]
    public void Yaml_paths_of_a_multi_document_stream_start_at_the_document_marker()
    {
        ConfigDocument doc = TreeFixtures.Parse("---\na: 1\n---\nspec:\n  replicas: 2\n", "yaml");

        Assert.Equal("---[0]", Path(doc, new ItemStep(0)));
        Assert.Equal("---[1]", Path(doc, new ItemStep(1)));
        Assert.Equal("---[1].spec", Path(doc, new ItemStep(1), new ItemStep(0), new MemberStep("spec", 0)));
        Assert.Equal(
            "---[1].spec.replicas",
            Path(doc, new ItemStep(1), new ItemStep(0), new MemberStep("spec", 0), new MemberStep("replicas", 0)));
    }

    [Fact]
    public void Yaml_comment_paths_are_the_parents_path_and_a_comment_marker_as_in_json()
    {
        ConfigDocument doc = TreeFixtures.Parse("# c\na: 1\nt:\n  # d\n  x: 2\n", "yaml");

        Assert.Equal("$/#comment[1]", Path(doc, new ItemStep(0), new CommentStep(0)));
        Assert.Equal("t/#comment[1]", Path(doc, new ItemStep(0), new MemberStep("t", 0), new CommentStep(0)));
    }

    [Fact]
    public void Yaml_kinds_name_the_scalar_kinds_of_the_core_schema_and_the_structural_parts()
    {
        ConfigDocument doc = TreeFixtures.Parse("b: true\ni: 1\nf: 1.5\nn: ~\ns: x\nm:\n  k: 1\nq: [1]\nal: &a 1\nr: *a\n", "yaml");

        Assert.Equal("boolean", PathText.KindName(Member(doc, "b"), Member(doc, "b").Parts[1]));
        Assert.Equal("integer", PathText.KindName(Member(doc, "i"), Member(doc, "i").Parts[1]));
        Assert.Equal("float", PathText.KindName(Member(doc, "f"), Member(doc, "f").Parts[1]));
        Assert.Equal("null", PathText.KindName(Member(doc, "n"), Member(doc, "n").Parts[1]));
        Assert.Equal("string", PathText.KindName(Member(doc, "s"), Member(doc, "s").Parts[1]));
        Assert.Equal("mapping", PathText.KindName(Member(doc, "m"), null));
        Assert.Equal("sequence", PathText.KindName(Member(doc, "q"), null));
        Assert.Equal("mapping", PathText.KindName(Member(doc, "m"), Member(doc, "m").Parts[1]));
        Assert.Equal("sequence", PathText.KindName(Member(doc, "q"), Member(doc, "q").Parts[1]));
        Assert.Equal("key", PathText.KindName(Member(doc, "s"), Member(doc, "s").Parts[0]));
        Assert.Equal("anchor", PathText.KindName(Member(doc, "al"), Member(doc, "al").Parts[1]));
        Assert.Equal("alias", PathText.KindName(Member(doc, "r"), Member(doc, "r").Parts[1]));
        Assert.Equal("root", PathText.KindName(TreeFixtures.Find(doc, new ItemStep(0)), TreeFixtures.Find(doc, new ItemStep(0)).Parts[0]));
    }

    [Fact]
    public void Yaml_a_document_row_is_a_document_and_its_index_part_too()
    {
        ConfigDocument doc = TreeFixtures.Parse("---\na: 1\n---\nb: 2\n", "yaml");
        DocNode second = TreeFixtures.Find(doc, new ItemStep(1));

        Assert.Equal("document", PathText.KindName(second, null));
        Assert.Equal("document", PathText.KindName(second, second.Parts[0]));
    }

    private static DocNode Member(ConfigDocument doc, string key)
    {
        return TreeFixtures.Find(doc, new ItemStep(0), new MemberStep(key, 0));
    }

    [Fact]
    public void Toml_paths_are_dotted_keys_with_brackets_for_items_and_dollar_for_the_root()
    {
        ConfigDocument doc = TreeFixtures.Parse("a.b = 1\n[[servers]]\nport = 80\n[[servers]]\nport = 81\n[t]\n\"my.key\" = 2\n", "toml");

        Assert.Equal("$", Path(doc, new ItemStep(0)));
        Assert.Equal("a", Path(doc, new ItemStep(0), new MemberStep("a", 0)));
        Assert.Equal("a.b", Path(doc, new ItemStep(0), new MemberStep("a", 0), new MemberStep("b", 0)));
        Assert.Equal("servers", Path(doc, new ItemStep(0), new MemberStep("servers", 0)));
        Assert.Equal("servers[1]", Path(doc, new ItemStep(0), new MemberStep("servers", 0), new ItemStep(1)));
        Assert.Equal(
            "servers[1].port",
            Path(doc, new ItemStep(0), new MemberStep("servers", 0), new ItemStep(1), new MemberStep("port", 0)));
        Assert.Equal("t.\"my.key\"", Path(doc, new ItemStep(0), new MemberStep("t", 0), new MemberStep("my.key", 0)));
    }

    [Fact]
    public void Toml_comment_paths_are_the_parents_path_and_a_comment_marker_as_in_json()
    {
        ConfigDocument doc = TreeFixtures.Parse("# c\na = 1\n[t]\n# d\nx = 2\n", "toml");

        Assert.Equal("$/#comment[1]", Path(doc, new ItemStep(0), new CommentStep(0)));
        Assert.Equal("t/#comment[1]", Path(doc, new ItemStep(0), new MemberStep("t", 0), new CommentStep(0)));
    }

    [Fact]
    public void Toml_kinds_name_each_scalar_by_its_toml_kind_and_each_container_by_its_shape()
    {
        ConfigDocument doc = TreeFixtures.Parse(
            "s = \"x\"\ni = 1\nf = 1.5\nb = true\no = 1979-05-27T07:32:00Z\nl = 1979-05-27T07:32:00\nd = 1979-05-27\nt = 07:32:00\n"
            + "items = [1]\n[table]\nk = 1\n[[rows]]\nx = 1\n",
            "toml");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));
        DocNode items = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("items", 0));
        DocNode item = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("items", 0), new ItemStep(0));
        DocNode table = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("table", 0));
        DocNode rows = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("rows", 0));
        DocNode row = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("rows", 0), new ItemStep(0));

        Assert.Equal("table", PathText.KindName(root, null));
        Assert.Equal("root", PathText.KindName(root, root.Parts[0]));
        Assert.Equal("table", PathText.KindName(root, root.Parts[1]));
        Assert.Equal("table", PathText.KindName(table, null));
        Assert.Equal("key", PathText.KindName(table, table.Parts[0]));
        Assert.Equal("array", PathText.KindName(items, null));
        Assert.Equal("array", PathText.KindName(items, items.Parts[1]));
        Assert.Equal("index", PathText.KindName(item, item.Parts[0]));
        Assert.Equal("integer", PathText.KindName(item, item.Parts[1]));
        Assert.Equal("array", PathText.KindName(rows, null));
        Assert.Equal("table", PathText.KindName(row, null));
        Assert.Equal("string", PathText.KindName(TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("s", 0)), null));
        Assert.Equal("integer", PathText.KindName(TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("i", 0)), null));
        Assert.Equal("float", PathText.KindName(TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("f", 0)), null));
        Assert.Equal("boolean", PathText.KindName(TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("b", 0)), null));
        Assert.Equal("offset date-time", PathText.KindName(TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("o", 0)), null));
        Assert.Equal("local date-time", PathText.KindName(TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("l", 0)), null));
        Assert.Equal("local date", PathText.KindName(TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("d", 0)), null));
        Assert.Equal("local time", PathText.KindName(TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("t", 0)), null));
    }

    [Fact]
    public void Toml_comment_kind_is_comment_and_the_comment_part_is_comment()
    {
        ConfigDocument doc = TreeFixtures.Parse("# c\na = 1\n", "toml");
        DocNode comment = TreeFixtures.Find(doc, new ItemStep(0), new CommentStep(0));

        Assert.Equal("comment", PathText.KindName(comment, null));
        Assert.Equal("comment", PathText.KindName(comment, comment.Parts[0]));
    }

    [Fact]
    public void Env_paths_are_the_full_key_with_double_underscores_and_the_root_is_dollar()
    {
        ConfigDocument doc = TreeFixtures.Parse("Logging__LogLevel__Default=info\n", "env");

        Assert.Equal("$", Path(doc, new ItemStep(0)));
        Assert.Equal("Logging", Path(doc, new ItemStep(0), new MemberStep("Logging", 0)));
        Assert.Equal("Logging__LogLevel", Path(doc, new ItemStep(0), new MemberStep("Logging", 0), new MemberStep("LogLevel", 0)));
        Assert.Equal(
            "Logging__LogLevel__Default",
            Path(doc, new ItemStep(0), new MemberStep("Logging", 0), new MemberStep("LogLevel", 0), new MemberStep("Default", 0)));
    }

    [Fact]
    public void Env_comment_paths_are_the_parents_path_and_a_comment_marker_as_in_json()
    {
        ConfigDocument doc = TreeFixtures.Parse("#a\nA=1\nLogging__X=2\nLogging__Y=3\n#b\n", "env");

        Assert.Equal("$/#comment[1]", Path(doc, new ItemStep(0), new CommentStep(0)));
        Assert.Equal("$/#comment[2]", Path(doc, new ItemStep(0), new CommentStep(1)));
    }

    [Fact]
    public void Env_comment_inside_a_group_has_the_group_path()
    {
        ConfigDocument doc = TreeFixtures.Parse("Logging__X=2\n#c\nLogging__Y=3\n", "env");

        Assert.Equal("Logging/#comment[1]", Path(doc, new ItemStep(0), new MemberStep("Logging", 0), new CommentStep(0)));
    }

    [Fact]
    public void Env_kinds_are_root_group_entry_comment_and_for_parts_key_and_string()
    {
        ConfigDocument doc = TreeFixtures.Parse("#c\nLogging__Level=info\n", "env");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));
        DocNode group = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("Logging", 0));
        DocNode entry = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("Logging", 0), new MemberStep("Level", 0));
        DocNode comment = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("Logging", 0), new CommentStep(0));

        Assert.Equal("root", PathText.KindName(root, null));
        Assert.Equal("root", PathText.KindName(root, root.Parts[0]));
        Assert.Equal("root", PathText.KindName(root, root.Parts[1]));
        Assert.Equal("group", PathText.KindName(group, null));
        Assert.Equal("key", PathText.KindName(group, group.Parts[0]));
        Assert.Equal("group", PathText.KindName(group, group.Parts[1]));
        Assert.Equal("entry", PathText.KindName(entry, null));
        Assert.Equal("key", PathText.KindName(entry, entry.Parts[0]));
        Assert.Equal("string", PathText.KindName(entry, entry.Parts[1]));
        Assert.Equal("comment", PathText.KindName(comment, null));
        Assert.Equal("comment", PathText.KindName(comment, comment.Parts[0]));
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
