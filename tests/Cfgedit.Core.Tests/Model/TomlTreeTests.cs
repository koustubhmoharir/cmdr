using Cfgedit.Core.Model;
using Cfgedit.Core.Toml;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// The TOML document tree: tables merged from their pieces, arrays of tables, inline values, comment attachment and key spans. The
/// expected trees are worked out by hand from the source offsets, so they are an independent check of the builder.
/// </summary>
public sealed class TomlTreeTests
{
    private static readonly IConfigFormat Toml = Formats.FromName("toml")!;

    [Fact]
    public void Empty_file_has_only_the_root_table_with_a_zero_count()
    {
        ConfigDocument document = ModelTestSupport.Parse("", Toml);

        string expected = """
            Document / [0,0)
              TomlTable /Item(0) [0,0) Root"$"[0,0)- Summary"0"[0,0)-
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Scalars_of_the_common_kinds_are_members_with_key_and_value_parts()
    {
        const string text = "s = \"a\"\ni = 1_000\nf = 1.5\nb = true\nd = 1979-05-27T07:32:00Z\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Toml);

        // Each row spans its key through its value. The value part is the decoded string, or the source text of other kinds.
        string expected = """
            Document / [0,60)
              TomlTable /Item(0) [0,60) Root"$"[0,0)- Summary"5"[0,0)-
                TomlScalar /Item(0)/Member("s", 0) [0,7) toml=String Key"s"[0,1)+ Value"a"[4,7)+
                TomlScalar /Item(0)/Member("i", 0) [8,17) toml=Integer Key"i"[8,9)+ Value"1_000"[12,17)+
                TomlScalar /Item(0)/Member("f", 0) [18,25) toml=Float Key"f"[18,19)+ Value"1.5"[22,25)+
                TomlScalar /Item(0)/Member("b", 0) [26,34) toml=Boolean Key"b"[26,27)+ Value"true"[30,34)+
                TomlScalar /Item(0)/Member("d", 0) [35,59) toml=OffsetDateTime Key"d"[35,36)+ Value"1979-05-27T07:32:00Z"[39,59)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void A_table_defined_by_a_header_dotted_keys_and_another_header_is_merged_in_order_of_first_appearance()
    {
        const string text = "[a]\nx = 1\nc.d = 2\n[a.b]\ny = 3\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Toml);

        // a has the members x, c (from its dotted key) and b (from [a.b]), in that order. Its span runs from its header to y.
        string expected = """
            Document / [0,30)
              TomlTable /Item(0) [0,30) Root"$"[0,0)- Summary"1"[0,0)-
                TomlTable /Item(0)/Member("a", 0) [0,29) Key"a"[1,2)+ Summary"3"[1,1)-
                  TomlScalar /Item(0)/Member("a", 0)/Member("x", 0) [4,9) toml=Integer Key"x"[4,5)+ Value"1"[8,9)+
                  TomlTable /Item(0)/Member("a", 0)/Member("c", 0) [10,17) Key"c"[10,11)+ Summary"1"[10,10)-
                    TomlScalar /Item(0)/Member("a", 0)/Member("c", 0)/Member("d", 0) [12,17) toml=Integer Key"d"[12,13)+ Value"2"[16,17)+
                  TomlTable /Item(0)/Member("a", 0)/Member("b", 0) [18,29) Key"b"[21,22)+ Summary"1"[21,21)-
                    TomlScalar /Item(0)/Member("a", 0)/Member("b", 0)/Member("y", 0) [24,29) toml=Integer Key"y"[24,25)+ Value"3"[28,29)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void A_parent_made_by_a_deeper_header_is_an_implicit_table_with_the_header_as_its_piece()
    {
        const string text = "[x.y.z]\nk = 1\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Toml);

        string expected = """
            Document / [0,14)
              TomlTable /Item(0) [0,14) Root"$"[0,0)- Summary"1"[0,0)-
                TomlTable /Item(0)/Member("x", 0) [0,13) Key"x"[1,2)+ Summary"1"[1,1)-
                  TomlTable /Item(0)/Member("x", 0)/Member("y", 0) [0,13) Key"y"[3,4)+ Summary"1"[3,3)-
                    TomlTable /Item(0)/Member("x", 0)/Member("y", 0)/Member("z", 0) [0,13) Key"z"[5,6)+ Summary"1"[5,5)-
                      TomlScalar /Item(0)/Member("x", 0)/Member("y", 0)/Member("z", 0)/Member("k", 0) [8,13) toml=Integer Key"k"[8,9)+ Value"1"[12,13)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Arrays_of_tables_with_sub_tables_follow_the_spec_example()
    {
        const string text = "[[fruit]]\nname = \"apple\"\n[fruit.physical]\ncolor = \"red\"\n[[fruit.varieties]]\nname = \"red delicious\"\n[[fruit]]\nname = \"banana\"\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Toml);

        // Each [[fruit]] is an item with its index; [fruit.physical] belongs to the item it follows, and [[fruit.varieties]] makes an
        // array inside that item. The key of fruit is its first header's segment.
        string expected = """
            Document / [0,125)
              TomlTable /Item(0) [0,125) Root"$"[0,0)- Summary"1"[0,0)-
                TomlArray /Item(0)/Member("fruit", 0) [0,124) Key"fruit"[2,7)+ Summary"2"[2,2)-
                  TomlTable /Item(0)/Member("fruit", 0)/Item(0) [0,98) Index"0"[0,0)- Summary"3"[0,0)-
                    TomlScalar /Item(0)/Member("fruit", 0)/Item(0)/Member("name", 0) [10,24) toml=String Key"name"[10,14)+ Value"apple"[17,24)+
                    TomlTable /Item(0)/Member("fruit", 0)/Item(0)/Member("physical", 0) [25,55) Key"physical"[32,40)+ Summary"1"[32,32)-
                      TomlScalar /Item(0)/Member("fruit", 0)/Item(0)/Member("physical", 0)/Member("color", 0) [42,55) toml=String Key"color"[42,47)+ Value"red"[50,55)+
                    TomlArray /Item(0)/Member("fruit", 0)/Item(0)/Member("varieties", 0) [56,98) Key"varieties"[64,73)+ Summary"1"[64,64)-
                      TomlTable /Item(0)/Member("fruit", 0)/Item(0)/Member("varieties", 0)/Item(0) [56,98) Index"0"[56,56)- Summary"1"[56,56)-
                        TomlScalar /Item(0)/Member("fruit", 0)/Item(0)/Member("varieties", 0)/Item(0)/Member("name", 0) [76,98) toml=String Key"name"[76,80)+ Value"red delicious"[83,98)+
                  TomlTable /Item(0)/Member("fruit", 0)/Item(1) [99,124) Index"1"[99,99)- Summary"1"[99,99)-
                    TomlScalar /Item(0)/Member("fruit", 0)/Item(1)/Member("name", 0) [109,124) toml=String Key"name"[109,113)+ Value"banana"[116,124)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Inline_tables_and_arrays_nest_with_their_own_members_and_items()
    {
        const string text = "t = { a = [1, { b = 2 }], c.d = \"x\" }\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Toml);

        // The inline table t holds the array a, whose second item is an inline table, and the dotted member c.d.
        string expected = """
            Document / [0,38)
              TomlTable /Item(0) [0,38) Root"$"[0,0)- Summary"1"[0,0)-
                TomlTable /Item(0)/Member("t", 0) [0,37) Key"t"[0,1)+ Summary"2"[0,0)-
                  TomlArray /Item(0)/Member("t", 0)/Member("a", 0) [6,24) Key"a"[6,7)+ Summary"2"[6,6)-
                    TomlScalar /Item(0)/Member("t", 0)/Member("a", 0)/Item(0) [11,12) toml=Integer Index"0"[11,11)- Value"1"[11,12)+
                    TomlTable /Item(0)/Member("t", 0)/Member("a", 0)/Item(1) [14,23) Index"1"[14,14)- Summary"1"[14,14)-
                      TomlScalar /Item(0)/Member("t", 0)/Member("a", 0)/Item(1)/Member("b", 0) [16,21) toml=Integer Key"b"[16,17)+ Value"2"[20,21)+
                  TomlTable /Item(0)/Member("t", 0)/Member("c", 0) [26,35) Key"c"[26,27)+ Summary"1"[26,26)-
                    TomlScalar /Item(0)/Member("t", 0)/Member("c", 0)/Member("d", 0) [28,35) toml=String Key"d"[28,29)+ Value"x"[32,35)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Comments_inside_an_array_are_children_of_the_array_in_place()
    {
        const string text = "arr = [\n  1, # one\n  # two\n  2\n]\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Toml);

        string expected = """
            Document / [0,33)
              TomlTable /Item(0) [0,33) Root"$"[0,0)- Summary"1"[0,0)-
                TomlArray /Item(0)/Member("arr", 0) [0,32) Key"arr"[0,3)+ Summary"2"[0,0)-
                  TomlScalar /Item(0)/Member("arr", 0)/Item(0) [10,11) toml=Integer Index"0"[10,10)- Value"1"[10,11)+
                  TomlComment /Item(0)/Member("arr", 0)/Comment(0) [13,18) Comment" one"[14,18)+
                  TomlComment /Item(0)/Member("arr", 0)/Comment(1) [21,26) Comment" two"[22,26)+
                  TomlScalar /Item(0)/Member("arr", 0)/Item(1) [29,30) toml=Integer Index"1"[29,29)- Value"2"[29,30)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Comments_attach_above_keys_and_headers_and_trail_their_lines_while_a_blank_line_leaves_them_standing()
    {
        const string text = "# top\na = 1\n# above\nb = 2 # tr\n\n# loose\n\n[t]\n# above hdr\n[u] # tr hdr\nk = 3\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Toml);

        // "# top" and "# above" attach to a and b; "# tr" trails b. "# loose" is followed by a blank line, so it stands in the root,
        // before t. "# above hdr" attaches to the header [u], and the trailing comment of [u] follows it as the first children of u.
        string expected = """
            Document / [0,76)
              TomlTable /Item(0) [0,76) Root"$"[0,0)- Summary"4"[0,0)-
                TomlComment /Item(0)/Comment(0) [0,5) Comment" top"[1,5)+
                TomlScalar /Item(0)/Member("a", 0) [6,11) toml=Integer Key"a"[6,7)+ Value"1"[10,11)+
                TomlComment /Item(0)/Comment(1) [12,19) Comment" above"[13,19)+
                TomlScalar /Item(0)/Member("b", 0) [20,25) toml=Integer Key"b"[20,21)+ Value"2"[24,25)+
                TomlComment /Item(0)/Comment(2) [26,30) Comment" tr"[27,30)+
                TomlComment /Item(0)/Comment(3) [32,39) Comment" loose"[33,39)+
                TomlTable /Item(0)/Member("t", 0) [41,44) Key"t"[42,43)+ Summary"0"[42,42)-
                TomlTable /Item(0)/Member("u", 0) [45,75) Key"u"[58,59)+ Summary"1"[58,58)-
                  TomlComment /Item(0)/Member("u", 0)/Comment(0) [45,56) Comment" above hdr"[46,56)+
                  TomlComment /Item(0)/Member("u", 0)/Comment(1) [61,69) Comment" tr hdr"[62,69)+
                  TomlScalar /Item(0)/Member("u", 0)/Member("k", 0) [70,75) toml=Integer Key"k"[70,71)+ Value"3"[74,75)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void Each_row_has_the_pieces_that_define_it_and_the_document_row_has_none()
    {
        const string text = "[a]\nx = 1\nc.d = 2\n[a.b]\ny = 3\n";
        ConfigDocument document = ModelTestSupport.Parse(text, Toml);
        var model = (TomlModel)document.Syntax;

        DocNode a = document.Root.Children[0].Children[0];
        DocNode c = a.Children[1];
        DocNode b = a.Children[2];
        Assert.Equal("[a]", SourceOf(text, model.PiecesOf(a)[0]));
        Assert.Equal("c.d = 2", SourceOf(text, model.PiecesOf(c)[0]));
        Assert.Equal("[a.b]", SourceOf(text, model.PiecesOf(b)[0]));
        Assert.Empty(model.PiecesOf(document.Root));
    }

    /// <summary>The source text a piece covers: a header from its brackets, a key/value line from its key to its value.</summary>
    private static string SourceOf(string text, TomlNode piece)
    {
        return piece switch
        {
            TomlHeaderItem header => text[header.OpenSpan.Start..header.CloseSpan.End],
            TomlKeyValueItem keyValue => text[keyValue.Key.Start..keyValue.Value.End],
            _ => throw new InvalidOperationException("Unexpected piece: " + piece.GetType().Name),
        };
    }
}
