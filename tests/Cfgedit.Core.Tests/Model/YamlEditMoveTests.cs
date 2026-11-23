using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// Move on YAML: block entries and items with their attached and trailing comments and nested lines, compact '- key:' items that move as
/// wholes, flow items (one line, several lines, trailing commas), documents with their markers, anchors and aliases that a move must keep in
/// order, the units that cannot move, the last line without a break, block scalars that keep their line breaks, and CRLF.
/// </summary>
public sealed class YamlEditMoveTests
{
    private static readonly IConfigFormat Yaml = EditTestSupport.Yaml;

    private const string FirstKey = "the first key of a '- key:' item cannot move; move the item";

    private const string FirstItem = "the first item of a '- -' item cannot move; move the item";

    private const string Trailing = "a trailing comment moves with its entry";

    private const string Marker = "only documents with a '---' marker can move";

    private const string KeptBreak = "this cannot move: a block scalar keeps the line breaks at the end of the file";

    private const string RootCannotMove = "the root value cannot be moved";

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    private static NodePath Document(int index) => At(Item(index));

    // ----- Block entries and items -----

    [Fact]
    public void A_block_entry_moves_up_with_its_nested_lines_and_attached_comment()
    {
        const string text = "# c1\na: 1\nb:\n  x: 1\n  y: 2 # t\n# c2\nc: 3\n";

        string moved = Edit(text, Yaml, new MoveNode(Path(Member("b")), 0));

        Assert.Equal("b:\n  x: 1\n  y: 2 # t\n# c1\na: 1\n# c2\nc: 3\n", moved);
    }

    [Fact]
    public void A_block_entry_moves_down_past_a_neighbour_with_its_attached_comment()
    {
        const string text = "# c1\na: 1\nb:\n  x: 1\n  y: 2 # t\n# c2\nc: 3\n";

        string moved = Edit(text, Yaml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal("b:\n  x: 1\n  y: 2 # t\n# c1\na: 1\n# c2\nc: 3\n", moved);
    }

    [Fact]
    public void A_block_entry_moves_to_the_end_past_a_group_of_comment_and_entry()
    {
        const string text = "# c1\na: 1\nb:\n  x: 1\n  y: 2 # t\n# c2\nc: 3\n";

        string moved = Edit(text, Yaml, new MoveNode(Path(Member("b")), 4));

        Assert.Equal("# c1\na: 1\n# c2\nc: 3\nb:\n  x: 1\n  y: 2 # t\n", moved);
    }

    [Fact]
    public void Blank_lines_between_units_stay_in_place()
    {
        const string text = "a: 1\n\nb: 2\nc: 3\n";

        string moved = Edit(text, Yaml, new MoveNode(Path(Member("a")), 2));

        Assert.Equal("b: 2\n\nc: 3\na: 1\n", moved);
    }

    [Fact]
    public void An_indented_block_sequence_item_moves_with_its_dash_line()
    {
        const string text = "items:\n  - a\n  - b\n  - c\n";

        string moved = Edit(text, Yaml, new MoveNode(Path(Member("items"), Item(0)), 2));

        Assert.Equal("items:\n  - b\n  - c\n  - a\n", moved);
    }

    [Fact]
    public void A_block_sequence_item_moves_up_when_the_dashes_are_not_indented()
    {
        const string text = "items:\n- a\n- b\n";

        string moved = Edit(text, Yaml, new MoveNode(Path(Member("items"), Item(1)), 0));

        Assert.Equal("items:\n- b\n- a\n", moved);
    }

    [Fact]
    public void A_compact_item_moves_as_a_whole_including_its_dash()
    {
        const string text = "- a: 1\n  b: 2\n- c: 3\n";

        string moved = Edit(text, Yaml, new MoveNode(Path(Item(1)), 0));

        Assert.Equal("- c: 3\n- a: 1\n  b: 2\n", moved);
    }

    [Fact]
    public void The_first_key_of_a_compact_item_cannot_move()
    {
        CfgeditException ex = Fails("- a: 1\n  b: 2\n", Yaml, new MoveNode(Path(Item(0), Member("a")), 1));

        Assert.Equal(FirstKey, ex.Message);
    }

    [Fact]
    public void Another_key_cannot_move_above_the_first_key_of_a_compact_item()
    {
        CfgeditException ex = Fails("- a: 1\n  b: 2\n", Yaml, new MoveNode(Path(Item(0), Member("b")), 0));

        Assert.Equal(FirstKey, ex.Message);
    }

    [Fact]
    public void The_first_item_of_a_sequence_on_a_dash_line_cannot_move()
    {
        CfgeditException ex = Fails("- - a\n  - b\n", Yaml, new MoveNode(Path(Item(0), Item(0)), 1));

        Assert.Equal(FirstItem, ex.Message);
    }

    [Fact]
    public void A_later_item_cannot_move_above_the_first_item_on_a_dash_line()
    {
        CfgeditException ex = Fails("- - a\n  - b\n- c\n", Yaml, new MoveNode(Path(Item(0), Item(1)), 0));

        Assert.Equal(FirstItem, ex.Message);
    }

    [Fact]
    public void A_sequence_item_below_a_dash_line_item_moves_with_its_line()
    {
        string moved = Edit("- c\n- - a\n  - b\n", Yaml, new MoveNode(Path(Item(0)), 1));

        Assert.Equal("- - a\n  - b\n- c\n", moved);
    }

    [Fact]
    public void A_trailing_comment_cannot_move_alone()
    {
        CfgeditException ex = Fails("a: 1 # t\nb: 2\n", Yaml, new MoveNode(Path(Comment(0)), 1));

        Assert.Equal(Trailing, ex.Message);
    }

    [Fact]
    public void The_root_value_cannot_move()
    {
        CfgeditException ex = Fails("a: 1\n", Yaml, new MoveNode(Document(0), 0));

        Assert.Equal(RootCannotMove, ex.Message);
    }

    // ----- Flow collections -----

    [Fact]
    public void A_flow_item_moves_to_the_end_of_a_one_line_sequence()
    {
        string moved = Edit("k: [a, b, c]\n", Yaml, new MoveNode(Path(Member("k"), Item(0)), 2));

        Assert.Equal("k: [b, c, a]\n", moved);
    }

    [Fact]
    public void A_flow_item_moves_to_the_front_of_a_several_line_sequence()
    {
        string moved = Edit("k: [\n  a,\n  b,\n  c\n  ]\n", Yaml, new MoveNode(Path(Member("k"), Item(2)), 0));

        Assert.Equal("k: [\n  c,\n  a,\n  b\n  ]\n", moved);
    }

    [Fact]
    public void A_flow_item_keeps_its_comment_line_above_it()
    {
        string moved = Edit("k: [\n  # one\n  a,\n  b\n  ]\n", Yaml, new MoveNode(Path(Member("k"), Item(1)), 0));

        Assert.Equal("k: [\n  b,\n  # one\n  a\n  ]\n", moved);
    }

    [Fact]
    public void A_flow_item_with_a_trailing_comment_after_its_comma_moves_with_it()
    {
        string moved = Edit("k: [\n  a, # ca\n  b\n  ]\n", Yaml, new MoveNode(Path(Member("k"), Item(0)), 1));

        Assert.Equal("k: [\n  b,\n  a # ca\n  ]\n", moved);
    }

    [Fact]
    public void A_flow_entry_with_a_trailing_comment_cannot_land_where_the_bracket_would_follow_the_comment()
    {
        CfgeditException ex = Fails("k: [a, # c\n  b]\n", Yaml, new MoveNode(Path(Member("k"), Item(0)), 1));

        Assert.Equal("this cannot move there: a comment would run into the next entry", ex.Message);
    }

    [Fact]
    public void The_trailing_comma_of_a_flow_sequence_stays_at_its_end()
    {
        string moved = Edit("k: [a, b,]\n", Yaml, new MoveNode(Path(Member("k"), Item(0)), 1));

        Assert.Equal("k: [b, a,]\n", moved);
    }

    [Fact]
    public void A_flow_mapping_entry_moves_with_its_comma()
    {
        string moved = Edit("{x: 1, y: 2}\n", Yaml, new MoveNode(Path(Member("y")), 0));

        Assert.Equal("{y: 2, x: 1}\n", moved);
    }

    // ----- Documents -----

    [Fact]
    public void Documents_with_explicit_markers_swap()
    {
        string moved = Edit("---\na: 1\n---\nb: 2\n", Yaml, new MoveNode(Document(1), 0));

        Assert.Equal("---\nb: 2\n---\na: 1\n", moved);
    }

    [Fact]
    public void A_document_without_a_marker_cannot_move()
    {
        CfgeditException ex = Fails("a: 1\n---\nb: 2\n", Yaml, new MoveNode(Document(1), 0));

        Assert.Equal(Marker, ex.Message);
    }

    // ----- Anchors and aliases -----

    [Fact]
    public void An_alias_cannot_move_above_its_anchor()
    {
        CfgeditException ex = Fails("a: &x 1\nb: *x\n", Yaml, new MoveNode(Path(Member("b")), 0));

        Assert.Equal("*x would come before &x", ex.Message);
    }

    [Fact]
    public void An_anchored_entry_cannot_move_below_an_alias_that_uses_it()
    {
        CfgeditException ex = Fails("a: &x 1\nb: *x\n", Yaml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal("*x would come before &x", ex.Message);
    }

    [Fact]
    public void An_anchor_cannot_move_where_an_alias_would_name_another_anchor_of_the_same_name()
    {
        CfgeditException ex = Fails("a: &x 1\nb: &x 2\nc: *x\n", Yaml, new MoveNode(Path(Member("b")), 0));

        Assert.Equal("*x would refer to another &x", ex.Message);
    }

    [Fact]
    public void An_alias_and_its_anchor_move_together_inside_the_same_unit()
    {
        string moved = Edit("a: 1\nb:\n  c: &x 2\n  d: *x\ne: 3\n", Yaml, new MoveNode(Path(Member("b")), 0));

        Assert.Equal("b:\n  c: &x 2\n  d: *x\na: 1\ne: 3\n", moved);
    }

    // ----- The last line and line breaks -----

    [Fact]
    public void A_last_line_without_a_break_moves_up_and_the_moved_line_takes_one()
    {
        string moved = Edit("a: 1\nb: 2", Yaml, new MoveNode(Path(Member("b")), 0));

        Assert.Equal("b: 2\na: 1", moved);
    }

    [Fact]
    public void A_line_moves_to_the_end_of_a_file_without_a_final_break()
    {
        string moved = Edit("a: 1\nb: 2\nc: 3", Yaml, new MoveNode(Path(Member("a")), 2));

        Assert.Equal("b: 2\nc: 3\na: 1", moved);
    }

    [Fact]
    public void A_last_line_moves_to_the_front_of_a_file_without_a_final_break()
    {
        string moved = Edit("a: 1\nb: 2\nc: 3", Yaml, new MoveNode(Path(Member("c")), 0));

        Assert.Equal("c: 3\na: 1\nb: 2", moved);
    }

    [Fact]
    public void A_literal_block_that_keeps_its_lines_moves_with_them()
    {
        string moved = Edit("a: |+\n  x\n\nb: 1\n", Yaml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal("b: 1\na: |+\n  x\n\n", moved);
    }

    [Fact]
    public void A_literal_block_that_keeps_its_lines_cannot_take_the_end_of_a_file_without_a_final_break()
    {
        CfgeditException ex = Fails("a: |+\n  x\n\nb: 1", Yaml, new MoveNode(Path(Member("a")), 1));

        Assert.Equal(KeptBreak, ex.Message);
    }

    [Fact]
    public void CRLF_lines_move_with_the_document_line_break()
    {
        string moved = Edit("a: 1\r\nb: 2\r\n", Yaml, new MoveNode(Path(Member("b")), 0));

        Assert.Equal("b: 2\r\na: 1\r\n", moved);
    }

    [Fact]
    public void CRLF_lines_gain_the_document_line_break_at_a_missing_final_break()
    {
        string moved = Edit("a: 1\r\nb: 2", Yaml, new MoveNode(Path(Member("b")), 0));

        Assert.Equal("b: 2\r\na: 1", moved);
    }

    // ----- Focus -----

    [Fact]
    public void After_a_move_the_moved_entry_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("a: 1\nb: 2\nc: 3\n", Yaml, new MoveNode(Path(Member("a")), 2));

        Assert.Equal(Path(Member("a")), focus);
    }

    [Fact]
    public void After_a_move_a_moved_item_takes_the_focus_at_its_new_index()
    {
        var (_, focus) = EditWithFocus("- a\n- b\n- c\n", Yaml, new MoveNode(Path(Item(0)), 2));

        Assert.Equal(Path(Item(2)), focus);
    }

    [Fact]
    public void After_a_move_of_a_comment_the_comment_takes_the_focus()
    {
        var (_, focus) = EditWithFocus("# c\na: 1\n\n# d\nb: 2\n", Yaml, new MoveNode(Path(Comment(0)), 1));

        Assert.Equal(Path(Comment(0)), focus);
    }
}
