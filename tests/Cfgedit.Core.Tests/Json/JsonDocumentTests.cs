using System.Text;
using Cfgedit.Core.Json;
using static Cfgedit.Core.Tests.Json.JsonTestSupport;

namespace Cfgedit.Core.Tests.Json;

public sealed class JsonDocumentTests
{
    // ----- Spans with exact offsets -----

    [Fact]
    public void Spans_are_exact_offsets_into_the_text()
    {
        const string text = "{\"a\": [1, {\"b\": 2}]}";
        JsonDocument doc = Parse(text, JsonDialect.Json);

        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        Assert.Equal(0, root.Start);
        Assert.Equal(20, root.End);
        Assert.Single(root.Entries);

        JsonMember a = Assert.IsType<JsonMember>(root.Entries[0]);
        Assert.Equal(1, a.Start);
        Assert.Equal(19, a.End);
        Assert.Equal(1, a.Key.Start);
        Assert.Equal(4, a.Key.End);
        Assert.Equal(4, a.ColonStart);
        Assert.Null(a.CommaStart);

        JsonArray list = Assert.IsType<JsonArray>(a.Value);
        Assert.Equal(6, list.Start);
        Assert.Equal(19, list.End);
        Assert.Null(list.TrailingCommaStart);
        Assert.Equal(2, list.Entries.Count);

        JsonItem one = Assert.IsType<JsonItem>(list.Entries[0]);
        Assert.Equal(7, one.Start);
        Assert.Equal(8, one.End);
        Assert.Equal(8, one.CommaStart);

        JsonItem inner = Assert.IsType<JsonItem>(list.Entries[1]);
        Assert.Equal(10, inner.Start);
        Assert.Equal(18, inner.End);
        Assert.Null(inner.CommaStart);

        JsonObject innerObject = Assert.IsType<JsonObject>(inner.Value);
        Assert.Equal(10, innerObject.Start);
        Assert.Equal(18, innerObject.End);
        JsonMember b = Assert.IsType<JsonMember>(innerObject.Entries[0]);
        Assert.Equal(11, b.Start);
        Assert.Equal(14, b.Key.End);
        Assert.Equal(16, b.Value.Start);
        Assert.Equal(17, b.Value.End);
    }

    [Fact]
    public void Container_span_covers_both_brackets_and_newlines_inside_them()
    {
        const string text = "[\n  1\n]";
        JsonDocument doc = Parse(text, JsonDialect.Json);

        Assert.Equal(0, doc.Root.Start);
        Assert.Equal(text.Length, doc.Root.End);
        Assert.Equal(text, Slice(doc, doc.Root));
    }

    // ----- Comment placement -----

    [Fact]
    public void Comments_inside_a_member_come_before_it_and_comments_after_its_value_come_after_it()
    {
        string text = Lines(
            "{",
            "  /* before key */ \"a\" /* after key */ : /* before value */ 1 /* after value */ , // trailing",
            "  \"b\": 2",
            "}");
        JsonDocument doc = Parse(text, JsonDialect.Json5);

        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        Assert.Equal(7, root.Entries.Count);
        AssertComment(root.Entries[0], true, " before key ");
        AssertComment(root.Entries[1], true, " after key ");
        AssertComment(root.Entries[2], true, " before value ");

        JsonMember a = AssertMember(root.Entries[3], "a", '"');
        AssertAt(doc, a.Start, 2, 20);
        AssertAt(doc, a.End, 2, 62);
        AssertAt(doc, a.CommaStart, 2, 81);
        Assert.Equal("\"a\" /* after key */ : /* before value */ 1", Slice(doc, a));

        AssertComment(root.Entries[4], true, " after value ");
        AssertComment(root.Entries[5], false, " trailing");
        AssertMember(root.Entries[6], "b", '"');
        Assert.Null(((JsonMember)root.Entries[6]).CommaStart);
        Assert.Null(root.TrailingCommaStart);
    }

    [Fact]
    public void Comments_in_an_array_are_entries_in_source_order_with_exact_offsets()
    {
        const string text = "[ /* c */ 1 /* d */ , 2 ]";
        JsonDocument doc = Parse(text, JsonDialect.Json5);

        JsonArray list = Assert.IsType<JsonArray>(doc.Root);
        Assert.Equal(0, list.Start);
        Assert.Equal(25, list.End);
        Assert.Equal(4, list.Entries.Count);

        JsonComment c = Assert.IsType<JsonComment>(list.Entries[0]);
        Assert.Equal(2, c.Start);
        Assert.Equal(9, c.End);
        Assert.True(c.IsBlock);
        Assert.Equal(" c ", c.Content);

        JsonItem one = Assert.IsType<JsonItem>(list.Entries[1]);
        Assert.Equal(10, one.Start);
        Assert.Equal(11, one.End);
        Assert.Equal(20, one.CommaStart);

        JsonComment d = Assert.IsType<JsonComment>(list.Entries[2]);
        Assert.Equal(12, d.Start);
        Assert.Equal(19, d.End);
        Assert.Equal(" d ", d.Content);

        JsonItem two = Assert.IsType<JsonItem>(list.Entries[3]);
        Assert.Equal(22, two.Start);
        Assert.Equal(23, two.End);
        Assert.Null(two.CommaStart);
        Assert.Null(list.TrailingCommaStart);
    }

    [Fact]
    public void Comments_outside_the_root_value_are_leading_and_trailing()
    {
        const string text = "/* a */ {} // b\n/* c */";
        JsonDocument doc = Parse(text, JsonDialect.Json5);

        Assert.Equal([" a "], Contents(doc.Leading));
        Assert.Equal(0, doc.Leading[0].Start);
        Assert.Equal(7, doc.Leading[0].End);
        Assert.Equal(8, doc.Root.Start);
        Assert.Equal(10, doc.Root.End);
        Assert.Equal([" b", " c "], Contents(doc.Trailing));
        Assert.Equal(11, doc.Trailing[0].Start);
        Assert.Equal(15, doc.Trailing[0].End);
        Assert.Equal(16, doc.Trailing[1].Start);
        Assert.Equal(23, doc.Trailing[1].End);
    }

    [Fact]
    public void Comment_spans_include_their_markers_and_content_excludes_them()
    {
        const string text = "{\n  // line\n  /* block */\n  \"k\": 1\n}";
        JsonDocument doc = Parse(text, JsonDialect.Jsonc);

        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        JsonComment line = Assert.IsType<JsonComment>(root.Entries[0]);
        Assert.Equal("// line", Slice(doc, line));
        Assert.Equal(" line", line.Content);
        Assert.False(line.IsBlock);

        JsonComment block = Assert.IsType<JsonComment>(root.Entries[1]);
        Assert.Equal("/* block */", Slice(doc, block));
        Assert.Equal(" block ", block.Content);
        Assert.True(block.IsBlock);
    }

    [Fact]
    public void Line_comment_content_excludes_the_line_break()
    {
        JsonDocument doc = Parse("[1, // one\r\n 2]", JsonDialect.Jsonc);

        JsonArray list = Assert.IsType<JsonArray>(doc.Root);
        JsonComment comment = Assert.IsType<JsonComment>(list.Entries[1]);
        Assert.Equal(" one", comment.Content);
        Assert.Equal("// one", Slice(doc, comment));
    }

    // ----- Trailing commas -----

    [Theory]
    [InlineData(JsonDialect.Jsonc)]
    [InlineData(JsonDialect.Json5)]
    public void One_trailing_comma_is_recorded_in_objects_and_arrays(JsonDialect dialect)
    {
        // Offsets: the array's comma is at 8, the member a's comma at 10, and the object's trailing comma at 18.
        JsonDocument doc = Parse("{\"a\": [1,], \"b\": 2,}", dialect);

        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        Assert.Equal(18, root.TrailingCommaStart);
        JsonMember b = Assert.IsType<JsonMember>(root.Entries[1]);
        Assert.Equal(18, b.CommaStart);

        JsonMember a = Assert.IsType<JsonMember>(root.Entries[0]);
        Assert.Equal(10, a.CommaStart);
        JsonArray list = Assert.IsType<JsonArray>(a.Value);
        Assert.Equal(8, list.TrailingCommaStart);
        Assert.Equal(8, Assert.IsType<JsonItem>(list.Entries[0]).CommaStart);
    }

    [Fact]
    public void A_middle_comma_is_not_a_trailing_comma()
    {
        JsonDocument doc = Parse("[1, 2]", JsonDialect.Jsonc);

        JsonArray list = Assert.IsType<JsonArray>(doc.Root);
        Assert.Null(list.TrailingCommaStart);
        Assert.Equal(2, Assert.IsType<JsonItem>(list.Entries[0]).CommaStart);
        Assert.Null(Assert.IsType<JsonItem>(list.Entries[1]).CommaStart);
    }

    [Fact]
    public void Trailing_comma_with_a_comment_after_it_is_still_trailing()
    {
        JsonDocument doc = Parse("[1, // last\n]", JsonDialect.Jsonc);

        JsonArray list = Assert.IsType<JsonArray>(doc.Root);
        Assert.Equal(2, list.TrailingCommaStart);
        Assert.Equal(2, Assert.IsType<JsonItem>(list.Entries[0]).CommaStart);
        Assert.Equal(" last", Assert.IsType<JsonComment>(list.Entries[1]).Content);
    }

    // ----- Scalars -----

    [Theory]
    [InlineData("1.0", JsonDialect.Json, JsonScalarKind.Number, "1.0")]
    [InlineData("1e3", JsonDialect.Json, JsonScalarKind.Number, "1e3")]
    [InlineData("-0", JsonDialect.Json, JsonScalarKind.Number, "-0")]
    [InlineData("-2.5E-3", JsonDialect.Jsonc, JsonScalarKind.Number, "-2.5E-3")]
    [InlineData("0x1F", JsonDialect.Json5, JsonScalarKind.Number, "0x1F")]
    [InlineData(".5", JsonDialect.Json5, JsonScalarKind.Number, ".5")]
    [InlineData("+1", JsonDialect.Json5, JsonScalarKind.Number, "+1")]
    [InlineData("5.", JsonDialect.Json5, JsonScalarKind.Number, "5.")]
    [InlineData("Infinity", JsonDialect.Json5, JsonScalarKind.Number, "Infinity")]
    [InlineData("-Infinity", JsonDialect.Json5, JsonScalarKind.Number, "-Infinity")]
    [InlineData("+Infinity", JsonDialect.Json5, JsonScalarKind.Number, "+Infinity")]
    [InlineData("NaN", JsonDialect.Json5, JsonScalarKind.Number, "NaN")]
    [InlineData("true", JsonDialect.Json, JsonScalarKind.True, "true")]
    [InlineData("false", JsonDialect.Jsonc, JsonScalarKind.False, "false")]
    [InlineData("null", JsonDialect.Json5, JsonScalarKind.Null, "null")]
    public void Each_scalar_kind_is_the_root_with_its_raw_text(string text, JsonDialect dialect, JsonScalarKind kind, string raw)
    {
        JsonDocument doc = Parse(text, dialect);

        AssertScalar(doc.Root, kind, raw);
        Assert.Equal(0, doc.Root.Start);
        Assert.Equal(text.Length, doc.Root.End);
    }

    [Theory]
    [InlineData("\"s\"", JsonDialect.Json, "\"s\"", "s", '"')]
    [InlineData("'s'", JsonDialect.Json5, "'s'", "s", '\'')]
    [InlineData("\"\\u00e9\"", JsonDialect.Jsonc, "\"\\u00e9\"", "\u00E9", '"')]
    public void String_root_has_raw_value_and_quote(string text, JsonDialect dialect, string raw, string value, char quote)
    {
        JsonDocument doc = Parse(text, dialect);

        AssertString(doc.Root, raw, value, quote);
    }

    [Fact]
    public void Numbers_keep_their_spelling_in_members()
    {
        JsonDocument doc = Parse("{\"a\": 1.0, \"b\": 1e3, \"c\": -0, \"d\": 0x1F, \"e\": .5}", JsonDialect.Json5);

        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        Assert.Equal("1.0", Raw(root, 0));
        Assert.Equal("1e3", Raw(root, 1));
        Assert.Equal("-0", Raw(root, 2));
        Assert.Equal("0x1F", Raw(root, 3));
        Assert.Equal(".5", Raw(root, 4));

        static string Raw(JsonObject obj, int index) => ((JsonScalar)((JsonMember)obj.Entries[index]).Value).Raw;
    }

    // ----- Keys -----

    [Fact]
    public void Json5_keys_are_identifiers_keywords_infinity_nan_and_quoted_names()
    {
        const string text = "{Infinity: 1, NaN: 2, true: 3, false: 4, null: 5, \\u0062: 6, $: 7, 'x y': 8, \"\": 9}";
        JsonDocument doc = Parse(text, JsonDialect.Json5);

        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        string[] names = ["Infinity", "NaN", "true", "false", "null", "b", "$", "x y", ""];
        Assert.Equal(names.Length, root.Entries.Count);
        // The first seven keys are bare: the identifiers, Infinity, NaN and the keywords, and one escaped identifier.
        for (int i = 0; i < 7; i++)
        {
            JsonMember member = Assert.IsType<JsonMember>(root.Entries[i]);
            Assert.Equal(names[i], member.Key.Name);
            Assert.Null(member.Key.Quote);
        }

        JsonMember quoted = Assert.IsType<JsonMember>(root.Entries[7]);
        Assert.Equal("x y", quoted.Key.Name);
        Assert.Equal('\'', quoted.Key.Quote);
        JsonMember empty = Assert.IsType<JsonMember>(root.Entries[8]);
        Assert.Equal("", empty.Key.Name);
        Assert.Equal('"', empty.Key.Quote);

        JsonMember escaped = Assert.IsType<JsonMember>(root.Entries[5]);
        Assert.Equal(text.IndexOf("\\u0062", StringComparison.Ordinal), escaped.Key.Start);
        Assert.Equal(text.IndexOf("\\u0062", StringComparison.Ordinal) + 6, escaped.Key.End);
    }

    [Fact]
    public void Json5_key_spans_match_the_name_as_written()
    {
        JsonDocument doc = Parse("{ 'a\\x41': 1, Infinity: 2 }", JsonDialect.Json5);

        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        JsonMember quoted = Assert.IsType<JsonMember>(root.Entries[0]);
        Assert.Equal("'a\\x41'", Slice(doc, quoted.Key.Start, quoted.Key.End));
        Assert.Equal("aA", quoted.Key.Name);
    }

    [Fact]
    public void Duplicate_keys_are_accepted_in_source_order()
    {
        JsonDocument doc = Parse("{\"a\": 1, \"a\": 2}", JsonDialect.Json);

        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        Assert.Equal(2, root.Entries.Count);
        JsonMember first = Assert.IsType<JsonMember>(root.Entries[0]);
        JsonMember second = Assert.IsType<JsonMember>(root.Entries[1]);
        Assert.Equal("a", first.Key.Name);
        Assert.Equal("a", second.Key.Name);
        AssertScalar(first.Value, JsonScalarKind.Number, "1");
        AssertScalar(second.Value, JsonScalarKind.Number, "2");
    }

    [Fact]
    public void Empty_object_and_array_have_no_entries()
    {
        JsonDocument doc = Parse("{\"a\": {}, \"b\": [ ]}", JsonDialect.Json);

        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        Assert.Empty(Assert.IsType<JsonObject>(((JsonMember)root.Entries[0]).Value).Entries);
        Assert.Empty(Assert.IsType<JsonArray>(((JsonMember)root.Entries[1]).Value).Entries);
    }

    // ----- Errors: exact message, line and column -----

    public static IEnumerable<object[]> Errors()
    {
        const string noValue = "expected a value but found end of file";
        yield return [string.Empty, JsonDialect.Json, noValue, 1, 1];
        yield return ["  \n ", JsonDialect.Jsonc, noValue, 2, 2];
        yield return ["// one\n/* two */\n", JsonDialect.Jsonc, noValue, 3, 1];
        yield return ["{\"a\" 1}", JsonDialect.Json, "expected ':' but found number", 1, 6];
        yield return ["{\"a\": 1 \"b\": 2}", JsonDialect.Json, "expected ',' or '}' but found string", 1, 9];
        yield return ["{\n  \"a\": 1\n  \"b\": 2\n}", JsonDialect.Json, "expected ',' or '}' but found string", 3, 3];
        yield return ["[1 2]", JsonDialect.Json, "expected ',' or ']' but found number", 1, 4];
        yield return ["[1,,2]", JsonDialect.Json, "expected a value but found ','", 1, 4];
        yield return ["[1,,2]", JsonDialect.Jsonc, "expected a value or ']' but found ','", 1, 4];
        yield return ["[,1]", JsonDialect.Json5, "expected a value or ']' but found ','", 1, 2];
        yield return ["{,\"a\":1}", JsonDialect.Json, "expected a key or '}' but found ','", 1, 2];
        yield return ["[1,]", JsonDialect.Json, "expected a value but found ']'", 1, 4];
        yield return ["{\"a\":1,}", JsonDialect.Json, "expected a key but found '}'", 1, 8];
        yield return ["[1,,]", JsonDialect.Jsonc, "expected a value or ']' but found ','", 1, 4];
        yield return ["{\"a\":1,,}", JsonDialect.Json5, "expected a key or '}' but found ','", 1, 8];
        yield return ["{\"a\": 1", JsonDialect.Json, "expected ',' or '}' but found end of file", 1, 8];
        yield return ["[1, 2", JsonDialect.Json, "expected ',' or ']' but found end of file", 1, 6];
        yield return ["[1,", JsonDialect.Json, noValue, 1, 4];
        yield return ["[1, 2}", JsonDialect.Json, "expected ',' or ']' but found '}'", 1, 6];
        yield return ["{\"a\": [1, 2}", JsonDialect.Json5, "expected ',' or ']' but found '}'", 1, 12];
        yield return ["{} {}", JsonDialect.Json, "expected end of file but found '{'", 1, 4];
        yield return ["1,", JsonDialect.Jsonc, "expected end of file but found ','", 1, 2];
        yield return ["{\"a\": }", JsonDialect.Json, "expected a value but found '}'", 1, 7];
        yield return ["{1: 2}", JsonDialect.Json5, "expected a key or '}' but found number", 1, 2];
        yield return ["{-Infinity: 1}", JsonDialect.Json5, "expected a key or '}' but found number", 1, 2];
        yield return ["{+Infinity: 1}", JsonDialect.Json5, "expected a key or '}' but found number", 1, 2];
        yield return ["{true: 1}", JsonDialect.Json, "expected a key or '}' but found 'true'", 1, 2];
        yield return ["{a: 1}", JsonDialect.Jsonc, "unquoted name 'a' is only allowed in JSON5", 1, 2];
        yield return ["{'a': 1}", JsonDialect.Jsonc, "single-quoted strings are only allowed in JSON5", 1, 2];
        yield return ["// x\n1", JsonDialect.Json, "comments are not allowed in JSON", 1, 1];

        // An astral character is two UTF-16 units, so the column after it is two more than a BMP character would give.
        yield return ["[\"\U0001F600\" 1]", JsonDialect.Json, "expected ',' or ']' but found number", 1, 7];
        yield return ["[\"\U0001F600\", x]", JsonDialect.Json, "unquoted name 'x' is only allowed in JSON5", 1, 8];
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public void Invalid_text_reports_the_message_and_position(string text, JsonDialect dialect, string message, int line, int column)
    {
        AssertError(text, dialect, message, line, column);
    }

    [Theory]
    [InlineData("[1,]", JsonDialect.Jsonc)]
    [InlineData("[1,]", JsonDialect.Json5)]
    [InlineData("{\"a\":1,}", JsonDialect.Jsonc)]
    [InlineData("{\"a\":1,}", JsonDialect.Json5)]
    public void Trailing_comma_is_accepted_in_jsonc_and_json5(string text, JsonDialect dialect)
    {
        JsonDocument doc = Parse(text, dialect);

        Assert.NotNull(doc.Root);
    }

    // ----- Depth -----

    [Fact]
    public void Mixed_nesting_at_the_cap_parses_and_is_256_levels_deep()
    {
        JsonDocument doc = Parse(MixedNesting(JsonDocument.MaxDepth), JsonDialect.Json);

        int levels = 0;
        JsonValue value = doc.Root;
        while (value is JsonObject or JsonArray)
        {
            levels++;
            value = value is JsonObject obj
                ? ((JsonMember)obj.Entries[0]).Value
                : ((JsonItem)((JsonArray)value).Entries[0]).Value;
        }

        Assert.Equal(256, levels);
        AssertScalar(value, JsonScalarKind.Number, "1");
    }

    [Fact]
    public void Mixed_nesting_one_past_the_cap_fails_at_the_opening_bracket_that_exceeds_it()
    {
        // Each level is on its own line, so the 257th opening bracket is at line 257, column 1.
        AssertError(MixedNesting(JsonDocument.MaxDepth + 1), JsonDialect.Json, "nesting is deeper than 256 levels", 257, 1);
    }

    [Fact]
    public void Nesting_of_100000_fails_cleanly_at_the_cap()
    {
        AssertError(MixedNesting(100_000), JsonDialect.Json, "nesting is deeper than 256 levels", 257, 1);
    }

    [Fact]
    public void Single_line_array_nesting_fails_at_column_257()
    {
        string accepted = new string('[', 256) + new string(']', 256);
        Assert.NotNull(Parse(accepted, JsonDialect.Json).Root);

        AssertError(new string('[', 257), JsonDialect.Json, "nesting is deeper than 256 levels", 1, 257);
    }

    /// <summary>
    /// Nesting of the given depth, alternating objects and arrays. Each opening bracket starts its own line, so that its
    /// position is the start of a line. The innermost value is 1.
    /// </summary>
    private static string MixedNesting(int depth)
    {
        var opens = new string[depth];
        for (int i = 0; i < depth; i++)
        {
            opens[i] = i % 2 == 0 ? "{\"k\":" : "[";
        }

        var closes = new StringBuilder(depth);
        for (int i = depth - 1; i >= 0; i--)
        {
            closes.Append(i % 2 == 0 ? '}' : ']');
        }

        return string.Join("\n", opens) + "1" + closes;
    }
}
