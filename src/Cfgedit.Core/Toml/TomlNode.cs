namespace Cfgedit.Core.Toml;

/// <summary>A span of the source text: from <see cref="Start"/> up to, not including, <see cref="End"/>.</summary>
/// <param name="Start">The offset of the first character.</param>
/// <param name="End">The offset just after the last character.</param>
public readonly record struct TomlSpan(int Start, int End)
{
    /// <summary>The number of UTF-16 units in the span.</summary>
    public int Length => End - Start;
}

/// <summary>
/// A node of the TOML concrete syntax tree. <see cref="Start"/> and <see cref="End"/> are offsets into the source text, and
/// <see cref="End"/> is exclusive. Nodes are immutable. Punctuation and whitespace that a node does not name are spans of its
/// own, so that every character of the source belongs to exactly one span.
/// </summary>
public abstract class TomlNode
{
    internal TomlNode(int start, int end)
    {
        Start = start;
        End = end;
    }

    /// <summary>The offset of the first character of the node.</summary>
    public int Start { get; }

    /// <summary>The offset just after the last character of the node.</summary>
    public int End { get; }
}

/// <summary>
/// A comment: the <c>#</c> that starts it, and the text after it, up to the end of the line. The span excludes the line break.
/// </summary>
public sealed class TomlComment : TomlNode
{
    internal TomlComment(int start, int end, string content)
        : base(start, end)
    {
        Content = content;
    }

    /// <summary>The text after the <c>#</c>, without the line break. It keeps any leading or trailing spaces.</summary>
    public string Content { get; }
}

/// <summary>
/// A run of spaces, tabs and line breaks inside an array or an inline table. Comments inside an array are separate
/// <see cref="TomlComment"/> entries, so a run is never split by one.
/// </summary>
public sealed class TomlWhitespace : TomlNode
{
    internal TomlWhitespace(int start, int end)
        : base(start, end)
    {
    }
}
