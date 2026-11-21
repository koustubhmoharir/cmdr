using System.Text;
using Cfgedit.Core.Json;
using Cfgedit.Core.Text;
using static Cfgedit.Core.Tests.Json.JsonTestSupport;

namespace Cfgedit.Core.Tests.Json;

public sealed class JsonFixtureTests
{
    // ----- Tree shape -----

    [Fact]
    public void Appsettings_jsonc_tree_keys_values_comments_and_commas()
    {
        JsonDocument doc = Fixture("appsettings.jsonc", JsonDialect.Jsonc);

        Assert.Equal(JsonDialect.Jsonc, doc.Dialect);
        Assert.Equal([" Application settings. Synthetic example."], Contents(doc.Leading));
        Assert.Equal([" end of file"], Contents(doc.Trailing));

        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        AssertAt(doc, root.Start, 2, 1);
        AssertAt(doc, root.End, 19, 2);
        AssertAt(doc, root.TrailingCommaStart, 18, 17);
        Assert.Equal(7, root.Entries.Count);

        AssertComment(root.Entries[0], true, " Logging levels by category. ");
        JsonMember logging = AssertMember(root.Entries[1], "Logging", '"');
        AssertAt(doc, logging.CommaStart, 9, 4);
        Assert.Equal(
            Lines(
                "\"Logging\": {",
                "    \"LogLevel\": {",
                "      \"Default\": \"Information\", // the floor",
                "      \"Microsoft.AspNetCore\": \"Warning\",",
                "    },",
                "  }"),
            Slice(doc, logging));

        JsonObject loggingValue = Assert.IsType<JsonObject>(logging.Value);
        AssertAt(doc, loggingValue.TrailingCommaStart, 8, 6);
        Assert.Single(loggingValue.Entries);

        JsonMember logLevel = AssertMember(loggingValue.Entries[0], "LogLevel", '"');
        AssertAt(doc, logLevel.CommaStart, 8, 6);
        JsonObject levels = Assert.IsType<JsonObject>(logLevel.Value);
        AssertAt(doc, levels.TrailingCommaStart, 7, 40);
        Assert.Equal(3, levels.Entries.Count);

        JsonMember def = AssertMember(levels.Entries[0], "Default", '"');
        AssertAt(doc, def.CommaStart, 6, 31);
        AssertString(def.Value, "\"Information\"", "Information", '"');
        AssertComment(levels.Entries[1], false, " the floor");
        AssertAt(doc, levels.Entries[1].Start, 6, 33);
        AssertAt(doc, levels.Entries[1].End, 6, 45);
        JsonMember microsoft = AssertMember(levels.Entries[2], "Microsoft.AspNetCore", '"');
        AssertAt(doc, microsoft.CommaStart, 7, 40);
        AssertString(microsoft.Value, "\"Warning\"", "Warning", '"');

        JsonMember allowed = AssertMember(root.Entries[2], "AllowedHosts", '"');
        AssertAt(doc, allowed.Start, 10, 3);
        AssertAt(doc, allowed.Key.End, 10, 17);
        AssertAt(doc, allowed.End, 10, 22);
        AssertAt(doc, allowed.CommaStart, 10, 22);
        AssertString(allowed.Value, "\"*\"", "*", '"');

        JsonMember port = AssertMember(root.Entries[3], "Port", '"');
        AssertAt(doc, port.CommaStart, 11, 15);
        AssertScalar(port.Value, JsonScalarKind.Number, "5000");

        JsonMember features = AssertMember(root.Entries[4], "Features", '"');
        AssertAt(doc, features.CommaStart, 16, 4);
        JsonArray list = Assert.IsType<JsonArray>(features.Value);
        Assert.Equal(
            Lines("[", "    \"search\",", "    /* \"beta\", */", "    \"export\",", "  ]"),
            Slice(doc, list));
        AssertAt(doc, list.TrailingCommaStart, 15, 13);
        Assert.Equal(3, list.Entries.Count);
        JsonItem search = AssertItem(list.Entries[0]);
        AssertAt(doc, search.CommaStart, 13, 13);
        AssertString(search.Value, "\"search\"", "search", '"');
        AssertComment(list.Entries[1], true, " \"beta\", ");
        JsonItem export = AssertItem(list.Entries[2]);
        AssertAt(doc, export.CommaStart, 15, 13);
        AssertString(export.Value, "\"export\"", "export", '"');

        JsonMember ratio = AssertMember(root.Entries[5], "Ratio", '"');
        AssertAt(doc, ratio.CommaStart, 17, 16);
        AssertScalar(ratio.Value, JsonScalarKind.Number, "0.75");

        JsonMember debug = AssertMember(root.Entries[6], "Debug", '"');
        AssertAt(doc, debug.CommaStart, 18, 17);
        AssertScalar(debug.Value, JsonScalarKind.False, "false");
    }

    [Fact]
    public void Tsconfig_jsonc_tree_keys_values_comments_and_commas()
    {
        JsonDocument doc = Fixture("tsconfig.jsonc", JsonDialect.Jsonc);

        Assert.Empty(doc.Leading);
        Assert.Empty(doc.Trailing);
        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        Assert.Null(root.TrailingCommaStart);
        Assert.Equal(4, root.Entries.Count);

        JsonMember compilerOptions = AssertMember(root.Entries[0], "compilerOptions", '"');
        AssertAt(doc, compilerOptions.CommaStart, 12, 4);
        JsonObject options = Assert.IsType<JsonObject>(compilerOptions.Value);
        AssertAt(doc, options.TrailingCommaStart, 11, 6);
        Assert.Equal(5, options.Entries.Count);

        JsonMember target = AssertMember(options.Entries[0], "target", '"');
        AssertAt(doc, target.CommaStart, 3, 23);
        AssertString(target.Value, "\"es2022\"", "es2022", '"');
        AssertComment(options.Entries[1], false, " emitted language level");

        JsonMember strict = AssertMember(options.Entries[2], "strict", '"');
        AssertAt(doc, strict.CommaStart, 4, 19);
        AssertScalar(strict.Value, JsonScalarKind.True, "true");

        JsonMember paths = AssertMember(options.Entries[3], "paths", '"');
        AssertAt(doc, paths.CommaStart, 7, 6);
        JsonObject pathValue = Assert.IsType<JsonObject>(paths.Value);
        AssertAt(doc, pathValue.TrailingCommaStart, 6, 26);
        Assert.Single(pathValue.Entries);
        JsonMember app = AssertMember(pathValue.Entries[0], "@app/*", '"');
        AssertAt(doc, app.CommaStart, 6, 26);
        JsonArray appTargets = Assert.IsType<JsonArray>(app.Value);
        Assert.Null(appTargets.TrailingCommaStart);
        AssertString(AssertItem(appTargets.Entries[0]).Value, "\"src/*\"", "src/*", '"');
        Assert.Null(AssertItem(appTargets.Entries[0]).CommaStart);

        JsonMember lib = AssertMember(options.Entries[4], "lib", '"');
        AssertAt(doc, lib.CommaStart, 11, 6);
        JsonArray libItems = Assert.IsType<JsonArray>(lib.Value);
        AssertAt(doc, libItems.TrailingCommaStart, 10, 12);
        Assert.Equal(2, libItems.Entries.Count);
        AssertAt(doc, AssertItem(libItems.Entries[0]).CommaStart, 9, 15);
        AssertString(AssertItem(libItems.Entries[0]).Value, "\"es2022\"", "es2022", '"');
        AssertAt(doc, AssertItem(libItems.Entries[1]).CommaStart, 10, 12);
        AssertString(AssertItem(libItems.Entries[1]).Value, "\"dom\"", "dom", '"');

        AssertComment(root.Entries[1], false, " files to include");

        JsonMember include = AssertMember(root.Entries[2], "include", '"');
        AssertAt(doc, include.CommaStart, 14, 29);
        JsonArray includeItems = Assert.IsType<JsonArray>(include.Value);
        Assert.Null(includeItems.TrailingCommaStart);
        AssertString(AssertItem(includeItems.Entries[0]).Value, "\"src/**/*.ts\"", "src/**/*.ts", '"');

        JsonMember exclude = AssertMember(root.Entries[3], "exclude", '"');
        Assert.Null(exclude.CommaStart);
        Assert.Equal("[\"node_modules\"]", Slice(doc, exclude.Value));
    }

    [Fact]
    public void Example_json5_tree_covers_every_json5_form()
    {
        JsonDocument doc = Fixture("example.json5", JsonDialect.Json5);

        Assert.Equal([" This is a JSON5 example document."], Contents(doc.Leading));
        Assert.Empty(doc.Trailing);
        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        AssertAt(doc, root.TrailingCommaStart, 19, 15);
        Assert.Equal(19, root.Entries.Count);
        Assert.All(root.Entries, e => Assert.IsType<JsonMember>(e));

        JsonMember unquoted = AssertMember(root.Entries[0], "unquoted", null);
        AssertAt(doc, unquoted.CommaStart, 3, 43);
        AssertString(unquoted.Value, "'and you can quote me on that'", "and you can quote me on that", '\'');

        JsonMember singleQuotes = AssertMember(root.Entries[1], "singleQuotes", null);
        AssertString(singleQuotes.Value, "'I can use \"double quotes\" here'", "I can use \"double quotes\" here", '\'');

        JsonMember lineBreaks = AssertMember(root.Entries[2], "lineBreaks", null);
        AssertAt(doc, lineBreaks.CommaStart, 6, 11);
        AssertString(
            lineBreaks.Value,
            Lines("\"Look, Mom! \\", "No \\\\n's!\""),
            "Look, Mom! No \\n's!",
            '"');

        JsonMember hexadecimal = AssertMember(root.Entries[3], "hexadecimal", null);
        AssertAt(doc, hexadecimal.CommaStart, 7, 23);
        AssertScalar(hexadecimal.Value, JsonScalarKind.Number, "0xdecaf");

        JsonMember leading = AssertMember(root.Entries[4], "leadingDecimalPoint", null);
        AssertAt(doc, leading.CommaStart, 8, 32);
        AssertScalar(leading.Value, JsonScalarKind.Number, ".8675309");

        JsonMember trailingDot = AssertMember(root.Entries[5], "andTrailing", null);
        AssertAt(doc, trailingDot.CommaStart, 8, 55);
        AssertScalar(trailingDot.Value, JsonScalarKind.Number, "8675309.");

        JsonMember positive = AssertMember(root.Entries[6], "positiveSign", null);
        AssertAt(doc, positive.CommaStart, 9, 19);
        AssertScalar(positive.Value, JsonScalarKind.Number, "+1");

        JsonMember trailingComma = AssertMember(root.Entries[7], "trailingComma", null);
        AssertAt(doc, trailingComma.CommaStart, 10, 30);
        AssertString(trailingComma.Value, "'in objects'", "in objects", '\'');

        JsonMember andIn = AssertMember(root.Entries[8], "andIn", null);
        AssertAt(doc, andIn.CommaStart, 10, 50);
        JsonArray arrays = Assert.IsType<JsonArray>(andIn.Value);
        AssertAt(doc, arrays.TrailingCommaStart, 10, 48);
        Assert.Single(arrays.Entries);
        AssertString(AssertItem(arrays.Entries[0]).Value, "'arrays'", "arrays", '\'');

        JsonMember backwards = AssertMember(root.Entries[9], "backwardsCompatible", '"');
        AssertAt(doc, backwards.CommaStart, 11, 37);
        AssertString(backwards.Value, "\"with JSON\"", "with JSON", '"');

        JsonMember infinite = AssertMember(root.Entries[10], "infinite", null);
        AssertAt(doc, infinite.CommaStart, 12, 21);
        AssertScalar(infinite.Value, JsonScalarKind.Number, "Infinity");

        JsonMember notANumber = AssertMember(root.Entries[11], "notANumber", null);
        AssertAt(doc, notANumber.CommaStart, 13, 18);
        AssertScalar(notANumber.Value, JsonScalarKind.Number, "NaN");

        JsonMember negative = AssertMember(root.Entries[12], "negative", null);
        AssertAt(doc, negative.CommaStart, 14, 22);
        AssertScalar(negative.Value, JsonScalarKind.Number, "-Infinity");

        AssertMember(root.Entries[13], "true", null);
        AssertScalar(((JsonMember)root.Entries[13]).Value, JsonScalarKind.True, "true");
        AssertMember(root.Entries[14], "null", null);
        AssertScalar(((JsonMember)root.Entries[14]).Value, JsonScalarKind.Null, "null");

        JsonMember escaped = AssertMember(root.Entries[15], "abc", null);
        AssertAt(doc, escaped.Key.Start, 17, 3);
        AssertAt(doc, escaped.Key.End, 17, 11);
        AssertAt(doc, escaped.CommaStart, 17, 33);
        AssertString(escaped.Value, "'escaped identifier'", "escaped identifier", '\'');

        AssertMember(root.Entries[16], "quoted-key", '\'');
        AssertScalar(((JsonMember)root.Entries[16]).Value, JsonScalarKind.Number, "1");
        AssertMember(root.Entries[17], "$", '"');
        AssertMember(root.Entries[18], "_", null);
        AssertAt(doc, ((JsonMember)root.Entries[18]).Key.Start, 19, 11);
    }

    [Fact]
    public void Strict_json_tree_keeps_escapes_nesting_and_numbers()
    {
        JsonDocument doc = Fixture("strict.json", JsonDialect.Json);

        AssertStrictTree(doc);
    }

    [Fact]
    public void Strict_json_has_no_comments_and_no_trailing_commas()
    {
        JsonDocument doc = Fixture("strict.json", JsonDialect.Json);

        Assert.Empty(doc.Leading);
        Assert.Empty(doc.Trailing);
        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        Assert.Null(root.TrailingCommaStart);
        // Every member but the last has a comma.
        Assert.All(root.Entries.Take(5), e => Assert.NotNull(((JsonMember)e).CommaStart));
        Assert.Null(((JsonMember)root.Entries[5]).CommaStart);
    }

    /// <summary>The strict fixture's tree. The generated BOM and UTF-16 inputs are checked with it too.</summary>
    private static void AssertStrictTree(JsonDocument doc)
    {
        Assert.Empty(doc.Leading);
        Assert.Empty(doc.Trailing);
        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        Assert.Equal(6, root.Entries.Count);

        AssertString(AssertMember(root.Entries[0], "name", '"').Value, "\"cfgedit\"", "cfgedit", '"');
        AssertString(AssertMember(root.Entries[1], "version", '"').Value, "\"1.0.0-rc.1\"", "1.0.0-rc.1", '"');

        JsonArray tags = Assert.IsType<JsonArray>(AssertMember(root.Entries[2], "tags", '"').Value);
        Assert.Equal(3, tags.Entries.Count);
        AssertString(AssertItem(tags.Entries[0]).Value, "\"config\"", "config", '"');
        AssertString(AssertItem(tags.Entries[2]).Value, "\"tui\"", "tui", '"');
        Assert.Null(tags.TrailingCommaStart);

        // The escapes decode: \" \/ \t é and a surrogate pair written as two escapes.
        AssertString(
            AssertMember(root.Entries[3], "escapes", '"').Value,
            "\"quote \\\" slash \\/ tab \\t unicode \\u00e9 pair \\ud83d\\ude00\"",
            "quote \" slash / tab \t unicode é pair \U0001F600",
            '"');

        JsonObject nested = Assert.IsType<JsonObject>(AssertMember(root.Entries[4], "nested", '"').Value);
        Assert.Equal(3, nested.Entries.Count);
        JsonObject empty = Assert.IsType<JsonObject>(AssertMember(nested.Entries[0], "empty", '"').Value);
        Assert.Empty(empty.Entries);
        JsonArray list = Assert.IsType<JsonArray>(AssertMember(nested.Entries[1], "list", '"').Value);
        Assert.Empty(list.Entries);
        JsonArray flags = Assert.IsType<JsonArray>(AssertMember(nested.Entries[2], "flags", '"').Value);
        Assert.Equal(3, flags.Entries.Count);
        AssertScalar(AssertItem(flags.Entries[0]).Value, JsonScalarKind.True, "true");
        AssertScalar(AssertItem(flags.Entries[1]).Value, JsonScalarKind.False, "false");
        AssertScalar(AssertItem(flags.Entries[2]).Value, JsonScalarKind.Null, "null");

        JsonArray numbers = Assert.IsType<JsonArray>(AssertMember(root.Entries[5], "numbers", '"').Value);
        string[] raws = ["0", "-0", "1.0", "1e3", "-2.5E-3", "123456789012345678901234567890"];
        Assert.Equal(raws.Length, numbers.Entries.Count);
        for (int i = 0; i < raws.Length; i++)
        {
            AssertScalar(AssertItem(numbers.Entries[i]).Value, JsonScalarKind.Number, raws[i]);
        }
    }

    [Fact]
    public void Lines_fixture_has_exact_spans_and_commas()
    {
        JsonDocument doc = Fixture("lines.json", JsonDialect.Json);

        Assert.False(doc.Source.EndsWithNewline);
        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        AssertAt(doc, root.Start, 1, 1);
        AssertAt(doc, root.End, 7, 2);
        Assert.Equal(doc.Source.Text, Slice(doc, root));
        Assert.Equal(2, root.Entries.Count);

        JsonMember a = AssertMember(root.Entries[0], "a", '"');
        AssertAt(doc, a.Start, 2, 3);
        AssertAt(doc, a.CommaStart, 2, 9);
        AssertScalar(a.Value, JsonScalarKind.Number, "1");

        JsonMember b = AssertMember(root.Entries[1], "b", '"');
        AssertAt(doc, b.Start, 3, 3);
        Assert.Null(b.CommaStart);
        JsonArray items = Assert.IsType<JsonArray>(b.Value);
        AssertAt(doc, items.Start, 3, 8);
        AssertAt(doc, items.End, 6, 4);
        Assert.Equal(2, items.Entries.Count);
        AssertAt(doc, AssertItem(items.Entries[0]).CommaStart, 4, 8);
        Assert.Null(AssertItem(items.Entries[1]).CommaStart);
        Assert.Null(items.TrailingCommaStart);
    }

    // ----- Generated inputs: CRLF, BOM and UTF-16 -----

    [Fact]
    public void Lines_fixture_as_crlf_without_final_newline_parses_with_crlf_positions()
    {
        string lf = File.ReadAllText(FixturePath("lines.json"));
        string crlf = lf.Replace("\n", "\r\n");
        byte[] bytes = Encoding.UTF8.GetBytes(crlf);

        JsonDocument doc = JsonDocument.Parse(SourceText.Decode(bytes), JsonDialect.Json);

        Assert.Equal("\r\n", doc.Source.Newline);
        Assert.False(doc.Source.EndsWithNewline);
        JsonObject root = Assert.IsType<JsonObject>(doc.Root);
        Assert.Equal(2, root.Entries.Count);
        JsonMember a = AssertMember(root.Entries[0], "a", '"');
        AssertAt(doc, a.CommaStart, 2, 9);
        JsonMember b = AssertMember(root.Entries[1], "b", '"');
        AssertAt(doc, b.Start, 3, 3);
        AssertAt(doc, root.End, 7, 2);
        Assert.Equal(crlf, Slice(doc, root));
    }

    [Fact]
    public void Strict_fixture_with_a_utf8_bom_parses_to_the_same_tree()
    {
        byte[] body = Encoding.UTF8.GetBytes(File.ReadAllText(FixturePath("strict.json")));
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. body];

        SourceText source = SourceText.Decode(bytes);
        Assert.True(source.HasBom);
        JsonDocument doc = JsonDocument.Parse(source, JsonDialect.Json);

        AssertStrictTree(doc);
    }

    [Fact]
    public void Strict_fixture_in_utf16_le_with_a_bom_parses_to_the_same_tree()
    {
        byte[] bytes = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(File.ReadAllText(FixturePath("strict.json")))];

        SourceText source = SourceText.Decode(bytes);
        Assert.True(source.HasBom);
        Assert.Equal(Encoding.Unicode.CodePage, source.Encoding.CodePage);
        JsonDocument doc = JsonDocument.Parse(source, JsonDialect.Json);

        AssertStrictTree(doc);
    }

    [Fact]
    public void Strict_fixture_in_utf16_be_with_a_bom_parses_to_the_same_tree()
    {
        byte[] bytes = [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(File.ReadAllText(FixturePath("strict.json")))];

        SourceText source = SourceText.Decode(bytes);
        Assert.True(source.HasBom);
        Assert.Equal(Encoding.BigEndianUnicode.CodePage, source.Encoding.CodePage);
        JsonDocument doc = JsonDocument.Parse(source, JsonDialect.Json);

        AssertStrictTree(doc);
    }

    // ----- Byte round trip -----

    public static IEnumerable<object[]> RoundTripInputs()
    {
        byte[] strict = File.ReadAllBytes(FixturePath("strict.json"));
        byte[] lines = File.ReadAllBytes(FixturePath("lines.json"));
        yield return [File.ReadAllBytes(FixturePath("appsettings.jsonc")), JsonDialect.Jsonc];
        yield return [File.ReadAllBytes(FixturePath("tsconfig.jsonc")), JsonDialect.Jsonc];
        yield return [File.ReadAllBytes(FixturePath("example.json5")), JsonDialect.Json5];
        yield return [strict, JsonDialect.Json];
        yield return [lines, JsonDialect.Json];
        yield return [Encoding.UTF8.GetBytes(File.ReadAllText(FixturePath("lines.json")).Replace("\n", "\r\n")), JsonDialect.Json];
        yield return [Encoding.UTF8.GetBytes(File.ReadAllText(FixturePath("lines.json")).Replace("\n", "\r\n") + "\r\n"), JsonDialect.Json];
        yield return [(byte[])[0xEF, 0xBB, 0xBF, .. strict], JsonDialect.Json];
        yield return [(byte[])[0xFF, 0xFE, .. Encoding.Unicode.GetBytes(File.ReadAllText(FixturePath("strict.json")))], JsonDialect.Json];
        yield return [(byte[])[0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(File.ReadAllText(FixturePath("strict.json")))], JsonDialect.Json];
        yield return [(byte[])[0xFF, 0xFE, .. Encoding.Unicode.GetBytes(File.ReadAllText(FixturePath("appsettings.jsonc")))], JsonDialect.Jsonc];
    }

    [Theory]
    [MemberData(nameof(RoundTripInputs))]
    public void Every_fixture_survives_decode_then_encode_and_parses(byte[] bytes, JsonDialect dialect)
    {
        SourceText source = SourceText.Decode(bytes);

        Assert.Equal(bytes, source.Encode());
        Assert.NotNull(JsonDocument.Parse(source, dialect));
    }

    [Fact]
    public void Tokens_of_every_fixture_tile_the_text()
    {
        foreach (object[] input in RoundTripInputs())
        {
            byte[] bytes = (byte[])input[0];
            JsonDialect dialect = (JsonDialect)input[1];
            SourceText source = SourceText.Decode(bytes);

            int position = 0;
            foreach (JsonToken token in JsonLexer.Tokenize(source, dialect))
            {
                Assert.Equal(position, token.Start);
                position = token.End;
            }

            Assert.Equal(source.Text.Length, position);
        }
    }
}
