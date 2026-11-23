using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// MoveNodeTo and NestBy on YAML: moves into a mapping or sequence above or below, keys, shapes (a member as an item, an item as a member,
/// compact items), block and flow destinations, the empty collections, re-indenting, comments, anchors and aliases, multi-document files,
/// focus, and the nest rules at the edge of a root collection.
/// </summary>
public sealed class YamlEditMoveToTests
{
    private static readonly IConfigFormat Yaml = EditTestSupport.Yaml;

    private static NodePath Root => At(Item(0));

    [Fact]
    public void A_member_moves_into_the_mapping_above_and_goes_last()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "x:\n  a: 1\ny: 2\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("y")), At(Item(0), Member("x")), 1, null));

        Assert.Equal("x:\n  a: 1\n  y: 2\n", text);
        Assert.Equal(At(Item(0), Member("x"), Member("y")), focus);
    }

    [Fact]
    public void A_member_moves_into_the_mapping_below_and_goes_first()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "a: 1\nb:\n  x: 1\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("b")), 0, null));

        Assert.Equal("b:\n  a: 1\n  x: 1\n", text);
        Assert.Equal(At(Item(0), Member("b"), Member("a")), focus);
    }

    [Fact]
    public void An_item_moves_into_the_sequence_above_and_goes_first()
    {
        string text = Edit(
            "items:\n  - a\n  - b\nother:\n  - c\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("other"), Item(0)), At(Item(0), Member("items")), 0, null));

        Assert.Equal("items:\n  - c\n  - a\n  - b\nother: []\n", text);
    }

    [Fact]
    public void An_item_moves_into_the_sequence_below_and_goes_in_the_middle()
    {
        string text = Edit(
            "a:\n  - x\nlist:\n  - p\n  - q\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("a"), Item(0)), At(Item(0), Member("list")), 1, null));

        Assert.Equal("a: []\nlist:\n  - p\n  - x\n  - q\n", text);
    }

    [Fact]
    public void A_nested_entry_moves_out_before_its_parent()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "box:\n  a: 1\n  b: 2\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("box"), Member("a")), Root, 0, null));

        Assert.Equal("a: 1\nbox:\n  b: 2\n", text);
        Assert.Equal(At(Item(0), Member("a")), focus);
    }

    [Fact]
    public void A_nested_entry_moves_out_after_its_parent()
    {
        string text = Edit(
            "box:\n  a: 1\n  b: 2\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("box"), Member("b")), Root, 1, null));

        Assert.Equal("box:\n  a: 1\nb: 2\n", text);
    }

    [Fact]
    public void A_member_becomes_an_item_when_it_enters_a_sequence()
    {
        string text = Edit(
            "k: v\nlist:\n  - a\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("list")), 1, null));

        Assert.Equal("list:\n  - a\n  - v\n", text);
    }

    [Fact]
    public void A_member_with_a_block_mapping_value_enters_a_sequence_in_the_compact_form()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "cfg:\n  a: 1\n  b: 2\nlist:\n  - x\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("cfg")), At(Item(0), Member("list")), 1, null));

        Assert.Equal("list:\n  - x\n  - a: 1\n    b: 2\n", text);
        Assert.Equal(At(Item(0), Member("list"), Item(1)), focus);
    }

    [Fact]
    public void A_member_with_a_block_sequence_value_enters_a_sequence_in_the_compact_form()
    {
        string text = Edit(
            "cfg:\n  - a\n  - b\nlist:\n  - x\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("cfg")), At(Item(0), Member("list")), 1, null));

        Assert.Equal("list:\n  - x\n  - - a\n    - b\n", text);
    }

    [Fact]
    public void A_member_with_a_block_value_and_properties_enters_a_sequence_with_the_properties_on_the_dash_line()
    {
        string text = Edit(
            "cfg: &c\n  a: 1\nlist:\n  - x\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("cfg")), At(Item(0), Member("list")), 1, null));

        Assert.Equal("list:\n  - x\n  - &c\n    a: 1\n", text);
    }

    [Fact]
    public void A_compact_item_enters_a_mapping_with_its_key_and_its_collection_below_the_key()
    {
        string text = Edit(
            "list:\n  - a: 1\n    b: 2\n  - c\nbox:\n  x: 1\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("list"), Item(0)), At(Item(0), Member("box")), 1, "k"));

        Assert.Equal("list:\n  - c\nbox:\n  x: 1\n  k:\n    a: 1\n    b: 2\n", text);
    }

    [Fact]
    public void A_scalar_item_enters_a_mapping_with_the_new_key()
    {
        string text = Edit(
            "items:\n  - v\nbox:\n  a: 1\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("items"), Item(0)), At(Item(0), Member("box")), 1, "z"));

        Assert.Equal("items: []\nbox:\n  a: 1\n  z: v\n", text);
    }

    [Fact]
    public void A_member_keeps_its_key_when_it_enters_a_mapping_and_takes_the_new_key_when_it_is_renamed()
    {
        string kept = Edit("k: 1\nbox:\n  a: 1\n", Yaml, new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("box")), 1, null));
        string renamed = Edit("k: 1\nbox:\n  a: 1\n", Yaml, new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("box")), 1, "n"));

        Assert.Equal("box:\n  a: 1\n  k: 1\n", kept);
        Assert.Equal("box:\n  a: 1\n  n: 1\n", renamed);
    }

    [Fact]
    public void A_member_enters_a_block_sequence_that_is_not_indented_under_its_key()
    {
        string text = Edit("k: v\nitems:\n- a\n", Yaml, new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("items")), 1, null));

        Assert.Equal("items:\n- a\n- v\n", text);
    }

    [Fact]
    public void A_member_enters_a_block_sequence_that_is_indented_under_its_key()
    {
        string text = Edit("k: v\nitems:\n  - a\n", Yaml, new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("items")), 1, null));

        Assert.Equal("items:\n  - a\n  - v\n", text);
    }

    [Fact]
    public void A_member_enters_an_empty_mapping_on_its_key_line_as_the_insert_does()
    {
        string text = Edit("k: 1\nbox: {}\n", Yaml, new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("box")), 0, null));

        Assert.Equal("box:\n  k: 1\n", text);
    }

    [Fact]
    public void A_member_enters_an_empty_flow_sequence_as_an_item()
    {
        string text = Edit("k: 1\nlist: []\n", Yaml, new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("list")), 0, null));

        Assert.Equal("list:\n  - 1\n", text);
    }

    [Fact]
    public void A_flow_collection_value_moves_into_a_block_sequence_with_its_text_kept()
    {
        string text = Edit("k: [1, 2]\nlist:\n  - x\n", Yaml, new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("list")), 1, null));

        Assert.Equal("list:\n  - x\n  - [1, 2]\n", text);
    }

    [Fact]
    public void A_flow_member_enters_a_block_sequence_as_an_item()
    {
        string text = Edit("box: {k: 1, j: 2}\nlist:\n  - x\n", Yaml, new MoveNodeTo(At(Item(0), Member("box"), Member("k")), At(Item(0), Member("list")), 1, null));

        Assert.Equal("box: {j: 2}\nlist:\n  - x\n  - 1\n", text);
    }

    [Fact]
    public void A_flow_item_enters_a_block_mapping_with_the_new_key()
    {
        string text = Edit("box: [1, 2]\nmap:\n  a: 1\n", Yaml, new MoveNodeTo(At(Item(0), Member("box"), Item(0)), At(Item(0), Member("map")), 1, "n"));

        Assert.Equal("box: [2]\nmap:\n  a: 1\n  n: 1\n", text);
    }

    [Fact]
    public void A_flow_member_enters_a_flow_sequence_and_an_emptied_flow_mapping_stays_flow()
    {
        string text = Edit("box: {k: 1}\nlist: [x]\n", Yaml, new MoveNodeTo(At(Item(0), Member("box"), Member("k")), At(Item(0), Member("list")), 0, null));

        Assert.Equal("box: {}\nlist: [1, x]\n", text);
    }

    [Fact]
    public void The_last_entry_moves_out_and_leaves_its_block_collection_empty_as_a_delete_does()
    {
        string text = Edit(
            "box:\n  only: 1\nother:\n  x: 2\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("box"), Member("only")), At(Item(0), Member("other")), 1, null));

        Assert.Equal("box: {}\nother:\n  x: 2\n  only: 1\n", text);
    }

    [Fact]
    public void Comments_attached_to_the_moved_entry_move_with_it()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "# c1\na: 1\nbox:\n  x: 1\nz: 9\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("box")), 1, null));

        Assert.Equal("box:\n  x: 1\n  # c1\n  a: 1\nz: 9\n", text);
        Assert.Equal(At(Item(0), Member("box"), Member("a")), focus);
    }

    [Fact]
    public void A_multi_line_value_moves_deeper_with_its_literal_block_and_comment_kept()
    {
        string text = Edit(
            "a:\n  k: |\n    line1\n    # not a comment, it is content\n  # real\n  z: 1\np:\n    q: 1\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("p")), 1, null));

        Assert.Equal(
            "p:\n    q: 1\n    a:\n      k: |\n        line1\n        # not a comment, it is content\n      # real\n      z: 1\n",
            text);
    }

    [Fact]
    public void A_multi_line_value_moves_shallower_with_its_literal_block_and_comment_kept()
    {
        string text = Edit(
            "outer:\n  inner:\n    k: |\n      line1\n    # real\n    z: 1\nbox:\n  x: 1\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("outer"), Member("inner")), Root, 1, null));

        Assert.Equal("outer: {}\ninner:\n  k: |\n    line1\n  # real\n  z: 1\nbox:\n  x: 1\n", text);
    }

    [Fact]
    public void A_literal_block_with_an_indentation_indicator_is_refused_when_it_moves()
    {
        CfgeditException ex = Fails(
            "a:\n  k: |2\n      line1\nbox:\n  x: 1\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("box")), 1, null));

        Assert.Contains("indentation indicator", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_multi_line_plain_scalar_moves_deeper_and_its_continuation_line_moves_with_it()
    {
        string text = Edit(
            "a:\n  k: one\n    two\np:\n    q: 1\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("p")), 1, null));

        Assert.Equal("p:\n    q: 1\n    a:\n      k: one\n        two\n", text);
    }

    [Fact]
    public void A_trailing_comment_moves_with_its_value_when_a_member_becomes_an_item()
    {
        string text = Edit("a: 1 # t\nlist:\n  - x\n", Yaml, new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("list")), 1, null));

        Assert.Equal("list:\n  - x\n  - 1 # t\n", text);
    }

    [Fact]
    public void A_move_keeps_crlf_line_breaks()
    {
        string text = Edit("a: 1\r\nbox:\r\n  x: 1\r\n", Yaml, new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("box")), 1, null));

        Assert.Equal("box:\r\n  x: 1\r\n  a: 1\r\n", text);
    }

    [Fact]
    public void A_scalar_moves_into_a_flow_sequence_as_a_flow_item()
    {
        string text = Edit("k: 1\nbox: [a]\n", Yaml, new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("box")), 1, null));

        Assert.Equal("box: [a, 1]\n", text);
    }

    [Fact]
    public void A_block_mapping_value_is_refused_in_a_flow_collection()
    {
        CfgeditException ex = Fails(
            "k:\n  x: 1\nbox: [a]\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("box")), 1, null));

        Assert.Equal("a block value cannot go into a flow collection", ex.Message);
    }

    [Fact]
    public void A_plain_scalar_with_a_comma_is_refused_in_a_flow_collection()
    {
        CfgeditException ex = Fails(
            "k: a,b\nbox: [x]\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("box")), 1, null));

        Assert.Equal("a plain scalar with a flow indicator cannot go into a flow collection as it is written: quote it first", ex.Message);
    }

    [Fact]
    public void A_moved_scalar_keeps_its_text_when_it_is_quoted_for_a_flow_collection()
    {
        string text = Edit("k: 'a,b'\nbox: [x]\n", Yaml, new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("box")), 1, null));

        Assert.Equal("box: [x, 'a,b']\n", text);
    }

    [Fact]
    public void An_anchor_moved_after_its_alias_is_refused()
    {
        CfgeditException ex = Fails(
            "a: &x 1\nb: *x\nbox:\n  z: 0\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("box")), 1, null));

        Assert.Equal("the alias *x would come before its anchor &x", ex.Message);
    }

    [Fact]
    public void An_alias_moved_before_its_anchor_is_refused()
    {
        CfgeditException ex = Fails(
            "box:\n  z: 0\nd: &x 2\nb: *x\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("b")), At(Item(0), Member("box")), 0, null));

        Assert.Equal("the alias *x would come before its anchor &x", ex.Message);
    }

    [Fact]
    public void An_alias_whose_nearest_anchor_would_change_because_of_a_duplicate_anchor_is_refused()
    {
        CfgeditException ex = Fails(
            "a: &x 1\nbox:\n  p: &x 2\nb: *x\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("box"), Member("p")), Root, 0, null));

        Assert.Equal("the alias *x would no longer refer to the same anchor", ex.Message);
    }

    [Fact]
    public void An_anchored_node_moves_where_its_aliases_still_resolve()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "a: &x 1\nbox:\n  z: 0\nb: *x\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("box")), 1, null));

        Assert.Equal("box:\n  z: 0\n  a: &x 1\nb: *x\n", text);
        Assert.Equal(At(Item(0), Member("box"), Member("a")), focus);
    }

    [Fact]
    public void A_merge_key_is_refused_in_a_sequence()
    {
        CfgeditException ex = Fails(
            "base: &b\n  x: 1\nm:\n  <<: *b\nlist:\n  - y\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("m"), Member("<<")), At(Item(0), Member("list")), 0, null));

        Assert.Equal("a merge key cannot move into a sequence", ex.Message);
    }

    [Fact]
    public void A_key_that_is_already_in_the_destination_is_refused()
    {
        CfgeditException ex = Fails("a: 1\nbox:\n  a: 2\n", Yaml, new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("box")), 1, null));

        Assert.Equal("a key named 'a' already exists here", ex.Message);
    }

    [Fact]
    public void An_item_entering_a_mapping_without_a_key_is_refused()
    {
        CfgeditException ex = Fails(
            "items:\n  - v\nbox:\n  a: 1\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("items"), Item(0)), At(Item(0), Member("box")), 0, null));

        Assert.Equal("a key is needed for an entry of a mapping", ex.Message);
    }

    [Fact]
    public void A_key_given_for_an_item_of_a_sequence_is_refused()
    {
        CfgeditException ex = Fails(
            "k: 1\nlist:\n  - a\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Member("k")), At(Item(0), Member("list")), 0, "x"));

        Assert.Equal("an item of a sequence has no key", ex.Message);
    }

    [Fact]
    public void A_node_cannot_move_into_a_document_other_than_its_own()
    {
        CfgeditException ex = Fails(
            "---\nx: 1\n---\nb:\n  c: 2\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Item(0), Member("x")), At(Item(1), Item(0), Member("b")), 0, null));

        Assert.Equal("a node cannot move into another document", ex.Message);
    }

    [Fact]
    public void A_comment_moves_alone_into_a_block_mapping()
    {
        string text = Edit(
            "a: 1\n# lone\nbox:\n  x: 1\n",
            Yaml,
            new MoveNodeTo(At(Item(0), Comment(0)), At(Item(0), Member("box")), 1, null));

        Assert.Equal("a: 1\nbox:\n  x: 1\n  # lone\n", text);
    }

    [Fact]
    public void A_move_inside_the_second_document_stays_there_and_its_focus_is_in_that_document()
    {
        (string text, NodePath? focus) = EditWithFocus(
            "---\nx: 1\n---\na: 1\nb:\n  c: 2\n",
            Yaml,
            new MoveNodeTo(At(Item(1), Item(0), Member("a")), At(Item(1), Item(0), Member("b")), 1, null));

        Assert.Equal("---\nx: 1\n---\nb:\n  c: 2\n  a: 1\n", text);
        Assert.Equal(At(Item(1), Item(0), Member("b"), Member("a")), focus);
    }

    [Fact]
    public void The_root_mapping_of_a_document_has_no_grandparent_to_nest_into()
    {
        Assert.Null(ModelTestSupport.Parse("a: 1\nbox:\n  x: 1\n", Yaml).NestBy(At(Item(0), Member("a")), -1));
        Assert.Null(ModelTestSupport.Parse("---\na: 1\n---\nb: 2\n", Yaml).NestBy(At(Item(1), Item(0), Member("b")), -1));
    }

    [Fact]
    public void Nest_down_into_the_mapping_below_puts_the_member_first()
    {
        NestStep? step = ModelTestSupport.Parse("a: 1\nb:\n  x: 1\n", Yaml).NestBy(At(Item(0), Member("a")), 1);

        Assert.Equal(new MoveNodeTo(At(Item(0), Member("a")), At(Item(0), Member("b")), 0, null), step!.Op);
        Assert.False(step.NeedsKey);
    }

    [Fact]
    public void Nest_up_from_a_sequence_item_into_a_mapping_above_asks_for_a_key()
    {
        // The first item has no sibling above it, so it leaves the sequence: it goes into the root mapping, just before the sequence.
        NestStep? step = ModelTestSupport.Parse("box:\n  x: 1\nlist:\n  - v\n", Yaml).NestBy(At(Item(0), Member("list"), Item(0)), -1);

        Assert.Equal(new MoveNodeTo(At(Item(0), Member("list"), Item(0)), Root, 1, null), step!.Op);
        Assert.True(step.NeedsKey);
    }

    [Fact]
    public void Nest_out_of_a_nested_mapping_goes_before_its_parent()
    {
        NestStep? step = ModelTestSupport.Parse("box:\n  a: 1\n  b: 2\n", Yaml).NestBy(At(Item(0), Member("box"), Member("a")), -1);

        Assert.Equal(new MoveNodeTo(At(Item(0), Member("box"), Member("a")), Root, 0, null), step!.Op);
    }
}
