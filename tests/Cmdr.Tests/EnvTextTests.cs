using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class EnvTextTests
{
    [Fact]
    public void Format_writes_key_value_lines_and_bang_for_removed_variables()
    {
        var env = new Dictionary<string, string?> { ["FORCE_COLOR"] = "1", ["SECRET"] = null, ["EMPTY"] = string.Empty };

        Assert.Equal("FORCE_COLOR=1\n!SECRET\nEMPTY=", EnvText.Format(env));
    }

    [Fact]
    public void Parse_reads_values_that_contain_equals_signs()
    {
        EnvParseResult result = EnvText.Parse("OPTS=a=b=c");

        Assert.Empty(result.Errors);
        Assert.Equal("a=b=c", result.Values["OPTS"]);
    }

    [Fact]
    public void Key_equals_with_nothing_after_is_the_empty_string()
    {
        EnvParseResult result = EnvText.Parse("EMPTY=");

        Assert.Empty(result.Errors);
        Assert.Equal(string.Empty, result.Values["EMPTY"]);
    }

    [Fact]
    public void Bang_key_is_null_meaning_remove()
    {
        EnvParseResult result = EnvText.Parse("!PATH");

        Assert.Empty(result.Errors);
        Assert.True(result.Values.ContainsKey("PATH"));
        Assert.Null(result.Values["PATH"]);
    }

    [Fact]
    public void Blank_lines_are_ignored_and_carriage_returns_removed()
    {
        EnvParseResult result = EnvText.Parse("\r\nA=1\r\n   \n\nB=2\n");

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "A", "B" }, result.Values.Keys);
        Assert.Equal("1", result.Values["A"]);
        Assert.Equal("2", result.Values["B"]);
    }

    [Fact]
    public void Line_without_equals_or_bang_is_an_error_with_its_line_number()
    {
        EnvParseResult result = EnvText.Parse("A=1\nnonsense");

        Assert.Single(result.Errors);
        Assert.Contains("Line 2", result.Errors[0]);
        Assert.DoesNotContain("A", result.Values.Keys.Where(k => k != "A"));
    }

    [Fact]
    public void Empty_key_is_an_error()
    {
        EnvParseResult equals = EnvText.Parse("=value");
        EnvParseResult bang = EnvText.Parse("!");

        Assert.Single(equals.Errors);
        Assert.Single(bang.Errors);
    }

    [Fact]
    public void Duplicate_keys_are_an_error()
    {
        EnvParseResult result = EnvText.Parse("A=1\nA=2");

        Assert.Single(result.Errors);
        Assert.Contains("'A'", result.Errors[0]);
    }

    [Fact]
    public void Surrounding_spaces_of_a_key_are_trimmed_but_not_of_the_value()
    {
        EnvParseResult result = EnvText.Parse("  KEY  = v ");

        Assert.Empty(result.Errors);
        Assert.Equal(" v ", result.Values["KEY"]);
    }

    [Fact]
    public void Empty_text_has_no_variables()
    {
        EnvParseResult result = EnvText.Parse(string.Empty);

        Assert.Empty(result.Errors);
        Assert.Empty(result.Values);
    }

    [Fact]
    public void Format_then_parse_round_trips()
    {
        var env = new Dictionary<string, string?> { ["A"] = "x=y", ["B"] = string.Empty, ["C"] = null };

        EnvParseResult result = EnvText.Parse(EnvText.Format(env));

        Assert.Empty(result.Errors);
        Assert.Equal(env, result.Values);
    }

    [Fact]
    public void CanEdit_is_true_for_ordinary_values_removals_and_empty_values()
    {
        Assert.True(EnvText.CanEdit(new Dictionary<string, string?> { ["A"] = "1", ["B"] = null, ["C"] = string.Empty }));
    }

    [Fact]
    public void CanEdit_is_true_for_no_variables()
    {
        Assert.True(EnvText.CanEdit(new Dictionary<string, string?>()));
    }

    [Fact]
    public void CanEdit_is_true_for_values_with_equals_signs_and_backslashes()
    {
        Assert.True(EnvText.CanEdit(new Dictionary<string, string?> { ["OPTS"] = "a=b=c", ["PATH_LIKE"] = @"C:\bin\x" }));
    }

    [Theory]
    [InlineData("first\nsecond")]
    [InlineData("x\r\ny")]
    [InlineData("value\r")]
    [InlineData("first\nB=second")]
    [InlineData("first\n!B")]
    [InlineData("x\n\ny")]
    public void CanEdit_is_false_for_values_the_line_form_cannot_hold(string value)
    {
        Assert.False(EnvText.CanEdit(new Dictionary<string, string?> { ["A"] = value }));
    }

    [Fact]
    public void CanEdit_is_false_when_a_key_cannot_be_written()
    {
        Assert.False(EnvText.CanEdit(new Dictionary<string, string?> { [" PADDED"] = "1" }));
        Assert.False(EnvText.CanEdit(new Dictionary<string, string?> { ["A=B"] = "1" }));
    }
}
