using Cfgedit.Core.Env;
using Cfgedit.Core.Model;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// The .env document tree (groups from "__", comments attached to entries, duplicate keys), the lines each node covers, the flat
/// logical tree, and the refusal of every edit. The expected trees are worked out by hand from the source offsets.
/// </summary>
public sealed class EnvTreeTests
{
    private static readonly IConfigFormat Env = Formats.FromName("env")!;

    private static readonly NodePath Root = NodePath.Empty.Append(new ItemStep(0));

    [Fact]
    public void Empty_file_has_only_the_root_group_with_a_zero_count()
    {
        ConfigDocument document = ModelTestSupport.Parse("", Env);

        string expected = """
            Document / [0,0)
              EnvGroup /Item(0) [0,0) Root"$"[0,0)- Summary"0"[0,0)-
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Flat_keys_are_entries_of_the_root_group_with_key_and_value_parts()
    {
        ConfigDocument document = ModelTestSupport.Parse("A=1\nB=two\n", Env);

        string expected = """
            Document / [0,10)
              EnvGroup /Item(0) [0,10) Root"$"[0,0)- Summary"2"[0,0)-
                EnvEntry /Item(0)/Member("A", 0) [0,4) Key"A"[0,1)+ Value"1"[2,3)+
                EnvEntry /Item(0)/Member("B", 0) [4,10) Key"B"[4,5)+ Value"two"[6,9)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Prefixed_keys_group_under_their_first_entry_even_when_an_unrelated_key_comes_between()
    {
        const string text = "Logging__LogLevel__Default=debug\nOther=x\nLogging__Console=1\nLogging__LogLevel__System=warn\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Env);

        // The group Logging is where its first entry is, before Other. Its group keeps the entries after Other, and its span
        // runs from its first line to its last. Each group's Key part is the segment in its first entry's key.
        string expected = """
            Document / [0,91)
              EnvGroup /Item(0) [0,91) Root"$"[0,0)- Summary"2"[0,0)-
                EnvGroup /Item(0)/Member("Logging", 0) [0,91) Key"Logging"[0,7)+ Summary"2"[7,7)-
                  EnvGroup /Item(0)/Member("Logging", 0)/Member("LogLevel", 0) [0,91) Key"LogLevel"[9,17)+ Summary"2"[17,17)-
                    EnvEntry /Item(0)/Member("Logging", 0)/Member("LogLevel", 0)/Member("Default", 0) [0,33) Key"Default"[19,26)+ Value"debug"[27,32)+
                    EnvEntry /Item(0)/Member("Logging", 0)/Member("LogLevel", 0)/Member("System", 0) [60,91) Key"System"[79,85)+ Value"warn"[86,90)+
                  EnvEntry /Item(0)/Member("Logging", 0)/Member("Console", 0) [41,60) Key"Console"[50,57)+ Value"1"[58,59)+
                EnvEntry /Item(0)/Member("Other", 0) [33,41) Key"Other"[33,38)+ Value"x"[39,40)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Keys_with_an_empty_segment_or_a_segment_that_would_not_round_trip_are_not_split()
    {
        // A____B, __A and A__ have an empty segment. A___B splits into A and _B, and _B starts with an underscore after the first
        // segment. _A__B splits, since its first segment may start with an underscore.
        const string text = "A____B=1\n__A=2\nA__=3\nA___B=4\n_A__B=5\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Env);

        string expected = """
            Document / [0,37)
              EnvGroup /Item(0) [0,37) Root"$"[0,0)- Summary"5"[0,0)-
                EnvEntry /Item(0)/Member("A____B", 0) [0,9) Key"A____B"[0,6)+ Value"1"[7,8)+
                EnvEntry /Item(0)/Member("__A", 0) [9,15) Key"__A"[9,12)+ Value"2"[13,14)+
                EnvEntry /Item(0)/Member("A__", 0) [15,21) Key"A__"[15,18)+ Value"3"[19,20)+
                EnvEntry /Item(0)/Member("A___B", 0) [21,29) Key"A___B"[21,26)+ Value"4"[27,28)+
                EnvGroup /Item(0)/Member("_A", 0) [29,37) Key"_A"[29,31)+ Summary"1"[31,31)-
                  EnvEntry /Item(0)/Member("_A", 0)/Member("B", 0) [29,37) Key"B"[33,34)+ Value"5"[35,36)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void A_segment_before_the_last_may_not_end_in_an_underscore_but_the_last_may()
    {
        // X__Y_ splits into X and Y_: only the segments before the last are checked for a trailing underscore.
        ConfigDocument document = ModelTestSupport.Parse("X__Y_=1\n", Env);

        string expected = """
            Document / [0,8)
              EnvGroup /Item(0) [0,8) Root"$"[0,0)- Summary"1"[0,0)-
                EnvGroup /Item(0)/Member("X", 0) [0,8) Key"X"[0,1)+ Summary"1"[1,1)-
                  EnvEntry /Item(0)/Member("X", 0)/Member("Y_", 0) [0,8) Key"Y_"[3,5)+ Value"1"[6,7)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void An_entry_and_a_group_of_the_same_name_are_siblings_numbered_in_their_order_when_the_group_comes_first()
    {
        ConfigDocument document = ModelTestSupport.Parse("A__B=2\nA=1\n", Env);

        string expected = """
            Document / [0,11)
              EnvGroup /Item(0) [0,11) Root"$"[0,0)- Summary"2"[0,0)-
                EnvGroup /Item(0)/Member("A", 0) [0,7) Key"A"[0,1)+ Summary"1"[1,1)-
                  EnvEntry /Item(0)/Member("A", 0)/Member("B", 0) [0,7) Key"B"[3,4)+ Value"2"[5,6)+
                EnvEntry /Item(0)/Member("A", 1) [7,11) Key"A"[7,8)+ Value"1"[9,10)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void An_entry_and_a_group_of_the_same_name_are_siblings_numbered_in_their_order_when_the_entry_comes_first()
    {
        ConfigDocument document = ModelTestSupport.Parse("A=1\nA__B=2\n", Env);

        string expected = """
            Document / [0,11)
              EnvGroup /Item(0) [0,11) Root"$"[0,0)- Summary"2"[0,0)-
                EnvEntry /Item(0)/Member("A", 0) [0,4) Key"A"[0,1)+ Value"1"[2,3)+
                EnvGroup /Item(0)/Member("A", 1) [4,11) Key"A"[4,5)+ Summary"1"[5,5)-
                  EnvEntry /Item(0)/Member("A", 1)/Member("B", 0) [4,11) Key"B"[7,8)+ Value"2"[9,10)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Duplicate_keys_are_separate_entries_numbered_in_file_order()
    {
        ConfigDocument document = ModelTestSupport.Parse("Dup=1\nDup=2\n", Env);

        string expected = """
            Document / [0,12)
              EnvGroup /Item(0) [0,12) Root"$"[0,0)- Summary"2"[0,0)-
                EnvEntry /Item(0)/Member("Dup", 0) [0,6) Key"Dup"[0,3)+ Value"1"[4,5)+
                EnvEntry /Item(0)/Member("Dup", 1) [6,12) Key"Dup"[6,9)+ Value"2"[10,11)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void A_comment_line_is_attached_to_the_entry_below_it_and_otherwise_stands_at_the_root()
    {
        // "# attached" is followed by an entry, so it is that entry's (the root's) child just before it. "# separated" is followed by a
        // blank line, and "# end" by the end of the file: both are root children at their place. "# about Y" is attached to B__Y, so it
        // sits in group B before Y.
        const string text = "# attached\nA=1\n# separated\n\nB__X=2\n# about Y\nB__Y=3\n# end\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Env);

        string expected = """
            Document / [0,58)
              EnvGroup /Item(0) [0,58) Root"$"[0,0)- Summary"2"[0,0)-
                EnvComment /Item(0)/Comment(0) [0,11) Comment" attached"[1,10)+
                EnvEntry /Item(0)/Member("A", 0) [11,15) Key"A"[11,12)+ Value"1"[13,14)+
                EnvComment /Item(0)/Comment(1) [15,27) Comment" separated"[16,26)+
                EnvGroup /Item(0)/Member("B", 0) [28,52) Key"B"[28,29)+ Summary"2"[29,29)-
                  EnvEntry /Item(0)/Member("B", 0)/Member("X", 0) [28,35) Key"X"[31,32)+ Value"2"[33,34)+
                  EnvComment /Item(0)/Member("B", 0)/Comment(0) [35,45) Comment" about Y"[36,44)+
                  EnvEntry /Item(0)/Member("B", 0)/Member("Y", 0) [45,52) Key"Y"[48,49)+ Value"3"[50,51)+
                EnvComment /Item(0)/Comment(2) [52,58) Comment" end"[53,57)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void A_comment_attached_to_the_first_entry_of_a_group_lies_inside_the_group_and_widens_its_span()
    {
        ConfigDocument document = ModelTestSupport.Parse("# head\nA__X=1\n", Env);

        string expected = """
            Document / [0,14)
              EnvGroup /Item(0) [0,14) Root"$"[0,0)- Summary"1"[0,0)-
                EnvGroup /Item(0)/Member("A", 0) [0,14) Key"A"[7,8)+ Summary"1"[8,8)-
                  EnvComment /Item(0)/Member("A", 0)/Comment(0) [0,7) Comment" head"[1,6)+
                  EnvEntry /Item(0)/Member("A", 0)/Member("X", 0) [7,14) Key"X"[10,11)+ Value"1"[12,13)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Export_and_quotes_leave_the_key_span_and_give_the_value_span_with_its_quotes()
    {
        ConfigDocument document = ModelTestSupport.Parse("export Q=\"x\"\n", Env);

        string expected = """
            Document / [0,13)
              EnvGroup /Item(0) [0,13) Root"$"[0,0)- Summary"1"[0,0)-
                EnvEntry /Item(0)/Member("Q", 0) [0,13) Key"Q"[7,8)+ Value"x"[9,12)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void A_quoted_value_over_two_lines_spans_both_lines_and_shows_its_break_as_backslash_n()
    {
        ConfigDocument document = ModelTestSupport.Parse("M=\"a\nb\"\nN=1\n", Env);

        string expected = """
            Document / [0,12)
              EnvGroup /Item(0) [0,12) Root"$"[0,0)- Summary"2"[0,0)-
                EnvEntry /Item(0)/Member("M", 0) [0,8) Key"M"[0,1)+ Value"a\nb"[2,7)+
                EnvEntry /Item(0)/Member("N", 0) [8,12) Key"N"[8,9)+ Value"1"[10,11)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void A_node_covers_its_own_lines_a_group_covers_its_entries_and_comments_and_the_document_covers_all()
    {
        const string text = "# attached\nA=1\n# separated\n\nB__X=2\n# about Y\nB__Y=3\n# end\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Env);
        var syntax = Assert.IsType<EnvDocument>(document.Syntax);
        DocNode groupB = document.Root.Children[0].Children[3];

        Assert.Equal(new[] { 28, 35, 45 }, EnvModelBuilder.ItemsOf(syntax, groupB).Select(item => item.Start));
        Assert.Equal(new[] { 11 }, EnvModelBuilder.ItemsOf(syntax, document.Root.Children[0].Children[1]).Select(item => item.Start));
        Assert.Equal(new[] { 0 }, EnvModelBuilder.ItemsOf(syntax, document.Root.Children[0].Children[0]).Select(item => item.Start));
        Assert.Equal(7, EnvModelBuilder.ItemsOf(syntax, document.Root).Count);
    }

    [Fact]
    public void The_logical_tree_is_flat_in_file_order_with_full_keys_comments_and_decoded_values()
    {
        const string text = "Logging__Level=debug\n# note\nA=1\nB__C__D='x y'\n";
        LogicalNode logical = Env.ToLogical(ModelTestSupport.Parse(text, Env));

        var expected = new LogicalNode(
            LogicalKind.Document,
            null,
            null,
            null,
            [],
            [
                new LogicalNode(LogicalKind.Scalar, "Logging__Level", JsonValueKind.String, "debug", [], []),
                new LogicalNode(LogicalKind.Comment, null, null, " note", [], []),
                new LogicalNode(LogicalKind.Scalar, "A", JsonValueKind.String, "1", [], []),
                new LogicalNode(LogicalKind.Scalar, "B__C__D", JsonValueKind.String, "x y", [], []),
            ]);
        Assert.Equal(expected, logical);
    }

    [Fact]
    public void Export_quotes_spacing_and_blank_lines_do_not_change_the_logical_tree()
    {
        LogicalNode plain = Env.ToLogical(ModelTestSupport.Parse("A=1\nB=x\n# c\n", Env));
        LogicalNode dressed = Env.ToLogical(ModelTestSupport.Parse("export A = '1'\n\n  B = \"x\"\n# c", Env));

        Assert.Equal(plain, dressed);
    }

    [Fact]
    public void A_trailing_comment_is_a_comment_row_right_after_its_entry_and_counts_in_the_comment_indices()
    {
        // "# note" follows A on its line and "#y" follows the quoted value of B. The comment line before C is attached to C, so it
        // comes after the two trailing comments, and its index counts them.
        const string text = "A=1 # note\nB='x' #y\n# line\nC=2\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Env);

        string expected = """
            Document / [0,31)
              EnvGroup /Item(0) [0,31) Root"$"[0,0)- Summary"3"[0,0)-
                EnvEntry /Item(0)/Member("A", 0) [0,11) Key"A"[0,1)+ Value"1"[2,3)+
                EnvComment /Item(0)/Comment(0) [4,10) Comment" note"[5,10)+
                EnvEntry /Item(0)/Member("B", 0) [11,20) Key"B"[11,12)+ Value"x"[13,16)+
                EnvComment /Item(0)/Comment(1) [17,19) Comment"y"[18,19)+
                EnvComment /Item(0)/Comment(2) [20,27) Comment" line"[21,26)+
                EnvEntry /Item(0)/Member("C", 0) [27,31) Key"C"[27,28)+ Value"2"[29,30)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void The_logical_tree_puts_a_trailing_comment_right_after_its_entry()
    {
        LogicalNode logical = Env.ToLogical(ModelTestSupport.Parse("A=1 # note\nB='x' #y\n# line\nC=2\n", Env));

        var expected = new LogicalNode(
            LogicalKind.Document,
            null,
            null,
            null,
            [],
            [
                new LogicalNode(LogicalKind.Scalar, "A", JsonValueKind.String, "1", [], []),
                new LogicalNode(LogicalKind.Comment, null, null, " note", [], []),
                new LogicalNode(LogicalKind.Scalar, "B", JsonValueKind.String, "x", [], []),
                new LogicalNode(LogicalKind.Comment, null, null, "y", [], []),
                new LogicalNode(LogicalKind.Comment, null, null, " line", [], []),
                new LogicalNode(LogicalKind.Scalar, "C", JsonValueKind.String, "2", [], []),
            ]);
        Assert.Equal(expected, logical);
    }

    [Fact]
    public void A_trailing_comment_covers_its_entry_line_and_is_told_apart_from_the_entry_by_its_start()
    {
        ConfigDocument document = ModelTestSupport.Parse("A=1 # note\nB='x' #y\n# line\nC=2\n", Env);
        var syntax = Assert.IsType<EnvDocument>(document.Syntax);
        DocNode trailing = document.Root.Children[0].Children[1];

        // The node starts at its '#', while the entry's item starts at the line start.
        Assert.Equal(4, trailing.Start);
        Assert.Equal(new[] { 0 }, EnvModelBuilder.ItemsOf(syntax, trailing).Select(item => item.Start));
        Assert.IsType<EnvEntry>(EnvModelBuilder.ItemsOf(syntax, trailing).Single());

        // The document covers each of its four lines once, even though two of them have a trailing comment.
        Assert.Equal(new[] { 0, 11, 20, 27 }, EnvModelBuilder.ItemsOf(syntax, document.Root).Select(item => item.Start));
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void A_key_with_more_segments_than_the_json_nesting_cap_is_not_split(int segments, bool split)
    {
        string key = string.Join("__", Enumerable.Repeat("A", segments));
        ConfigDocument document = ModelTestSupport.Parse(key + "=1\n", Env);

        DocNode node = document.Root.Children[0];
        while (node.Kind == NodeKind.EnvGroup)
        {
            node = node.Children[0];
        }

        // Split, the path has the root step and one member step per segment. Not split, it has the root step and the one entry.
        Assert.Equal(NodeKind.EnvEntry, node.Kind);
        Assert.Equal(split ? 1 + segments : 2, node.Path.Count);
        Assert.Equal(split ? "A" : key, node.Parts[0].Value);
    }

    [Fact]
    public void Moves_duplicates_type_changes_and_attribute_edits_are_refused_with_their_reasons()
    {
        ConfigDocument document = ModelTestSupport.Parse("A=1\n", Env);
        NodePath entry = Root.Append(new MemberStep("A", 0));
        (EditOp Operation, string Reason)[] refusals =
        [
            (new MoveNode(entry, 0), "moving is not supported in .env yet"),
            (new DuplicateNode(entry), "duplicating is not supported in .env yet"),
            (new ChangeJsonType(entry, JsonValueKind.Number, "2"), "`.env` values are all strings"),
            (new InsertAttribute(Root, 0, "x", "y"), "attributes are not part of .env"),
            (new DeleteAttribute(Root, 0), "attributes are not part of .env"),
            (new MoveAttribute(Root, 0, 0), "attributes are not part of .env"),
        ];

        foreach ((EditOp operation, string reason) in refusals)
        {
            CfgeditException error = Assert.Throws<CfgeditException>(() => document.Format.Plan(document, operation));
            Assert.Equal(reason, error.Message);
        }
    }

}
