using System.Text;
using Cfgedit.Core.Json;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Tests.Json;

public sealed class JsonSyntaxTests
{
    private const int RandomSeed = 20260601;
    private const int RandomCount = 2000;

    private static void AssertDecodeError(string literal, JsonDialect dialect, string message)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => JsonSyntax.DecodeString(literal, dialect));

        Assert.Equal(message, ex.Message);
    }

    // ----- Escapes -----

    public static IEnumerable<object[]> StrictJsonEscapes()
    {
        yield return ["\"\\\"\"", "\""];
        yield return ["\"\\\\\"", "\\"];
        yield return ["\"\\/\"", "/"];
        yield return ["\"\\b\"", "\b"];
        yield return ["\"\\f\"", "\f"];
        yield return ["\"\\n\"", "\n"];
        yield return ["\"\\r\"", "\r"];
        yield return ["\"\\t\"", "\t"];
        yield return ["\"\\u0041\"", "A"];
        yield return ["\"\\u00e9\"", "\u00E9"];
        yield return ["\"\\uD83D\\uDE00\"", "\U0001F600"];
        yield return ["\"\\u0000\"", "\0"];
        yield return ["\"\\u001F\"", "\u001F"];
    }

    [Theory]
    [MemberData(nameof(StrictJsonEscapes))]
    public void Strict_json_escapes_decode(string literal, string expected)
    {
        Assert.Equal(expected, JsonSyntax.DecodeString(literal, JsonDialect.Json));
        Assert.Equal(expected, JsonSyntax.DecodeString(literal, JsonDialect.Jsonc));
    }

    public static IEnumerable<object[]> Json5Escapes()
    {
        yield return ["\"\\\"\"", "\""];
        yield return ["'\\''", "'"];
        yield return ["\"\\'\"", "'"];
        yield return ["'\\\"'", "\""];
        yield return ["\"\\\\\"", "\\"];
        yield return ["\"\\/\"", "/"];
        yield return ["\"\\b\\f\\n\\r\\t\"", "\b\f\n\r\t"];
        yield return ["\"\\v\"", "\v"];
        yield return ["\"\\0\"", "\0"];
        yield return ["\"\\x41\"", "A"];
        yield return ["\"\\xfF\"", "\u00FF"];
        yield return ["\"\\u0041\"", "A"];
        yield return ["\"\\a\"", "a"];
        yield return ["\"\\q\\z\"", "qz"];
        yield return ["\"a\\\nb\"", "ab"];
        yield return ["\"a\\\r\nb\"", "ab"];
        yield return ["\"a\\\rb\"", "ab"];
        yield return ["\"a\\\u2028b\"", "ab"];
        yield return ["\"a\\\u2029b\"", "ab"];
        yield return ["\"\\\U0001F600\"", "\U0001F600"];
    }

    [Theory]
    [MemberData(nameof(Json5Escapes))]
    public void Json5_escapes_decode(string literal, string expected)
    {
        Assert.Equal(expected, JsonSyntax.DecodeString(literal, JsonDialect.Json5));
    }

    // Lone surrogates are built in code: test data is serialized by the runner and would lose them.
    [Fact]
    public void Lone_surrogate_escapes_decode_to_the_lone_surrogate()
    {
        Assert.Equal(((char)0xD800).ToString(), JsonSyntax.DecodeString("\"\\uD800\"", JsonDialect.Json));
        Assert.Equal(((char)0xDC00).ToString(), JsonSyntax.DecodeString("\"\\uDc00\"", JsonDialect.Jsonc));
        Assert.Equal(((char)0xD83D).ToString(), JsonSyntax.DecodeString("\"\\uD83D\"", JsonDialect.Json5));
    }

    [Fact]
    public void Lone_surrogates_are_encoded_as_lowercase_escapes()
    {
        string value = "" + (char)0xD800 + "x" + (char)0xDC00;

        Assert.Equal("\"\\ud800x\\udc00\"", JsonSyntax.EncodeString(value, '"', JsonDialect.Json));
    }

    [Theory]
    [InlineData("\"\\'\"", "invalid escape sequence \\'")]
    [InlineData("\"\\v\"", "invalid escape sequence \\v")]
    [InlineData("\"\\x41\"", "invalid escape sequence \\x")]
    [InlineData("\"\\0\"", "invalid escape sequence \\0")]
    [InlineData("\"\\a\"", "invalid escape sequence \\a")]
    [InlineData("\"\\\n\"", "invalid escape sequence \\U+000A")]
    [InlineData("\"\\u12\"", "invalid \\u escape: expected four hex digits")]
    [InlineData("\"\\uZZZZ\"", "invalid \\u escape: expected four hex digits")]
    public void Strict_json_rejects_escapes_outside_its_list(string literal, string message)
    {
        AssertDecodeError(literal, JsonDialect.Json, message);
        AssertDecodeError(literal, JsonDialect.Jsonc, message);
    }

    [Theory]
    [InlineData("\"\\01\"", "\\0 must not be followed by a digit")]
    [InlineData("\"\\09\"", "\\0 must not be followed by a digit")]
    [InlineData("\"\\1\"", "octal escape sequences are not allowed")]
    [InlineData("\"\\8\"", "octal escape sequences are not allowed")]
    [InlineData("\"\\x4\"", "invalid \\x escape: expected two hex digits")]
    [InlineData("\"\\xG1\"", "invalid \\x escape: expected two hex digits")]
    [InlineData("\"\\u12\"", "invalid \\u escape: expected four hex digits")]
    public void Json5_rejects_bad_escapes(string literal, string message)
    {
        AssertDecodeError(literal, JsonDialect.Json5, message);
    }

    [Fact]
    public void Escape_at_the_end_of_the_text_is_unterminated()
    {
        AssertDecodeError("\"abc\\", JsonDialect.Json5, "unterminated string");
    }

    [Fact]
    public void Single_quoted_literal_is_not_a_string_in_strict_json()
    {
        AssertDecodeError("'a'", JsonDialect.Json, "not a string literal");
        AssertDecodeError("'a'", JsonDialect.Jsonc, "not a string literal");
    }

    [Fact]
    public void Empty_literal_is_not_a_string()
    {
        AssertDecodeError("", JsonDialect.Json5, "not a string literal");
    }

    [Fact]
    public void Text_after_the_closing_quote_is_rejected()
    {
        AssertDecodeError("\"a\"b", JsonDialect.Json, "unexpected text after the string literal");
    }

    [Fact]
    public void Unterminated_literal_is_rejected()
    {
        AssertDecodeError("\"abc", JsonDialect.Json, "unterminated string");
    }

    [Fact]
    public void Raw_control_character_is_rejected_in_strict_json_and_allowed_in_json5()
    {
        AssertDecodeError("\"a\tb\"", JsonDialect.Json, "control character U+0009 must be escaped in a string");
        Assert.Equal("a\tb", JsonSyntax.DecodeString("\"a\tb\"", JsonDialect.Json5));
    }

    [Fact]
    public void Raw_u_2028_and_u_2029_are_allowed_in_every_dialect()
    {
        foreach (JsonDialect dialect in Enum.GetValues<JsonDialect>())
        {
            Assert.Equal("a\u2028b\u2029", JsonSyntax.DecodeString("\"a\u2028b\u2029\"", dialect));
        }
    }

    [Fact]
    public void Raw_line_break_is_rejected_in_json5()
    {
        AssertDecodeError("\"a\nb\"", JsonDialect.Json5, "a line break must be escaped in a string");
    }

    // ----- EncodeString exact output -----

    [Theory]
    [InlineData("a\"b", '"', JsonDialect.Json, "\"a\\\"b\"")]
    [InlineData("it's", '\'', JsonDialect.Json5, "'it\\'s'")]
    [InlineData("it's", '"', JsonDialect.Json, "\"it's\"")]
    [InlineData("say \"hi\"", '"', JsonDialect.Json5, "\"say \\\"hi\\\"\"")]
    [InlineData("\\", '"', JsonDialect.Json, "\"\\\\\"")]
    [InlineData("\0\u0001\u001f\u007f", '"', JsonDialect.Json, "\"\\u0000\\u0001\\u001f\\u007f\"")]
    [InlineData("\b\f\n\r\t", '"', JsonDialect.Json, "\"\\b\\f\\n\\r\\t\"")]
    [InlineData("\u2028\u2029", '"', JsonDialect.Json, "\"\\u2028\\u2029\"")]
    [InlineData("\U0001F600", '"', JsonDialect.Json, "\"\U0001F600\"")]
    [InlineData("e\u0301\u200D", '"', JsonDialect.Json, "\"e\u0301\u200D\"")]
    [InlineData("]]>&<", '"', JsonDialect.Json, "\"]]>&<\"")]
    [InlineData("\r\n", '"', JsonDialect.Json5, "\"\\r\\n\"")]
    [InlineData("", '"', JsonDialect.Json, "\"\"")]
    [InlineData("\u0085\u00A0", '"', JsonDialect.Json, "\"\u0085\u00A0\"")]
    [InlineData("\u00E9\u65E5", '\'', JsonDialect.Json5, "'\u00E9\u65E5'")]
    public void Encode_string_matches_the_expected_literal(string value, char quote, JsonDialect dialect, string expected)
    {
        Assert.Equal(expected, JsonSyntax.EncodeString(value, quote, dialect));
    }

    [Fact]
    public void Single_quote_is_rejected_outside_json5()
    {
        Assert.Throws<ArgumentException>(() => JsonSyntax.EncodeString("x", '\'', JsonDialect.Json));
        Assert.Throws<ArgumentException>(() => JsonSyntax.EncodeString("x", '\'', JsonDialect.Jsonc));
    }

    [Fact]
    public void Quote_other_than_double_or_single_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => JsonSyntax.EncodeString("x", 'a', JsonDialect.Json5));
        Assert.Throws<ArgumentException>(() => JsonSyntax.EncodeString("x", '`', JsonDialect.Json5));
    }

    // ----- Identifier names -----

    [Theory]
    [InlineData("$", true)]
    [InlineData("_", true)]
    [InlineData("_1", true)]
    [InlineData("a", true)]
    [InlineData("a1", true)]
    [InlineData("1a", false)]
    [InlineData("", false)]
    [InlineData("\u00E9", true)]
    [InlineData("\u65E5\u672C", true)]
    [InlineData("a\u200Db", true)]
    [InlineData("\u200Da", false)]
    [InlineData("a\u0301", true)]
    [InlineData("a\u0663", true)]
    [InlineData("\u0663", false)]
    [InlineData("\u2160", true)]
    [InlineData("\U0001D49C", true)]
    [InlineData("a-b", false)]
    [InlineData("a b", false)]
    [InlineData("a\\u0062", false)]
    [InlineData("\uD835", false)]
    [InlineData("a\u00AD", false)]
    public void Identifier_name_edge_cases(string name, bool expected)
    {
        Assert.Equal(expected, JsonSyntax.IsIdentifierName(name));
    }

    [Theory]
    [InlineData("name", null, '"', JsonDialect.Json5, "name")]
    [InlineData("name", null, '\'', JsonDialect.Json5, "name")]
    [InlineData("name", null, '"', JsonDialect.Json, "\"name\"")]
    [InlineData("name", null, '"', JsonDialect.Jsonc, "\"name\"")]
    [InlineData("name", '\'', '"', JsonDialect.Json5, "'name'")]
    [InlineData("name", '"', '\'', JsonDialect.Json5, "\"name\"")]
    [InlineData("with space", null, '\'', JsonDialect.Json5, "'with space'")]
    [InlineData("1abc", null, '"', JsonDialect.Json5, "\"1abc\"")]
    [InlineData("", null, '"', JsonDialect.Json5, "\"\"")]
    [InlineData("class", null, '"', JsonDialect.Json5, "class")]
    [InlineData("a\"b", null, '"', JsonDialect.Json5, "\"a\\\"b\"")]
    [InlineData("\u043A\u043B\u044E\u0447", null, '"', JsonDialect.Json5, "\u043A\u043B\u044E\u0447")]
    public void Encode_key_choices(string name, char? originalQuote, char preferredQuote, JsonDialect dialect, string expected)
    {
        Assert.Equal(expected, JsonSyntax.EncodeKey(name, originalQuote, preferredQuote, dialect));
    }

    // ----- Numbers -----

    [Theory]
    [InlineData("0", true)]
    [InlineData("-0", true)]
    [InlineData("10", true)]
    [InlineData("0.5", true)]
    [InlineData("-1.5e+10", true)]
    [InlineData("1E5", true)]
    [InlineData("0e0", true)]
    [InlineData("123.456E-7", true)]
    [InlineData("00", false)]
    [InlineData("01", false)]
    [InlineData("-01", false)]
    [InlineData("+1", false)]
    [InlineData(".5", false)]
    [InlineData("1.", false)]
    [InlineData("1e", false)]
    [InlineData("1e+", false)]
    [InlineData("0.e1", false)]
    [InlineData("0x1", false)]
    [InlineData("Infinity", false)]
    [InlineData("NaN", false)]
    [InlineData("-", false)]
    [InlineData("", false)]
    [InlineData(" 1", false)]
    [InlineData("1 ", false)]
    [InlineData("1.5.2", false)]
    public void Strict_json_number_matrix(string text, bool expected)
    {
        Assert.Equal(expected, JsonSyntax.IsValidNumber(text, JsonDialect.Json));
        Assert.Equal(expected, JsonSyntax.IsValidNumber(text, JsonDialect.Jsonc));
    }

    [Theory]
    [InlineData("+1", true)]
    [InlineData("-1", true)]
    [InlineData(".5", true)]
    [InlineData("5.", true)]
    [InlineData("-.5e3", true)]
    [InlineData("0x1F", true)]
    [InlineData("-0xff", true)]
    [InlineData("0XAB", true)]
    [InlineData("0x0", true)]
    [InlineData("-0X1f", true)]
    [InlineData("+0", true)]
    [InlineData("+.5", true)]
    [InlineData("5.e3", true)]
    [InlineData("Infinity", true)]
    [InlineData("-Infinity", true)]
    [InlineData("+Infinity", true)]
    [InlineData("NaN", true)]
    [InlineData("+NaN", true)]
    [InlineData("-NaN", true)]
    [InlineData("1e05", true)]
    [InlineData("0.0", true)]
    [InlineData("0", true)]
    [InlineData("01", false)]
    [InlineData("00", false)]
    [InlineData("-01", false)]
    [InlineData("0x", false)]
    [InlineData("0xG", false)]
    [InlineData("0x-1", false)]
    [InlineData("0x1.5", false)]
    [InlineData("1_000", false)]
    [InlineData(".", false)]
    [InlineData("+", false)]
    [InlineData("-", false)]
    [InlineData("e5", false)]
    [InlineData("1e", false)]
    [InlineData("1e+", false)]
    [InlineData("--1", false)]
    [InlineData("+-1", false)]
    [InlineData("Infinity1", false)]
    [InlineData("infinity", false)]
    [InlineData("+ 1", false)]
    [InlineData(" 1", false)]
    [InlineData("1.2.3", false)]
    [InlineData("0b101", false)]
    [InlineData("0o7", false)]
    [InlineData("1d", false)]
    [InlineData("1f", false)]
    [InlineData("", false)]
    public void Json5_number_matrix(string text, bool expected)
    {
        Assert.Equal(expected, JsonSyntax.IsValidNumber(text, JsonDialect.Json5));
    }

    // ----- Round trips -----

    [Fact]
    public void Round_trip_over_the_nasty_corpus_in_each_valid_quote()
    {
        foreach (string value in NastyStrings.All)
        {
            AssertRoundTrip(value, '"', JsonDialect.Json);
            AssertRoundTrip(value, '"', JsonDialect.Jsonc);
            AssertRoundTrip(value, '"', JsonDialect.Json5);
            AssertRoundTrip(value, '\'', JsonDialect.Json5);
        }
    }

    [Fact]
    public void Round_trip_over_seeded_random_strings_in_each_valid_quote()
    {
        var random = new Random(RandomSeed);
        for (int n = 0; n < RandomCount; n++)
        {
            string value = RandomString(random);
            AssertRoundTrip(value, '"', JsonDialect.Json);
            AssertRoundTrip(value, '"', JsonDialect.Json5);
            AssertRoundTrip(value, '\'', JsonDialect.Json5);
        }
    }

    private static void AssertRoundTrip(string value, char quote, JsonDialect dialect)
    {
        string literal = JsonSyntax.EncodeString(value, quote, dialect);

        Assert.Equal(value, JsonSyntax.DecodeString(literal, dialect));

        // The literal must lex as exactly one string token that covers it, then the end of the file.
        IReadOnlyList<JsonToken> tokens = JsonLexer.Tokenize(SourceText.Create(literal, Encoding.UTF8, false), dialect);
        Assert.Equal(
            [new JsonToken(JsonTokenKind.String, 0, literal.Length), new JsonToken(JsonTokenKind.EndOfFile, literal.Length, 0)],
            tokens);
    }

    /// <summary>Random strings that mix every class of awkward character. The expectation is the input itself.</summary>
    private static string RandomString(Random random)
    {
        var sb = new StringBuilder();
        int length = random.Next(0, 40);
        for (int i = 0; i < length; i++)
        {
            switch (random.Next(10))
            {
                case 0:
                case 1:
                case 2:
                    sb.Append((char)random.Next(0x20, 0x7F));
                    break;
                case 3:
                    sb.Append((char)random.Next(0x00, 0x20));
                    break;
                case 4:
                    sb.Append((char)random.Next(0x7F, 0xA0));
                    break;
                case 5:
                    sb.Append(random.Next(2) == 0 ? '\u2028' : '\u2029');
                    break;
                case 6:
                    sb.Append((char)random.Next(0xD800, 0xE000));
                    break;
                case 7:
                    sb.Append(new Rune(random.Next(0x10000, 0x110000)).ToString());
                    break;
                case 8:
                    sb.Append((char)random.Next(0xA0, 0xD800));
                    break;
                default:
                    string[] specials = ["\\", "\"", "'", "\u200D", "\u0301", "\0", "]]>", "&", "<", "\r\n", "\r", "\n", "\u00A0", "\uFEFF"];
                    sb.Append(specials[random.Next(specials.Length)]);
                    break;
            }
        }

        return sb.ToString();
    }
}
