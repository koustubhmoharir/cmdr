using Cfgedit.Core;
using Cfgedit.Core.Model;
using Cfgedit.Core.Toml;

namespace Cfgedit.Core.Tests.Toml;

/// <summary>
/// The Tomlyn cross-check: the comparer names the first difference between two data trees, the mismatch error has its prefix, and
/// the check passes for the data of real texts and fails for a text that differs from the data.
/// </summary>
public sealed class TomlCrossCheckTests
{
    [Fact]
    public void Tables_are_compared_by_name_so_their_member_order_does_not_matter()
    {
        LogicalNode expected = Table(null, Integer("a", "1"), Integer("b", "2"));
        LogicalNode actual = Table(null, Integer("b", "2"), Integer("a", "1"));

        Assert.Null(TomlCrossCheck.FirstDifference(expected, actual));
    }

    [Fact]
    public void Arrays_are_compared_in_order()
    {
        LogicalNode expected = Table(null, Array("x", Integer(null, "1"), Integer(null, "2")));
        LogicalNode actual = Table(null, Array("x", Integer(null, "2"), Integer(null, "1")));

        Assert.Equal("at x[0]: cfgedit has Integer \"1\", Tomlyn has Integer \"2\"", TomlCrossCheck.FirstDifference(expected, actual));
    }

    [Fact]
    public void A_different_value_is_reported_with_its_path()
    {
        LogicalNode expected = Table(null, Table("a", Integer("b", "1")));
        LogicalNode actual = Table(null, Table("a", Integer("b", "2")));

        Assert.Equal("at a.b: cfgedit has Integer \"1\", Tomlyn has Integer \"2\"", TomlCrossCheck.FirstDifference(expected, actual));
    }

    [Fact]
    public void A_key_that_only_one_side_has_is_reported()
    {
        LogicalNode expected = Table(null, Integer("k", "1"));
        LogicalNode actual = Table(null);

        Assert.Equal("at k: cfgedit has it, Tomlyn does not", TomlCrossCheck.FirstDifference(expected, actual));
        Assert.Equal("at k: Tomlyn has it, cfgedit does not", TomlCrossCheck.FirstDifference(actual, expected));
    }

    [Fact]
    public void A_scalar_of_another_kind_with_the_same_text_is_a_difference()
    {
        LogicalNode expected = Table(null, Integer("k", "1"));
        LogicalNode actual = Table(null, new LogicalNode(LogicalKind.Scalar, "k", null, "1", [], [], tomlKind: TomlValueKind.String));

        Assert.Equal("at k: cfgedit has Integer \"1\", Tomlyn has String \"1\"", TomlCrossCheck.FirstDifference(expected, actual));
    }

    [Fact]
    public void An_array_of_another_length_is_a_difference()
    {
        LogicalNode expected = Table(null, Array("x", Integer(null, "1")));
        LogicalNode actual = Table(null, Array("x"));

        Assert.Equal("at x: cfgedit has 1 items, Tomlyn has 0", TomlCrossCheck.FirstDifference(expected, actual));
    }

    [Fact]
    public void A_mismatch_is_an_internal_parse_mismatch_that_names_the_difference()
    {
        LogicalNode expected = Table(null, Integer("a", "1"));
        LogicalNode actual = Table(null, Integer("a", "2"));

        CfgeditException ex = Assert.Throws<CfgeditException>(() => TomlCrossCheck.Compare(expected, actual));

        Assert.StartsWith("internal parse mismatch: at a: ", ex.Message, StringComparison.Ordinal);
        Assert.EndsWith("(please report this)", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Equal_data_passes_the_check_for_the_text_it_came_from()
    {
        const string text = "a = 1979-05-27 07:32:00Z\nb = [1, 2.5, 'x']\n[t]\nc = { d = true }\ne.f = -inf\n";
        LogicalNode data = DataOf(text);

        TomlCrossCheck.Verify(text, data);
    }

    [Fact]
    public void Scalars_of_every_kind_agree_with_tomlyn()
    {
        const string text = "s = \"\"\"\nx\\ty\"\"\"\ni = 0x1F\nf = nan\nb = false\n"
            + "od = 1979-05-27T07:32:00.5-07:00\nld = 1979-05-27T07:32:00\nlt = 07:32:00\nd = 1979-05-27\n";

        TomlCrossCheck.Verify(text, DataOf(text));
    }

    [Fact]
    public void Date_times_in_other_spellings_agree_once_canonical()
    {
        const string text = "a = 1979-05-27t07:32:00z\nb = 1979-05-27 07:32:00.10\n";

        TomlCrossCheck.Verify(text, DataOf(text));
    }

    [Fact]
    public void Data_that_differs_from_the_text_fails_the_check()
    {
        LogicalNode data = DataOf("a = 1\n");

        CfgeditException ex = Assert.Throws<CfgeditException>(() => TomlCrossCheck.Verify("a = 2\n", data));

        Assert.Equal("internal parse mismatch: at a: cfgedit has Integer \"1\", Tomlyn has Integer \"2\" (please report this)", ex.Message);
    }

    [Fact]
    public void Text_that_tomlyn_refuses_fails_the_check_as_a_mismatch()
    {
        LogicalNode data = DataOf("a = 1\n");

        CfgeditException ex = Assert.Throws<CfgeditException>(() => TomlCrossCheck.Verify("a = [1\n", data));

        Assert.StartsWith("internal parse mismatch: Tomlyn refuses text that cfgedit accepts: ", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The data of a text, as cfgedit's model has it.</summary>
    private static LogicalNode DataOf(string text)
    {
        TomlDocument syntax = TomlDocument.Parse(TomlTestSupport.Utf8(text));
        return TomlModelBuilder.Build(syntax).DataLogical();
    }

    private static LogicalNode Table(string? name, params LogicalNode[] children)
    {
        return new LogicalNode(LogicalKind.Object, name, null, null, [], children);
    }

    private static LogicalNode Array(string? name, params LogicalNode[] items)
    {
        return new LogicalNode(LogicalKind.Array, name, null, null, [], items);
    }

    private static LogicalNode Integer(string? name, string value)
    {
        return new LogicalNode(LogicalKind.Scalar, name, null, value, [], [], tomlKind: TomlValueKind.Integer);
    }
}
