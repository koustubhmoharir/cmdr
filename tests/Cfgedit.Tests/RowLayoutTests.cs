using System.Text;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;
using Cfgedit.Tui;

namespace Cfgedit.Tests;

/// <summary>Parsing and row helpers shared by the tree-layout tests.</summary>
internal static class TreeFixtures
{
    public static ConfigDocument Parse(string text, string format)
    {
        IConfigFormat formatValue = Formats.FromName(format) ?? throw new ArgumentException(format, nameof(format));
        return ConfigDocument.Parse(SourceText.Create(text, Encoding.UTF8, false), formatValue);
    }

    public static DocNode Find(ConfigDocument document, params PathStep[] steps)
    {
        var path = new NodePath(steps);
        return document.Resolve(path) ?? throw new InvalidOperationException("No node at " + path + ".");
    }

    public static Row RowOf(DocNode node, int depth) => new(node, false, depth);

    public static Row EndRowOf(DocNode node, int depth) => new(node, true, depth);
}

public sealed class RowLayoutTests
{
    // ----- JSON -----

    [Fact]
    public void Json_root_object_shows_summary_expanded()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"name\": \"web\"}", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("\x25BE ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("{…}", StyleKind.Punctuation, 1), new Segment(" (1)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 11) }, lines.Parts);
    }

    [Fact]
    public void Json_root_object_shows_summary_collapsed_with_the_same_summary()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"name\": \"web\"}", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), true, 80);

        Assert.Equal(
            new[]
            {
                new Segment("\x25B8 ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("{…}", StyleKind.Punctuation, 1), new Segment(" (1)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 11) }, lines.Parts);
    }

    [Fact]
    public void Json_member_container_summary_in_brackets()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"a\": [1, 2, 3]}", "json");
        DocNode member = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 1), true, 80);

        Assert.Equal(
            new[]
            {
                new Segment("\x25B8 ", StyleKind.Fold, -1),
                new Segment("a", StyleKind.Key, 0),
                new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("[…]", StyleKind.Punctuation, 1),
                new Segment(" (3)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 5), new PartRange(0, 7, 14) }, lines.Parts);
    }

    [Fact]
    public void Json_member_string_has_key_colon_and_value()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"name\": \"web\"}", "json");
        DocNode member = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("name", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("name", StyleKind.Key, 0),
                new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("web", StyleKind.String, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 8), new PartRange(0, 10, 13) }, lines.Parts);
    }

    [Fact]
    public void Json_item_shows_index_and_value()
    {
        ConfigDocument doc = TreeFixtures.Parse("[10, \"x\"]", "json");
        DocNode item = TreeFixtures.Find(doc, new ItemStep(0), new ItemStep(1));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(item, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("1", StyleKind.Index, 0),
                new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("x", StyleKind.String, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 5), new PartRange(0, 7, 8) }, lines.Parts);
    }

    [Fact]
    public void Json_number_bool_and_null_values_have_their_styles()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"n\": -1.5e3, \"t\": true, \"z\": null}", "json");

        RowLines number = RowLayout.Layout(TreeFixtures.RowOf(TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("n", 0)), 1), false, 80);
        RowLines boolean = RowLayout.Layout(TreeFixtures.RowOf(TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("t", 0)), 1), false, 80);
        RowLines nothing = RowLayout.Layout(TreeFixtures.RowOf(TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("z", 0)), 1), false, 80);

        Assert.Equal(new Segment("-1.5e3", StyleKind.Number, 1), number.Lines[0].Segments[3]);
        Assert.Equal(new PartRange(0, 7, 13), number.Parts[1]);
        Assert.Equal(new Segment("true", StyleKind.Bool, 1), boolean.Lines[0].Segments[3]);
        Assert.Equal(new PartRange(0, 7, 11), boolean.Parts[1]);
        Assert.Equal(new Segment("null", StyleKind.Null, 1), nothing.Lines[0].Segments[3]);
        Assert.Equal(new PartRange(0, 7, 11), nothing.Parts[1]);
    }

    [Fact]
    public void Json_empty_string_value_is_a_pair_of_quotes_in_punctuation_style()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"e\": \"\"}", "json");
        DocNode member = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("e", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("e", StyleKind.Key, 0),
                new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("\"\"", StyleKind.Punctuation, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 5), new PartRange(0, 7, 9) }, lines.Parts);
    }

    [Fact]
    public void Json_empty_key_shows_quotes_in_the_key_part()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"\": 1}", "json");
        DocNode member = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("\"\"", StyleKind.Punctuation, 0),
                new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("1", StyleKind.Number, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 6), new PartRange(0, 8, 9) }, lines.Parts);
    }

    [Fact]
    public void Json_string_with_line_feed_and_tab_keeps_one_run_per_style()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"s\": \"a\\nb\\tc\"}", "json");
        DocNode member = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("s", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("s", StyleKind.Key, 0),
                new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.String, 1),
                new Segment("\x23CE", StyleKind.Glyph, 1),
                new Segment("b", StyleKind.String, 1),
                new Segment("\x2192", StyleKind.Glyph, 1),
                new Segment("c", StyleKind.String, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new PartRange(0, 7, 12), lines.Parts[1]);
    }

    [Fact]
    public void Json_control_character_shows_a_middle_dot_glyph()
    {
        // The JSON text holds the escape \u0001, written here without a \u sequence in the source.
        ConfigDocument doc = TreeFixtures.Parse("{\"c\": \"\\" + "u0001\"}", "json");
        DocNode member = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("c", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("c", StyleKind.Key, 0),
                new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("\x00B7", StyleKind.Glyph, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new PartRange(0, 7, 8), lines.Parts[1]);
    }

    [Fact]
    public void Json_root_scalar_has_no_key_or_colon()
    {
        ConfigDocument doc = TreeFixtures.Parse("\"web\"", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 80);

        Assert.Equal(
            new[] { new Segment("  ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1), new Segment("web", StyleKind.String, 1) },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 7) }, lines.Parts);
    }

    [Fact]
    public void Json_empty_object_shows_zero_summary_and_is_not_collapsible()
    {
        ConfigDocument doc = TreeFixtures.Parse("{}", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("$", StyleKind.ReadOnly, 0),
                new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("{…}", StyleKind.Punctuation, 1),
                new Segment(" (0)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 11) }, lines.Parts);
    }

    [Fact]
    public void Json_empty_array_member_shows_brackets_with_an_ellipsis_and_zero_count()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"a\": []}", "json");
        DocNode member = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 1), false, 80);

        Assert.Equal("  a: [\x2026] (0)", TextOf(lines.Lines[0]));
        Assert.Equal(new[] { new PartRange(0, 4, 5), new PartRange(0, 7, 14) }, lines.Parts);
    }

    [Fact]
    public void Jsonc_line_comment_shows_dimmed_markers()
    {
        ConfigDocument doc = TreeFixtures.Parse("// hi\n{\"a\": 1}", "jsonc");
        DocNode comment = TreeFixtures.Find(doc, new CommentStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(comment, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("//", StyleKind.Punctuation, -1),
                new Segment(" hi", StyleKind.Comment, 0),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 7) }, lines.Parts);
    }

    [Fact]
    public void Jsonc_block_comment_shows_dimmed_markers()
    {
        ConfigDocument doc = TreeFixtures.Parse("/* x */ {\"a\": 1}", "jsonc");
        DocNode comment = TreeFixtures.Find(doc, new CommentStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(comment, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("/*", StyleKind.Punctuation, -1),
                new Segment(" x ", StyleKind.Comment, 0),
                new Segment("*/", StyleKind.Punctuation, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 7) }, lines.Parts);
    }

    // ----- XML -----

    [Fact]
    public void Xml_self_closing_element_has_no_space_before_the_slash()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a/>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.ElementName, 0),
                new Segment("/>", StyleKind.Punctuation, -1),
                new Segment(" ", StyleKind.Summary, -1),
                new Segment("(0)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 4), new PartRange(0, 7, 10) }, lines.Parts);
    }

    [Fact]
    public void Xml_self_closing_with_attribute_drops_the_space_before_the_slash()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a x=\"1\" />", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.ElementName, 0),
                new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("x", StyleKind.AttributeName, 1),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("1", StyleKind.AttributeValue, 2),
                new Segment("\"/>", StyleKind.Punctuation, -1),
                new Segment(" ", StyleKind.Summary, -1),
                new Segment("(0)", StyleKind.Summary, 3),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 4), new PartRange(0, 5, 6), new PartRange(0, 8, 9), new PartRange(0, 13, 16) }, lines.Parts);
    }

    [Fact]
    public void Xml_expanded_element_shows_open_tag_and_an_end_row()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r a=\"1\"><c/></r>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("r", 0));

        RowLines open = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);
        RowLines end = RowLayout.Layout(TreeFixtures.EndRowOf(element, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("\x25BE ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("r", StyleKind.ElementName, 0),
                new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.AttributeName, 1),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("1", StyleKind.AttributeValue, 2),
                new Segment("\">", StyleKind.Punctuation, -1),
            },
            open.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 4), new PartRange(0, 5, 6), new PartRange(0, 8, 9) }, open.Parts);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("</", StyleKind.Punctuation, -1),
                new Segment("r", StyleKind.ElementName, 0),
                new Segment(">", StyleKind.Punctuation, -1),
            },
            end.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 5) }, end.Parts);
    }

    [Fact]
    public void Xml_collapsed_element_shows_ellipsis_and_closing_tag()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r a=\"1\"><c/></r>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("r", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), true, 80);

        Assert.Equal(
            new[]
            {
                new Segment("\x25B8 ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("r", StyleKind.ElementName, 0),
                new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.AttributeName, 1),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("1", StyleKind.AttributeValue, 2),
                new Segment("\">", StyleKind.Punctuation, -1),
                new Segment("\x2026", StyleKind.Glyph, -1),
                new Segment("</", StyleKind.Punctuation, -1),
                new Segment("r", StyleKind.ElementName, -1),
                new Segment(">", StyleKind.Punctuation, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 4), new PartRange(0, 5, 6), new PartRange(0, 8, 9) }, lines.Parts);
    }

    [Fact]
    public void Xml_text_only_element_shows_text_and_closing_tag()
    {
        ConfigDocument doc = TreeFixtures.Parse("<add key=\"k\">value</add>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("add", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("add", StyleKind.ElementName, 0),
                new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("key", StyleKind.AttributeName, 1),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("k", StyleKind.AttributeValue, 2),
                new Segment("\">", StyleKind.Punctuation, -1),
                new Segment("value", StyleKind.Text, 3),
                new Segment("</", StyleKind.Punctuation, -1),
                new Segment("add", StyleKind.ElementName, -1),
                new Segment(">", StyleKind.Punctuation, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(
            new[] { new PartRange(0, 3, 6), new PartRange(0, 7, 10), new PartRange(0, 12, 13), new PartRange(0, 15, 20) },
            lines.Parts);
    }

    [Fact]
    public void Xml_empty_text_only_element_has_a_zero_width_text_range()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a></a>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.ElementName, 0),
                new Segment("></", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.ElementName, -1),
                new Segment(">", StyleKind.Punctuation, -1),
                new Segment(" ", StyleKind.Summary, -1),
                new Segment("(0)", StyleKind.Summary, 2),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 4), new PartRange(0, 5, 5), new PartRange(0, 10, 13) }, lines.Parts);
    }

    [Fact]
    public void Xml_element_with_only_a_line_break_inside_has_an_end_row()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a>\n</a>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("a", 0));

        RowLines open = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);
        RowLines end = RowLayout.Layout(TreeFixtures.EndRowOf(element, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.ElementName, 0),
                new Segment(">", StyleKind.Punctuation, -1),
                new Segment(" ", StyleKind.Summary, -1),
                new Segment("(0)", StyleKind.Summary, 1),
            },
            open.Lines[0].Segments);
        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("</", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.ElementName, 0),
                new Segment(">", StyleKind.Punctuation, -1),
            },
            end.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 5) }, end.Parts);
    }

    [Fact]
    public void Xml_child_element_indents_two_columns_per_depth()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r><c/></r>", "xml");
        DocNode child = TreeFixtures.Find(doc, new ElementStep("r", 0), new ElementStep("c", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(child, 1), false, 80);

        Assert.Equal(2, lines.Lines[0].Indent);
        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("c", StyleKind.ElementName, 0),
                new Segment("/>", StyleKind.Punctuation, -1),
                new Segment(" ", StyleKind.Summary, -1),
                new Segment("(0)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 5, 6), new PartRange(0, 9, 12) }, lines.Parts);
    }

    [Fact]
    public void Xml_cdata_shows_dimmed_markers()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r><![CDATA[a<b]]></r>", "xml");
        DocNode cdata = TreeFixtures.Find(doc, new ElementStep("r", 0), new TextStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(cdata, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<![CDATA[", StyleKind.Punctuation, -1),
                new Segment("a<b", StyleKind.CData, 0),
                new Segment("]]>", StyleKind.Punctuation, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 13, 16) }, lines.Parts);
    }

    [Fact]
    public void Xml_comment_shows_markers_and_content()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r><!-- hi --></r>", "xml");
        DocNode comment = TreeFixtures.Find(doc, new ElementStep("r", 0), new CommentStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(comment, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<!--", StyleKind.Punctuation, -1),
                new Segment(" hi ", StyleKind.Comment, 0),
                new Segment("-->", StyleKind.Punctuation, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 8, 12) }, lines.Parts);
    }

    [Fact]
    public void Xml_processing_instruction_shows_target_and_data()
    {
        ConfigDocument doc = TreeFixtures.Parse("<?pi data?><r/>", "xml");
        DocNode pi = TreeFixtures.Find(doc, new PIStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(pi, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<?", StyleKind.Punctuation, -1),
                new Segment("pi", StyleKind.ElementName, 0),
                new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("data", StyleKind.Text, 1),
                new Segment("?>", StyleKind.Punctuation, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 6), new PartRange(0, 7, 11) }, lines.Parts);
    }

    [Fact]
    public void Xml_declaration_and_doctype_are_read_only_source()
    {
        ConfigDocument doc = TreeFixtures.Parse("<?xml version=\"1.0\"?><!DOCTYPE r><r/>", "xml");
        DocNode declaration = TreeFixtures.Find(doc, new DeclarationStep());
        DocNode doctype = TreeFixtures.Find(doc, new DoctypeStep());

        RowLines declarationLines = RowLayout.Layout(TreeFixtures.RowOf(declaration, 0), false, 80);
        RowLines doctypeLines = RowLayout.Layout(TreeFixtures.RowOf(doctype, 0), false, 80);

        Assert.Equal(
            new[] { new Segment("  ", StyleKind.Fold, -1), new Segment("<?xml version=\"1.0\"?>", StyleKind.ReadOnly, 0) },
            declarationLines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 23) }, declarationLines.Parts);
        Assert.Equal(
            new[] { new Segment("  ", StyleKind.Fold, -1), new Segment("<!DOCTYPE r>", StyleKind.ReadOnly, 0) },
            doctypeLines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 14) }, doctypeLines.Parts);
    }

    [Fact]
    public void Xml_text_node_in_mixed_content_has_its_own_row()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r>a<b/>c</r>", "xml");
        DocNode text = TreeFixtures.Find(doc, new ElementStep("r", 0), new TextStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(text, 1), false, 80);

        Assert.Equal(
            new[] { new Segment("  ", StyleKind.Fold, -1), new Segment("a", StyleKind.Text, 0) },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 5) }, lines.Parts);
    }

    [Fact]
    public void Xml_text_with_line_feed_shows_glyph_inside_the_text_part()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r>a\nb</r>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("r", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("r", StyleKind.ElementName, 0),
                new Segment(">", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.Text, 1),
                new Segment("\x23CE", StyleKind.Glyph, 1),
                new Segment("b", StyleKind.Text, 1),
                new Segment("</", StyleKind.Punctuation, -1),
                new Segment("r", StyleKind.ElementName, -1),
                new Segment(">", StyleKind.Punctuation, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 4), new PartRange(0, 5, 8) }, lines.Parts);
    }

    [Fact]
    public void Xml_raw_text_is_followed_by_a_raw_marker()
    {
        ConfigDocument doc = TreeFixtures.Parse("<!DOCTYPE r [<!ENTITY e \"x\">]><r>&e;</r>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("r", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("r", StyleKind.ElementName, 0),
                new Segment(">", StyleKind.Punctuation, -1),
                new Segment("&e;", StyleKind.Text, 1),
                new Segment(" raw", StyleKind.RawMarker, -1),
                new Segment("</", StyleKind.Punctuation, -1),
                new Segment("r", StyleKind.ElementName, -1),
                new Segment(">", StyleKind.Punctuation, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 4), new PartRange(0, 5, 8) }, lines.Parts);
    }

    [Fact]
    public void Xml_raw_attribute_value_is_followed_by_a_raw_marker()
    {
        ConfigDocument doc = TreeFixtures.Parse("<!DOCTYPE r [<!ENTITY e \"x\">]><r a=\"&e;\"/>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("r", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("r", StyleKind.ElementName, 0),
                new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.AttributeName, 1),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("&e;", StyleKind.AttributeValue, 2),
                new Segment("\"", StyleKind.Punctuation, -1),
                new Segment(" raw", StyleKind.RawMarker, -1),
                new Segment("/>", StyleKind.Punctuation, -1),
                new Segment(" ", StyleKind.Summary, -1),
                new Segment("(0)", StyleKind.Summary, 3),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 4), new PartRange(0, 5, 6), new PartRange(0, 8, 11), new PartRange(0, 19, 22) }, lines.Parts);
    }

    [Fact]
    public void Xml_attribute_value_with_a_tab_reference_shows_a_glyph_run()
    {
        ConfigDocument doc = TreeFixtures.Parse("<r a=\"x&#9;y\"/>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("r", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("r", StyleKind.ElementName, 0),
                new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.AttributeName, 1),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("x", StyleKind.AttributeValue, 2),
                new Segment("\x2192", StyleKind.Glyph, 2),
                new Segment("y", StyleKind.AttributeValue, 2),
                new Segment("\"/>", StyleKind.Punctuation, -1),
                new Segment(" ", StyleKind.Summary, -1),
                new Segment("(0)", StyleKind.Summary, 3),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new PartRange(0, 8, 11), lines.Parts[2]);
    }

    // ----- XML wrapping -----

    private const string Log4net = "<log4net debug=\"false\" threshold=\"ALL\" update=\"Merge\"/>";

    [Fact]
    public void Xml_attributes_on_one_line_when_they_fit()
    {
        ConfigDocument doc = TreeFixtures.Parse(Log4net, "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("log4net", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Single(lines.Lines);
        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("log4net", StyleKind.ElementName, 0),
                new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("debug", StyleKind.AttributeName, 1),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("false", StyleKind.AttributeValue, 2),
                new Segment("\" ", StyleKind.Punctuation, -1),
                new Segment("threshold", StyleKind.AttributeName, 3),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("ALL", StyleKind.AttributeValue, 4),
                new Segment("\" ", StyleKind.Punctuation, -1),
                new Segment("update", StyleKind.AttributeName, 5),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("Merge", StyleKind.AttributeValue, 6),
                new Segment("\"/>", StyleKind.Punctuation, -1),
                new Segment(" ", StyleKind.Summary, -1),
                new Segment("(0)", StyleKind.Summary, 7),
            },
            lines.Lines[0].Segments);
        Assert.Equal(
            new[]
            {
                new PartRange(0, 3, 10),
                new PartRange(0, 11, 16),
                new PartRange(0, 18, 23),
                new PartRange(0, 25, 34),
                new PartRange(0, 36, 39),
                new PartRange(0, 41, 47),
                new PartRange(0, 49, 54),
                new PartRange(0, 58, 61),
            },
            lines.Parts);
    }

    [Fact]
    public void Xml_attribute_that_does_not_fit_wraps_aligned_under_the_first_attribute()
    {
        ConfigDocument doc = TreeFixtures.Parse(Log4net, "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("log4net", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 40);

        Assert.Equal(2, lines.Lines.Count);
        Assert.Equal(0, lines.Lines[0].Indent);
        Assert.Equal(11, lines.Lines[1].Indent);
        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("log4net", StyleKind.ElementName, 0),
                new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("debug", StyleKind.AttributeName, 1),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("false", StyleKind.AttributeValue, 2),
                new Segment("\" ", StyleKind.Punctuation, -1),
                new Segment("threshold", StyleKind.AttributeName, 3),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("ALL", StyleKind.AttributeValue, 4),
                new Segment("\"", StyleKind.Punctuation, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(
            new[]
            {
                new Segment("update", StyleKind.AttributeName, 5),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("Merge", StyleKind.AttributeValue, 6),
                new Segment("\"/>", StyleKind.Punctuation, -1),
                new Segment(" ", StyleKind.Summary, -1),
                new Segment("(0)", StyleKind.Summary, 7),
            },
            lines.Lines[1].Segments);
        Assert.Equal(
            new[]
            {
                new PartRange(0, 3, 10),
                new PartRange(0, 11, 16),
                new PartRange(0, 18, 23),
                new PartRange(0, 25, 34),
                new PartRange(0, 36, 39),
                new PartRange(1, 11, 17),
                new PartRange(1, 19, 24),
                new PartRange(1, 28, 31),
            },
            lines.Parts);
    }

    [Fact]
    public void Xml_wrap_falls_back_to_four_columns_when_alignment_leaves_too_few()
    {
        ConfigDocument doc = TreeFixtures.Parse(Log4net, "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("log4net", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 30);

        Assert.Equal(3, lines.Lines.Count);
        Assert.Equal(4, lines.Lines[1].Indent);
        Assert.Equal(4, lines.Lines[2].Indent);
        Assert.Equal(
            new[] { new Segment("threshold", StyleKind.AttributeName, 3), new Segment("=\"", StyleKind.Punctuation, -1), new Segment("ALL", StyleKind.AttributeValue, 4), new Segment("\"", StyleKind.Punctuation, -1) },
            lines.Lines[1].Segments);
        Assert.Equal(
            new[]
            {
                new Segment("update", StyleKind.AttributeName, 5),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("Merge", StyleKind.AttributeValue, 6),
                new Segment("\"/>", StyleKind.Punctuation, -1),
                new Segment(" ", StyleKind.Summary, -1),
                new Segment("(0)", StyleKind.Summary, 7),
            },
            lines.Lines[2].Segments);
        Assert.Equal(new PartRange(1, 4, 13), lines.Parts[3]);
        Assert.Equal(new PartRange(1, 15, 18), lines.Parts[4]);
        Assert.Equal(new PartRange(2, 4, 10), lines.Parts[5]);
        Assert.Equal(new PartRange(2, 12, 17), lines.Parts[6]);
        Assert.Equal(new PartRange(2, 21, 24), lines.Parts[7]);
    }

    [Fact]
    public void Xml_attribute_wider_than_a_line_is_cut_with_an_ellipsis_and_the_rest_dropped()
    {
        ConfigDocument doc = TreeFixtures.Parse(Log4net, "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("log4net", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 12);

        Assert.Equal(2, lines.Lines.Count);
        Assert.Equal(4, lines.Lines[1].Indent);
        Assert.Equal(
            new[] { new Segment("debug", StyleKind.AttributeName, 1), new Segment("=\"", StyleKind.Punctuation, -1), new Segment("\x2026", StyleKind.Glyph, -1) },
            lines.Lines[1].Segments);
        Assert.Equal(new PartRange(0, 3, 10), lines.Parts[0]);
        Assert.Equal(new PartRange(1, 4, 9), lines.Parts[1]);
        Assert.Equal(new PartRange(1, 11, 11), lines.Parts[2]);
        Assert.Equal(new PartRange(1, 11, 11), lines.Parts[6]);
    }

    [Fact]
    public void Xml_text_wraps_and_the_closing_tag_wraps_after_it()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a x=\"1\">long text here</a>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 20);

        Assert.Equal(3, lines.Lines.Count);
        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.ElementName, 0),
                new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("x", StyleKind.AttributeName, 1),
                new Segment("=\"", StyleKind.Punctuation, -1),
                new Segment("1", StyleKind.AttributeValue, 2),
                new Segment("\">", StyleKind.Punctuation, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(4, lines.Lines[1].Indent);
        Assert.Equal(new[] { new Segment("long text here", StyleKind.Text, 3) }, lines.Lines[1].Segments);
        Assert.Equal(4, lines.Lines[2].Indent);
        Assert.Equal(
            new[] { new Segment("</", StyleKind.Punctuation, -1), new Segment("a", StyleKind.ElementName, -1), new Segment(">", StyleKind.Punctuation, -1) },
            lines.Lines[2].Segments);
        Assert.Equal(new PartRange(1, 4, 18), lines.Parts[3]);
    }

    [Fact]
    public void Xml_oversize_text_is_cut_and_the_closing_tag_dropped()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a>abcdefghijklmnop</a>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 10);

        Assert.Equal(2, lines.Lines.Count);
        Assert.Equal(4, lines.Lines[1].Indent);
        Assert.Equal(
            new[] { new Segment("abcde", StyleKind.Text, 1), new Segment("\x2026", StyleKind.Glyph, -1) },
            lines.Lines[1].Segments);
        Assert.Equal(new PartRange(1, 4, 9), lines.Parts[1]);
    }

    [Fact]
    public void Xml_narrow_element_row_is_cut_on_one_line()
    {
        ConfigDocument doc = TreeFixtures.Parse("<log4net debug=\"false\"/>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("log4net", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 5);

        Assert.Single(lines.Lines);
        Assert.Equal(
            new[] { new Segment("  ", StyleKind.Fold, -1), new Segment("<", StyleKind.Punctuation, -1), new Segment("l", StyleKind.ElementName, 0), new Segment("\x2026", StyleKind.Glyph, -1) },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 4), new PartRange(0, 4, 4), new PartRange(0, 4, 4), new PartRange(0, 4, 4) }, lines.Parts);
    }

    // ----- Widths, graphemes and degenerate cases -----

    [Fact]
    public void Long_ascii_value_is_cut_with_an_ellipsis()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"name\": \"abcdefghij\"}", "json");
        DocNode member = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("name", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 1), false, 12);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("name", StyleKind.Key, 0),
                new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.String, 1),
                new Segment("\x2026", StyleKind.Glyph, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 8), new PartRange(0, 10, 11) }, lines.Parts);
    }

    [Fact]
    public void Cut_inside_a_key_drops_the_colon_and_value_to_zero_width()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"abcdefgh\": 1}", "json");
        DocNode member = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("abcdefgh", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 1), false, 8);

        Assert.Equal(
            new[] { new Segment("  ", StyleKind.Fold, -1), new Segment("abc", StyleKind.Key, 0), new Segment("\x2026", StyleKind.Glyph, -1) },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 7), new PartRange(0, 7, 7) }, lines.Parts);
    }

    [Fact]
    public void Cjk_wide_character_is_never_cut_in_half()
    {
        ConfigDocument doc = TreeFixtures.Parse("\"\x4E2D\x6587\x5B57\"", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        // The root marker and its space take two columns after the fold, so each width is two more than before.
        RowLines narrow = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 6);
        RowLines wide = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 7);

        Assert.Equal(
            new[] { new Segment("  ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1), new Segment("\x2026", StyleKind.Glyph, -1) },
            narrow.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 4) }, narrow.Parts);
        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("\x4E2D", StyleKind.String, 1), new Segment("\x2026", StyleKind.Glyph, -1),
            },
            wide.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 6) }, wide.Parts);
    }

    [Fact]
    public void Zwj_emoji_sequence_is_never_split()
    {
        ConfigDocument doc = TreeFixtures.Parse("\"a\xD83D\xDC68\x200D\xD83D\xDC69z\"", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 7);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.String, 1), new Segment("\x2026", StyleKind.Glyph, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 5) }, lines.Parts);
    }

    [Fact]
    public void Combining_sequence_is_never_split()
    {
        ConfigDocument doc = TreeFixtures.Parse("\"e\x0301x\"", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines cut = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 5);
        RowLines whole = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 6);

        Assert.Equal(
            new[] { new Segment("  ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1), new Segment("\x2026", StyleKind.Glyph, -1) },
            cut.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 4) }, cut.Parts);
        Assert.Equal(
            new[] { new Segment("  ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1), new Segment("e\x0301x", StyleKind.String, 1) },
            whole.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 6) }, whole.Parts);
    }

    [Fact]
    public void Surrogate_pair_is_never_split()
    {
        ConfigDocument doc = TreeFixtures.Parse("\"a\xD83D\xDE00b\"", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 6);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.String, 1), new Segment("\x2026", StyleKind.Glyph, -1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 5) }, lines.Parts);
    }

    [Fact]
    public void Carriage_return_and_line_feed_are_two_glyphs_in_one_run()
    {
        ConfigDocument doc = TreeFixtures.Parse("\"a\\r\\nb\"", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.String, 1), new Segment("\x240D\x23CE", StyleKind.Glyph, 1), new Segment("b", StyleKind.String, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 8) }, lines.Parts);
    }

    [Fact]
    public void Root_object_at_width_one_keeps_the_fold()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"name\": \"web\"}", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 1);

        Assert.Equal(new[] { new Segment("\x25BE", StyleKind.Fold, -1) }, lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 1, 1), new PartRange(0, 1, 1) }, lines.Parts);
    }

    [Fact]
    public void Root_object_at_width_two_keeps_the_fold_and_space()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"name\": \"web\"}", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 2);

        Assert.Equal(new[] { new Segment("\x25BE ", StyleKind.Fold, -1) }, lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 2), new PartRange(0, 2, 2) }, lines.Parts);
    }

    [Fact]
    public void Root_object_at_width_three_shows_an_ellipsis_after_the_fold()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"name\": \"web\"}", "json");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 3);

        Assert.Equal(new[] { new Segment("\x25BE ", StyleKind.Fold, -1), new Segment("\x2026", StyleKind.Glyph, -1) }, lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 2), new PartRange(0, 2, 2) }, lines.Parts);
    }

    [Fact]
    public void Member_narrower_than_its_indent_and_fold_is_cut_to_the_width()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"a\": {\"b\": 1}}", "json");
        DocNode member = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("a", 0), new MemberStep("b", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 2), false, 3);

        // Depth 2 gives an indent of 4, which the 3-column view cuts to 3: no fold and no segments fit.
        Assert.Equal(3, lines.Lines[0].Indent);
        Assert.Empty(lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 3), new PartRange(0, 3, 3) }, lines.Parts);
    }

    [Fact]
    public void Member_whose_indent_fits_keeps_the_fold_cut_to_the_width()
    {
        ConfigDocument doc = TreeFixtures.Parse("{\"a\": {\"b\": 1}}", "json");
        DocNode member = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(member, 1), false, 3);

        // Member a holds an object, so it is collapsible and its fold is the expanded glyph.
        Assert.Equal(2, lines.Lines[0].Indent);
        Assert.Equal(new[] { new Segment("\x25BE", StyleKind.Fold, -1) }, lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 3), new PartRange(0, 3, 3) }, lines.Parts);
    }

    [Fact]
    public void Xml_empty_element_shows_its_summary_after_the_slash()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a/>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1),
                new Segment("<", StyleKind.Punctuation, -1),
                new Segment("a", StyleKind.ElementName, 0),
                new Segment("/>", StyleKind.Punctuation, -1),
                new Segment(" ", StyleKind.Summary, -1),
                new Segment("(0)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 3, 4), new PartRange(0, 7, 10) }, lines.Parts);
    }

    [Fact]
    public void Xml_empty_element_with_an_end_tag_shows_its_summary_after_the_closing_tag()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a></a>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Single(lines.Lines);
        Assert.Equal("  <a></a> (0)", TextOf(lines.Lines[0]));
        Assert.Equal(new PartRange(0, 3, 4), lines.Parts[0]);
        Assert.Equal(new PartRange(0, 10, 13), lines.Parts[2]);
    }

    [Fact]
    public void Xml_empty_container_written_across_lines_shows_its_summary_after_the_start_tag()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a>\n</a>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("a", 0));

        RowLines start = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 80);

        Assert.Equal("  <a> (0)", TextOf(start.Lines[0]));
        Assert.Equal(new PartRange(0, 6, 9), start.Parts[1]);
    }

    [Fact]
    public void Xml_empty_element_with_wrapping_attributes_keeps_its_summary_with_the_tag_end()
    {
        ConfigDocument doc = TreeFixtures.Parse(Log4net, "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("log4net", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 30);

        Assert.True(lines.Lines.Count > 1);
        Assert.EndsWith("/> (0)", TextOf(lines.Lines[^1]));
        Assert.Equal(lines.Lines.Count - 1, lines.Parts[^1].Line);
    }

    // ----- .env -----

    [Fact]
    public void Env_root_shows_the_marker_and_its_count_like_the_json_root()
    {
        ConfigDocument doc = TreeFixtures.Parse("A=1\nB=2\n", "env");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("\x25BE ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("{…}", StyleKind.Punctuation, 1), new Segment(" (2)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 2, 3), new PartRange(0, 4, 11) }, lines.Parts);
    }

    [Fact]
    public void Env_group_shows_its_segment_and_count_like_a_json_member()
    {
        ConfigDocument doc = TreeFixtures.Parse("Logging__Level=info\nLogging__Default=x\n", "env");
        DocNode group = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("Logging", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(group, 1), false, 80);

        Assert.Equal(2, lines.Lines[0].Indent);
        Assert.Equal(
            new[]
            {
                new Segment("\x25BE ", StyleKind.Fold, -1), new Segment("Logging", StyleKind.Key, 0), new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("{…}", StyleKind.Punctuation, 1), new Segment(" (2)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 4, 11), new PartRange(0, 13, 20) }, lines.Parts);
    }

    [Fact]
    public void Env_entry_shows_its_segment_equals_sign_and_value_with_the_value_styled_as_a_string()
    {
        ConfigDocument doc = TreeFixtures.Parse("Logging__Level=info\n", "env");
        DocNode entry = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("Logging", 0), new MemberStep("Level", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(entry, 2), false, 80);

        Assert.Equal(4, lines.Lines[0].Indent);
        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("Level", StyleKind.Key, 0), new Segment(" = ", StyleKind.Punctuation, -1),
                new Segment("info", StyleKind.String, 1),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 6, 11), new PartRange(0, 14, 18) }, lines.Parts);
    }

    [Fact]
    public void Env_empty_value_shows_a_pair_of_quotes_in_punctuation_style()
    {
        ConfigDocument doc = TreeFixtures.Parse("A=\n", "env");
        DocNode entry = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("A", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(entry, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("A", StyleKind.Key, 0), new Segment(" = ", StyleKind.Punctuation, -1),
                new Segment("\"\"", StyleKind.Punctuation, 1),
            },
            lines.Lines[0].Segments);
    }

    [Fact]
    public void Env_value_with_a_line_feed_shows_the_line_feed_glyph()
    {
        ConfigDocument doc = TreeFixtures.Parse("A='x\ny'\n", "env");
        DocNode entry = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("A", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(entry, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("A", StyleKind.Key, 0), new Segment(" = ", StyleKind.Punctuation, -1),
                new Segment("x", StyleKind.String, 1), new Segment("\x23CE", StyleKind.Glyph, 1), new Segment("y", StyleKind.String, 1),
            },
            lines.Lines[0].Segments);
    }

    [Fact]
    public void Env_comment_shows_a_hash_and_its_text_as_the_file_has_it()
    {
        ConfigDocument doc = TreeFixtures.Parse("#  note\nA=1\n", "env");
        DocNode comment = TreeFixtures.Find(doc, new ItemStep(0), new CommentStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(comment, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("#", StyleKind.Punctuation, -1), new Segment("  note", StyleKind.Comment, 0),
            },
            lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 5, 11) }, lines.Parts);
    }

    [Fact]
    public void Env_trailing_comment_row_looks_like_a_comment_row()
    {
        ConfigDocument doc = TreeFixtures.Parse("A=1 # note\n", "env");
        DocNode comment = TreeFixtures.Find(doc, new ItemStep(0), new CommentStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(comment, 1), false, 80);

        Assert.Equal("  # note", TextOf(lines.Lines[0]));
    }

    // ----- TOML -----

    [Fact]
    public void Toml_root_shows_the_marker_and_its_count_like_the_json_root()
    {
        ConfigDocument doc = TreeFixtures.Parse("a = 1\n[b]\nc = 2\n", "toml");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("\x25BE ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("{…}", StyleKind.Punctuation, 1), new Segment(" (2)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
    }

    [Fact]
    public void Toml_table_member_shows_key_colon_and_count_like_a_json_member()
    {
        ConfigDocument doc = TreeFixtures.Parse("a = 1\n[b]\nc = 2\n", "toml");
        DocNode table = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("b", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(table, 1), false, 80);

        Assert.Equal(2, lines.Lines[0].Indent);
        Assert.Equal(
            new[]
            {
                new Segment("\x25BE ", StyleKind.Fold, -1), new Segment("b", StyleKind.Key, 0), new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("{…}", StyleKind.Punctuation, 1), new Segment(" (1)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
    }

    [Fact]
    public void Toml_scalar_member_shows_key_equals_sign_and_a_string_value_without_quotes()
    {
        ConfigDocument doc = TreeFixtures.Parse("a = \"x\"\n", "toml");
        DocNode scalar = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(scalar, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("a", StyleKind.Key, 0), new Segment(" = ", StyleKind.Punctuation, -1),
                new Segment("x", StyleKind.String, 1),
            },
            lines.Lines[0].Segments);
    }

    [Fact]
    public void Toml_array_member_shows_square_brackets_and_each_item_by_its_index()
    {
        ConfigDocument doc = TreeFixtures.Parse("items = [5, \"s\"]\n", "toml");
        DocNode array = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("items", 0));
        DocNode first = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("items", 0), new ItemStep(0));
        DocNode second = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("items", 0), new ItemStep(1));

        Assert.Equal(
            new[]
            {
                new Segment("\x25BE ", StyleKind.Fold, -1), new Segment("items", StyleKind.Key, 0), new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("[…]", StyleKind.Punctuation, 1), new Segment(" (2)", StyleKind.Summary, 1),
            },
            RowLayout.Layout(TreeFixtures.RowOf(array, 1), false, 80).Lines[0].Segments);
        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("0", StyleKind.Index, 0), new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("5", StyleKind.Number, 1),
            },
            RowLayout.Layout(TreeFixtures.RowOf(first, 2), false, 80).Lines[0].Segments);
        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("1", StyleKind.Index, 0), new Segment(": ", StyleKind.Punctuation, -1),
                new Segment("s", StyleKind.String, 1),
            },
            RowLayout.Layout(TreeFixtures.RowOf(second, 2), false, 80).Lines[0].Segments);
    }

    [Fact]
    public void Toml_empty_string_shows_a_pair_of_quotes_in_punctuation_style()
    {
        ConfigDocument doc = TreeFixtures.Parse("a = \"\"\n", "toml");
        DocNode scalar = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(scalar, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("a", StyleKind.Key, 0), new Segment(" = ", StyleKind.Punctuation, -1),
                new Segment("\"\"", StyleKind.Punctuation, 1),
            },
            lines.Lines[0].Segments);
    }

    [Fact]
    public void Toml_boolean_is_styled_as_a_boolean_and_date_time_as_a_number()
    {
        ConfigDocument doc = TreeFixtures.Parse("b = true\nd = 1979-05-27\n", "toml");
        DocNode boolean = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("b", 0));
        DocNode date = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("d", 0));

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("b", StyleKind.Key, 0), new Segment(" = ", StyleKind.Punctuation, -1),
                new Segment("true", StyleKind.Bool, 1),
            },
            RowLayout.Layout(TreeFixtures.RowOf(boolean, 1), false, 80).Lines[0].Segments);
        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("d", StyleKind.Key, 0), new Segment(" = ", StyleKind.Punctuation, -1),
                new Segment("1979-05-27", StyleKind.Number, 1),
            },
            RowLayout.Layout(TreeFixtures.RowOf(date, 1), false, 80).Lines[0].Segments);
    }

    [Fact]
    public void Toml_quoted_key_shows_the_decoded_key_without_quotes_and_an_empty_key_as_quotes()
    {
        ConfigDocument doc = TreeFixtures.Parse("\"my key\" = 1\n\"\" = 2\n", "toml");
        DocNode spaced = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("my key", 0));
        DocNode empty = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep(string.Empty, 0));

        Assert.Equal("  my key = 1", TextOf(RowLayout.Layout(TreeFixtures.RowOf(spaced, 1), false, 80).Lines[0]));
        Assert.Equal("  \"\" = 2", TextOf(RowLayout.Layout(TreeFixtures.RowOf(empty, 1), false, 80).Lines[0]));
    }

    [Fact]
    public void Toml_comment_shows_a_hash_and_its_text_as_the_file_has_it()
    {
        ConfigDocument doc = TreeFixtures.Parse("# note\na = 1\n", "toml");
        DocNode comment = TreeFixtures.Find(doc, new ItemStep(0), new CommentStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(comment, 1), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("  ", StyleKind.Fold, -1), new Segment("#", StyleKind.Punctuation, -1), new Segment(" note", StyleKind.Comment, 0),
            },
            lines.Lines[0].Segments);
    }

    // ----- YAML -----

    [Fact]
    public void Yaml_root_mapping_shows_the_marker_and_its_count_like_the_json_root()
    {
        ConfigDocument doc = TreeFixtures.Parse("a: 1\nb:\n  c: 2\n", "yaml");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 80);

        Assert.Equal(
            new[]
            {
                new Segment("\x25BE ", StyleKind.Fold, -1), new Segment("$", StyleKind.ReadOnly, 0), new Segment(" ", StyleKind.Punctuation, -1),
                new Segment("{…}", StyleKind.Punctuation, 1), new Segment(" (2)", StyleKind.Summary, 1),
            },
            lines.Lines[0].Segments);
    }

    [Fact]
    public void Yaml_members_show_key_colon_and_a_value_in_its_kind_style()
    {
        ConfigDocument doc = TreeFixtures.Parse("s: a\nn: 1\nf: 1.5\nb: true\nz:\nq: ''\ne: \"\"\nt: ~\n", "yaml");
        DocNode text = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("s", 0));
        DocNode number = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("n", 0));

        Assert.Equal("s: a", TextOf(RowLayout.Layout(TreeFixtures.RowOf(text, 1), false, 80).Lines[0]).Trim());
        Assert.Equal("n: 1", TextOf(RowLayout.Layout(TreeFixtures.RowOf(number, 1), false, 80).Lines[0]).Trim());
        Assert.Contains(new Segment("1", StyleKind.Number, 1), RowLayout.Layout(TreeFixtures.RowOf(number, 1), false, 80).Lines[0].Segments);
        Assert.Equal("f: 1.5", TextOf(RowLayout.Layout(TreeFixtures.RowOf(Member(doc, "f"), 1), false, 80).Lines[0]).Trim());
        Assert.Equal("b: true", TextOf(RowLayout.Layout(TreeFixtures.RowOf(Member(doc, "b"), 1), false, 80).Lines[0]).Trim());
        Assert.Equal("z: null", TextOf(RowLayout.Layout(TreeFixtures.RowOf(Member(doc, "z"), 1), false, 80).Lines[0]).Trim());
        Assert.Equal("q: \"\"", TextOf(RowLayout.Layout(TreeFixtures.RowOf(Member(doc, "q"), 1), false, 80).Lines[0]).Trim());
        Assert.Equal("e: \"\"", TextOf(RowLayout.Layout(TreeFixtures.RowOf(Member(doc, "e"), 1), false, 80).Lines[0]).Trim());
        Assert.Equal("t: ~", TextOf(RowLayout.Layout(TreeFixtures.RowOf(Member(doc, "t"), 1), false, 80).Lines[0]).Trim());
    }

    [Fact]
    public void Yaml_anchors_tags_and_aliases_are_shown_read_only_between_the_key_and_the_value()
    {
        ConfigDocument doc = TreeFixtures.Parse("a: &x !!str 1\nb: *x\n", "yaml");
        DocNode anchored = Member(doc, "a");
        DocNode alias = Member(doc, "b");

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(anchored, 1), false, 80);

        Assert.Equal("a: &x !!str 1", TextOf(lines.Lines[0]).Trim());
        Assert.Contains(new Segment("&x", StyleKind.ReadOnly, 1), lines.Lines[0].Segments);
        Assert.Contains(new Segment("!!str", StyleKind.ReadOnly, 2), lines.Lines[0].Segments);
        Assert.Equal("b: *x", TextOf(RowLayout.Layout(TreeFixtures.RowOf(alias, 1), false, 80).Lines[0]).Trim());
        Assert.Contains(new Segment("*x", StyleKind.ReadOnly, 1), RowLayout.Layout(TreeFixtures.RowOf(alias, 1), false, 80).Lines[0].Segments);
    }

    [Fact]
    public void Yaml_sequence_items_show_their_index_then_the_value_and_a_sequence_shows_its_count()
    {
        ConfigDocument doc = TreeFixtures.Parse("list:\n  - 1\n  - a\n", "yaml");
        DocNode list = Member(doc, "list");
        DocNode first = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("list", 0), new ItemStep(0));

        Assert.Equal("▾ list: […] (2)", TextOf(RowLayout.Layout(TreeFixtures.RowOf(list, 1), false, 80).Lines[0]).Trim());
        Assert.Equal("0: 1", TextOf(RowLayout.Layout(TreeFixtures.RowOf(first, 2), false, 80).Lines[0]).Trim());
    }

    [Fact]
    public void Yaml_documents_show_their_index_as_a_row_and_comments_as_hash_and_text()
    {
        ConfigDocument doc = TreeFixtures.Parse("---\na: 1\n---\nb: 2\n", "yaml");
        DocNode second = TreeFixtures.Find(doc, new ItemStep(1));
        ConfigDocument commented = TreeFixtures.Parse("# note\na: 1\n", "yaml");
        DocNode comment = TreeFixtures.Find(commented, new ItemStep(0), new CommentStep(0));

        Assert.Equal("\x25BE ---[1]", TextOf(RowLayout.Layout(TreeFixtures.RowOf(second, 0), false, 80).Lines[0]));
        Assert.Equal("# note", TextOf(RowLayout.Layout(TreeFixtures.RowOf(comment, 1), false, 80).Lines[0]).Trim());
    }

    [Fact]
    public void Yaml_an_empty_stream_shows_the_empty_root_mapping()
    {
        ConfigDocument doc = TreeFixtures.Parse(string.Empty, "yaml");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        Assert.Equal("  $ {…} (0)", TextOf(RowLayout.Layout(TreeFixtures.RowOf(root, 0), false, 80).Lines[0]));
    }

    private static DocNode Member(ConfigDocument doc, string key) => TreeFixtures.Find(doc, new ItemStep(0), new MemberStep(key, 0));

    private static string TextOf(DisplayLine line) => string.Concat(line.Segments.Select(segment => segment.Text));

    [Fact]
    public void Width_zero_gives_an_empty_line_with_zero_width_ranges()
    {
        ConfigDocument doc = TreeFixtures.Parse("<a/>", "xml");
        DocNode element = TreeFixtures.Find(doc, new ElementStep("a", 0));

        RowLines lines = RowLayout.Layout(TreeFixtures.RowOf(element, 0), false, 0);

        Assert.Single(lines.Lines);
        Assert.Equal(0, lines.Lines[0].Indent);
        Assert.Empty(lines.Lines[0].Segments);
        Assert.Equal(new[] { new PartRange(0, 0, 0), new PartRange(0, 0, 0) }, lines.Parts);
    }
}
