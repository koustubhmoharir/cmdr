namespace Cfgedit.Core.Json;

/// <summary>
/// A value in a JSON-family document. <see cref="Start"/> and <see cref="End"/> are offsets into the source text, and
/// <see cref="End"/> is exclusive. A container spans from its opening bracket through its closing one.
/// </summary>
public abstract class JsonValue
{
    internal JsonValue(int start, int end)
    {
        Start = start;
        End = end;
    }

    /// <summary>The offset of the first character of the value.</summary>
    public int Start { get; }

    /// <summary>The offset just after the last character of the value.</summary>
    public int End { get; }
}

/// <summary>An object. Its entries are members and comments, in source order.</summary>
public sealed class JsonObject : JsonValue
{
    internal JsonObject(int start, int end, IReadOnlyList<JsonEntry> entries, int? trailingCommaStart)
        : base(start, end)
    {
        Entries = entries;
        TrailingCommaStart = trailingCommaStart;
    }

    /// <summary>The members and comments, in source order.</summary>
    public IReadOnlyList<JsonEntry> Entries { get; }

    /// <summary>The comma after the last member, when it is trailing (Jsonc and Json5). Null otherwise.</summary>
    public int? TrailingCommaStart { get; }
}

/// <summary>An array. Its entries are items and comments, in source order.</summary>
public sealed class JsonArray : JsonValue
{
    internal JsonArray(int start, int end, IReadOnlyList<JsonEntry> entries, int? trailingCommaStart)
        : base(start, end)
    {
        Entries = entries;
        TrailingCommaStart = trailingCommaStart;
    }

    /// <summary>The items and comments, in source order.</summary>
    public IReadOnlyList<JsonEntry> Entries { get; }

    /// <summary>The comma after the last item, when it is trailing (Jsonc and Json5). Null otherwise.</summary>
    public int? TrailingCommaStart { get; }
}

/// <summary>The kinds of scalar value.</summary>
public enum JsonScalarKind
{
    /// <summary>A string.</summary>
    String,

    /// <summary>A number, in any spelling the dialect allows.</summary>
    Number,

    /// <summary><c>true</c>.</summary>
    True,

    /// <summary><c>false</c>.</summary>
    False,

    /// <summary><c>null</c>.</summary>
    Null,
}

/// <summary>A scalar value: a string, a number, or one of the literals <c>true</c>, <c>false</c> and <c>null</c>.</summary>
public sealed class JsonScalar : JsonValue
{
    internal JsonScalar(int start, int end, JsonScalarKind kind, string raw, string? stringValue, char? quote)
        : base(start, end)
    {
        Kind = kind;
        Raw = raw;
        StringValue = stringValue;
        Quote = quote;
    }

    /// <summary>The kind of scalar.</summary>
    public JsonScalarKind Kind { get; }

    /// <summary>The exact source text of the scalar, quotes included for a string.</summary>
    public string Raw { get; }

    /// <summary>The decoded value of a string. Null for every other kind.</summary>
    public string? StringValue { get; }

    /// <summary>The quote a string was written with: <c>"</c> or <c>'</c>. Null for every other kind.</summary>
    public char? Quote { get; }
}
