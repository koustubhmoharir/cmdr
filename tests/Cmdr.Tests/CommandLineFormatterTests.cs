using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class CommandLineFormatterTests
{
    [Theory]
    [InlineData("sh", "sh")]
    [InlineData("/usr/bin/env-x_1.2", "/usr/bin/env-x_1.2")]
    [InlineData("a@b%c+d=e:f,g", "a@b%c+d=e:f,g")]
    public void Posix_bare_tokens_are_not_quoted(string token, string expected)
    {
        Assert.Equal(expected, CommandLineFormatter.QuotePosix(token));
    }

    [Fact]
    public void Posix_token_with_spaces_is_single_quoted()
    {
        Assert.Equal("'while :; do date; sleep 1; done'", CommandLineFormatter.QuotePosix("while :; do date; sleep 1; done"));
    }

    [Fact]
    public void Posix_embedded_single_quote_is_closed_escaped_and_reopened()
    {
        Assert.Equal("'it'\\''s'", CommandLineFormatter.QuotePosix("it's"));
    }

    [Fact]
    public void Posix_empty_token_is_two_single_quotes()
    {
        Assert.Equal("''", CommandLineFormatter.QuotePosix(""));
    }

    [Theory]
    [InlineData("$HOME")]
    [InlineData("*.txt")]
    [InlineData("a&b")]
    [InlineData("a\"b")]
    [InlineData("back\\slash")]
    public void Posix_special_characters_are_quoted(string token)
    {
        string quoted = CommandLineFormatter.QuotePosix(token);

        Assert.StartsWith("'", quoted);
        Assert.EndsWith("'", quoted);
        Assert.Equal(token, quoted[1..^1]);
    }

    [Fact]
    public void Posix_non_ascii_letters_are_quoted()
    {
        Assert.Equal("'é'", CommandLineFormatter.QuotePosix("é"));
    }

    [Fact]
    public void Format_joins_the_quoted_program_and_arguments()
    {
        string line = CommandLineFormatter.Format("sh", new[] { "-c", "while :; do date; sleep 1; done" }, windows: false);

        Assert.Equal("sh -c 'while :; do date; sleep 1; done'", line);
    }

    [Fact]
    public void Format_with_no_arguments_is_the_program_alone()
    {
        Assert.Equal("ls", CommandLineFormatter.Format("ls", Array.Empty<string>(), windows: false));
    }

    [Theory]
    [InlineData("cmd.exe", "cmd.exe")]
    [InlineData("C:\\tools\\x.exe", "C:\\tools\\x.exe")]
    public void Windows_bare_tokens_are_not_quoted(string token, string expected)
    {
        Assert.Equal(expected, CommandLineFormatter.QuoteWindows(token));
    }

    [Fact]
    public void Windows_token_with_a_space_is_wrapped_in_double_quotes()
    {
        Assert.Equal("\"C:\\Program Files\\x\"", CommandLineFormatter.QuoteWindows("C:\\Program Files\\x"));
    }

    [Fact]
    public void Windows_embedded_quote_is_backslash_escaped()
    {
        Assert.Equal("\"say \\\"hi\\\"\"", CommandLineFormatter.QuoteWindows("say \"hi\""));
    }

    [Fact]
    public void Windows_trailing_backslash_is_doubled_before_the_closing_quote()
    {
        Assert.Equal("\"a b\\\\\"", CommandLineFormatter.QuoteWindows("a b\\"));
    }

    [Fact]
    public void Windows_backslashes_before_a_quote_are_doubled_and_the_quote_escaped()
    {
        // a \ " b  ->  a \\ \" b  (inside the wrapping quotes; the space is in the token)
        Assert.Equal("\"a\\\\\\\"b x\"", CommandLineFormatter.QuoteWindows("a\\\"b x"));
    }

    [Fact]
    public void Windows_empty_token_is_an_empty_pair_of_double_quotes()
    {
        Assert.Equal("\"\"", CommandLineFormatter.QuoteWindows(""));
    }

    [Fact]
    public void Windows_format_quotes_the_program_and_each_argument()
    {
        string line = CommandLineFormatter.Format("my tool", new[] { "a b", "c" }, windows: true);

        Assert.Equal("\"my tool\" \"a b\" c", line);
    }
}
