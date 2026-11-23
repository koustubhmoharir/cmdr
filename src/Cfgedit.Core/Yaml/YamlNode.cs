namespace Cfgedit.Core.Yaml;

/// <summary>
/// A node of the YAML concrete syntax tree. <see cref="Start"/> and <see cref="End"/> are offsets into the source text, and
/// <see cref="End"/> is exclusive. Nodes are immutable. Every character of the source belongs to exactly one leaf of the tree,
/// so printing the leaves in source order reproduces the text.
/// </summary>
public abstract class YamlNode
{
    internal YamlNode(int start, int end)
    {
        Start = start;
        End = end;
    }

    /// <summary>The offset of the first character of the node.</summary>
    public int Start { get; }

    /// <summary>The offset just after the last character of the node.</summary>
    public int End { get; }

    /// <summary>The children of the node in source order. They tile the span of the node. A leaf has none.</summary>
    public virtual IReadOnlyList<YamlNode> Parts => [];
}

/// <summary>The kinds of leaf that are not comments.</summary>
public enum YamlTokenKind
{
    /// <summary>Spaces and tabs.</summary>
    Whitespace,

    /// <summary>A line break: LF, CRLF or a lone CR.</summary>
    LineBreak,

    /// <summary>A punctuation mark of the syntax: <c>-</c>, <c>:</c>, <c>?</c>, <c>,</c>, <c>[</c>, <c>]</c>, <c>{</c> or <c>}</c>.</summary>
    Indicator,

    /// <summary>The <c>---</c> marker that starts an explicit document.</summary>
    DocumentStart,

    /// <summary>The <c>...</c> marker that ends a document.</summary>
    DocumentEnd,

    /// <summary>The name of a directive, with its <c>%</c>, such as <c>%YAML</c>.</summary>
    DirectiveName,

    /// <summary>The arguments of a directive, such as <c>1.2</c>.</summary>
    DirectiveArguments,

    /// <summary>An anchor, with its <c>&amp;</c>.</summary>
    Anchor,

    /// <summary>A tag, as written: <c>!</c>, <c>!!str</c>, <c>!h!x</c> or <c>!&lt;uri&gt;</c>.</summary>
    Tag,

    /// <summary>An alias, with its <c>*</c>.</summary>
    Alias,

    /// <summary>The raw text of a plain or quoted scalar, with its quotes. It may span lines.</summary>
    Scalar,

    /// <summary>The header of a block scalar: its <c>|</c> or <c>&gt;</c> and its indicators.</summary>
    BlockHeader,

    /// <summary>The raw text of a block scalar's lines, from the first line to the last line it uses, with their line breaks.</summary>
    BlockBody,
}

/// <summary>A leaf of the tree other than a comment: a run of spaces, a line break, a punctuation mark, a marker, a property or the raw text of a scalar.</summary>
public sealed class YamlToken : YamlNode
{
    internal YamlToken(int start, int end, YamlTokenKind kind, string text)
        : base(start, end)
    {
        Kind = kind;
        Text = text;
    }

    /// <summary>What the token is.</summary>
    public YamlTokenKind Kind { get; }

    /// <summary>The source text of the token, exactly as written.</summary>
    public string Text { get; }
}

/// <summary>
/// A comment: the <c>#</c> that starts it, and the text after it, up to the end of the line. The span excludes the line break.
/// </summary>
public sealed class YamlComment : YamlNode
{
    internal YamlComment(int start, int end, string content, bool isTrailing)
        : base(start, end)
    {
        Content = content;
        IsTrailing = isTrailing;
    }

    /// <summary>The text after the <c>#</c>, without the line break. It keeps any spaces around it.</summary>
    public string Content { get; }

    /// <summary>
    /// True when content comes before the comment on its line, so the comment belongs to that content. False for a comment
    /// that starts its line.
    /// </summary>
    public bool IsTrailing { get; }
}

/// <summary>A node whose children are kept in source order. Its span runs from its first child to its last child.</summary>
public abstract class YamlCompositeNode : YamlNode
{
    private readonly IReadOnlyList<YamlNode> _parts;

    internal YamlCompositeNode(IReadOnlyList<YamlNode> parts)
        : base(FirstStart(parts), LastEnd(parts))
    {
        _parts = parts;
    }

    /// <inheritdoc />
    public override IReadOnlyList<YamlNode> Parts => _parts;

    private static int FirstStart(IReadOnlyList<YamlNode> parts)
    {
        if (parts.Count == 0)
        {
            throw new ArgumentException("A composite node needs at least one part.", nameof(parts));
        }

        return parts[0].Start;
    }

    private static int LastEnd(IReadOnlyList<YamlNode> parts)
    {
        return parts.Count == 0 ? 0 : parts[^1].End;
    }
}
