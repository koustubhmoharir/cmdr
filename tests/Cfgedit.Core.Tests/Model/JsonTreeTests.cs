using Cfgedit.Core.Model;

namespace Cfgedit.Core.Tests.Model;

/// <summary>The shape of the JSON document tree: kinds, parts, spans, children and paths, for each dialect.</summary>
public sealed class JsonTreeTests
{
    private static readonly IConfigFormat StrictJson = Formats.FromName("json")!;
    private static readonly IConfigFormat Jsonc = Formats.FromName("jsonc")!;
    private static readonly IConfigFormat Json5 = Formats.FromName("json5")!;

    [Fact]
    public void Root_scalar_is_the_single_item_of_the_document_with_only_a_value_part()
    {
        ConfigDocument document = ModelTestSupport.Parse("\"hi\"", StrictJson);

        string expected = """
            Document / [0,4)
              JsonScalar /Item(0) [0,4) json=String Root"$"[0,0)- Value"hi"[0,4)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Empty_array_has_a_zero_summary_and_no_children()
    {
        ConfigDocument document = ModelTestSupport.Parse("[]", StrictJson);

        string expected = """
            Document / [0,2)
              JsonArray /Item(0) [0,2) json=Array Root"$"[0,0)- Summary"0"[0,1)-
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Strict_object_with_a_duplicate_key_numbers_the_repeats_and_nests_arrays()
    {
        ConfigDocument document = ModelTestSupport.Parse("{\"a\": 1, \"b\": [true, null], \"a\": \"x\"}", StrictJson);

        string expected = """
            Document / [0,37)
              JsonObject /Item(0) [0,37) json=Object Root"$"[0,0)- Summary"3"[0,1)-
                JsonScalar /Item(0)/Member("a", 0) [1,7) json=Number Key"a"[1,4)+ Value"1"[6,7)+
                JsonArray /Item(0)/Member("b", 0) [9,26) json=Array Key"b"[9,12)+ Summary"2"[14,15)-
                  JsonScalar /Item(0)/Member("b", 0)/Item(0) [15,19) json=True Index"0"[15,15)- Value"true"[15,19)+
                  JsonScalar /Item(0)/Member("b", 0)/Item(1) [21,25) json=Null Index"1"[21,21)- Value"null"[21,25)+
                JsonScalar /Item(0)/Member("a", 1) [28,36) json=String Key"a"[28,31)+ Value"x"[33,36)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Jsonc_comments_sit_at_the_document_object_and_array_levels()
    {
        // The leading comment is the document's first child, the trailing one its last. Comment indices count comments
        // among their siblings only.
        const string text = "// head\n{ /* k */ \"a\": 1, // line\n \"b\": [ /* in */ 2 ] } /* tail */";
        ConfigDocument document = ModelTestSupport.Parse(text, Jsonc);

        string expected = """
            Document / [0,67)
              JsonComment /Comment(0) [0,7) Comment" head"[2,7)+
              JsonObject /Item(0) [8,56) json=Object Root"$"[8,8)- Summary"2"[8,9)-
                JsonComment /Item(0)/Comment(0) [10,17) Comment" k "[12,15)+
                JsonScalar /Item(0)/Member("a", 0) [18,24) json=Number Key"a"[18,21)+ Value"1"[23,24)+
                JsonComment /Item(0)/Comment(1) [26,33) Comment" line"[28,33)+
                JsonArray /Item(0)/Member("b", 0) [35,54) json=Array Key"b"[35,38)+ Summary"1"[40,41)-
                  JsonComment /Item(0)/Member("b", 0)/Comment(0) [42,50) Comment" in "[44,48)+
                  JsonScalar /Item(0)/Member("b", 0)/Item(0) [51,52) json=Number Index"0"[51,51)- Value"2"[51,52)+
              JsonComment /Comment(1) [57,67) Comment" tail "[59,65)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Json5_bare_keys_and_its_number_spellings_keep_their_raw_text()
    {
        ConfigDocument document = ModelTestSupport.Parse("{ a: 'x', b: Infinity, c: 0x1F, d: +1, }", Json5);

        string expected = """
            Document / [0,40)
              JsonObject /Item(0) [0,40) json=Object Root"$"[0,0)- Summary"4"[0,1)-
                JsonScalar /Item(0)/Member("a", 0) [2,8) json=String Key"a"[2,3)+ Value"x"[5,8)+
                JsonScalar /Item(0)/Member("b", 0) [10,21) json=Number Key"b"[10,11)+ Value"Infinity"[13,21)+
                JsonScalar /Item(0)/Member("c", 0) [23,30) json=Number Key"c"[23,24)+ Value"0x1F"[26,30)+
                JsonScalar /Item(0)/Member("d", 0) [32,37) json=Number Key"d"[32,33)+ Value"+1"[35,37)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Document_node_has_no_parts_and_spans_the_whole_text()
    {
        ConfigDocument document = ModelTestSupport.Parse("[1]\n", StrictJson);

        Assert.Equal(NodeKind.Document, document.Root.Kind);
        Assert.Empty(document.Root.Parts);
        Assert.Empty(document.Root.EndParts);
        Assert.Null(document.Root.Parent);
        Assert.Equal(NodePath.Empty, document.Root.Path);
        Assert.Equal(0, document.Root.Start);
        Assert.Equal(4, document.Root.End);
    }

    [Theory]
    [InlineData("{}", PartKind.Summary)]
    [InlineData("[1]", PartKind.Summary)]
    [InlineData("\"s\"", PartKind.Value)]
    public void Root_value_starts_with_a_zero_width_uneditable_marker_and_its_members_have_none(string text, PartKind second)
    {
        ConfigDocument document = ModelTestSupport.Parse(text, StrictJson);

        DocNode root = Assert.Single(document.Root.Children);
        DocPart marker = root.Parts[0];
        Assert.Equal(PartKind.Root, marker.Kind);
        Assert.Equal("$", marker.Value);
        Assert.False(marker.Editable);
        Assert.Equal(root.Start, marker.Start);
        Assert.Equal(marker.Start, marker.End);
        Assert.Equal(second, root.Parts[1].Kind);
        Assert.Equal(2, root.Parts.Count);
        Assert.All(root.Children, child => Assert.DoesNotContain(child.Parts, part => part.Kind == PartKind.Root));
    }

    [Fact]
    public void Root_value_is_item_zero_and_its_children_point_back_to_it()
    {
        ConfigDocument document = ModelTestSupport.Parse("[10]", StrictJson);

        DocNode root = Assert.Single(document.Root.Children);
        Assert.Same(document.Root, root.Parent);
        Assert.Equal("/Item(0)", root.Path.ToString());
        DocNode item = Assert.Single(root.Children);
        Assert.Same(root, item.Parent);
        Assert.Equal("/Item(0)/Item(0)", item.Path.ToString());
    }

    [Fact]
    public void Parts_are_editable_only_for_keys_and_scalar_values_and_comment_text()
    {
        ConfigDocument document = ModelTestSupport.Parse("// c\n{\"k\": [null]}", Jsonc);

        DocNode comment = document.Root.Children[0];
        DocNode root = document.Root.Children[1];
        DocNode member = root.Children[0];
        DocNode item = member.Children[0];

        Assert.Equal(PartKind.Comment, Assert.Single(comment.Parts).Kind);
        Assert.True(Assert.Single(comment.Parts).Editable);
        Assert.Equal(PartKind.Summary, root.Parts[1].Kind);
        Assert.False(root.Parts[1].Editable);
        Assert.Equal(PartKind.Key, member.Parts[0].Kind);
        Assert.True(member.Parts[0].Editable);
        Assert.Equal(PartKind.Summary, member.Parts[1].Kind);
        Assert.False(member.Parts[1].Editable);
        Assert.Equal(PartKind.Index, item.Parts[0].Kind);
        Assert.False(item.Parts[0].Editable);
        Assert.Equal(PartKind.Value, item.Parts[1].Kind);
        Assert.True(item.Parts[1].Editable);
    }

    [Fact]
    public void Summary_counts_members_and_items_but_not_comments()
    {
        ConfigDocument document = ModelTestSupport.Parse("{ /* a */ \"x\": 1, // b\n \"y\": { // c\n } }", Jsonc);

        // Children of the root object: a comment, member x, a comment, member y.
        DocNode root = document.Root.Children[0];
        Assert.Equal("2", root.Parts[1].Value);
        DocNode inner = root.Children[3];
        Assert.Equal("/Item(0)/Member(\"y\", 0)", inner.Path.ToString());
        Assert.Equal("0", inner.Parts[1].Value);
        Assert.Equal(NodeKind.JsonComment, Assert.Single(inner.Children).Kind);
    }
}
