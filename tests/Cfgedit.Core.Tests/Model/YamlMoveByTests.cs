using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// <see cref="ConfigDocument.MoveBy"/> on YAML: the move one sibling unit up or down, null at the ends, across comment groups, and a comment
/// that moves alone. Each move returned is also applied, to check that the text does what it names.
/// </summary>
public sealed class YamlMoveByTests
{
    private static readonly IConfigFormat Yaml = EditTestSupport.Yaml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    [Fact]
    public void Entries_at_the_ends_of_a_mapping_have_no_move_beyond_them()
    {
        ConfigDocument document = ModelTestSupport.Parse("a: 1\nb: 2\n", Yaml);

        Assert.Null(document.MoveBy(Path(Member("a")), -1));
        Assert.Null(document.MoveBy(Path(Member("b")), 1));
        Assert.Equal(new MoveNode(Path(Member("a")), 1), document.MoveBy(Path(Member("a")), 1));
        Assert.Equal(new MoveNode(Path(Member("b")), 0), document.MoveBy(Path(Member("b")), -1));
        Assert.Equal("b: 2\na: 1\n", Edit("a: 1\nb: 2\n", Yaml, document.MoveBy(Path(Member("a")), 1)!));
    }

    [Fact]
    public void Documents_with_markers_move_by_one_with_alt_up_and_down()
    {
        const string Text = "---\na: 1\n---\nb: 2\n";
        ConfigDocument document = ModelTestSupport.Parse(Text, Yaml);

        Assert.Null(document.MoveBy(At(Item(0)), -1));
        Assert.Equal(new MoveNode(At(Item(0)), 1), document.MoveBy(At(Item(0)), 1));
        Assert.Equal("---\nb: 2\n---\na: 1\n", Edit(Text, Yaml, document.MoveBy(At(Item(0)), 1)!));
    }

    [Fact]
    public void Items_at_the_ends_of_a_sequence_have_no_move_beyond_them()
    {
        ConfigDocument document = ModelTestSupport.Parse("- a\n- b\n", Yaml);

        Assert.Null(document.MoveBy(Path(Item(0)), -1));
        Assert.Null(document.MoveBy(Path(Item(1)), 1));
        Assert.Equal("- b\n- a\n", Edit("- a\n- b\n", Yaml, document.MoveBy(Path(Item(1)), -1)!));
    }

    [Fact]
    public void A_flow_item_and_a_compact_item_move_by_one_sibling()
    {
        const string flow = "k: [\n  a,\n  b\n  ]\n";
        ConfigDocument flowDocument = ModelTestSupport.Parse(flow, Yaml);
        MoveNode? up = flowDocument.MoveBy(Path(Member("k"), Item(1)), -1);
        Assert.Equal(new MoveNode(Path(Member("k"), Item(1)), 0), up);
        Assert.Equal("k: [\n  b,\n  a\n  ]\n", Edit(flow, Yaml, up!));

        const string compact = "- a: 1\n  b: 2\n- c: 3\n";
        ConfigDocument compactDocument = ModelTestSupport.Parse(compact, Yaml);
        MoveNode? compactUp = compactDocument.MoveBy(Path(Item(1)), -1);
        Assert.Equal(new MoveNode(Path(Item(1)), 0), compactUp);
        Assert.Equal("- c: 3\n- a: 1\n  b: 2\n", Edit(compact, Yaml, compactUp!));
    }

    [Fact]
    public void A_move_passes_a_whole_comment_group_at_once()
    {
        const string text = "# c\na: 1\n\n# d\nb: 2\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Yaml);

        MoveNode? down = document.MoveBy(Path(Member("a")), 1);
        Assert.Equal(new MoveNode(Path(Member("a")), 2), down);
        Assert.Equal("# d\nb: 2\n\n# c\na: 1\n", Edit(text, Yaml, down!));

        MoveNode? up = document.MoveBy(Path(Member("b")), -1);
        Assert.Equal(new MoveNode(Path(Member("b")), 0), up);
        Assert.Equal("# d\nb: 2\n\n# c\na: 1\n", Edit(text, Yaml, up!));
    }

    [Fact]
    public void A_standalone_comment_moves_alone_past_the_entry_it_is_attached_to()
    {
        const string text = "# c\na: 1\n\n# d\nb: 2\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Yaml);

        MoveNode? move = document.MoveBy(Path(Comment(0)), 1);

        Assert.Equal(new MoveNode(Path(Comment(0)), 1), move);
        Assert.Equal("a: 1\n# c\n\n# d\nb: 2\n", Edit(text, Yaml, move!));
    }

    [Fact]
    public void The_first_comment_of_a_file_cannot_move_up()
    {
        ConfigDocument document = ModelTestSupport.Parse("# c\na: 1\n", Yaml);

        Assert.Null(document.MoveBy(Path(Comment(0)), -1));
    }

    [Fact]
    public void A_trailing_comment_moved_on_its_own_is_refused_when_applied()
    {
        ConfigDocument document = ModelTestSupport.Parse("a: 1 # t\nb: 2\n", Yaml);

        MoveNode? move = document.MoveBy(Path(Comment(0)), 1);

        Assert.NotNull(move);
        Assert.Equal("a: 1 # t\nb: 2\n", document.Source.Text);
        Assert.Equal("a trailing comment moves with its entry", Fails("a: 1 # t\nb: 2\n", Yaml, move!).Message);
    }
}
