using System.Text;
using Cfgedit.Core.Toml;
using Shared.Core;
using static Cfgedit.Core.Tests.Toml.TomlTestSupport;

namespace Cfgedit.Core.Tests.Toml;

/// <summary>
/// The parser's tree and its errors: each value kind, each string form, arrays, inline tables, headers, keys, comments and line
/// breaks. Error messages are exact, with the line and column of the offending character.
/// </summary>
public sealed class TomlParserTests
{
    [Fact]
    public void Integers_keep_their_raw_text_radix_and_value()
    {
        Assert.Equal(42, IntegerOf("42", 10, out string raw).Value);
        Assert.Equal("42", raw);
        Assert.Equal(99, IntegerOf("+99", 10, out _).Value);
        Assert.Equal(-17, IntegerOf("-17", 10, out _).Value);
        Assert.Equal(1000, IntegerOf("1_000", 10, out _).Value);
        Assert.Equal(0xDEADBEEF, IntegerOf("0xDEAD_beef", 16, out _).Value);
        Assert.Equal(493, IntegerOf("0o755", 8, out _).Value);
        Assert.Equal(13, IntegerOf("0b1101", 2, out _).Value);
        Assert.Equal(long.MaxValue, IntegerOf("9223372036854775807", 10, out _).Value);
        Assert.Equal(long.MinValue, IntegerOf("-9223372036854775808", 10, out _).Value);
        Assert.Equal(long.MaxValue, IntegerOf("0x7fffffffffffffff", 16, out _).Value);
    }

    [Fact]
    public void Floats_keep_their_raw_text_and_value()
    {
        Assert.Equal(1.0, FloatOf("1.0", out string raw));
        Assert.Equal("1.0", raw);
        Assert.Equal(-0.5, FloatOf("-0.5", out _));
        Assert.Equal(6.626e-34, FloatOf("6.626e-34", out _));
        Assert.Equal(1000.0, FloatOf("1e3", out _));
        Assert.Equal(1000.5, FloatOf("1_000.5", out _));
        Assert.Equal(double.PositiveInfinity, FloatOf("+inf", out _));
        Assert.Equal(double.NegativeInfinity, FloatOf("-inf", out _));
        Assert.True(double.IsNaN(FloatOf("nan", out _)));
        Assert.True(double.IsNaN(FloatOf("-nan", out _)));
    }

    [Fact]
    public void Booleans_are_lower_case_only()
    {
        Assert.True(Assert.IsType<TomlBoolean>(ValueOf("true")).Value);
        Assert.False(Assert.IsType<TomlBoolean>(ValueOf("false")).Value);
        AssertParseError("k = True\n", "expected a value but found 'True'", 1, 5);
    }

    [Theory]
    [InlineData("1979-05-27T07:32:00Z", TomlDateTimeKind.OffsetDateTime)]
    [InlineData("1979-05-27 07:32:00.999-07:00", TomlDateTimeKind.OffsetDateTime)]
    [InlineData("1979-05-27t07:32:00z", TomlDateTimeKind.OffsetDateTime)]
    [InlineData("1979-05-27T07:32:00", TomlDateTimeKind.LocalDateTime)]
    [InlineData("1979-05-27", TomlDateTimeKind.LocalDate)]
    [InlineData("07:32:00.5", TomlDateTimeKind.LocalTime)]
    public void Date_times_have_their_kind(string text, TomlDateTimeKind kind)
    {
        var value = Assert.IsType<TomlDateTime>(ValueOf(text));
        Assert.Equal(kind, value.Kind);
        Assert.Equal(text, value.Raw);
    }

    [Fact]
    public void A_space_separated_date_time_is_one_value()
    {
        SingleKeyValue("k = 1979-05-27 07:32:00Z # tail\n", out TomlDocument doc);
        var item = Assert.IsType<TomlKeyValueItem>(doc.Items[0]);
        Assert.Equal("1979-05-27 07:32:00Z", ((TomlDateTime)item.Value).Raw);
        Assert.Equal(" tail", item.Comment!.Content);
    }

    [Fact]
    public void A_date_followed_by_a_comment_is_a_local_date()
    {
        SingleKeyValue("k = 1979-05-27 # tail\n", out TomlDocument doc);
        var item = Assert.IsType<TomlKeyValueItem>(doc.Items[0]);
        Assert.Equal(TomlDateTimeKind.LocalDate, ((TomlDateTime)item.Value).Kind);
        Assert.Equal(" tail", item.Comment!.Content);
    }

    [Theory]
    [InlineData("1979-05-27T07:32:60Z")]
    [InlineData("2000-02-29")]
    [InlineData("2024-02-29T23:59:59.123456+23:59")]
    [InlineData("0000-01-01")]
    public void Valid_date_times_are_accepted(string text)
    {
        Assert.True(TomlEncoder.TryParseDateTime(text, out _), text);
        Assert.IsType<TomlDateTime>(ValueOf(text));
    }

    [Theory]
    [InlineData("1979-13-27", "invalid date-time '1979-13-27': the month must be 01 to 12")]
    [InlineData("07:32:00.12345678", "invalid date-time '07:32:00.12345678': cfgedit supports at most 7 fractional-second digits")]
    [InlineData("1979-05-27T07:32:00.12345678Z", "invalid date-time '1979-05-27T07:32:00.12345678Z': cfgedit supports at most 7 fractional-second digits")]
    [InlineData("2023-02-29", "invalid date-time '2023-02-29': the day must be 01 to 28 for that month")]
    [InlineData("1900-02-29", "invalid date-time '1900-02-29': the day must be 01 to 28 for that month")]
    [InlineData("2023-04-31", "invalid date-time '2023-04-31': the day must be 01 to 30 for that month")]
    [InlineData("2023-01-00", "invalid date-time '2023-01-00': the day must be 01 to 31 for that month")]
    [InlineData("07:60:00", "invalid date-time '07:60:00': the minute must be 00 to 59")]
    [InlineData("24:00:00", "invalid date-time '24:00:00': the hour must be 00 to 23")]
    [InlineData("07:00:61", "invalid date-time '07:00:61': the second must be 00 to 60")]
    [InlineData("1979-05-27T07:32:00+24:00", "invalid date-time '1979-05-27T07:32:00+24:00': the offset is out of range")]
    public void Date_times_out_of_range_are_refused_with_the_field_named(string text, string message)
    {
        Assert.Equal(message, Assert.Throws<CfgeditException>(() => ValueOf(text)).Message);
    }

    [Fact]
    public void Basic_strings_decode_their_escapes()
    {
        var value = Assert.IsType<TomlString>(ValueOf("\"a\\tb\\\\c\\\"d\""));
        Assert.Equal(TomlStringForm.Basic, value.Form);
        Assert.Equal("\"a\\tb\\\\c\\\"d\"", value.Raw);
        Assert.Equal("a\tb\\c\"d", value.Value);
        Assert.Equal("\b\t\n\f\r\"\\", ((TomlString)ValueOf("\"\\b\\t\\n\\f\\r\\\"\\\\\"")).Value);
        Assert.Equal("é😀", ((TomlString)ValueOf("\"\\u00E9\\U0001F600\"")).Value);
        Assert.Equal("\0", ((TomlString)ValueOf("\"\\u0000\"")).Value);
    }

    [Fact]
    public void Literal_strings_keep_backslashes_and_tabs()
    {
        var value = Assert.IsType<TomlString>(ValueOf("'C:\\Users\\a\tb'"));
        Assert.Equal(TomlStringForm.Literal, value.Form);
        Assert.Equal("C:\\Users\\a\tb", value.Value);
    }

    [Fact]
    public void Multi_line_basic_trims_the_opening_newline_and_keeps_the_rest()
    {
        var value = Assert.IsType<TomlString>(ValueOf("\"\"\"\none\n  two\"\"\""));
        Assert.Equal(TomlStringForm.MultiLineBasic, value.Form);
        Assert.Equal("one\n  two", value.Value);
    }

    [Fact]
    public void Multi_line_basic_reads_a_crlf_newline_as_lf()
    {
        var value = Assert.IsType<TomlString>(ValueOf("\"\"\"\r\none\r\ntwo\"\"\""));
        Assert.Equal("one\ntwo", value.Value);
        Assert.Equal("\"\"\"\r\none\r\ntwo\"\"\"", value.Raw);
    }

    [Fact]
    public void Multi_line_basic_trims_a_line_ending_backslash_with_the_next_whitespace()
    {
        Assert.Equal("a b", ((TomlString)ValueOf("\"\"\"a \\\n   b\"\"\"")).Value);
        Assert.Equal("a b", ((TomlString)ValueOf("\"\"\"a \\  \r\n\r\n\t b\"\"\"")).Value);
    }

    [Fact]
    public void Multi_line_strings_may_end_with_one_or_two_quotation_marks()
    {
        Assert.Equal("x\"\"", ((TomlString)ValueOf("\"\"\"x\"\"\"\"\"")).Value);
        Assert.Equal("a\"", ((TomlString)ValueOf("\"\"\"a\"\"\"\"")).Value);
        Assert.Equal("\"", ((TomlString)ValueOf("\"\"\"\\\"\"\"\"")).Value);
    }

    [Fact]
    public void Multi_line_literal_keeps_backslashes_and_trims_the_opening_newline()
    {
        var value = Assert.IsType<TomlString>(ValueOf("'''\nC:\\x\n'''"));
        Assert.Equal(TomlStringForm.MultiLineLiteral, value.Form);
        Assert.Equal("C:\\x\n", value.Value);
        Assert.Equal(string.Empty, ((TomlString)ValueOf("''''''")).Value);
        Assert.Equal("'x'", ((TomlString)ValueOf("''''x''''")).Value);
    }

    [Theory]
    [InlineData("k = \"abc\n", "a basic string must close on the same line", 1, 5)]
    [InlineData("k = \"abc", "a basic string is not closed", 1, 5)]
    [InlineData("k = \"a\rb\"\n", "a basic string must close on the same line", 1, 5)]
    [InlineData("k = \"\u0001\"\n", "control character U+0001 is not allowed in a string", 1, 6)]
    [InlineData("k = \"\u007F\"\n", "control character U+007F is not allowed in a string", 1, 6)]
    [InlineData("k = \"\\x41\"\n", "invalid escape sequence: 'x' follows the backslash", 1, 6)]
    [InlineData("k = \"\\u12\"\n", "a \\u escape needs 4 hexadecimal digits", 1, 6)]
    [InlineData("k = \"\\uD800\"\n", "a unicode escape must be a Unicode scalar value", 1, 6)]
    [InlineData("k = \"\\U00110000\"\n", "a unicode escape must be a Unicode scalar value", 1, 6)]
    [InlineData("k = 'abc\n", "a literal string must close on the same line", 1, 5)]
    [InlineData("k = 'abc", "a literal string is not closed", 1, 5)]
    [InlineData("k = '\u0000'\n", "control character U+0000 is not allowed in a string", 1, 6)]
    [InlineData("k = \"\"\"abc", "a multi-line basic string is not closed", 1, 5)]
    [InlineData("k = '''abc", "a multi-line literal string is not closed", 1, 5)]
    [InlineData("k = \"\"\"a\"\"\"\"\"\"\"\n", "a multi-line string cannot end with more than two quotation marks before its closing delimiter", 1, 9)]
    [InlineData("k = \"\"\"a\rb\"\"\"\n", "a carriage return must be followed by a line feed", 1, 9)]
    [InlineData("k = \"\"\"\\  x\"\"\"\n", "invalid escape sequence: spaces after a backslash are allowed only at the end of a line", 1, 8)]
    public void String_errors_are_refused_at_the_offending_character(string text, string message, int line, int column)
    {
        AssertParseError(text, message, line, column);
    }

    [Fact]
    public void A_lone_surrogate_in_a_string_is_refused()
    {
        AssertParseError("k = \"\uD800\"\n", "a lone surrogate is not valid in TOML", 1, 6);
    }

    [Fact]
    public void A_surrogate_pair_in_a_string_is_kept_whole()
    {
        Assert.Equal("\U0001F600", ((TomlString)ValueOf("\"\U0001F600\"")).Value);
    }

    [Theory]
    [InlineData("k = 01\n", "invalid integer '01'", 1, 5)]
    [InlineData("k = 00\n", "invalid integer '00'", 1, 5)]
    [InlineData("k = 1__0\n", "invalid integer '1__0'", 1, 5)]
    [InlineData("k = -0x1\n", "invalid integer '-0x1'", 1, 5)]
    [InlineData("k = 0x\n", "invalid integer '0x'", 1, 5)]
    [InlineData("k = 9223372036854775808\n", "integer '9223372036854775808' is outside the 64-bit signed range", 1, 5)]
    [InlineData("k = 0x8000000000000000\n", "integer '0x8000000000000000' is outside the 64-bit signed range", 1, 5)]
    [InlineData("k = 1.\n", "invalid float '1.'", 1, 5)]
    [InlineData("k = 1e\n", "invalid float '1e'", 1, 5)]
    [InlineData("k = .5\n", "expected a value but found '.5'", 1, 5)]
    [InlineData("k = infinity\n", "invalid float 'infinity'", 1, 5)]
    [InlineData("k = 1 2\n", "expected end of line but found '2'", 1, 7)]
    public void Number_errors_are_refused_at_the_value(string text, string message, int line, int column)
    {
        AssertParseError(text, message, line, column);
    }

    [Theory]
    [InlineData("a\n", "expected '=' after the key but found end of line", 1, 2)]
    [InlineData("a", "expected '=' after the key but found end of file", 1, 2)]
    [InlineData("= 1\n", "expected a key but found '='", 1, 1)]
    [InlineData("k = \n", "expected a value but found end of line", 1, 5)]
    [InlineData("k =", "expected a value but found end of file", 1, 4)]
    [InlineData("a. = 1\n", "expected a key after '.' but found '='", 1, 4)]
    [InlineData("a..b = 1\n", "expected a key after '.' but found '.'", 1, 3)]
    [InlineData("a = 1 b\n", "expected end of line but found 'b'", 1, 7)]
    [InlineData("a = 1\rb = 2\n", "a carriage return must be followed by a line feed", 1, 6)]
    [InlineData("a = 1 # bad\u0001\n", "control character U+0001 is not allowed in a comment", 1, 12)]
    [InlineData("[a\n", "expected ']' to close the table name but found end of line", 1, 3)]
    [InlineData("[[a]\n", "expected ']]' to close the array table name but found end of line", 1, 5)]
    [InlineData("[a]]\n", "expected end of line but found ']'", 1, 4)]
    [InlineData("[]\n", "expected a key but found ']'", 1, 2)]
    [InlineData("[ a\n]\n", "expected ']' to close the table name but found end of line", 1, 4)]
    public void Line_structure_errors_are_refused_at_the_offending_character(string text, string message, int line, int column)
    {
        AssertParseError(text, message, line, column);
    }

    [Theory]
    [InlineData("a = [1 2]\n", "expected ',' or ']' but found '2'", 1, 8)]
    [InlineData("a = [,]\n", "expected a value but found ','", 1, 6)]
    [InlineData("a = [1,,2]\n", "expected a value but found ','", 1, 8)]
    [InlineData("a = [1", "expected a value or ']' but found end of file", 1, 7)]
    [InlineData("a = { b = 1,}\n", "a trailing comma is not allowed in an inline table", 1, 13)]
    [InlineData("a = { b = 1\n}\n", "a newline is not allowed in an inline table", 1, 12)]
    [InlineData("a = { b = 1 c = 2 }\n", "expected ',' or '}' but found 'c'", 1, 13)]
    [InlineData("a = {", "an inline table is not closed", 1, 5)]
    [InlineData("a = { b 1 }\n", "expected '=' after the key but found '1'", 1, 9)]
    public void Container_errors_are_refused_at_the_offending_character(string text, string message, int line, int column)
    {
        AssertParseError(text, message, line, column);
    }

    [Fact]
    public void Arrays_nest_up_to_the_depth_limit_and_no_further()
    {
        string ok = "k = " + new string('[', 256) + new string(']', 256) + "\n";
        Assert.IsType<TomlArray>(SingleKeyValue(ok, out _).Value);

        string deep = "k = " + new string('[', 257) + new string(']', 257) + "\n";
        AssertParseError(deep, "nesting is deeper than 256 levels", 1, 261);
    }

    [Fact]
    public void Inline_tables_nest_up_to_the_depth_limit_and_no_further()
    {
        string ok = "k = " + string.Concat(Enumerable.Repeat("{a=", 256)) + "1" + new string('}', 256) + "\n";
        Assert.IsType<TomlInlineTable>(SingleKeyValue(ok, out _).Value);

        // Each "{a=" is three characters, so the 257th brace is at offset 4 + 3 * 256.
        string deep = "k = " + string.Concat(Enumerable.Repeat("{a=", 257)) + "1" + new string('}', 257) + "\n";
        AssertParseError(deep, "nesting is deeper than 256 levels", 1, 773);
    }

    [Fact]
    public void Mixed_nesting_counts_arrays_and_inline_tables_together()
    {
        // Each "[{a=" adds two levels, so the 257th level is the bracket of the 129th unit, at offset 4 + 4 * 128.
        string deep = "k = " + string.Concat(Enumerable.Repeat("[{a=", 129)) + "1" + new string('}', 129) + new string(']', 129) + "\n";
        AssertParseError(deep, "nesting is deeper than 256 levels", 1, 517);
    }

    [Fact]
    public void Every_key_part_kind_is_decoded()
    {
        SingleKeyValue("a.b . \"c d\".'e'.1234 = 1\n", out TomlDocument doc);
        var item = Assert.IsType<TomlKeyValueItem>(doc.Items[0]);
        Assert.Equal(new[] { "a", "b", "c d", "e", "1234" }, item.Key.Parts.Select(p => p.Name));
        Assert.Equal(new[] { TomlKeyForm.Bare, TomlKeyForm.Bare, TomlKeyForm.Basic, TomlKeyForm.Literal, TomlKeyForm.Bare }, item.Key.Parts.Select(p => p.Form));
        Assert.Equal(new[] { ".", " . ", ".", "." }, item.Key.Separators.Select(s => doc.Source.Text.Substring(s.Start, s.Length)));
    }

    [Fact]
    public void A_quoted_key_is_decoded_and_keeps_its_raw_text()
    {
        SingleKeyValue("\"a\\u0062\" = 1\n", out TomlDocument doc);
        TomlKeyPart part = Assert.Single(((TomlKeyValueItem)doc.Items[0]).Key.Parts);
        Assert.Equal("ab", part.Name);
        Assert.Equal("\"a\\u0062\"", part.Raw);
        Assert.Equal(TomlKeyForm.Basic, part.Form);

        SingleKeyValue("'a\\b' = 1\n", out doc);
        Assert.Equal("a\\b", ((TomlKeyValueItem)doc.Items[0]).Key.Parts[0].Name);
    }

    [Fact]
    public void An_array_keeps_the_comments_and_line_breaks_before_its_comma()
    {
        SingleKeyValue("a = [1 # one\n, 2]\n", out TomlDocument doc);
        var array = Assert.IsType<TomlArray>(((TomlKeyValueItem)doc.Items[0]).Value);
        Assert.Equal(3, array.Entries.Count);

        var first = Assert.IsType<TomlArrayItem>(array.Entries[0]);
        Assert.NotNull(first.Comma);
        Assert.Equal(3, first.BeforeComma.Count);
        Assert.Equal(" one", Assert.IsType<TomlComment>(first.BeforeComma[1]).Content);

        Assert.IsType<TomlWhitespace>(array.Entries[1]);
        var last = Assert.IsType<TomlArrayItem>(array.Entries[2]);
        Assert.Null(last.Comma);
        Assert.Empty(last.BeforeComma);
    }

    [Fact]
    public void A_comment_after_an_item_and_before_the_next_is_an_array_entry()
    {
        SingleKeyValue("a = [1, # two\n 2]\n", out TomlDocument doc);
        var array = Assert.IsType<TomlArray>(((TomlKeyValueItem)doc.Items[0]).Value);
        Assert.Equal(
            new[] { "TomlArrayItem", "TomlWhitespace", "TomlComment", "TomlWhitespace", "TomlArrayItem" },
            array.Entries.Select(e => e.GetType().Name));
    }

    [Fact]
    public void A_key_value_item_holds_its_parts_and_its_trailing_comment()
    {
        SingleKeyValue("  a . b = \"x\" # note\n", out TomlDocument doc);
        var item = Assert.IsType<TomlKeyValueItem>(doc.Items[0]);
        Assert.Equal("  ", doc.Source.Text.Substring(item.Indent.Start, item.Indent.Length));
        Assert.Equal(" = ", doc.Source.Text.Substring(item.Assignment.Start, item.Assignment.Length));
        Assert.Equal(" ", doc.Source.Text.Substring(item.TrailingSpace.Start, item.TrailingSpace.Length));
        Assert.Equal(" note", item.Comment!.Content);
        Assert.Equal("\n", doc.Source.Text.Substring(item.LineBreak.Start, item.LineBreak.Length));
    }

    [Fact]
    public void A_table_header_holds_the_spaces_inside_its_brackets()
    {
        SingleItem("[ a . \"b c\" ]\n", out TomlDocument doc);
        var header = Assert.IsType<TomlTableItem>(doc.Items[0]);
        Assert.Equal(" ", doc.Source.Text.Substring(header.SpaceAfterOpen.Start, header.SpaceAfterOpen.Length));
        Assert.Equal(" ", doc.Source.Text.Substring(header.SpaceBeforeClose.Start, header.SpaceBeforeClose.Length));
        Assert.Equal(new[] { "a", "b c" }, header.Key.Parts.Select(p => p.Name));
        Assert.Equal("[", doc.Source.Text.Substring(header.OpenSpan.Start, header.OpenSpan.Length));
        Assert.Equal("]", doc.Source.Text.Substring(header.CloseSpan.Start, header.CloseSpan.Length));
    }

    [Fact]
    public void An_array_table_header_has_double_brackets()
    {
        SingleItem("[[ x ]] # list\n", out TomlDocument doc);
        var header = Assert.IsType<TomlArrayTableItem>(doc.Items[0]);
        Assert.Equal("[[", doc.Source.Text.Substring(header.OpenSpan.Start, header.OpenSpan.Length));
        Assert.Equal("]]", doc.Source.Text.Substring(header.CloseSpan.Start, header.CloseSpan.Length));
        Assert.Equal(" list", header.Comment!.Content);
    }

    [Fact]
    public void Each_line_is_one_item_of_its_kind_in_source_order()
    {
        TomlDocument doc = Parse("a = 1\n\n# c\n[t]\n[[u]]  # x\n  \n");
        Assert.Collection(
            doc.Items,
            item => Assert.IsType<TomlKeyValueItem>(item),
            item => Assert.IsType<TomlBlankItem>(item),
            item => Assert.IsType<TomlCommentItem>(item),
            item => Assert.IsType<TomlTableItem>(item),
            item => Assert.IsType<TomlArrayTableItem>(item),
            item => Assert.IsType<TomlBlankItem>(item));
        AssertTiles(doc);
    }

    [Fact]
    public void An_empty_text_has_no_items()
    {
        Assert.Empty(Parse(string.Empty).Items);
        TomlDocument blank = Parse("\n\n");
        Assert.Equal(2, blank.Items.Count);
        Assert.All(blank.Items, item => Assert.IsType<TomlBlankItem>(item));
    }

    [Fact]
    public void A_last_line_without_a_line_break_has_an_empty_line_break()
    {
        TomlDocument doc = Parse("a = 1");
        var item = Assert.IsType<TomlKeyValueItem>(doc.Items[0]);
        Assert.Equal(0, item.LineBreak.Length);
        Assert.Equal(5, item.End);
        AssertTiles(doc);
    }

    [Fact]
    public void Crlf_is_one_line_break_and_a_lone_carriage_return_is_an_error()
    {
        TomlDocument doc = Parse("a = 1\r\nb = 2\r\n");
        Assert.All(doc.Items, item => Assert.Equal(2, item.LineBreak.Length));
        AssertTiles(doc);
    }

    [Fact]
    public void Leading_spaces_and_tabs_are_the_indent_of_their_line()
    {
        TomlDocument doc = Parse("  a = 1\n\t# c\n");
        Assert.Equal("  ", doc.Source.Text.Substring(doc.Items[0].Indent.Start, doc.Items[0].Indent.Length));
        Assert.Equal("\t", doc.Source.Text.Substring(doc.Items[1].Indent.Start, doc.Items[1].Indent.Length));
        AssertTiles(doc);
    }

    [Fact]
    public void A_byte_order_mark_is_kept_outside_the_tree()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a = 1\n")];
        SourceText source = SourceText.Decode(bytes);
        TomlDocument doc = TomlDocument.Parse(source);
        Assert.True(source.HasBom);
        Assert.Equal("a = 1\n", source.Text);
        Assert.Equal(bytes, source.Encode());
        AssertTiles(doc);
    }

    [Fact]
    public void A_utf16_text_is_refused_because_toml_is_utf8()
    {
        var source = SourceText.Create("a = 1\n", Encoding.Unicode, false);
        CfgeditException ex = Assert.Throws<CfgeditException>(() => TomlDocument.Parse(source));
        Assert.Equal("a TOML file must be UTF-8", ex.Message);
    }

    [Fact]
    public void A_control_character_in_a_key_or_outside_a_string_is_refused()
    {
        AssertParseError("a\u0001 = 1\n", "expected '=' after the key but found U+0001", 1, 2);
        AssertParseError("a = 1\u00A0\n", "expected end of line but found '\u00A0'", 1, 6);
    }

    [Fact]
    public void Every_character_belongs_to_exactly_one_span_for_a_mixed_document()
    {
        const string text = "# head\n[t]\na = [\n  1, # one\n  { b = \"x\" },\n]\n\"q\" = '''\nx'''  # tail\n[[u]]\nc.d = 1979-05-27\n";
        TomlDocument doc = Parse(text);
        AssertTiles(doc);
        Assert.Equal(text, Reassemble(doc));
    }

    private static TomlInteger IntegerOf(string text, int radix, out string raw)
    {
        var value = Assert.IsType<TomlInteger>(ValueOf(text));
        Assert.Equal(radix, value.Base);
        raw = value.Raw;
        return value;
    }

    private static double FloatOf(string text, out string raw)
    {
        var value = Assert.IsType<TomlFloat>(ValueOf(text));
        raw = value.Raw;
        return value.Value;
    }
}
