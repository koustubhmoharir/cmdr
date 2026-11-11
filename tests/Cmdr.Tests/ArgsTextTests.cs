using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class ArgsTextTests
{
    [Fact]
    public void Format_writes_one_argument_per_line()
    {
        Assert.Equal("-c\necho hi", ArgsText.Format(new[] { "-c", "echo hi" }));
    }

    [Fact]
    public void Format_of_no_arguments_is_empty()
    {
        Assert.Equal(string.Empty, ArgsText.Format(Array.Empty<string>()));
    }

    [Fact]
    public void Parse_splits_on_newlines_and_keeps_spaces_inside_an_argument()
    {
        Assert.Equal(new[] { "-c", "echo hello; sleep 2" }, ArgsText.Parse("-c\necho hello; sleep 2"));
    }

    [Fact]
    public void Parse_of_empty_text_is_no_arguments()
    {
        Assert.Empty(ArgsText.Parse(string.Empty));
    }

    [Fact]
    public void Parse_ignores_one_trailing_newline()
    {
        Assert.Equal(new[] { "a", "b" }, ArgsText.Parse("a\nb\n"));
    }

    [Fact]
    public void Parse_keeps_empty_lines_in_the_middle_as_empty_arguments()
    {
        Assert.Equal(new[] { "a", "", "b" }, ArgsText.Parse("a\n\nb"));
    }

    [Fact]
    public void Parse_drops_only_the_final_newline_so_a_second_one_is_an_empty_argument()
    {
        Assert.Equal(new[] { "a", "" }, ArgsText.Parse("a\n\n"));
    }

    [Fact]
    public void Parse_removes_carriage_returns_at_line_ends()
    {
        Assert.Equal(new[] { "a", "b" }, ArgsText.Parse("a\r\nb\r\n"));
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Format_then_parse_round_trips(string[] args)
    {
        Assert.Equal(args, ArgsText.Parse(ArgsText.Format(args)));
    }

    public static IEnumerable<object[]> RoundTripCases()
    {
        yield return new object[] { new[] { "a" } };
        yield return new object[] { new[] { "a", "" } };
        yield return new object[] { new[] { "", "a" } };
        yield return new object[] { new[] { "", "" } };
        yield return new object[] { new[] { "x y", "--flag=1", "" } };
        yield return new object[] { new[] { "" } };
    }

    [Fact]
    public void A_list_with_no_arguments_round_trips()
    {
        Assert.Empty(ArgsText.Parse(ArgsText.Format(Array.Empty<string>())));
    }
}
