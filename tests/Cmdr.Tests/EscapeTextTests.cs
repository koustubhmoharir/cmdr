using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class EscapeTextTests
{
    [Theory]
    [InlineData("q\n", "q\\n")]
    [InlineData("\r\n", "\\r\\n")]
    [InlineData("a\tb", "a\\tb")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData("\u001b", "\\e")]
    [InlineData("\u0001\u007f", "\\x01\\x7F")]
    [InlineData("plain text", "plain text")]
    [InlineData("", "")]
    public void Format_escapes_control_characters_and_backslashes(string value, string expected)
    {
        Assert.Equal(expected, EscapeText.Format(value));
    }

    [Fact]
    public void Format_of_null_is_empty()
    {
        Assert.Equal(string.Empty, EscapeText.Format(null));
    }

    [Theory]
    [InlineData("q\\n", "q\n")]
    [InlineData("\\r\\t\\\\", "\r\t\\")]
    [InlineData("\\e[0m", "\u001b[0m")]
    [InlineData("\\x41\\x0a", "A\n")]
    [InlineData("\\x7f", "\u007f")]
    [InlineData("\\X41", null)]
    public void Parse_understands_the_escapes(string text, string? expected)
    {
        EscapeParseResult result = EscapeText.Parse(text);

        if (expected is null)
        {
            Assert.NotNull(result.Error);
        }
        else
        {
            Assert.Null(result.Error);
            Assert.Equal(expected, result.Value);
        }
    }

    [Theory]
    [InlineData("\\q")]
    [InlineData("trailing\\")]
    [InlineData("\\x")]
    [InlineData("\\x4")]
    [InlineData("\\xZZ")]
    [InlineData("\\x4G")]
    public void Unknown_or_malformed_escapes_are_errors(string text)
    {
        EscapeParseResult result = EscapeText.Parse(text);

        Assert.NotNull(result.Error);
        Assert.StartsWith("Stop input:", result.Error);
    }

    [Theory]
    [InlineData("q\n")]
    [InlineData("\u0000\u001b\u007f\r\n\t\\ end")]
    [InlineData("\u0080ÿ unicode é 漢")]
    [InlineData("")]
    public void Format_then_parse_round_trips(string value)
    {
        EscapeParseResult result = EscapeText.Parse(EscapeText.Format(value));

        Assert.Null(result.Error);
        Assert.Equal(value, result.Value);
    }
}
