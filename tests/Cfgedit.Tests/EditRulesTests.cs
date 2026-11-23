using Cfgedit.Core.Model;
using Cfgedit.Tui;

namespace Cfgedit.Tests;

/// <summary>
/// Which editor a part opens with. The inline field is checked by a round trip through the Terminal.Gui text field, the multi-line rule
/// on its own, and the kinds on real documents.
/// </summary>
public sealed class EditRulesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("web")]
    [InlineData("two words")]
    [InlineData("caf\xE9")]
    [InlineData("\U0001F44D\U0001F3FD")]
    public void A_single_line_value_round_trips_through_a_text_field(string text)
    {
        Assert.True(EditRules.CanEditInline(text));
        Assert.True(EditRules.CanEditMultiline(text));
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    public void A_value_with_a_line_break_cannot_be_edited_inline(string text)
    {
        Assert.False(EditRules.CanEditInline(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\nb")]
    [InlineData("x\n\n")]
    [InlineData("a\tb")]
    [InlineData("a\0b")]
    [InlineData("\U0001F44D\U0001F3FD\nb")]
    public void A_value_without_a_CR_can_be_edited_in_the_multi_line_editor(string text)
    {
        Assert.True(EditRules.CanEditMultiline(text));
    }

    [Theory]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    [InlineData("a\r")]
    [InlineData("\r")]
    public void A_value_with_a_CR_cannot_be_edited_in_the_multi_line_editor(string text)
    {
        Assert.False(EditRules.CanEditMultiline(text));
    }

    [Fact]
    public void A_lone_carriage_return_can_be_edited_in_neither_editor()
    {
        Assert.False(EditRules.CanEditInline("a\rb"));
        Assert.False(EditRules.CanEditMultiline("a\rb"));
        Assert.False(EditRules.CanEditMultiline("a\r"));
    }

    [Fact]
    public void A_json_string_opens_the_inline_field_and_a_value_with_a_line_break_opens_the_multi_line_editor()
    {
        ConfigDocument document = TreeFixtures.Parse("{\"plain\": \"web\", \"lines\": \"a\\nb\", \"cr\": \"a\\rb\"}", "json");

        Assert.Equal(EditorKind.Inline, EditRules.KindOf(TreeFixtures.Find(document, new ItemStep(0), new MemberStep("plain", 0)), ValueOf(document, "plain")));
        Assert.Equal(EditorKind.Multiline, EditRules.KindOf(TreeFixtures.Find(document, new ItemStep(0), new MemberStep("lines", 0)), ValueOf(document, "lines")));
        Assert.Equal(EditorKind.None, EditRules.KindOf(TreeFixtures.Find(document, new ItemStep(0), new MemberStep("cr", 0)), ValueOf(document, "cr")));
        Assert.Equal(EditRules.TextModeOnly, EditRules.RefusalOf(ValueOf(document, "cr")));
    }

    [Fact]
    public void A_json_boolean_and_a_json_null_open_the_type_menu_and_never_flip_or_edit_inline()
    {
        ConfigDocument document = TreeFixtures.Parse("{\"on\": true, \"off\": false, \"none\": null}", "json");
        DocNode on = TreeFixtures.Find(document, new ItemStep(0), new MemberStep("on", 0));
        DocNode off = TreeFixtures.Find(document, new ItemStep(0), new MemberStep("off", 0));
        DocNode none = TreeFixtures.Find(document, new ItemStep(0), new MemberStep("none", 0));

        Assert.Equal(EditorKind.Type, EditRules.KindOf(on, ValueOf(document, "on")));
        Assert.Equal(EditorKind.Type, EditRules.KindOf(off, ValueOf(document, "off")));
        Assert.Equal(EditorKind.Type, EditRules.KindOf(none, ValueOf(document, "none")));
    }

    [Fact]
    public void The_index_and_summary_parts_cannot_be_edited_and_say_so()
    {
        ConfigDocument document = TreeFixtures.Parse("{\"tags\": [\"a\"]}", "json");
        DocNode root = TreeFixtures.Find(document, new ItemStep(0));
        DocNode item = TreeFixtures.Find(document, new ItemStep(0), new MemberStep("tags", 0), new ItemStep(0));
        DocPart summary = root.Parts.Single(part => part.Kind == PartKind.Summary);
        DocPart index = item.Parts.Single(part => part.Kind == PartKind.Index);

        Assert.Equal(EditorKind.None, EditRules.KindOf(root, summary));
        Assert.Equal(EditorKind.None, EditRules.KindOf(item, index));
        Assert.Equal(EditRules.CannotEdit, EditRules.RefusalOf(summary));
        Assert.Equal(EditRules.CannotEdit, EditRules.RefusalOf(index));
    }

    [Fact]
    public void The_summary_of_an_empty_XML_element_cannot_be_edited_and_says_so()
    {
        ConfigDocument document = TreeFixtures.Parse("<r><c/></r>", "xml");
        DocNode element = TreeFixtures.Find(document, new ElementStep("r", 0), new ElementStep("c", 0));
        DocPart summary = element.Parts.Single(part => part.Kind == PartKind.Summary);

        Assert.Equal(EditorKind.None, EditRules.KindOf(element, summary));
        Assert.Equal(EditRules.CannotEdit, EditRules.RefusalOf(summary));
    }

    [Fact]
    public void The_root_marker_cannot_be_edited_and_says_so()
    {
        ConfigDocument document = TreeFixtures.Parse("{}", "json");
        DocNode root = TreeFixtures.Find(document, new ItemStep(0));
        DocPart marker = root.Parts.Single(part => part.Kind == PartKind.Root);

        Assert.Equal(EditorKind.None, EditRules.KindOf(root, marker));
        Assert.Equal(EditRules.CannotEdit, EditRules.RefusalOf(marker));
    }

    [Fact]
    public void Env_entry_values_open_the_inline_field_or_the_multi_line_editor_and_never_the_type_menu()
    {
        ConfigDocument doc = TreeFixtures.Parse("A=1\nE=\nB='x\ny'\n", "env");
        DocNode a = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("A", 0));
        DocNode empty = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("E", 0));
        DocNode b = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("B", 0));

        Assert.Equal(EditorKind.Inline, EditRules.KindOf(a, a.Parts[1]));
        Assert.Equal(EditorKind.Inline, EditRules.KindOf(empty, empty.Parts[1]));
        Assert.Equal(EditorKind.Multiline, EditRules.KindOf(b, b.Parts[1]));
    }

    [Fact]
    public void Env_keys_of_entries_and_groups_and_comments_open_the_inline_field()
    {
        ConfigDocument doc = TreeFixtures.Parse("#c\nLogging__Level=info\n", "env");
        DocNode group = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("Logging", 0));
        DocNode entry = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("Logging", 0), new MemberStep("Level", 0));
        DocNode comment = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("Logging", 0), new CommentStep(0));

        Assert.Equal(EditorKind.Inline, EditRules.KindOf(group, group.Parts[0]));
        Assert.Equal(EditorKind.Inline, EditRules.KindOf(entry, entry.Parts[0]));
        Assert.Equal(EditorKind.Inline, EditRules.KindOf(comment, comment.Parts[0]));
    }

    [Fact]
    public void Env_root_marker_and_summaries_cannot_be_edited()
    {
        ConfigDocument doc = TreeFixtures.Parse("Logging__Level=info\n", "env");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));
        DocNode group = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("Logging", 0));

        Assert.Equal(EditorKind.None, EditRules.KindOf(root, root.Parts[0]));
        Assert.Equal(EditorKind.None, EditRules.KindOf(root, root.Parts[1]));
        Assert.Equal(EditorKind.None, EditRules.KindOf(group, group.Parts[1]));
    }

    [Fact]
    public void Toml_scalars_open_the_inline_field_or_the_multi_line_editor_and_never_the_type_menu()
    {
        ConfigDocument doc = TreeFixtures.Parse("a = true\nb = 1\nc = \"\"\nd = \"\"\"x\ny\"\"\"\n", "toml");
        DocNode boolean = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("a", 0));
        DocNode integer = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("b", 0));
        DocNode empty = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("c", 0));
        DocNode lines = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("d", 0));

        Assert.Equal(EditorKind.Inline, EditRules.KindOf(boolean, boolean.Parts[1]));
        Assert.Equal(EditorKind.Inline, EditRules.KindOf(integer, integer.Parts[1]));
        Assert.Equal(EditorKind.Inline, EditRules.KindOf(empty, empty.Parts[1]));
        Assert.Equal(EditorKind.Multiline, EditRules.KindOf(lines, lines.Parts[1]));
    }

    [Fact]
    public void Toml_keys_and_comments_open_the_inline_field_but_a_key_with_a_line_break_does_not_open_at_all()
    {
        ConfigDocument doc = TreeFixtures.Parse("\"my key\" = 1\n\"a\\nb\" = 2\n# c\n", "toml");
        DocNode spaced = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("my key", 0));
        DocNode broken = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("a\nb", 0));
        DocNode comment = TreeFixtures.Find(doc, new ItemStep(0), new CommentStep(0));

        Assert.Equal(EditorKind.Inline, EditRules.KindOf(spaced, spaced.Parts[0]));
        Assert.Equal(EditorKind.None, EditRules.KindOf(broken, broken.Parts[0]));
        Assert.Equal(EditorKind.Inline, EditRules.KindOf(comment, comment.Parts[0]));
    }

    [Fact]
    public void Toml_root_marker_and_summaries_cannot_be_edited()
    {
        ConfigDocument doc = TreeFixtures.Parse("[t]\nx = 1\n", "toml");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));
        DocNode table = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("t", 0));

        Assert.Equal(EditorKind.None, EditRules.KindOf(root, root.Parts[0]));
        Assert.Equal(EditorKind.None, EditRules.KindOf(root, root.Parts[1]));
        Assert.Equal(EditorKind.None, EditRules.KindOf(table, table.Parts[1]));
    }

    [Fact]
    public void Yaml_scalars_open_the_inline_field_or_the_multi_line_editor_and_never_the_type_menu()
    {
        ConfigDocument doc = TreeFixtures.Parse("a: true\nb: 1\nc: ''\nd: |\n  x\n  y\ne:\n", "yaml");
        DocNode boolean = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("a", 0));
        DocNode integer = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("b", 0));
        DocNode empty = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("c", 0));
        DocNode block = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("d", 0));
        DocNode none = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("e", 0));

        Assert.Equal(EditorKind.Inline, EditRules.KindOf(boolean, boolean.Parts[1]));
        Assert.Equal(EditorKind.Inline, EditRules.KindOf(integer, integer.Parts[1]));
        Assert.Equal(EditorKind.Inline, EditRules.KindOf(empty, empty.Parts[1]));
        Assert.Equal(EditorKind.Multiline, EditRules.KindOf(block, block.Parts[1]));
        Assert.Equal(EditorKind.Inline, EditRules.KindOf(none, none.Parts[1]));
    }

    [Fact]
    public void Yaml_keys_open_the_inline_field_but_a_key_with_a_line_break_does_not_open_at_all()
    {
        ConfigDocument doc = TreeFixtures.Parse("plain: 1\n\"a\\nb\": 2\n", "yaml");
        DocNode plain = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("plain", 0));
        DocNode broken = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("a\nb", 0));

        Assert.Equal(EditorKind.Inline, EditRules.KindOf(plain, plain.Parts[0]));
        Assert.Equal(EditorKind.None, EditRules.KindOf(broken, broken.Parts[0]));
    }

    [Fact]
    public void Yaml_anchors_tags_aliases_roots_summaries_and_indexes_cannot_be_edited()
    {
        ConfigDocument doc = TreeFixtures.Parse("a: &x !!str 1\nb: *x\nl:\n  - 1\n", "yaml");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));
        DocNode anchored = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("a", 0));
        DocNode alias = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("b", 0));
        DocNode item = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("l", 0), new ItemStep(0));

        Assert.Equal(EditorKind.None, EditRules.KindOf(root, root.Parts[0]));
        Assert.Equal(EditorKind.None, EditRules.KindOf(root, root.Parts[1]));
        Assert.Equal(EditorKind.None, EditRules.KindOf(anchored, anchored.Parts[1]));
        Assert.Equal(EditorKind.None, EditRules.KindOf(anchored, anchored.Parts[2]));
        Assert.Equal(EditorKind.Inline, EditRules.KindOf(anchored, anchored.Parts[3]));
        Assert.Equal(EditorKind.None, EditRules.KindOf(alias, alias.Parts[1]));
        Assert.Equal(EditorKind.None, EditRules.KindOf(item, item.Parts[0]));
    }

    private static DocPart ValueOf(ConfigDocument document, string key)
    {
        DocNode node = TreeFixtures.Find(document, new ItemStep(0), new MemberStep(key, 0));
        return node.Parts.Single(part => part.Kind == PartKind.Value);
    }
}
