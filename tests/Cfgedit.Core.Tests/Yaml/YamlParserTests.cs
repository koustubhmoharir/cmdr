using System.Text;
using Cfgedit.Core.Text;
using Cfgedit.Core.Yaml;
using static Cfgedit.Core.Tests.Yaml.YamlTestSupport;

namespace Cfgedit.Core.Tests.Yaml;

/// <summary>
/// The syntax tree: each construct parses to the expected node kinds with the expected spans, the text tiles exactly, and printing the
/// tree reproduces the source byte for byte.
/// </summary>
public sealed class YamlParserTests
{
    public static IEnumerable<object[]> Snippets()
    {
        yield return ["a: 1\nb: two\n"];
        yield return ["a: 1"];
        yield return [""];
        yield return ["# only a comment\n"];
        yield return ["\n\n  \n"];
        yield return ["key:\n"];
        yield return ["key:\nnext: 1\n"];
        yield return ["- a\n- b\n- c\n"];
        yield return ["- a: 1\n  b: 2\n- c: 3\n"];
        yield return ["- - a\n  - b\n- c\n"];
        yield return ["-\n- x\n-\n"];
        yield return ["a:\n- x\n- y\nb: 1\n"];
        yield return ["a:\n  b:\n    c: deep\n  d: 1\ne: 2\n"];
        yield return ["{a: 1, b: [x, y], c: {d: e}}\n"];
        yield return ["{a: 1, b: 2,}\n"];
        yield return ["[1, 2, 3,]\n"];
        yield return ["[\n  a,\n  b\n]\n"];
        yield return ["{\n  \"a\": 1,\n  'b': 2\n}\n"];
        yield return ["{\"a\":1,\"b\":[true]}\n"];
        yield return ["k: 'it''s'\n"];
        yield return ["k: \"tab\\there \\u00e9\"\n"];
        yield return ["k: |\n  line one\n  line two\n"];
        yield return ["k: >-\n  folded\n  text\n\n  next\n"];
        yield return ["k: |2+\n   indented\n\n\n"];
        yield return ["- |\n  in a sequence\n- >\n  folded\n"];
        yield return ["a: &x 1\nb: *x\n"];
        yield return ["a: !!str 1\nb: !local value\nc: !<tag:example.com,2000:x> v\n"];
        yield return ["base: &base\n  x: 1\nderived:\n  <<: *base\n  y: 2\n"];
        yield return ["&a key: value\n"];
        yield return ["- &a\n  x: 1\n- !!map\n  y: 2\n"];
        yield return ["key: # a comment\n  value\n"];
        yield return ["a: 1 # trailing\n# own line\nb: 2 # again\n"];
        yield return ["%YAML 1.2\n%TAG !e! tag:example.com,2000:\n---\n!e!foo bar\n...\n"];
        yield return ["---\na: 1\n...\n---\nb: 2\n"];
        yield return ["--- text\n...\n"];
        yield return ["--- |\n  block\n"];
        yield return ["a: 1\r\nb: 2\r\n"];
        yield return ["a: 1\rb: 2\r"];
        yield return ["\uFEFFa: 1\n"];
        yield return ["k: \"multi\n  line\n\n  quoted\"\n"];
        yield return ["k: plain\n  continued\n\n  again\n"];
        yield return ["emoji: \"\U0001F600\"\n"];
    }

    [Theory]
    [MemberData(nameof(Snippets))]
    public void Snippet_tiles_and_prints_back_to_its_source(string text)
    {
        YamlDocument doc = Parse(text);
        AssertTiles(doc);
        Assert.Equal(text, Reassemble(doc));
    }

    [Fact]
    public void Block_mapping_has_entries_with_keys_colons_and_values()
    {
        YamlDocument doc = Parse("a: 1\nb:   two\n");
        YamlBlockMapping mapping = RootMapping(doc);
        Assert.Equal(2, mapping.Entries.Count);
        YamlMappingEntry first = mapping.Entries[0];
        Assert.Equal("a", first.Key.Value);
        Assert.Equal(":", first.Colon.Text);
        Assert.Equal(0, first.Colon.Start - 1);
        Assert.Equal("1", Assert.IsType<YamlScalar>(first.Value).Value);
        YamlMappingEntry second = mapping.Entries[1];
        Assert.Equal("two", Assert.IsType<YamlScalar>(second.Value).Value);
        Assert.Equal(6, second.Colon.Start);
    }

    [Fact]
    public void Empty_value_is_null_and_an_empty_plain_scalar_with_properties_is_a_node()
    {
        YamlBlockMapping mapping = RootMapping(Parse("a:\nb: &x\nc: !!str\n"));
        Assert.Null(mapping.Entries[0].Value);
        YamlScalar withAnchor = Assert.IsType<YamlScalar>(mapping.Entries[1].Value);
        Assert.True(withAnchor.IsEmpty);
        Assert.Equal("&x", withAnchor.Anchor!.Text);
        YamlScalar withTag = Assert.IsType<YamlScalar>(mapping.Entries[2].Value);
        Assert.Equal("!!str", withTag.Tag!.Text);
        Assert.Equal(YamlCoreKind.String, withTag.Kind);
    }

    [Fact]
    public void Compact_sequence_items_hold_mappings_at_the_column_of_their_first_key()
    {
        YamlDocument doc = Parse("- a: 1\n  b: 2\n- c: 3\n");
        var sequence = Assert.IsType<YamlBlockSequence>(Assert.Single(doc.Documents).Root);
        Assert.Equal(2, sequence.Items.Count);
        var first = Assert.IsType<YamlBlockMapping>(sequence.Items[0].Value);
        Assert.Equal(2, first.Entries.Count);
        Assert.Equal(2, first.Start - sequence.Items[0].Start);
        var second = Assert.IsType<YamlBlockMapping>(sequence.Items[1].Value);
        Assert.Equal("c", second.Entries.Single().Key.Value);
    }

    [Fact]
    public void Indentless_sequence_is_the_value_of_its_mapping_key()
    {
        YamlBlockMapping mapping = RootMapping(Parse("a:\n- x\n- y\nb: 1\n"));
        var sequence = Assert.IsType<YamlBlockSequence>(mapping.Entries[0].Value);
        Assert.Equal(2, sequence.Items.Count);
        Assert.Equal("b", mapping.Entries[1].Key.Value);
    }

    [Fact]
    public void Flow_collections_hold_their_items_and_allow_a_trailing_comma_and_several_lines()
    {
        YamlDocument doc = Parse("[\n  a,\n  {b: 1, c: [2]},\n]\n");
        var sequence = Assert.IsType<YamlFlowSequence>(Assert.Single(doc.Documents).Root);
        Assert.Equal(2, sequence.Items.Count);
        var map = Assert.IsType<YamlFlowMapping>(sequence.Items[1]);
        Assert.Equal(new[] { "b", "c" }, map.Entries.Select(e => e.Key.Value).ToArray());
        Assert.Equal("1", Assert.IsType<YamlScalar>(map.Entries[0].Value).Value);
        Assert.IsType<YamlFlowSequence>(map.Entries[1].Value);
    }

    [Fact]
    public void Flow_mapping_entry_without_a_value_has_no_colon()
    {
        YamlFlowMapping map = Assert.IsType<YamlFlowMapping>(Assert.Single(Parse("{a, b: }\n").Documents).Root);
        Assert.Null(map.Entries[0].Colon);
        Assert.Null(map.Entries[0].Value);
        Assert.NotNull(map.Entries[1].Colon);
        Assert.Null(map.Entries[1].Value);
    }

    [Fact]
    public void Flow_plain_scalar_takes_a_colon_only_before_a_space_or_a_flow_indicator()
    {
        YamlFlowMapping map = Assert.IsType<YamlFlowMapping>(Assert.Single(Parse("{a:b: c, d:,}\n").Documents).Root);
        Assert.Equal("a:b", map.Entries[0].Key.Value);
        Assert.Equal("d", map.Entries[1].Key.Value);
        Assert.Null(map.Entries[1].Value);
    }

    [Fact]
    public void Aliases_and_anchors_are_read_as_names_and_are_not_expanded()
    {
        YamlBlockMapping mapping = RootMapping(Parse("base: &b\n  x: 1\nuse: *b\n"));
        var anchored = Assert.IsType<YamlBlockMapping>(mapping.Entries[0].Value);
        Assert.Equal("&b", anchored.Anchor!.Text);
        var alias = Assert.IsType<YamlAlias>(mapping.Entries[1].Value);
        Assert.Equal("b", alias.Name);
        Assert.Equal("*b", Assert.IsType<YamlToken>(alias.Parts[0]).Text);
    }

    [Fact]
    public void Anchor_and_tag_may_come_in_either_order()
    {
        YamlBlockMapping mapping = RootMapping(Parse("a: !t &x v\nb: &x !t v\n"));
        var first = Assert.IsType<YamlScalar>(mapping.Entries[0].Value);
        Assert.Equal("!t", first.Tag!.Text);
        Assert.Equal("&x", first.Anchor!.Text);
        var second = Assert.IsType<YamlScalar>(mapping.Entries[1].Value);
        Assert.Equal("&x", second.Anchor!.Text);
        Assert.Equal("!t", second.Tag!.Text);
    }

    [Fact]
    public void Anchor_on_a_key_belongs_to_the_key()
    {
        YamlBlockMapping mapping = RootMapping(Parse("&k key: v\n"));
        Assert.Equal("&k", mapping.Entries.Single().Key.Anchor!.Text);
        Assert.Null(mapping.Anchor);
    }

    [Fact]
    public void Comments_are_marked_trailing_when_content_comes_before_them_on_their_line()
    {
        YamlDocument doc = Parse("a: 1 # trailing\n# own line\n");
        List<YamlComment> comments = Leaves(doc).OfType<YamlComment>().ToList();
        Assert.Equal(2, comments.Count);
        Assert.True(comments[0].IsTrailing);
        Assert.Equal(" trailing", comments[0].Content);
        Assert.False(comments[1].IsTrailing);
        Assert.Equal(" own line", comments[1].Content);
    }

    [Fact]
    public void Several_documents_each_hold_their_root_and_markers()
    {
        YamlDocument doc = Parse("---\na: 1\n...\n---\nb: 2\n");
        Assert.Equal(2, doc.Documents.Count);
        Assert.NotNull(doc.Documents[0].StartMarker);
        Assert.NotNull(doc.Documents[0].EndMarker);
        Assert.Equal("a", Assert.IsType<YamlBlockMapping>(doc.Documents[0].Root).Entries.Single().Key.Value);
        Assert.Equal("b", Assert.IsType<YamlBlockMapping>(doc.Documents[1].Root).Entries.Single().Key.Value);
        Assert.Null(doc.Documents[1].EndMarker);
    }

    [Fact]
    public void Directives_are_held_by_their_document()
    {
        YamlDocument doc = Parse("%YAML 1.2\n%TAG !e! tag:example.com,2000:\n---\n!e!foo bar\n");
        YamlDocumentNode document = Assert.Single(doc.Documents);
        Assert.Equal(new[] { "YAML", "TAG" }, document.Directives.Select(d => d.Name).ToArray());
        Assert.Equal("1.2", document.Directives[0].Arguments);
        Assert.Equal("!e! tag:example.com,2000:", document.Directives[1].Arguments);
        var scalar = Assert.IsType<YamlScalar>(document.Root);
        Assert.Equal("bar", scalar.Value);
        Assert.Equal("!e!foo", scalar.Tag!.Text);
    }

    [Fact]
    public void A_stream_of_comments_has_no_documents_but_keeps_its_text()
    {
        YamlDocument doc = Parse("# one\n\n# two\n");
        Assert.Empty(doc.Documents);
        AssertTiles(doc);
    }

    [Fact]
    public void Explicit_document_with_no_content_has_a_null_root()
    {
        YamlDocument doc = Parse("---\n...\n");
        Assert.Null(Assert.Single(doc.Documents).Root);
    }

    [Fact]
    public void Byte_order_mark_is_kept_by_the_source_and_not_in_the_text()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'a', (byte)':', (byte)' ', (byte)'1', (byte)'\n'];
        var source = SourceText.Decode(bytes, "bom.yaml");
        YamlDocument doc = YamlDocument.Parse(source);
        Assert.True(source.HasBom);
        Assert.Equal(bytes, source.Encode());
        Assert.Equal("a: 1\n", source.Text);
        AssertTiles(doc);
    }

    [Fact]
    public void A_lone_carriage_return_is_a_line_break()
    {
        YamlBlockMapping mapping = RootMapping(Parse("a: 1\rb: 2\r"));
        Assert.Equal(2, mapping.Entries.Count);
        Assert.Equal(2, Parse("a: 1\rb: 2\r").Source.GetPosition(5).Line);
    }

    [Fact]
    public void Nesting_at_the_limit_parses_and_one_more_level_is_refused()
    {
        string atLimit = new string('[', 256) + new string(']', 256);
        AssertTiles(Parse(atLimit));
        string over = new string('[', 257) + new string(']', 257);
        AssertRefused(over, "nesting is deeper than 256 levels");
    }

    [Fact]
    public void Block_nesting_at_the_limit_is_counted_too()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 257; i++)
        {
            sb.Append(new string(' ', i)).Append("k:\n");
        }

        AssertRefused(sb.ToString(), "nesting is deeper than 256 levels");
    }
}
