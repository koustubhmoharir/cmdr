using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// <see cref="ConfigDocument.MoveBy"/> on TOML: the move one sibling unit up or down, null at the ends, across comment groups, and fixed
/// around a table header's own trailing comment. Each move returned is also applied, to check that the text does what it names.
/// </summary>
public sealed class TomlMoveByTests
{
    private static readonly IConfigFormat Toml = EditTestSupport.Toml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    [Fact]
    public void Members_at_the_ends_of_a_section_have_no_move_beyond_them()
    {
        ConfigDocument document = ModelTestSupport.Parse("a = 1\nb = 2\n", Toml);

        Assert.Null(document.MoveBy(Path(Member("a")), -1));
        Assert.Null(document.MoveBy(Path(Member("b")), 1));
        Assert.Equal(new MoveNode(Path(Member("a")), 1), document.MoveBy(Path(Member("a")), 1));
        Assert.Equal(new MoveNode(Path(Member("b")), 0), document.MoveBy(Path(Member("b")), -1));
    }

    [Fact]
    public void A_move_passes_a_whole_comment_group_at_once()
    {
        const string text = "# c\na = 1\n\n# d\nb = 2\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Toml);

        MoveNode? down = document.MoveBy(Path(Member("a")), 1);
        Assert.Equal(new MoveNode(Path(Member("a")), 2), down);
        Assert.Equal("# d\nb = 2\n\n# c\na = 1\n", Edit(text, Toml, down!));

        MoveNode? up = document.MoveBy(Path(Member("b")), -1);
        Assert.Equal(new MoveNode(Path(Member("b")), 0), up);
        Assert.Equal("# d\nb = 2\n\n# c\na = 1\n", Edit(text, Toml, up!));
    }

    [Fact]
    public void A_standalone_comment_moves_alone_past_the_entry_it_is_attached_to()
    {
        const string text = "# c\na = 1\n\n# d\nb = 2\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Toml);

        MoveNode? move = document.MoveBy(Path(Comment(0)), 1);

        Assert.Equal(new MoveNode(Path(Comment(0)), 1), move);
        Assert.Equal("a = 1\n# c\n\n# d\nb = 2\n", Edit(text, Toml, move!));
    }

    [Fact]
    public void A_key_value_cannot_move_above_a_table_headers_own_trailing_comment()
    {
        ConfigDocument document = ModelTestSupport.Parse("[t] # h\nx = 1\ny = 2\n", Toml);

        Assert.Null(document.MoveBy(Path(Member("t"), Member("x")), -1));
        Assert.Null(document.MoveBy(Path(Member("t"), Comment(0)), 1));
        Assert.Null(document.MoveBy(Path(Member("t"), Comment(0)), -1));
        Assert.Equal(new MoveNode(Path(Member("t"), Member("y")), 1), document.MoveBy(Path(Member("t"), Member("y")), -1));
    }

    [Fact]
    public void Array_of_tables_items_move_as_regions_and_not_past_the_ends()
    {
        const string text = "[[x]]\na = 1\n[[x]]\na = 2\n[[x]]\na = 3\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Toml);

        Assert.Null(document.MoveBy(Path(Member("x"), Item(0)), -1));
        Assert.Null(document.MoveBy(Path(Member("x"), Item(2)), 1));
        MoveNode? move = document.MoveBy(Path(Member("x"), Item(1)), -1);
        Assert.Equal(new MoveNode(Path(Member("x"), Item(1)), 0), move);
        Assert.Equal("[[x]]\na = 2\n[[x]]\na = 1\n[[x]]\na = 3\n", Edit(text, Toml, move!));
    }

    [Fact]
    public void Inline_array_items_and_inline_table_members_move_by_one()
    {
        ConfigDocument document = ModelTestSupport.Parse("a = [1, 2]\nt = { p = 1, q = 2 }\n", Toml);

        Assert.Equal(new MoveNode(Path(Member("a"), Item(1)), 0), document.MoveBy(Path(Member("a"), Item(1)), -1));
        Assert.Null(document.MoveBy(Path(Member("a"), Item(0)), -1));
        Assert.Equal(new MoveNode(Path(Member("t"), Member("p")), 1), document.MoveBy(Path(Member("t"), Member("p")), 1));
        Assert.Null(document.MoveBy(Path(Member("t"), Member("q")), 1));
    }
}
