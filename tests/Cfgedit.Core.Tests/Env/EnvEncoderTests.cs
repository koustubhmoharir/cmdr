using Cfgedit.Core.Env;
using static Cfgedit.Core.Tests.Env.EnvTestSupport;

namespace Cfgedit.Core.Tests.Env;

public sealed class EnvEncoderTests
{
    public static IEnumerable<object[]> PlainestForms()
    {
        yield return ["", ""];
        yield return ["plain", "plain"];
        yield return ["a b", "a b"];
        yield return [" lead", "' lead'"];
        yield return ["trail ", "'trail '"];
        yield return ["\tlead", "'\tlead'"];
        yield return ["a\tb", "a\tb"];
        yield return ["a#b", "'a#b'"];
        yield return ["#", "'#'"];
        yield return ["it's", "\"it's\""];
        yield return ["say \"hi\"", "'say \"hi\"'"];
        yield return ["C:\\path", "'C:\\path'"];
        yield return ["$HOME", "'$HOME'"];
        yield return ["`x`", "'`x`'"];
        yield return ["line1\nline2", "'line1\nline2'"];
        yield return ["a\rb", "\"a\\rb\""];
        yield return ["\r\n", "\"\\r\n\""];
        yield return ["it's $5", "\"it's $5\""];
        yield return ["it's\\x", "\"it's\\\\x\""];
        yield return ["\"\\", "\"\\\"\\\\\""];
        yield return ["a\\\\b", "\"a\\\\\\\\b\""];
        yield return ["\n", "'\n'"];
        yield return ["$", "'$'"];
        yield return ["'", "\"'\""];
        yield return ["'\"", "\"'\\\"\""];
        yield return ["\\", "\"\\\\\""];
        yield return ["\u00e9t\u00e9", "\u00e9t\u00e9"];
        yield return ["a\u2028b", "a\u2028b"];
    }

    [Theory]
    [MemberData(nameof(PlainestForms))]
    public void Plainest_form_is_written_for_each_value(string value, string token)
    {
        Assert.Equal(token, EnvEncoder.Encode(value, "\n"));
    }

    [Fact]
    public void Line_feeds_inside_quotes_are_written_with_the_document_newline()
    {
        Assert.Equal("'line1\r\nline2'", EnvEncoder.Encode("line1\nline2", "\r\n"));
        Assert.Equal("\"it's\r\nx\"", EnvEncoder.Encode("it's\nx", "\r\n"));
        Assert.Equal("\"it's\rx\"", EnvEncoder.Encode("it's\nx", "\r"));
    }

    [Fact]
    public void Double_quotes_escape_backslash_and_quote_and_write_cr_as_an_escape()
    {
        Assert.Equal("\"a\\\"b\\\\c\\rd\\\"\"", EnvEncoder.Encode("a\"b\\c\rd\"", "\n"));
    }

    [Theory]
    [InlineData("plain", EnvQuote.None, "plain")]
    [InlineData("plain", EnvQuote.Single, "'plain'")]
    [InlineData("plain", EnvQuote.Double, "\"plain\"")]
    [InlineData("", EnvQuote.None, "")]
    [InlineData("", EnvQuote.Single, "''")]
    [InlineData("", EnvQuote.Double, "\"\"")]
    [InlineData("it's", EnvQuote.Single, "\"it's\"")]
    [InlineData("it's", EnvQuote.None, "\"it's\"")]
    [InlineData("a\rb", EnvQuote.Single, "\"a\\rb\"")]
    [InlineData(" lead", EnvQuote.None, "' lead'")]
    [InlineData("$x", EnvQuote.None, "'$x'")]
    [InlineData("a#b", EnvQuote.None, "'a#b'")]
    [InlineData("a b", EnvQuote.None, "a b")]
    [InlineData("a b", EnvQuote.Single, "'a b'")]
    [InlineData("a\nb", EnvQuote.None, "'a\nb'")]
    [InlineData("a\nb", EnvQuote.Double, "\"a\nb\"")]
    [InlineData("a\\b", EnvQuote.Single, "'a\\b'")]
    [InlineData("a\\b", EnvQuote.Double, "\"a\\\\b\"")]
    [InlineData("a\\\\b", EnvQuote.Single, "\"a\\\\\\\\b\"")]
    [InlineData("a\\", EnvQuote.Single, "\"a\\\\\"")]
    [InlineData("a\\\\b", EnvQuote.None, "\"a\\\\\\\\b\"")]
    public void Keeping_a_style_holds_when_it_can_and_otherwise_falls_back_to_the_plainest_form(
        string value, EnvQuote preferred, string token)
    {
        Assert.Equal(token, EnvEncoder.EncodeKeeping(value, preferred, "\n"));
    }

    [Theory]
    [InlineData("\u0000")]
    [InlineData("a\u0001b")]
    [InlineData("\u001B")]
    [InlineData("\u007F")]
    [InlineData("tab\u000Bvertical")]
    public void Control_characters_other_than_tab_lf_and_cr_are_refused(string value)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => EnvEncoder.Encode(value, "\n"));
        Assert.StartsWith("a .env value cannot contain control character U+", ex.Message);

        Assert.Throws<CfgeditException>(() => EnvEncoder.EncodeKeeping(value, EnvQuote.Double, "\n"));
        Assert.Throws<CfgeditException>(() => EnvEncoder.EncodeKeeping(value, EnvQuote.None, "\n"));
    }

    [Fact]
    public void Refusal_names_the_control_character_in_four_hex_digits()
    {
        CfgeditException nul = Assert.Throws<CfgeditException>(() => EnvEncoder.Encode("a\u0000", "\n"));
        Assert.Equal("a .env value cannot contain control character U+0000", nul.Message);

        CfgeditException escape = Assert.Throws<CfgeditException>(() => EnvEncoder.Encode("\u001B", "\n"));
        Assert.Equal("a .env value cannot contain control character U+001B", escape.Message);

        CfgeditException delete = Assert.Throws<CfgeditException>(() => EnvEncoder.Encode("\u007F", "\n"));
        Assert.Equal("a .env value cannot contain control character U+007F", delete.Message);
    }

    [Fact]
    public void Newline_must_be_lf_crlf_or_cr()
    {
        Assert.Throws<ArgumentException>(() => EnvEncoder.Encode("a", " "));
        Assert.Throws<ArgumentException>(() => EnvEncoder.EncodeKeeping("a", EnvQuote.None, "\n\n"));
    }

    [Theory]
    [InlineData("$HOME", EnvQuote.Double, true)]
    [InlineData("$HOME", EnvQuote.Single, false)]
    [InlineData("$HOME", EnvQuote.None, false)]
    [InlineData("plain", EnvQuote.Double, false)]
    [InlineData("it's $5", EnvQuote.Double, true)]
    [InlineData("a\\b", EnvQuote.Double, true)]
    [InlineData("a\\b", EnvQuote.Single, false)]
    public void Loader_warning_is_for_dollar_or_backslash_in_double_quotes(string value, EnvQuote written, bool expected)
    {
        Assert.Equal(expected, EnvEncoder.NeedsLoaderWarning(value, written));
    }

    [Theory]
    [InlineData("'x'", EnvQuote.Single)]
    [InlineData("\"x\"", EnvQuote.Double)]
    [InlineData("x", EnvQuote.None)]
    [InlineData("", EnvQuote.None)]
    public void Style_of_a_token_is_read_from_its_first_character(string token, EnvQuote expected)
    {
        Assert.Equal(expected, EnvEncoder.StyleOf(token));
    }

    [Theory]
    [InlineData("A", true)]
    [InlineData("_x", true)]
    [InlineData("A.b-c_9", true)]
    [InlineData("Logging__LogLevel", true)]
    [InlineData("", false)]
    [InlineData("1A", false)]
    [InlineData("A B", false)]
    [InlineData("a=b", false)]
    [InlineData("A:", false)]
    [InlineData("é", false)]
    [InlineData("A\n", false)]
    public void Key_validity_follows_the_pattern(string key, bool valid)
    {
        Assert.Equal(valid, EnvKey.IsValid(key));
    }
}
