using System.Text;
using Cfgedit.Core;
using Cfgedit.Core.Model;
using Shared.Core;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// The YAML document tree and the logical tree: exact dumps of rows, paths, parts and kinds, the placement of comments, read-only parts,
/// the empty file, scalar roots, several documents, implicit empty documents, tags and merge keys.
/// </summary>
public sealed class YamlTreeTests
{
    private static readonly YamlFormat Format = new();

    private static ConfigDocument Parse(string text)
    {
        return Format.Parse(SourceText.Create(text, Encoding.UTF8, false));
    }

    /// <summary>One line per row, indented by depth: the kind, the path, each part as [kind "value"], and the YAML kind of a scalar.</summary>
    private static string Dump(ConfigDocument document)
    {
        var sb = new StringBuilder();
        Append(document.Root, 0, sb);
        return sb.ToString();
    }

    private static void Append(DocNode node, int depth, StringBuilder sb)
    {
        sb.Append(' ', depth * 2).Append(node.Kind).Append(' ').Append(node.Path);
        foreach (DocPart part in node.Parts)
        {
            sb.Append(" [").Append(part.Kind).Append(' ').Append(DisplayText.Quote(part.Value)).Append(']');
        }

        if (node.YamlKind is { } kind)
        {
            sb.Append(" yaml:").Append(kind);
        }

        sb.Append('\n');
        foreach (DocNode child in node.Children)
        {
            Append(child, depth + 1, sb);
        }
    }

    [Fact]
    public void Comments_are_placed_with_their_entries_and_the_read_only_parts_are_shown()
    {
        ConfigDocument document = Parse(
            """
            # head comment
            a: 1 # trailing
            # above b
            b:
              # leading inner
              c: &anc 2
              # after c

              d: *anc
              e: !!str x
            d: [1, 2] # tail
            list:
              - one # t1
              # between items
              - two
            # end comment

            """);

        Assert.Equal(
            """
            Document /
              YamlMapping /Item(0) [Root "$"] [Summary "4"]
                YamlComment /Item(0)/Comment(0) [Comment " head comment"]
                YamlScalar /Item(0)/Member("a", 0) [Key "a"] [Value "1"] yaml:Integer
                YamlComment /Item(0)/Comment(1) [Comment " trailing"]
                YamlComment /Item(0)/Comment(2) [Comment " above b"]
                YamlMapping /Item(0)/Member("b", 0) [Key "b"] [Summary "3"]
                  YamlComment /Item(0)/Member("b", 0)/Comment(0) [Comment " leading inner"]
                  YamlScalar /Item(0)/Member("b", 0)/Member("c", 0) [Key "c"] [Anchor "&anc"] [Value "2"] yaml:Integer
                  YamlComment /Item(0)/Member("b", 0)/Comment(1) [Comment " after c"]
                  YamlAlias /Item(0)/Member("b", 0)/Member("d", 0) [Key "d"] [Alias "*anc"]
                  YamlScalar /Item(0)/Member("b", 0)/Member("e", 0) [Key "e"] [Tag "!!str"] [Value "x"] yaml:String
                YamlSequence /Item(0)/Member("d", 0) [Key "d"] [Summary "2"]
                  YamlScalar /Item(0)/Member("d", 0)/Item(0) [Index "0"] [Value "1"] yaml:Integer
                  YamlScalar /Item(0)/Member("d", 0)/Item(1) [Index "1"] [Value "2"] yaml:Integer
                YamlComment /Item(0)/Comment(3) [Comment " tail"]
                YamlSequence /Item(0)/Member("list", 0) [Key "list"] [Summary "2"]
                  YamlScalar /Item(0)/Member("list", 0)/Item(0) [Index "0"] [Value "one"] yaml:String
                  YamlComment /Item(0)/Member("list", 0)/Comment(0) [Comment " t1"]
                  YamlComment /Item(0)/Member("list", 0)/Comment(1) [Comment " between items"]
                  YamlScalar /Item(0)/Member("list", 0)/Item(1) [Index "1"] [Value "two"] yaml:String
                YamlComment /Item(0)/Comment(4) [Comment " end comment"]

            """,
            Dump(document));
    }

    [Fact]
    public void Several_documents_are_rows_with_their_index_and_the_comments_before_each_go_with_it()
    {
        ConfigDocument document = Parse(
            """
            # first doc comment
            ---
            name: one
            ...
            # second doc comment
            ---
            - x
            - y

            """);

        Assert.Equal(
            """
            Document /
              YamlDocument /Item(0) [Index "0"]
                YamlMapping /Item(0)/Item(0) [Root "$"] [Summary "1"]
                  YamlComment /Item(0)/Item(0)/Comment(0) [Comment " first doc comment"]
                  YamlScalar /Item(0)/Item(0)/Member("name", 0) [Key "name"] [Value "one"] yaml:String
              YamlDocument /Item(1) [Index "1"]
                YamlSequence /Item(1)/Item(0) [Root "$"] [Summary "2"]
                  YamlComment /Item(1)/Item(0)/Comment(0) [Comment " second doc comment"]
                  YamlScalar /Item(1)/Item(0)/Item(0) [Index "0"] [Value "x"] yaml:String
                  YamlScalar /Item(1)/Item(0)/Item(1) [Index "1"] [Value "y"] yaml:String

            """,
            Dump(document));
    }

    [Fact]
    public void An_empty_file_is_an_empty_mapping_root()
    {
        ConfigDocument document = Parse(string.Empty);

        Assert.Equal("Document /\n  YamlMapping /Item(0) [Root \"$\"] [Summary \"0\"]\n", Dump(document));
        Assert.Equal("Document\n  Object\n  Array name=\"#comments\"", Format.ToLogical(document).ToString());
    }

    [Fact]
    public void A_file_with_only_comments_has_the_comments_in_the_empty_root()
    {
        ConfigDocument document = Parse("# only\n");

        Assert.Equal(
            "Document /\n  YamlMapping /Item(0) [Root \"$\"] [Summary \"0\"]\n    YamlComment /Item(0)/Comment(0) [Comment \" only\"]\n",
            Dump(document));
    }

    [Fact]
    public void A_bare_document_end_marker_is_not_a_document()
    {
        ConfigDocument document = Parse("# c\n...\n");

        Assert.Equal(
            "Document\n  Object\n  Array name=\"#comments\"\n    Comment value=\" c\"",
            Format.ToLogical(document).ToString());
    }

    [Fact]
    public void A_scalar_root_is_a_row_with_the_root_marker_and_its_value()
    {
        ConfigDocument document = Parse("hello\n");

        Assert.Equal("Document /\n  YamlScalar /Item(0) [Root \"$\"] [Value \"hello\"] yaml:String\n", Dump(document));
        Assert.Equal(
            "Document\n  Scalar yaml=String plain value=\"hello\"\n  Array name=\"#comments\"",
            Format.ToLogical(document).ToString());
    }

    [Fact]
    public void A_key_with_no_value_is_a_null_scalar_with_an_empty_value_part()
    {
        ConfigDocument document = Parse("a:\nb: ~\nc: !!null\n");

        Assert.Equal(
            """
            Document /
              YamlMapping /Item(0) [Root "$"] [Summary "3"]
                YamlScalar /Item(0)/Member("a", 0) [Key "a"] [Value ""] yaml:Null
                YamlScalar /Item(0)/Member("b", 0) [Key "b"] [Value "~"] yaml:Null
                YamlScalar /Item(0)/Member("c", 0) [Key "c"] [Tag "!!null"] [Value ""] yaml:Null

            """,
            Dump(document));
    }

    [Fact]
    public void A_custom_tag_handle_resolves_to_its_full_name_in_the_logical_tree()
    {
        ConfigDocument document = Parse("%TAG !e! tag:example.com,2000:\n---\nx: !e!foo bar\n");

        Assert.Equal(
            """
            Document
              Object
                Scalar name="x" yaml=String plain tag="tag:example.com,2000:foo" value="bar"
              Array name="#comments"
            """,
            Format.ToLogical(document).ToString());
    }

    [Fact]
    public void Flow_entries_without_a_value_are_null_scalars_and_a_merge_key_is_an_ordinary_member_with_an_alias()
    {
        ConfigDocument document = Parse("base: &b {x: 1}\nd:\n  <<: *b\n  y: 2\nf: {a, z: 3}\n");

        Assert.Equal(
            """
            Document
              Object
                Object name="base" &b
                  Scalar name="x" yaml=Integer plain value="1"
                Object name="d"
                  Alias name="<<" value="b"
                  Scalar name="y" yaml=Integer plain value="2"
                Object name="f"
                  Scalar name="a" yaml=Null plain value=""
                  Scalar name="z" yaml=Integer plain value="3"
              Array name="#comments"
            """,
            Format.ToLogical(document).ToString());
    }

    [Fact]
    public void A_tagged_value_that_does_not_read_as_its_tag_is_refused_at_the_value()
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse("a: !!int foo\n"));

        Assert.Equal("the value \"foo\" does not read as !!int", ex.Message);
        Assert.Equal(1, ex.Line);
        Assert.Equal(4, ex.Column);
    }

    [Fact]
    public void A_tab_on_a_blank_line_is_refused_because_YamlDotNet_reads_it_differently()
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse("foo: 1\n\t\nbar: 2\n"));

        Assert.Equal("cfgedit cannot verify a tab on a blank line between entries; YamlDotNet reads it differently", ex.Message);
        Assert.Equal(2, ex.Line);
        Assert.Equal(1, ex.Column);
    }

    [Fact]
    public void A_block_scalar_of_blank_lines_with_spaces_at_the_end_is_refused_because_YamlDotNet_reads_it_differently()
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse("- |+\n   \n"));

        Assert.Equal(
            "cfgedit cannot verify a block scalar of blank lines with spaces at the end of the text; YamlDotNet reads it differently",
            ex.Message);
    }

    [Fact]
    public void A_block_scalar_that_ends_in_spaces_with_no_line_break_is_refused_because_YamlDotNet_reads_it_differently()
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse("foo: |\n  x\n   "));

        Assert.Equal(
            "cfgedit cannot verify a block scalar that ends in spaces with no line break; YamlDotNet reads it differently",
            ex.Message);
    }

    private static ConfigDocument Fixture(string name)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "yaml", name));
        return Format.Parse(SourceText.Decode(bytes, name));
    }

    private static DocNode Row(ConfigDocument document, string path)
    {
        return Rows(document.Root).Single(node => node.Path.ToString() == path);
    }

    private static IEnumerable<DocNode> Rows(DocNode node)
    {
        yield return node;
        foreach (DocNode child in node.Children)
        {
            foreach (DocNode inner in Rows(child))
            {
                yield return inner;
            }
        }
    }

    [Fact]
    public void Compose_file_shows_the_logging_anchor_and_its_two_aliases_as_read_only_rows()
    {
        ConfigDocument document = Fixture("docker-compose.yml");

        DocNode anchor = Row(document, "/Item(0)/Member(\"x-logging\", 0)");
        Assert.Contains(anchor.Parts, part => part.Kind == PartKind.Anchor && part.Value == "&default-logging" && !part.Editable);
        foreach (string service in new[] { "web", "cache" })
        {
            DocNode alias = Row(document, $"/Item(0)/Member(\"services\", 0)/Member(\"{service}\", 0)/Member(\"logging\", 0)");
            Assert.Equal(NodeKind.YamlAlias, alias.Kind);
            Assert.Contains(alias.Parts, part => part.Kind == PartKind.Alias && part.Value == "*default-logging" && !part.Editable);
        }
    }

    [Fact]
    public void Two_document_manifest_is_two_document_rows_each_with_one_root()
    {
        ConfigDocument document = Fixture("kubernetes.yaml");

        DocNode[] rows = document.Root.Children.Where(child => child.Kind == NodeKind.YamlDocument).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.Single(row.Children, child => child.Kind != NodeKind.YamlComment));
    }

    [Fact]
    public void Appsettings_has_an_anchored_mapping_and_merge_keys_that_are_aliases_of_it()
    {
        ConfigDocument document = Fixture("appsettings.yaml");

        DocNode defaults = Row(document, "/Item(0)/Member(\"Defaults\", 0)");
        Assert.Contains(defaults.Parts, part => part.Kind == PartKind.Anchor && part.Value == "&defaults");
        DocNode development = Row(document, "/Item(0)/Member(\"Development\", 0)/Member(\"<<\", 0)");
        Assert.Equal(NodeKind.YamlAlias, development.Kind);
        Assert.Contains(development.Parts, part => part.Kind == PartKind.Alias && part.Value == "*defaults");
        DocNode production = Row(document, "/Item(0)/Member(\"Production\", 0)/Member(\"<<\", 0)/Item(0)");
        Assert.Equal(NodeKind.YamlAlias, production.Kind);
    }

    [Fact]
    public void Workflow_keeps_the_on_key_as_text_in_the_tree()
    {
        ConfigDocument document = Fixture("github-workflow.yml");

        DocNode on = Row(document, "/Item(0)/Member(\"on\", 0)");
        Assert.Contains(on.Parts, part => part.Kind == PartKind.Key && part.Value == "on");
    }

    [Fact]
    public void Every_row_of_a_fixture_has_a_syntax_node_for_the_planner()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "yaml", "docker-compose.yml"));
        ConfigDocument document = Format.Parse(SourceText.Decode(bytes, "docker-compose.yml"));

        var model = (YamlModel)document.Syntax;
        var stack = new Stack<DocNode>();
        stack.Push(document.Root);
        while (stack.Count > 0)
        {
            DocNode node = stack.Pop();
            if (node.Kind != NodeKind.Document)
            {
                Assert.NotNull(model.SyntaxOf(node));
            }

            foreach (DocNode child in node.Children)
            {
                stack.Push(child);
            }
        }
    }
}
