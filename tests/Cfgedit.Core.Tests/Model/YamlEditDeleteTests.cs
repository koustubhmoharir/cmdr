using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>DeleteNode on YAML: entries and items with their comments and nested content, flow commas, emptied collections, anchors, and documents.</summary>
public sealed class YamlEditDeleteTests
{
    private static readonly IConfigFormat Yaml = EditTestSupport.Yaml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    private static DeleteNode Delete(params PathStep[] steps) => new(Path(steps));

    // ----- Block entries and items -----

    [Fact]
    public void A_block_entry_is_deleted_with_its_line()
    {
        Assert.Equal("a: 1\nc: 3\n", Edit("a: 1\nb: 2\nc: 3\n", Yaml, Delete(Member("b"))));
    }

    [Fact]
    public void A_comment_attached_above_an_entry_goes_with_it()
    {
        Assert.Equal("a: 1\n", Edit("a: 1\n# about b\nb: 2\n", Yaml, Delete(Member("b"))));
    }

    [Fact]
    public void A_comment_separated_by_a_blank_line_stays()
    {
        Assert.Equal("a: 1\n# c\n\n", Edit("a: 1\n# c\n\nb: 2\n", Yaml, Delete(Member("b"))));
    }

    [Fact]
    public void A_trailing_comment_goes_with_its_entry()
    {
        Assert.Equal("a: 1\nc: 3\n", Edit("a: 1\nb: 2 # two\nc: 3\n", Yaml, Delete(Member("b"))));
    }

    [Fact]
    public void A_nested_block_entry_is_deleted_with_all_its_lines()
    {
        Assert.Equal("b: 3\n", Edit("a:\n  x: 1\n  y: 2\nb: 3\n", Yaml, Delete(Member("a"))));
    }

    [Fact]
    public void A_block_scalar_entry_is_deleted_with_its_body()
    {
        Assert.Equal("b: 1\n", Edit("a: |\n  x\n  y\nb: 1\n", Yaml, Delete(Member("a"))));
    }

    [Fact]
    public void A_block_sequence_item_is_deleted_with_its_line()
    {
        Assert.Equal("- a\n- c\n", Edit("- a\n- b\n- c\n", Yaml, Delete(Item(1))));
    }

    [Fact]
    public void The_first_entry_of_a_mapping_in_a_sequence_item_is_deleted_from_its_dash_line()
    {
        Assert.Equal("- b: 2\n", Edit("- a: 1\n  b: 2\n", Yaml, Delete(Item(0), Member("a"))));
    }

    [Fact]
    public void The_last_entry_of_a_mapping_in_a_sequence_item_is_deleted_with_its_line()
    {
        Assert.Equal("- a: 1\n", Edit("- a: 1\n  b: 2\n", Yaml, Delete(Item(0), Member("b"))));
    }

    [Fact]
    public void Deleting_the_last_entry_of_a_block_mapping_leaves_an_empty_mapping_on_the_key_line()
    {
        Assert.Equal("key: {}\n", Edit("key:\n  a: 1\n", Yaml, Delete(Member("key"), Member("a"))));
    }

    [Fact]
    public void Deleting_the_last_item_of_a_block_sequence_leaves_an_empty_sequence()
    {
        Assert.Equal("key: []\nb: 1\n", Edit("key:\n  - a\nb: 1\n", Yaml, Delete(Member("key"), Item(0))));
    }

    [Fact]
    public void Deleting_the_only_entry_of_a_root_mapping_makes_the_root_empty()
    {
        Assert.Equal("{}\n", Edit("a: 1\n", Yaml, Delete(Member("a"))));
    }

    [Fact]
    public void Deleting_the_only_entry_of_a_mapping_in_a_sequence_item_leaves_an_empty_mapping_after_the_dash()
    {
        Assert.Equal("- {}\n", Edit("- a: 1\n", Yaml, Delete(Item(0), Member("a"))));
    }

    [Fact]
    public void An_empty_collection_keeps_its_anchor_and_loses_its_entries_comments()
    {
        Assert.Equal("key: &x {}\n", Edit("key: &x\n  # inside\n  a: 1\n", Yaml, Delete(Member("key"), Member("a"))));
    }

    // ----- Flow entries and items -----

    [Fact]
    public void A_middle_flow_entry_is_deleted_with_its_comma()
    {
        Assert.Equal("{a: 1, c: 3}\n", Edit("{a: 1, b: 2, c: 3}\n", Yaml, Delete(Member("b"))));
    }

    [Fact]
    public void The_last_flow_entry_is_deleted_with_the_comma_before_it()
    {
        Assert.Equal("{a: 1}\n", Edit("{a: 1, b: 2}\n", Yaml, Delete(Member("b"))));
    }

    [Fact]
    public void The_only_flow_entry_leaves_empty_braces()
    {
        Assert.Equal("{}\n", Edit("{a: 1}\n", Yaml, Delete(Member("a"))));
        Assert.Equal("[]\n", Edit("[1]\n", Yaml, Delete(Item(0))));
    }

    [Fact]
    public void A_flow_sequence_item_is_deleted_with_its_comma()
    {
        Assert.Equal("[2, 3]\n", Edit("[1, 2, 3]\n", Yaml, Delete(Item(0))));
    }

    [Fact]
    public void A_flow_item_with_a_trailing_comment_after_its_comma_goes_with_the_comment()
    {
        Assert.Equal("[\n 2]\n", Edit("[1, # c\n 2]\n", Yaml, Delete(Item(0))));
    }

    [Fact]
    public void A_flow_item_on_its_own_line_is_deleted_with_the_line()
    {
        Assert.Equal("[\n  1,\n]\n", Edit("[\n  1,\n  2\n]\n", Yaml, Delete(Item(1))));
    }

    // ----- Anchors and aliases -----

    [Fact]
    public void An_anchor_that_an_alias_refers_to_cannot_be_deleted()
    {
        CfgeditException ex = Fails("a: &x 1\nb: *x\n", Yaml, Delete(Member("a")));

        Assert.Equal("*x refers to it", ex.Message);
    }

    [Fact]
    public void An_anchor_that_no_alias_refers_to_can_be_deleted()
    {
        Assert.Equal("b: 2\n", Edit("a: &x 1\nb: 2\n", Yaml, Delete(Member("a"))));
    }

    [Fact]
    public void An_alias_can_be_deleted()
    {
        Assert.Equal("a: &x 1\n", Edit("a: &x 1\nb: *x\n", Yaml, Delete(Member("b"))));
    }

    // ----- Comments -----

    [Fact]
    public void A_comment_line_is_deleted_with_its_line()
    {
        Assert.Equal("a: 1\n", Edit("# x\na: 1\n", Yaml, Delete(Comment(0))));
    }

    [Fact]
    public void A_trailing_comment_is_deleted_with_the_spaces_before_it()
    {
        Assert.Equal("a: 1\n", Edit("a: 1 # c\n", Yaml, Delete(Comment(0))));
    }

    // ----- Documents -----

    [Fact]
    public void A_document_is_deleted_with_its_marker_and_its_content()
    {
        Assert.Equal("a: 1\n---\nc: 3\n", Edit("a: 1\n---\nb: 2\n---\nc: 3\n", Yaml, DeleteDocument(1)));
    }

    [Fact]
    public void Deleting_the_first_of_two_documents_leaves_the_second_with_its_marker()
    {
        Assert.Equal("---\nb: 2\n", Edit("a: 1\n---\nb: 2\n", Yaml, DeleteDocument(0)));
    }

    [Fact]
    public void Deleting_the_last_of_two_documents_leaves_the_first()
    {
        Assert.Equal("a: 1\n", Edit("a: 1\n---\nb: 2\n", Yaml, DeleteDocument(1)));
    }

    [Fact]
    public void A_single_document_root_cannot_be_deleted()
    {
        CfgeditException ex = Fails("a: 1\n", Yaml, new DeleteNode(At(Item(0))));

        Assert.Equal("the root value cannot be deleted", ex.Message);
    }

    [Fact]
    public void A_document_root_cannot_be_deleted_apart_from_its_document()
    {
        CfgeditException ex = Fails("---\na: 1\n---\nb: 2\n", Yaml, new DeleteNode(At(Item(0), Item(0))));

        Assert.Equal("the root value cannot be deleted", ex.Message);
    }

    /// <summary>Deletes a document of a stream with several documents: the path of the document is its index.</summary>
    private static DeleteNode DeleteDocument(int index) => new(At(Item(index)));

    // ----- Focus -----

    [Fact]
    public void After_a_delete_the_next_sibling_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("a: 1\nb: 2\nc: 3\n", Yaml, Delete(Member("b")));

        Assert.Equal(Path(Member("c")), focus);
    }

    [Fact]
    public void After_deleting_the_last_entry_the_previous_one_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("a: 1\nb: 2\n", Yaml, Delete(Member("b")));

        Assert.Equal(Path(Member("a")), focus);
    }

    [Fact]
    public void After_emptying_a_collection_the_collection_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("key:\n  a: 1\n", Yaml, Delete(Member("key"), Member("a")));

        Assert.Equal(Path(Member("key")), focus);
    }

    [Fact]
    public void After_a_document_is_deleted_the_document_that_follows_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("a: 1\n---\nb: 2\n---\nc: 3\n", Yaml, DeleteDocument(1));

        Assert.Equal(At(Item(1)), focus);
    }

    /// <summary>
    /// Every node of a corpus of nasty inputs (comments, block scalars, flow collections, anchors, tags, documents, CRLF), under every kind of
    /// edit. A refusal is the user's; an internal error means the planner and the logical edit disagree, which is cfgedit's bug.
    /// </summary>
    [Theory]
    [InlineData("a: 1\nb: [x, {c: d}]\n")]
    [InlineData("# top\na: |\n  one\n  two\n\nb: >-\n  f\n# between\nc: &x \"q\"\nd: *x\n")]
    [InlineData("- a: 1\n  b:\n    - &y 2\n    - *y\n- [1, 2]\n- {k: v}\n")]
    [InlineData("a: 1 # t1\n# above\nb: 2\n---\nc:\n  - d # t2\n...\n---\n{e: [f, g]}\n")]
    [InlineData("x: y\r\nz:\r\n  - w\r\n")]
    [InlineData("'q': !!str 5\n\"r\": !!int 6\ns: !foo bar\nt: ~\n")]
    [InlineData("---\nroot\n")]
    [InlineData("a: |+\n  keep\n\n# after keep\nb: 1\n")]
    [InlineData("k: [\n  1, # one\n  2]\n")]
    [InlineData("k: [\n  {x: y, # xy\n   z: 2}\n  ]\n")]
    [InlineData("k: [\n  1,\n  # standalone\n  3\n  ]\n")]
    [InlineData("k: [\n  1, # one\n  {x: y, # xy\n   z: [2]},\n  # standalone\n  3\n  ]\n")]
    [InlineData("- - a\n  - b\n- - c\n")]
    [InlineData("&k key: &v value\n&e ~: null\n")]
    [InlineData("a:\nb: !!null\nc: ''\nd: \"\"\n")]
    [InlineData("# c0\n---\na: 1\n# between\n---\n# lead\nb: 2 # t\n...\n# end\n")]
    [InlineData("a: 1\n\n\n# gap\n\nb: 2\n")]
    [InlineData("{a: 1, b: 2}\n")]
    [InlineData("- &i !!str x\n- !t\n  y: 1\n")]
    public void Every_edit_of_every_node_is_applied_or_refused_and_never_an_internal_error(string text)
    {
        ConfigDocument doc = ModelTestSupport.Parse(text, Yaml);
        string[] values = ["x", string.Empty, "true", "1", "1.5", "a\nb", " lead\nb", "it's", "x: y", "# no", "null", "~"];
        var failures = new List<string>();
        foreach (DocNode node in ModelTestSupport.AllNodes(doc.Root))
        {
            var ops = new List<EditOp> { new DeleteNode(node.Path), new SetPart(node.Path, PartKind.Comment, -1, "c") };
            foreach (string v in values)
            {
                ops.Add(new SetPart(node.Path, PartKind.Value, -1, v));
                ops.Add(new SetPart(node.Path, PartKind.Key, -1, v));
                ops.Add(new SetPart(node.Path, PartKind.Comment, -1, v));
            }

            foreach (YamlNewKind kind in Enum.GetValues<YamlNewKind>())
            {
                ops.Add(new ChangeYamlType(node.Path, kind, null));
                ops.Add(new ChangeYamlType(node.Path, kind, "5"));
            }

            ops.Add(new DuplicateNode(node.Path));
            if (node.Kind is NodeKind.YamlMapping or NodeKind.YamlSequence)
            {
                for (int index = 0; index <= node.Children.Count; index++)
                {
                    foreach (string? key in new string?[] { null, "zz" })
                    {
                        foreach (YamlNewKind kind in Enum.GetValues<YamlNewKind>())
                        {
                            ops.Add(new InsertNode(node.Path, index, new NewYamlValue(key, kind, "1")));
                            ops.Add(new InsertNode(node.Path, index, new NewYamlValue(key, kind, "a\nb")));
                        }
                    }

                    ops.Add(new InsertNode(node.Path, index, new NewYamlComment("c")));
                }
            }

            foreach (EditOp op in ops)
            {
                try
                {
                    doc.Apply(op);
                }
                catch (CfgeditException ex)
                {
                    if (ex.Message.Contains("internal", StringComparison.Ordinal))
                    {
                        failures.Add($"{node.Path} {op.GetType().Name} {op} => {ex.Message.Split('\n')[0]}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // ----- CRLF -----

    [Fact]
    public void A_block_entry_in_a_CRLF_document_is_deleted_with_its_line_break()
    {
        Assert.Equal("b: 2\r\n", Edit("a: 1\r\nb: 2\r\n", Yaml, Delete(Member("a"))));
    }
}
