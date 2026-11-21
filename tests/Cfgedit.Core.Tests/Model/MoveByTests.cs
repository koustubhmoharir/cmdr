using Cfgedit.Core;
using Cfgedit.Core.Model;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// <see cref="ConfigDocument.MoveBy"/> and <see cref="ConfigDocument.MoveAttributeBy"/>: the operation they return, and the text that
/// applying it gives. Groups are an entry with its attached comments; a free comment is a group of its own.
/// </summary>
public sealed class MoveByTests
{
    private static readonly IConfigFormat XmlConfig = Formats.FromName("xml")!;
    private static readonly NodePath XmlRoot = At(Element("r"));

    [Fact]
    public void A_member_moves_up_past_the_member_before_it()
    {
        const string text = "{\n  \"a\": 1,\n  \"b\": 2,\n  \"c\": 3\n}";

        MoveNode move = Move(text, Strict, At(Item(0), Member("c")), -1);

        Assert.Equal(1, move.NewIndex);
        Assert.Equal("{\n  \"a\": 1,\n  \"c\": 3,\n  \"b\": 2\n}", Edit(text, Strict, move));
    }

    [Fact]
    public void A_member_moves_down_past_the_member_after_it()
    {
        const string text = "{\n  \"a\": 1,\n  \"b\": 2,\n  \"c\": 3\n}";

        MoveNode move = Move(text, Strict, At(Item(0), Member("a")), 1);

        Assert.Equal(1, move.NewIndex);
        Assert.Equal("{\n  \"b\": 2,\n  \"a\": 1,\n  \"c\": 3\n}", Edit(text, Strict, move));
    }

    [Fact]
    public void An_array_item_moves_down()
    {
        const string text = "[\n  1,\n  2,\n  3\n]";

        MoveNode move = Move(text, Strict, At(Item(0), Item(1)), 1);

        Assert.Equal(2, move.NewIndex);
        Assert.Equal("[\n  1,\n  3,\n  2\n]", Edit(text, Strict, move));
    }

    [Fact]
    public void At_the_first_or_last_position_there_is_no_move()
    {
        const string text = "{\n  \"a\": 1,\n  \"b\": 2\n}";

        Assert.Null(ModelTestSupport.Parse(text, Strict).MoveBy(At(Item(0), Member("a")), -1));
        Assert.Null(ModelTestSupport.Parse(text, Strict).MoveBy(At(Item(0), Member("b")), 1));
    }

    [Fact]
    public void A_member_moves_across_a_free_comment_as_one_step()
    {
        // The comment is separated from "b" by a blank line, so it is a group of its own, not an attached comment.
        const string text = "{\n  \"a\": 1,\n  // free\n\n  \"b\": 2\n}";

        MoveNode move = Move(text, Jsonc, At(Item(0), Member("b")), -1);

        Assert.Equal(1, move.NewIndex);
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2\n  // free\n\n}", Edit(text, Jsonc, move));
    }

    [Fact]
    public void A_free_comment_moves_down_past_the_member_after_it()
    {
        const string text = "{\n  \"a\": 1,\n  // free\n\n  \"b\": 2\n}";

        MoveNode move = Move(text, Jsonc, At(Item(0), Comment(0)), 1);

        Assert.Equal(2, move.NewIndex);
        Assert.Equal("{\n  \"a\": 1,\n\n  \"b\": 2\n  // free\n}", Edit(text, Jsonc, move));
    }

    [Fact]
    public void An_entry_moves_with_its_leading_and_trailing_comments()
    {
        // "// lead a" is on the line above a, and "// trail a" is on a's line, so both belong to a.
        const string text = "{\n  // lead a\n  \"a\": 1, // trail a\n  \"b\": 2\n}";

        MoveNode move = Move(text, Jsonc, At(Item(0), Member("a")), 1);

        Assert.Equal(1, move.NewIndex);
        Assert.Equal("{\n  \"b\": 2,\n  // lead a\n  \"a\": 1 // trail a\n}", Edit(text, Jsonc, move));
    }

    [Fact]
    public void A_comment_moves_alone_out_of_the_group_of_the_entry_it_leads()
    {
        const string text = "{\n  \"a\": 1,\n  // lead b\n  \"b\": 2\n}";

        MoveNode move = Move(text, Jsonc, At(Item(0), Comment(0)), -1);

        Assert.Equal(0, move.NewIndex);
        Assert.Equal("{\n  // lead b\n  \"a\": 1,\n  \"b\": 2\n}", Edit(text, Jsonc, move));
    }

    [Fact]
    public void A_comment_moves_down_past_the_entry_it_leads()
    {
        // The comment is in b's group, so the move passes b alone, and the comment ends after it.
        const string text = "{\n  \"a\": 1,\n  // lead b\n  \"b\": 2\n}";

        MoveNode move = Move(text, Jsonc, At(Item(0), Comment(0)), 1);

        Assert.Equal(2, move.NewIndex);
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2\n  // lead b\n}", Edit(text, Jsonc, move));
    }

    [Fact]
    public void A_trailing_comment_of_an_entry_moves_past_its_sibling_comment_alone()
    {
        // Both comments are on a's line, so both belong to a. Moving the first one down passes the second alone, and a stays put.
        const string text = "{\n  \"a\": 1, /*t1*/ /*t2*/\n  \"b\": 2\n}";

        MoveNode move = Move(text, Jsonc, At(Item(0), Comment(0)), 1);

        Assert.Equal(2, move.NewIndex);
        Assert.Equal("{\n  \"a\": 1, /*t2*/ /*t1*/\n  \"b\": 2\n}", Edit(text, Jsonc, move));
    }

    [Fact]
    public void The_root_value_cannot_move_and_the_first_comment_cannot_move_up()
    {
        const string text = "// c\n[1]";

        Assert.Null(ModelTestSupport.Parse(text, Jsonc).MoveBy(RootValue, -1));
        Assert.Null(ModelTestSupport.Parse(text, Jsonc).MoveBy(RootValue, 1));
        Assert.Null(ModelTestSupport.Parse(text, Jsonc).MoveBy(At(Comment(0)), -1));
    }

    [Fact]
    public void A_top_level_comment_moves_below_the_root_value()
    {
        const string text = "// c\n[1]";

        MoveNode move = Move(text, Jsonc, At(Comment(0)), 1);

        Assert.Equal(1, move.NewIndex);
        Assert.Equal("[1]\n// c", Edit(text, Jsonc, move));
    }

    [Fact]
    public void An_element_moves_up_past_its_sibling()
    {
        const string text = "<r>\n  <a/>\n  <b/>\n  <c/>\n</r>";

        MoveNode move = Move(text, XmlConfig, At(Element("r"), Element("c")), -1);

        Assert.Equal(1, move.NewIndex);
        Assert.Equal("<r>\n  <a/>\n  <c/>\n  <b/>\n</r>", Edit(text, XmlConfig, move));
    }

    [Fact]
    public void An_element_moves_across_a_free_comment_as_one_step()
    {
        const string text = "<r>\n  <a/>\n  <!--free-->\n\n  <b/>\n</r>";

        MoveNode move = Move(text, XmlConfig, At(Element("r"), Element("b")), -1);

        Assert.Equal(1, move.NewIndex);
        Assert.Equal("<r>\n  <a/>\n  <b/>\n  <!--free-->\n\n</r>", Edit(text, XmlConfig, move));
    }

    [Fact]
    public void An_element_moves_with_its_leading_and_trailing_comments()
    {
        const string text = "<r>\n  <!--lead-->\n  <a/> <!--trail-->\n  <b/>\n</r>";

        MoveNode move = Move(text, XmlConfig, At(Element("r"), Element("a")), 1);

        Assert.Equal(1, move.NewIndex);
        Assert.Equal("<r>\n  <b/>\n  <!--lead-->\n  <a/> <!--trail-->\n</r>", Edit(text, XmlConfig, move));
    }

    [Fact]
    public void A_top_level_comment_moves_after_the_root_element_but_not_above_the_declaration()
    {
        const string text = "<?xml version=\"1.0\"?>\n<!--c-->\n<r/>";

        Assert.Null(ModelTestSupport.Parse(text, XmlConfig).MoveBy(At(Comment(0)), -1));

        MoveNode move = Move(text, XmlConfig, At(Comment(0)), 1);
        Assert.Equal("<?xml version=\"1.0\"?>\n<r/>\n<!--c-->", Edit(text, XmlConfig, move));
    }

    [Fact]
    public void The_root_element_cannot_move()
    {
        const string text = "<!--c-->\n<r/>";

        Assert.Null(ModelTestSupport.Parse(text, XmlConfig).MoveBy(XmlRoot, -1));
        Assert.Null(ModelTestSupport.Parse(text, XmlConfig).MoveBy(XmlRoot, 1));
    }

    [Fact]
    public void An_attribute_moves_down_and_up_and_stops_at_the_ends()
    {
        const string text = "<r a=\"1\" b=\"2\" c=\"3\"/>";
        ConfigDocument document = ModelTestSupport.Parse(text, XmlConfig);

        MoveAttribute down = Assert.IsType<MoveAttribute>(document.MoveAttributeBy(XmlRoot, 0, 1));
        Assert.Equal(new MoveAttribute(XmlRoot, 0, 1), down);
        Assert.Equal("<r b=\"2\" a=\"1\" c=\"3\"/>", Edit(text, XmlConfig, down));

        Assert.Equal("<r a=\"1\" c=\"3\" b=\"2\"/>", Edit(text, XmlConfig, Assert.IsType<MoveAttribute>(document.MoveAttributeBy(XmlRoot, 1, 1))));
        Assert.Equal("<r b=\"2\" a=\"1\" c=\"3\"/>", Edit(text, XmlConfig, Assert.IsType<MoveAttribute>(document.MoveAttributeBy(XmlRoot, 1, -1))));
        Assert.Null(document.MoveAttributeBy(XmlRoot, 0, -1));
        Assert.Null(document.MoveAttributeBy(XmlRoot, 2, 1));
    }

    [Fact]
    public void An_attribute_moves_across_lines_keeping_the_layout()
    {
        const string text = "<r\n  a=\"1\"\n  b=\"2\"/>";

        MoveAttribute move = Assert.IsType<MoveAttribute>(ModelTestSupport.Parse(text, XmlConfig).MoveAttributeBy(XmlRoot, 1, -1));

        Assert.Equal("<r\n  b=\"2\"\n  a=\"1\"/>", Edit(text, XmlConfig, move));
    }

    [Fact]
    public void A_delta_other_than_one_or_minus_one_is_refused()
    {
        ConfigDocument document = ModelTestSupport.Parse("{\"a\": 1, \"b\": 2}", Strict);

        Assert.Throws<ArgumentOutOfRangeException>(() => document.MoveBy(At(Item(0), Member("a")), 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.MoveBy(At(Item(0), Member("a")), 0));
    }

    [Fact]
    public void A_missing_node_or_attribute_is_refused()
    {
        ConfigDocument document = ModelTestSupport.Parse("<r a=\"1\"/>", XmlConfig);

        Assert.Throws<ArgumentException>(() => document.MoveBy(At(Element("r"), Element("missing")), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.MoveAttributeBy(XmlRoot, 1, 1));
    }

    /// <summary>The move that <see cref="ConfigDocument.MoveBy"/> returns, which must exist.</summary>
    private static MoveNode Move(string text, IConfigFormat format, NodePath node, int delta)
    {
        return Assert.IsType<MoveNode>(ModelTestSupport.Parse(text, format).MoveBy(node, delta));
    }
}
