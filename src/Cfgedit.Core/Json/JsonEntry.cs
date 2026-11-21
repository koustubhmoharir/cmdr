namespace Cfgedit.Core.Json;

/// <summary>
/// An entry of an object or array: a member, an item, or a comment. Entries are kept in source order. Start and End are
/// offsets into the source text, and End is exclusive.
/// </summary>
public abstract class JsonEntry
{
    internal JsonEntry(int start, int end)
    {
        Start = start;
        End = end;
    }

    /// <summary>The offset of the first character of the entry.</summary>
    public int Start { get; }

    /// <summary>The offset just after the last character of the entry. A member or item does not include its comma.</summary>
    public int End { get; }
}

/// <summary>
/// A comment. Its span includes the markers (<c>//</c>, or <c>/*</c> and <c>*/</c>), and <see cref="Content"/> is the text
/// between them.
/// </summary>
public sealed class JsonComment : JsonEntry
{
    internal JsonComment(int start, int end, bool isBlock, string content)
        : base(start, end)
    {
        IsBlock = isBlock;
        Content = content;
    }

    /// <summary>True for a <c>/* */</c> comment, false for a <c>//</c> comment.</summary>
    public bool IsBlock { get; }

    /// <summary>The comment text without its markers. A line comment excludes its line break.</summary>
    public string Content { get; }
}

/// <summary>An object member: a key, a colon and a value. Its span runs from the key to the end of the value.</summary>
public sealed class JsonMember : JsonEntry
{
    internal JsonMember(int start, int end, JsonKey key, int colonStart, JsonValue value, int? commaStart)
        : base(start, end)
    {
        Key = key;
        ColonStart = colonStart;
        Value = value;
        CommaStart = commaStart;
    }

    /// <summary>The key.</summary>
    public JsonKey Key { get; }

    /// <summary>The offset of the colon.</summary>
    public int ColonStart { get; }

    /// <summary>The value.</summary>
    public JsonValue Value { get; }

    /// <summary>The offset of the comma that follows this member, or null when there is none.</summary>
    public int? CommaStart { get; }
}

/// <summary>An array item: a value. Its span is the span of the value.</summary>
public sealed class JsonItem : JsonEntry
{
    internal JsonItem(int start, int end, JsonValue value, int? commaStart)
        : base(start, end)
    {
        Value = value;
        CommaStart = commaStart;
    }

    /// <summary>The value.</summary>
    public JsonValue Value { get; }

    /// <summary>The offset of the comma that follows this item, or null when there is none.</summary>
    public int? CommaStart { get; }
}
