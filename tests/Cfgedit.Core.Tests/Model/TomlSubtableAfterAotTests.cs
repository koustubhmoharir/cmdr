using Cfgedit.Core;
using Cfgedit.Core.Model;
using Cfgedit.Core.Toml;
using static Cfgedit.Core.Tests.Model.EditTestSupport;

namespace Cfgedit.Core.Tests.Model;

/// <summary>
/// A header that walks through an array of tables (<c>[x.sub]</c>, <c>[[x.list]]</c>) points to the array's most recent item. Tomlyn does not
/// read such a header that follows another table, so cfgedit refuses it with its own reason instead of an internal parse mismatch. The
/// accepted forms keep their placement, and the planner's operations on an item keep its sub-table sections with it.
/// </summary>
public sealed class TomlSubtableAfterAotTests
{
    private static readonly IConfigFormat Toml = EditTestSupport.Toml;

    private static NodePath Path(params PathStep[] steps) => At(new PathStep[] { Item(0) }.Concat(steps).ToArray());

    private static void AssertRefused(string text, string message)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => ModelTestSupport.Parse(text, Toml));
        Assert.Equal(message, ex.Message);
    }

    private static void AssertRefusedAt(string text, string message, int line, int column)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => ModelTestSupport.Parse(text, Toml));
        Assert.Equal(message, ex.Message);
        Assert.Equal(line, ex.Line);
        Assert.Equal(column, ex.Column);
    }

    // ----- Accepted forms -----

    [Fact]
    public void A_subtable_directly_after_its_array_item_is_a_table_of_that_item()
    {
        ConfigDocument document = ModelTestSupport.Parse("[[x]]\na = 1\n[x.sub]\nb = 2\n", Toml);

        string expected = """
            Document / [0,26)
              TomlTable /Item(0) [0,26) Root"$"[0,0)- Summary"1"[0,0)-
                TomlArray /Item(0)/Member("x", 0) [0,25) Key"x"[2,3)+ Summary"1"[2,2)-
                  TomlTable /Item(0)/Member("x", 0)/Item(0) [0,25) Index"0"[0,0)- Summary"2"[0,0)-
                    TomlScalar /Item(0)/Member("x", 0)/Item(0)/Member("a", 0) [6,11) toml=Integer Key"a"[6,7)+ Value"1"[10,11)+
                    TomlTable /Item(0)/Member("x", 0)/Item(0)/Member("sub", 0) [12,25) Key"sub"[15,18)+ Summary"1"[15,15)-
                      TomlScalar /Item(0)/Member("x", 0)/Item(0)/Member("sub", 0)/Member("b", 0) [20,25) toml=Integer Key"b"[20,21)+ Value"2"[24,25)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void A_subtable_after_two_items_is_a_table_of_the_last_item()
    {
        ConfigDocument document = ModelTestSupport.Parse("[[x]]\na = 1\n[[x]]\na = 2\n[x.sub]\nb = 2\n", Toml);

        string expected = """
            Document / [0,38)
              TomlTable /Item(0) [0,38) Root"$"[0,0)- Summary"1"[0,0)-
                TomlArray /Item(0)/Member("x", 0) [0,37) Key"x"[2,3)+ Summary"2"[2,2)-
                  TomlTable /Item(0)/Member("x", 0)/Item(0) [0,11) Index"0"[0,0)- Summary"1"[0,0)-
                    TomlScalar /Item(0)/Member("x", 0)/Item(0)/Member("a", 0) [6,11) toml=Integer Key"a"[6,7)+ Value"1"[10,11)+
                  TomlTable /Item(0)/Member("x", 0)/Item(1) [12,37) Index"1"[12,12)- Summary"2"[12,12)-
                    TomlScalar /Item(0)/Member("x", 0)/Item(1)/Member("a", 0) [18,23) toml=Integer Key"a"[18,19)+ Value"2"[22,23)+
                    TomlTable /Item(0)/Member("x", 0)/Item(1)/Member("sub", 0) [24,37) Key"sub"[27,30)+ Summary"1"[27,27)-
                      TomlScalar /Item(0)/Member("x", 0)/Item(1)/Member("sub", 0)/Member("b", 0) [32,37) toml=Integer Key"b"[32,33)+ Value"2"[36,37)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void A_deeper_subtable_of_a_contiguous_subtable_is_accepted()
    {
        ConfigDocument document = ModelTestSupport.Parse("[[x]]\na = 1\n[x.sub]\nb = 2\n[x.sub.deep]\nc = 3\n", Toml);

        string expected = """
            Document / [0,45)
              TomlTable /Item(0) [0,45) Root"$"[0,0)- Summary"1"[0,0)-
                TomlArray /Item(0)/Member("x", 0) [0,44) Key"x"[2,3)+ Summary"1"[2,2)-
                  TomlTable /Item(0)/Member("x", 0)/Item(0) [0,44) Index"0"[0,0)- Summary"2"[0,0)-
                    TomlScalar /Item(0)/Member("x", 0)/Item(0)/Member("a", 0) [6,11) toml=Integer Key"a"[6,7)+ Value"1"[10,11)+
                    TomlTable /Item(0)/Member("x", 0)/Item(0)/Member("sub", 0) [12,44) Key"sub"[15,18)+ Summary"2"[15,15)-
                      TomlScalar /Item(0)/Member("x", 0)/Item(0)/Member("sub", 0)/Member("b", 0) [20,25) toml=Integer Key"b"[20,21)+ Value"2"[24,25)+
                      TomlTable /Item(0)/Member("x", 0)/Item(0)/Member("sub", 0)/Member("deep", 0) [26,44) Key"deep"[33,37)+ Summary"1"[33,33)-
                        TomlScalar /Item(0)/Member("x", 0)/Item(0)/Member("sub", 0)/Member("deep", 0)/Member("c", 0) [39,44) toml=Integer Key"c"[39,40)+ Value"3"[43,44)+
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    // ----- Refused forms: Tomlyn reads them differently -----

    [Fact]
    public void A_subtable_after_another_table_is_refused_with_the_reason()
    {
        AssertRefused(
            "[[x]]\na = 1\n[y]\nk = 1\n[x.sub]\nb = 2\n",
            "cfgedit cannot verify a [x.sub] header that follows another table after [[x]]; Tomlyn reads it differently");
    }

    [Fact]
    public void A_subtable_of_the_last_item_after_another_table_is_refused_with_the_reason()
    {
        AssertRefused(
            "[[x]]\na = 1\n[[x]]\na = 2\n[y]\nk = 1\n[x.sub]\nb = 2\n",
            "cfgedit cannot verify a [x.sub] header that follows another table after [[x]]; Tomlyn reads it differently");
    }

    [Fact]
    public void A_list_of_tables_under_an_item_after_another_table_is_refused_with_the_reason()
    {
        AssertRefused(
            "[[x]]\na = 1\n[y]\nk = 1\n[[x.list]]\nc = 3\n",
            "cfgedit cannot verify a [x.list] header that follows another table after [[x]]; Tomlyn reads it differently");
    }

    [Fact]
    public void A_deeper_subtable_after_another_table_is_refused_with_the_reason()
    {
        AssertRefused(
            "[[x]]\na = 1\n[y]\nk = 1\n[x.sub.deep]\nb = 2\n",
            "cfgedit cannot verify a [x.sub.deep] header that follows another table after [[x]]; Tomlyn reads it differently");
    }

    [Fact]
    public void An_item_after_another_table_is_refused_at_the_offending_header()
    {
        AssertRefusedAt(
            "[[x]]\na = 1\n[y]\nk = 1\n[[x]]\na = 2\n",
            "cfgedit cannot verify [[x]] items separated by another table; Tomlyn reads them differently",
            5,
            1);
    }

    [Fact]
    public void A_nested_item_after_another_table_is_refused_with_the_dotted_name()
    {
        AssertRefusedAt(
            "[[a.b]]\na = 1\n[c]\nk = 1\n[[a.b]]\na = 2\n",
            "cfgedit cannot verify [[a.b]] items separated by another table; Tomlyn reads them differently",
            5,
            1);
    }

    [Fact]
    public void Contiguous_items_are_accepted()
    {
        ConfigDocument document = ModelTestSupport.Parse("[[x]]\n[[x]]\n", Toml);

        string expected = """
            Document / [0,12)
              TomlTable /Item(0) [0,12) Root"$"[0,0)- Summary"1"[0,0)-
                TomlArray /Item(0)/Member("x", 0) [0,11) Key"x"[2,3)+ Summary"2"[2,2)-
                  TomlTable /Item(0)/Member("x", 0)/Item(0) [0,5) Index"0"[0,0)- Summary"0"[0,0)-
                  TomlTable /Item(0)/Member("x", 0)/Item(1) [6,11) Index"1"[6,6)- Summary"0"[6,6)-
            """;
        Assert.Equal(expected, ModelTestSupport.Dump(document.Root));
    }

    [Fact]
    public void An_item_after_its_own_subtable_is_accepted()
    {
        ConfigDocument document = ModelTestSupport.Parse("[[x]]\na = 1\n[x.sub]\nb = 2\n[[x]]\na = 2\n", Toml);

        Assert.NotNull(document);
    }

    // ----- Planner: an item's sub-table sections go with the item -----

    [Fact]
    public void Delete_of_an_item_takes_its_deeper_subtable_sections_and_leaves_the_next_table()
    {
        const string text = "[[x]]\na = 1\n[x.sub]\nb = 2\n[x.sub.deep]\nc = 3\n[[x]]\na = 2\n[y]\nk = 1\n";

        string result = Edit(text, Toml, new DeleteNode(Path(Member("x"), Item(0))));

        Assert.Equal("[[x]]\na = 2\n[y]\nk = 1\n", result);
    }

    [Fact]
    public void Delete_of_the_last_item_takes_its_subtable_section_and_leaves_the_table_after_it()
    {
        const string text = "[[x]]\na = 1\n[[x]]\na = 2\n[x.sub]\nb = 2\n[y]\nk = 1\n";

        string result = Edit(text, Toml, new DeleteNode(Path(Member("x"), Item(1))));

        Assert.Equal("[[x]]\na = 1\n[y]\nk = 1\n", result);
    }

    [Fact]
    public void Move_of_an_item_takes_its_subtable_section_along()
    {
        const string text = "[[x]]\na = 1\n[x.sub]\nb = 2\n[[x]]\na = 2\n";

        string result = Edit(text, Toml, new MoveNode(Path(Member("x"), Item(0)), 1));

        Assert.Equal("[[x]]\na = 2\n[[x]]\na = 1\n[x.sub]\nb = 2\n", result);
    }

    [Fact]
    public void Duplicate_of_an_item_copies_its_subtable_section_after_it()
    {
        const string text = "[[x]]\na = 1\n[x.sub]\nb = 2\n[[x]]\na = 2\n";

        string result = Edit(text, Toml, new DuplicateNode(Path(Member("x"), Item(0))));

        Assert.Equal("[[x]]\na = 1\n[x.sub]\nb = 2\n[[x]]\na = 1\n[x.sub]\nb = 2\n[[x]]\na = 2\n", result);
    }
}
