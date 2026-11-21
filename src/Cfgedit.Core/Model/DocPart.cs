namespace Cfgedit.Core.Model;

/// <summary>
/// One part of a node's row. <see cref="Start"/> and <see cref="End"/> are the source span of the token or tokens the part
/// edits; the span is empty when the part has no text of its own.
/// </summary>
public sealed class DocPart
{
    internal DocPart(PartKind kind, string value, bool editable, bool isRaw, int start, int end, int attributeIndex = -1)
    {
        Kind = kind;
        Value = value;
        Editable = editable;
        IsRaw = isRaw;
        Start = start;
        End = end;
        AttributeIndex = attributeIndex;
    }

    /// <summary>The kind of part.</summary>
    public PartKind Kind { get; }

    /// <summary>The logical value: decoded and unescaped. For a summary, the count.</summary>
    public string Value { get; }

    /// <summary>True when the user can edit the part.</summary>
    public bool Editable { get; }

    /// <summary>
    /// True for an XML value that holds a reference to an entity other than the five predefined ones. <see cref="Value"/> is
    /// then the raw source text, and the part is edited as source.
    /// </summary>
    public bool IsRaw { get; }

    /// <summary>The offset of the first character of the part's source text.</summary>
    public int Start { get; }

    /// <summary>The offset just after the part's source text.</summary>
    public int End { get; }

    /// <summary>The attribute's index in its element, for <c>AttributeName</c> and <c>AttributeValue</c>. -1 otherwise.</summary>
    public int AttributeIndex { get; }
}
