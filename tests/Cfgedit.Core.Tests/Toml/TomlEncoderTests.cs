using Cfgedit.Core.Text;
using Cfgedit.Core.Toml;
using static Cfgedit.Core.Tests.Toml.TomlTestSupport;

namespace Cfgedit.Core.Tests.Toml;

/// <summary>
/// The encoders and the number and date-time checks, against exact expected text. The expectations are written out, not computed
/// by the encoder, so they check the output as a reader sees it.
/// </summary>
public sealed class TomlEncoderTests
{
    private const string Lf = "\n";
    private const string Crlf = "\r\n";

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a-b_9", "a-b_9")]
    [InlineData("1234", "1234")]
    [InlineData("", "\"\"")]
    [InlineData("a.b", "\"a.b\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("q\"", "\"q\\\"\"")]
    [InlineData("back\\", "\"back\\\\\"")]
    [InlineData("\n", "\"\\n\"")]
    [InlineData("é", "\"é\"")]
    [InlineData("\u007F", "\"\\u007F\"")]
    [InlineData("\0", "\"\\u0000\"")]
    public void Keys_are_bare_when_they_can_be_and_basic_quoted_otherwise(string name, string expected)
    {
        Assert.Equal(expected, TomlEncoder.EncodeKey(name));
    }

    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("plain", "\"plain\"")]
    [InlineData("say \"hi\"", "'say \"hi\"'")]
    [InlineData("C:\\path", "'C:\\path'")]
    [InlineData("it's \"x\"", "\"it's \\\"x\\\"\"")]
    [InlineData("tab\there", "\"tab\\there\"")]
    [InlineData("x\u0001", "\"x\\u0001\"")]
    [InlineData("\u007F", "\"\\u007F\"")]
    [InlineData("\0", "\"\\u0000\"")]
    [InlineData("\b\f", "\"\\b\\f\"")]
    [InlineData("é😀", "\"é😀\"")]
    [InlineData("\"\"\"", "'\"\"\"'")]
    public void A_single_line_string_takes_the_plainest_form(string value, string expected)
    {
        Assert.Equal(expected, TomlEncoder.EncodeString(value, Lf));
    }

    [Fact]
    public void A_string_with_a_line_break_is_multi_line_basic_with_the_document_line_break()
    {
        Assert.Equal("\"\"\"\na\nb\"\"\"", TomlEncoder.EncodeString("a\nb", Lf));
        Assert.Equal("\"\"\"\r\na\r\nb\"\"\"", TomlEncoder.EncodeString("a\nb", Crlf));
        Assert.Equal("\"\"\"\n\\r\"\"\"", TomlEncoder.EncodeString("\r", Lf));
        Assert.Equal("\"\"\"\n\\r\n\"\"\"", TomlEncoder.EncodeString("\r\n", Lf));
    }

    [Fact]
    public void Quotation_marks_in_a_multi_line_string_are_escaped_only_in_runs_of_three_or_more()
    {
        string expected = "\"\"\"\na\n" + "\\\"\\\"\\\"" + "\"\"\"";
        Assert.Equal(expected, TomlEncoder.EncodeString("a\n\"\"\"", Lf));
        // Two quotation marks are not escaped: with the closing delimiter they make five, which reads as two.
        Assert.Equal("\"\"\"\na\n\"\"\"\"\"", TomlEncoder.EncodeString("a\n\"\"", Lf));
    }

    [Theory]
    [InlineData("it's", TomlStringForm.Basic, "\"it's\"")]
    [InlineData("a\nb", TomlStringForm.Basic, "\"a\\nb\"")]
    [InlineData("abc", TomlStringForm.Literal, "'abc'")]
    [InlineData("C:\\x", TomlStringForm.Literal, "'C:\\x'")]
    [InlineData("it's", TomlStringForm.Literal, "\"it's\"")]
    [InlineData("x\u0001", TomlStringForm.Literal, "\"x\\u0001\"")]
    [InlineData("plain", TomlStringForm.MultiLineBasic, "\"\"\"\nplain\"\"\"")]
    [InlineData("a''b\n", TomlStringForm.MultiLineLiteral, "'''\na''b\n'''")]
    [InlineData("a\tb\n", TomlStringForm.MultiLineLiteral, "'''\na\tb\n'''")]
    [InlineData("a'''b", TomlStringForm.MultiLineLiteral, "\"a'''b\"")]
    [InlineData("x\u0001\n", TomlStringForm.MultiLineLiteral, "\"\"\"\nx\\u0001\n\"\"\"")]
    public void A_kept_form_is_used_only_when_it_can_hold_the_value(string value, TomlStringForm preferred, string expected)
    {
        Assert.Equal(expected, TomlEncoder.EncodeStringKeeping(value, preferred, Lf));
    }

    [Fact]
    public void A_literal_that_cannot_hold_a_line_break_falls_back_to_multi_line_basic()
    {
        Assert.Equal("\"\"\"\na\nb\"\"\"", TomlEncoder.EncodeStringKeeping("a\nb", TomlStringForm.Literal, Lf));
    }

    [Fact]
    public void A_multi_line_literal_cannot_hold_a_carriage_return()
    {
        Assert.Equal("\"\"\"\na\\r\nb\"\"\"", TomlEncoder.EncodeStringKeeping("a\r\nb", TomlStringForm.MultiLineLiteral, Lf));
    }

    [Fact]
    public void A_multi_line_literal_cannot_hold_three_apostrophes_in_a_row()
    {
        Assert.Equal("\"a'''b\"", TomlEncoder.EncodeStringKeeping("a'''b", TomlStringForm.MultiLineLiteral, Crlf));
        Assert.Equal("'''\r\na''\r\n'''", TomlEncoder.EncodeStringKeeping("a''\n", TomlStringForm.MultiLineLiteral, Crlf));
    }

    [Fact]
    public void A_lone_surrogate_is_refused_in_every_form()
    {
        // The values are built at run time: attribute arguments are stored as UTF-8, which cannot hold a lone surrogate.
        string[] values = [new string((char)0xD800, 1), "x" + (char)0xDFFF, "a\n" + (char)0xD800 + "b"];
        const string message = "a TOML string cannot contain a lone surrogate";
        foreach (string value in values)
        {
            Assert.Equal(message, Assert.Throws<CfgeditException>(() => TomlEncoder.EncodeString(value, Lf)).Message);
            Assert.Equal(message, Assert.Throws<CfgeditException>(() => TomlEncoder.EncodeStringKeeping(value, TomlStringForm.Basic, Lf)).Message);
            Assert.Equal(message, Assert.Throws<CfgeditException>(() => TomlEncoder.EncodeKey(value)).Message);
        }
    }

    [Fact]
    public void A_surrogate_pair_is_not_a_lone_surrogate()
    {
        Assert.Equal("\"\U0001F600\"", TomlEncoder.EncodeString("\U0001F600", Lf));
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\t")]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("\r\n\r\n")]
    public void Any_line_break_other_than_lf_or_crlf_is_refused(string newline)
    {
        Assert.Throws<ArgumentException>(() => TomlEncoder.EncodeString("x\n", newline));
        Assert.Throws<ArgumentException>(() => TomlEncoder.EncodeStringKeeping("x\n", TomlStringForm.MultiLineBasic, newline));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("+0", true)]
    [InlineData("-0", true)]
    [InlineData("1_000", true)]
    [InlineData("0x1F", true)]
    [InlineData("0xdead_BEEF", true)]
    [InlineData("0o17", true)]
    [InlineData("0b1010", true)]
    [InlineData("9223372036854775807", true)]
    [InlineData("-9223372036854775808", true)]
    [InlineData("01", false)]
    [InlineData("+", false)]
    [InlineData("1__0", false)]
    [InlineData("0x", false)]
    [InlineData("0X1", false)]
    [InlineData("-0x1", false)]
    [InlineData("1.0", false)]
    [InlineData("9223372036854775808", false)]
    [InlineData("-9223372036854775809", false)]
    [InlineData("0xFFFFFFFFFFFFFFFF", false)]
    [InlineData("", false)]
    public void Integer_text_is_checked_against_the_grammar_and_the_64_bit_range(string text, bool valid)
    {
        Assert.Equal(valid, TomlEncoder.IsValidInteger(text));
    }

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("-0.5", true)]
    [InlineData("1e3", true)]
    [InlineData("+1E-2", true)]
    [InlineData("6.626e-34", true)]
    [InlineData("1_000.0", true)]
    [InlineData("inf", true)]
    [InlineData("-inf", true)]
    [InlineData("+nan", true)]
    [InlineData("1", false)]
    [InlineData("1.", false)]
    [InlineData(".5", false)]
    [InlineData("01.0", false)]
    [InlineData("1e", false)]
    [InlineData("Infinity", false)]
    [InlineData("nan_", false)]
    [InlineData("1.0_", false)]
    public void Float_text_is_checked_against_the_grammar(string text, bool valid)
    {
        Assert.Equal(valid, TomlEncoder.IsValidFloat(text));
    }

    [Theory]
    [InlineData("1979-05-27T07:32:00Z", true, TomlDateTimeKind.OffsetDateTime)]
    [InlineData("1979-05-27 07:32:00-07:00", true, TomlDateTimeKind.OffsetDateTime)]
    [InlineData("1979-05-27 07:32:00", true, TomlDateTimeKind.LocalDateTime)]
    [InlineData("1979-05-27", true, TomlDateTimeKind.LocalDate)]
    [InlineData("07:32:00", true, TomlDateTimeKind.LocalTime)]
    [InlineData("07:32", false, TomlDateTimeKind.LocalDate)]
    [InlineData("1979-02-29", false, TomlDateTimeKind.LocalDate)]
    [InlineData("1979-05-27T07:32:00+24:00", false, TomlDateTimeKind.LocalDate)]
    [InlineData("07:32:00Z", false, TomlDateTimeKind.LocalDate)]
    public void Date_time_text_is_checked_and_gives_its_kind(string text, bool valid, TomlDateTimeKind kind)
    {
        Assert.Equal(valid, TomlEncoder.TryParseDateTime(text, out TomlDateTimeKind actual));
        if (valid)
        {
            Assert.Equal(kind, actual);
        }
    }

    [Fact]
    public void Encoded_strings_read_back_through_the_parser_with_the_same_value()
    {
        string token = TomlEncoder.EncodeString("a\n\"b\"", Crlf);
        var value = Assert.IsType<TomlString>(ValueOf(token));
        Assert.Equal("a\n\"b\"", value.Value);
    }

    [Fact]
    public void A_multi_line_basic_string_with_a_leading_line_break_keeps_it()
    {
        string token = TomlEncoder.EncodeString("\nx", Lf);
        Assert.Equal("\"\"\"\n\nx\"\"\"", token);
        Assert.Equal("\nx", ((TomlString)ValueOf(token)).Value);
    }
}
