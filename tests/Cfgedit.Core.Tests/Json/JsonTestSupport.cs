using System.Text;
using Cfgedit.Core.Json;
using Shared.Core;

namespace Cfgedit.Core.Tests.Json;

/// <summary>Shared helpers for the JSON tree tests. Expectations passed in are literal values, never computed by the parser.</summary>
internal static class JsonTestSupport
{
    public static SourceText Utf8(string text)
    {
        return SourceText.Create(text, Encoding.UTF8, false);
    }

    public static JsonDocument Parse(string text, JsonDialect dialect)
    {
        return JsonDocument.Parse(Utf8(text), dialect);
    }

    /// <summary>Joins lines with LF, so that a multi-line expectation reads as it is laid out.</summary>
    public static string Lines(params string[] lines)
    {
        return string.Join("\n", lines);
    }

    public static string FixturePath(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", "json", name);
    }

    /// <summary>Loads a fixture from the output folder and parses it through the byte path, as the editor does.</summary>
    public static JsonDocument Fixture(string name, JsonDialect dialect)
    {
        return JsonDocument.Parse(SourceText.Decode(File.ReadAllBytes(FixturePath(name)), name), dialect);
    }

    public static string Slice(JsonDocument doc, int start, int end)
    {
        return doc.Source.Text[start..end];
    }

    public static string Slice(JsonDocument doc, JsonEntry entry)
    {
        return Slice(doc, entry.Start, entry.End);
    }

    public static string Slice(JsonDocument doc, JsonValue value)
    {
        return Slice(doc, value.Start, value.End);
    }

    /// <summary>Asserts that an offset is at the given 1-based line and column.</summary>
    public static void AssertAt(JsonDocument doc, int? offset, int line, int column)
    {
        Assert.NotNull(offset);
        Assert.Equal(new LinePosition(line, column), doc.Source.GetPosition(offset.Value));
    }

    public static void AssertError(string text, JsonDialect dialect, string message, int line, int column)
    {
        CfgeditException ex = Assert.Throws<CfgeditException>(() => Parse(text, dialect));

        Assert.Equal(message, ex.Message);
        Assert.Equal(line, ex.Line);
        Assert.Equal(column, ex.Column);
    }

    public static IReadOnlyList<string> Contents(IEnumerable<JsonComment> comments)
    {
        return comments.Select(c => c.Content).ToArray();
    }

    public static void AssertComment(JsonEntry entry, bool isBlock, string content)
    {
        JsonComment comment = Assert.IsType<JsonComment>(entry);
        Assert.Equal(isBlock, comment.IsBlock);
        Assert.Equal(content, comment.Content);
    }

    public static JsonMember AssertMember(JsonEntry entry, string name, char? quote)
    {
        JsonMember member = Assert.IsType<JsonMember>(entry);
        Assert.Equal(name, member.Key.Name);
        Assert.Equal(quote, member.Key.Quote);
        return member;
    }

    public static JsonItem AssertItem(JsonEntry entry)
    {
        return Assert.IsType<JsonItem>(entry);
    }

    public static void AssertString(JsonValue value, string raw, string decoded, char quote)
    {
        JsonScalar scalar = Assert.IsType<JsonScalar>(value);
        Assert.Equal(JsonScalarKind.String, scalar.Kind);
        Assert.Equal(raw, scalar.Raw);
        Assert.Equal(decoded, scalar.StringValue);
        Assert.Equal(quote, scalar.Quote);
    }

    public static void AssertScalar(JsonValue value, JsonScalarKind kind, string raw)
    {
        JsonScalar scalar = Assert.IsType<JsonScalar>(value);
        Assert.Equal(kind, scalar.Kind);
        Assert.Equal(raw, scalar.Raw);
        Assert.Null(scalar.StringValue);
        Assert.Null(scalar.Quote);
    }
}
