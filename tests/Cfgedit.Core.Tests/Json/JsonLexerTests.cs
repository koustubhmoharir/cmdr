using System.Text;
using Cfgedit.Core.Json;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Tests.Json;

public sealed class JsonLexerTests
{
    private static IReadOnlyList<JsonToken> Lex(string text, JsonDialect dialect)
    {
        return JsonLexer.Tokenize(SourceText.Create(text, Encoding.UTF8, false), dialect);
    }

    private static void AssertError(string text, JsonDialect dialect, string message, int line, int column)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Lex(text, dialect));

        Assert.Equal(message, ex.Message);
        Assert.Equal(line, ex.Line);
        Assert.Equal(column, ex.Column);
    }

    /// <summary>The tokens without whitespace, so that expectations read as the structure.</summary>
    private static JsonTokenKind[] Kinds(string text, JsonDialect dialect)
    {
        return Lex(text, dialect)
            .Where(t => t.Kind != JsonTokenKind.Whitespace)
            .Select(t => t.Kind)
            .ToArray();
    }

    private static string Span(string text, JsonToken token)
    {
        return text.Substring(token.Start, token.Length);
    }

    /// <summary>The tiling invariant: the spans are contiguous, they cover the text, and the list ends with EndOfFile.</summary>
    private static void AssertTiles(string text, IReadOnlyList<JsonToken> tokens)
    {
        int position = 0;
        var builder = new StringBuilder();
        foreach (JsonToken token in tokens)
        {
            Assert.Equal(position, token.Start);
            builder.Append(Span(text, token));
            position = token.End;
        }

        Assert.Equal(text, builder.ToString());
        Assert.Equal(JsonTokenKind.EndOfFile, tokens[^1].Kind);
        Assert.Equal(0, tokens[^1].Length);
        Assert.Equal(text.Length, tokens[^1].Start);
    }

    private const string Jsonc = "// header comment\r\n{\r\n  /* block\r\n     comment */\r\n  \"name\": \"app\",\r\n  \"list\": [1, 2, ],\r\n}\r\n";

    private const string Json5 = "{\r\n  // spec-style example\r\n  unquoted: 'single', hex: 0xFF, plus: +1,\r\n  inf: -Infinity, nan: NaN, dot: .5,\r\n  trailing: [true, false, null,],\r\n}";

    // ----- Tiling -----

    [Theory]
    [InlineData("", JsonDialect.Json)]
    [InlineData("{}", JsonDialect.Json)]
    [InlineData("[1, \"two\", true, false, null]", JsonDialect.Json)]
    [InlineData(Jsonc, JsonDialect.Jsonc)]
    [InlineData(Json5, JsonDialect.Json5)]
    [InlineData("\u00A0\uFEFF// c\u2028x: 'y' /* z */", JsonDialect.Json5)]
    public void Tokens_tile_the_text_exactly(string text, JsonDialect dialect)
    {
        AssertTiles(text, Lex(text, dialect));
    }

    [Fact]
    public void Empty_text_is_only_end_of_file()
    {
        IReadOnlyList<JsonToken> tokens = Lex("", JsonDialect.Json);

        Assert.Equal([new JsonToken(JsonTokenKind.EndOfFile, 0, 0)], tokens);
    }

    // ----- Each token kind -----

    [Fact]
    public void Punctuation_and_the_keywords_in_strict_json()
    {
        Assert.Equal(
            [
                JsonTokenKind.LeftBrace, JsonTokenKind.LeftBracket, JsonTokenKind.RightBracket, JsonTokenKind.Colon,
                JsonTokenKind.Comma, JsonTokenKind.RightBrace, JsonTokenKind.True, JsonTokenKind.False,
                JsonTokenKind.Null, JsonTokenKind.EndOfFile,
            ],
            Kinds("{[]:,} true false null", JsonDialect.Json));
    }

    [Fact]
    public void Punctuation_tokens_are_one_character_each()
    {
        Assert.Equal(
            [
                JsonTokenKind.LeftBrace, JsonTokenKind.RightBrace, JsonTokenKind.LeftBracket, JsonTokenKind.RightBracket,
                JsonTokenKind.Colon, JsonTokenKind.Comma, JsonTokenKind.EndOfFile,
            ],
            Kinds("{}[]:,", JsonDialect.Json));
    }

    [Fact]
    public void Strict_json_document_has_the_expected_kinds()
    {
        Assert.Equal(
            [
                JsonTokenKind.LeftBrace, JsonTokenKind.String, JsonTokenKind.Colon, JsonTokenKind.LeftBracket,
                JsonTokenKind.Number, JsonTokenKind.Comma, JsonTokenKind.True, JsonTokenKind.Comma,
                JsonTokenKind.False, JsonTokenKind.Comma, JsonTokenKind.Null, JsonTokenKind.Comma,
                JsonTokenKind.String, JsonTokenKind.RightBracket, JsonTokenKind.RightBrace, JsonTokenKind.EndOfFile,
            ],
            Kinds("{\"a\": [1, true, false, null, \"s\"]}", JsonDialect.Json));
    }

    [Fact]
    public void Json5_keys_values_and_infinity_and_nan()
    {
        Assert.Equal(
            [
                JsonTokenKind.LeftBrace, JsonTokenKind.Identifier, JsonTokenKind.Colon, JsonTokenKind.Number,
                JsonTokenKind.Comma, JsonTokenKind.String, JsonTokenKind.Colon, JsonTokenKind.Number,
                JsonTokenKind.Comma, JsonTokenKind.Identifier, JsonTokenKind.Colon, JsonTokenKind.Number,
                JsonTokenKind.RightBrace, JsonTokenKind.EndOfFile,
            ],
            Kinds("{a:1,'b':Infinity,c:-NaN}", JsonDialect.Json5));
    }

    [Fact]
    public void Json5_reserved_word_as_a_key_is_lexed_as_its_keyword()
    {
        Assert.Equal(
            [JsonTokenKind.LeftBrace, JsonTokenKind.True, JsonTokenKind.Colon, JsonTokenKind.Null, JsonTokenKind.RightBrace, JsonTokenKind.EndOfFile],
            Kinds("{true:null}", JsonDialect.Json5));
    }

    [Fact]
    public void Json_with_comments_has_comment_tokens_with_exact_spans()
    {
        const string text = "// one\n/* two\nlines */ 1";

        IReadOnlyList<JsonToken> tokens = Lex(text, JsonDialect.Jsonc);

        Assert.Equal(
            [
                JsonTokenKind.LineComment, JsonTokenKind.Whitespace, JsonTokenKind.BlockComment,
                JsonTokenKind.Whitespace, JsonTokenKind.Number, JsonTokenKind.EndOfFile,
            ],
            tokens.Select(t => t.Kind).ToArray());
        Assert.Equal("// one", Span(text, tokens[0]));
        Assert.Equal("/* two\nlines */", Span(text, tokens[2]));
        Assert.Equal("1", Span(text, tokens[4]));
    }

    [Fact]
    public void Line_comment_ends_at_a_line_feed_or_carriage_return()
    {
        const string text = "//a\r//b\n1";

        IReadOnlyList<JsonToken> tokens = Lex(text, JsonDialect.Jsonc);

        Assert.Equal("//a", Span(text, tokens[0]));
        Assert.Equal(JsonTokenKind.Whitespace, tokens[1].Kind);
        Assert.Equal("//b", Span(text, tokens[2]));
    }

    [Fact]
    public void Json5_line_comment_ends_at_u_2028()
    {
        const string text = "//a\u2028b";

        IReadOnlyList<JsonToken> tokens = Lex(text, JsonDialect.Json5);

        Assert.Equal("//a", Span(text, tokens[0]));
        Assert.Equal(JsonTokenKind.Whitespace, tokens[1].Kind);
        Assert.Equal(JsonTokenKind.Identifier, tokens[2].Kind);
    }

    [Fact]
    public void Jsonc_line_comment_does_not_end_at_u_2028()
    {
        const string text = "//a\u2028b";

        IReadOnlyList<JsonToken> tokens = Lex(text, JsonDialect.Jsonc);

        Assert.Equal(text, Span(text, tokens[0]));
        Assert.Equal(JsonTokenKind.EndOfFile, tokens[1].Kind);
    }

    [Fact]
    public void Block_comment_may_span_lines()
    {
        const string text = "/* a\r\n b */";

        Assert.Equal(new JsonToken(JsonTokenKind.BlockComment, 0, text.Length), Lex(text, JsonDialect.Json5)[0]);
    }

    // ----- Whitespace -----

    [Fact]
    public void Strict_whitespace_is_one_token_of_space_tab_lf_and_cr()
    {
        Assert.Equal(
            [JsonTokenKind.Whitespace, JsonTokenKind.EndOfFile],
            Lex("\t\r\n ", JsonDialect.Json).Select(t => t.Kind).ToArray());
    }

    [Theory]
    [InlineData("\u000B", 0x000B)]
    [InlineData("\u000C", 0x000C)]
    [InlineData("\u00A0", 0x00A0)]
    [InlineData("\uFEFF", 0xFEFF)]
    [InlineData("\u2028", 0x2028)]
    [InlineData("\u2029", 0x2029)]
    [InlineData("\u3000", 0x3000)]
    public void Json5_whitespace_characters_are_accepted(string ws, int codePoint)
    {
        IReadOnlyList<JsonToken> tokens = Lex(ws, JsonDialect.Json5);

        Assert.Equal(JsonTokenKind.Whitespace, tokens[0].Kind);
        Assert.Equal(1, tokens[0].Length);
        Assert.Equal(codePoint, (int)Span(ws, tokens[0])[0]);
    }

    [Theory]
    [InlineData("\u000B", "invalid character U+000B")]
    [InlineData("\u000C", "invalid character U+000C")]
    [InlineData("\u00A0", "invalid character U+00A0")]
    [InlineData("\uFEFF", "invalid character U+FEFF")]
    [InlineData("\u2028", "invalid character U+2028")]
    [InlineData("\u3000", "invalid character U+3000")]
    public void Json_rejects_the_json5_only_whitespace_characters(string ws, string message)
    {
        AssertError(ws, JsonDialect.Json, message, 1, 1);
        AssertError(ws, JsonDialect.Jsonc, message, 1, 1);
    }

    // ----- Strings -----

    [Fact]
    public void Double_quoted_string_is_one_token_including_quotes()
    {
        const string text = "\"a\\\"b\"";

        Assert.Equal(new JsonToken(JsonTokenKind.String, 0, text.Length), Lex(text, JsonDialect.Json)[0]);
    }

    [Fact]
    public void Single_quoted_string_is_a_json5_string()
    {
        const string text = "'a\"b'";

        Assert.Equal(new JsonToken(JsonTokenKind.String, 0, text.Length), Lex(text, JsonDialect.Json5)[0]);
    }

    [Theory]
    [InlineData("'a'", JsonDialect.Json)]
    [InlineData("'a'", JsonDialect.Jsonc)]
    public void Single_quoted_string_is_rejected_outside_json5(string text, JsonDialect dialect)
    {
        AssertError(text, dialect, "single-quoted strings are only allowed in JSON5", 1, 1);
    }

    [Fact]
    public void Unterminated_string_is_reported_at_its_opening_quote()
    {
        AssertError("\"abc", JsonDialect.Json, "unterminated string", 1, 1);
    }

    [Fact]
    public void String_ending_in_a_backslash_is_unterminated()
    {
        AssertError("[\"abc\\", JsonDialect.Json5, "unterminated string", 1, 2);
    }

    [Fact]
    public void Invalid_escape_is_reported_at_the_backslash()
    {
        AssertError("\"a\\qb\"", JsonDialect.Json, "invalid escape sequence \\q", 1, 3);
    }

    [Fact]
    public void Single_quote_escape_is_invalid_in_strict_json()
    {
        AssertError("\"\\'\"", JsonDialect.Jsonc, "invalid escape sequence \\'", 1, 2);
    }

    [Fact]
    public void Bad_u_escape_is_reported_at_the_backslash()
    {
        AssertError("\"\\u12G4\"", JsonDialect.Json, "invalid \\u escape: expected four hex digits", 1, 2);
    }

    [Fact]
    public void Raw_control_character_is_rejected_in_strict_json()
    {
        AssertError("\"a\tb\"", JsonDialect.Json, "control character U+0009 must be escaped in a string", 1, 3);
    }

    [Fact]
    public void Raw_control_character_is_accepted_in_json5()
    {
        Assert.Equal(new JsonToken(JsonTokenKind.String, 0, 5), Lex("\"a\tb\"", JsonDialect.Json5)[0]);
    }

    [Fact]
    public void Raw_line_break_is_rejected_in_json5()
    {
        AssertError("\"a\rb\"", JsonDialect.Json5, "a line break must be escaped in a string", 1, 3);
    }

    [Fact]
    public void Raw_u_2028_is_accepted_in_strings_in_every_dialect()
    {
        foreach (JsonDialect dialect in Enum.GetValues<JsonDialect>())
        {
            Assert.Equal(new JsonToken(JsonTokenKind.String, 0, 5), Lex("\"a\u2028b\"", dialect)[0]);
        }
    }

    [Fact]
    public void Error_after_an_astral_character_counts_it_as_two_columns()
    {
        AssertError("\"\U0001F600\" #", JsonDialect.Json, "invalid character '#'", 1, 6);
    }

    [Fact]
    public void Error_after_an_astral_character_on_a_later_line_counts_columns_from_its_line()
    {
        AssertError("[\n\"\U0001F600\" #", JsonDialect.Json, "invalid character '#'", 2, 6);
    }

    [Fact]
    public void Escaped_string_error_after_an_astral_character_is_placed_correctly()
    {
        AssertError("\"\U0001F600\\q\"", JsonDialect.Json, "invalid escape sequence \\q", 1, 4);
    }

    // ----- Literals, identifiers -----

    [Fact]
    public void Capitalised_literal_is_rejected_in_strict_json()
    {
        AssertError("True", JsonDialect.Json, "unquoted name 'True' is only allowed in JSON5", 1, 1);
    }

    [Fact]
    public void Longer_word_is_rejected_in_strict_json()
    {
        AssertError("[truex]", JsonDialect.Jsonc, "unquoted name 'truex' is only allowed in JSON5", 1, 2);
    }

    [Fact]
    public void Identifiers_including_escapes_are_lexed_in_json5()
    {
        const string text = "$_ \u00FCn\u00EF \\u0061bc a\u200Db";

        IReadOnlyList<JsonToken> tokens = Lex(text, JsonDialect.Json5);

        Assert.Equal(JsonTokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("$_", Span(text, tokens[0]));
        Assert.Equal("\u00FCn\u00EF", Span(text, tokens[2]));
        Assert.Equal("\\u0061bc", Span(text, tokens[4]));
        Assert.Equal("a\u200Db", Span(text, tokens[6]));
    }

    [Fact]
    public void Identifier_spelled_like_a_keyword_with_an_escape_is_an_identifier()
    {
        Assert.Equal(JsonTokenKind.Identifier, Lex("\\u0074rue", JsonDialect.Json5)[0].Kind);
    }

    [Fact]
    public void Escape_that_is_not_an_identifier_character_is_rejected()
    {
        AssertError("\\u0031abc", JsonDialect.Json5, "escape does not produce a valid identifier character", 1, 1);
    }

    [Fact]
    public void Identifier_escape_other_than_u_is_rejected()
    {
        AssertError("\\x41", JsonDialect.Json5, "invalid escape in identifier: only \\uXXXX is allowed", 1, 1);
    }

    [Fact]
    public void Identifier_with_short_u_escape_is_rejected()
    {
        AssertError("{\\u00}", JsonDialect.Json5, "invalid \\u escape: expected four hex digits", 1, 2);
    }

    [Fact]
    public void Identifier_followed_by_a_minus_sign_is_a_bad_number()
    {
        AssertError("a-b", JsonDialect.Json5, "invalid number '-b'", 1, 2);
    }

    [Fact]
    public void Lone_surrogate_is_an_invalid_character()
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(
            () => JsonLexer.Tokenize(SourceText.Create("\uD800", Encoding.UTF8, false), JsonDialect.Json));

        Assert.Equal("invalid character U+D800", ex.Message);
        Assert.Equal(1, ex.Line);
        Assert.Equal(1, ex.Column);
    }

    [Fact]
    public void Hash_is_an_invalid_character_in_json5()
    {
        AssertError("#", JsonDialect.Json5, "invalid character '#'", 1, 1);
    }

    // ----- Numbers -----

    [Theory]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("10")]
    [InlineData("0.5")]
    [InlineData("-1.5e+10")]
    [InlineData("1E5")]
    [InlineData("0e0")]
    public void Strict_numbers_are_number_tokens(string number)
    {
        Assert.Equal(new JsonToken(JsonTokenKind.Number, 0, number.Length), Lex(number, JsonDialect.Json)[0]);
    }

    [Theory]
    [InlineData("01", "invalid number '01'")]
    [InlineData("1.", "invalid number '1.'")]
    [InlineData("+1", "invalid number '+1'")]
    [InlineData(".5", "invalid number '.5'")]
    [InlineData("0x1F", "invalid number '0x1F'")]
    [InlineData("1e", "invalid number '1e'")]
    [InlineData("-", "invalid number '-'")]
    [InlineData("1_000", "invalid number '1_000'")]
    public void Strict_json_rejects_numbers_outside_its_grammar(string number, string message)
    {
        AssertError(number, JsonDialect.Json, message, 1, 1);
    }

    [Theory]
    [InlineData("Infinity", "unquoted name 'Infinity' is only allowed in JSON5")]
    [InlineData("NaN", "unquoted name 'NaN' is only allowed in JSON5")]
    public void Strict_json_rejects_infinity_and_nan(string word, string message)
    {
        AssertError(word, JsonDialect.Json, message, 1, 1);
    }

    [Theory]
    [InlineData("+1")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("-.5e3")]
    [InlineData("0x1F")]
    [InlineData("-0xff")]
    [InlineData("0XAB")]
    [InlineData("+0")]
    [InlineData("0.5")]
    [InlineData("+Infinity")]
    [InlineData("-Infinity")]
    [InlineData("-NaN")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    [InlineData("1e05")]
    public void Json5_numbers_are_number_tokens(string number)
    {
        Assert.Equal(new JsonToken(JsonTokenKind.Number, 0, number.Length), Lex(number, JsonDialect.Json5)[0]);
    }

    [Theory]
    [InlineData("01", "invalid number '01'")]
    [InlineData("-01", "invalid number '-01'")]
    [InlineData("0x", "invalid number '0x'")]
    [InlineData("0xG", "invalid number '0xG'")]
    [InlineData(".", "invalid number '.'")]
    [InlineData("1e", "invalid number '1e'")]
    [InlineData("--1", "invalid number '--1'")]
    [InlineData("1_000", "invalid number '1_000'")]
    [InlineData("+Infinity1", "invalid number '+Infinity1'")]
    public void Json5_rejects_numbers_outside_its_grammar(string number, string message)
    {
        AssertError(number, JsonDialect.Json5, message, 1, 1);
    }

    [Fact]
    public void Lowercase_infinity_is_an_identifier_in_json5()
    {
        Assert.Equal(JsonTokenKind.Identifier, Lex("infinity", JsonDialect.Json5)[0].Kind);
    }

    // ----- Comments in strict JSON -----

    [Fact]
    public void Line_comment_is_rejected_in_strict_json()
    {
        AssertError("//x", JsonDialect.Json, "comments are not allowed in JSON", 1, 1);
    }

    [Fact]
    public void Block_comment_is_rejected_in_strict_json()
    {
        AssertError("/* x */", JsonDialect.Json, "comments are not allowed in JSON", 1, 1);
    }

    [Fact]
    public void Unterminated_block_comment_is_reported_at_its_start()
    {
        AssertError("[1, /* x", JsonDialect.Jsonc, "unterminated block comment", 1, 5);
    }

    [Fact]
    public void Slash_alone_is_an_invalid_character()
    {
        AssertError("1 /", JsonDialect.Jsonc, "invalid character '/'", 1, 3);
    }

    [Fact]
    public void Block_comment_must_not_reuse_its_opening_slash_as_its_closing_star()
    {
        AssertError("/*/", JsonDialect.Json5, "unterminated block comment", 1, 1);
    }

    // ----- Full documents -----

    [Fact]
    public void Jsonc_sample_has_comments_and_a_trailing_comma()
    {
        IReadOnlyList<JsonToken> tokens = Lex(Jsonc, JsonDialect.Jsonc);

        Assert.Contains(tokens, t => t.Kind == JsonTokenKind.LineComment && Span(Jsonc, t) == "// header comment");
        Assert.Contains(tokens, t => t.Kind == JsonTokenKind.BlockComment && Span(Jsonc, t) == "/* block\r\n     comment */");

        // The commas after "2" and after "]" are the trailing commas: the next significant token closes the container.
        int trailing = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != JsonTokenKind.Comma)
            {
                continue;
            }

            JsonTokenKind next = tokens.Skip(i + 1).First(t => t.Kind is not (JsonTokenKind.Whitespace or JsonTokenKind.LineComment or JsonTokenKind.BlockComment)).Kind;
            if (next is JsonTokenKind.RightBracket or JsonTokenKind.RightBrace)
            {
                trailing++;
            }
        }

        Assert.Equal(2, trailing);
    }

    [Fact]
    public void Json5_sample_lexes_each_form()
    {
        IReadOnlyList<JsonToken> tokens = Lex(Json5, JsonDialect.Json5);

        Assert.Contains(tokens, t => t.Kind == JsonTokenKind.Identifier && Span(Json5, t) == "unquoted");
        Assert.Contains(tokens, t => t.Kind == JsonTokenKind.String && Span(Json5, t) == "'single'");
        Assert.Contains(tokens, t => t.Kind == JsonTokenKind.Number && Span(Json5, t) == "0xFF");
        Assert.Contains(tokens, t => t.Kind == JsonTokenKind.Number && Span(Json5, t) == "-Infinity");
        Assert.Contains(tokens, t => t.Kind == JsonTokenKind.Number && Span(Json5, t) == "NaN");
        Assert.Contains(tokens, t => t.Kind == JsonTokenKind.LineComment && Span(Json5, t) == "// spec-style example");
    }
}
