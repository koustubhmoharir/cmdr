namespace Cfgedit.Core.Model;

/// <summary>
/// A node of the document tree that the editor shows: one row, with its parts, its children and, for XML elements that show
/// their children, an end row. Nodes are built once per parse and are immutable from the outside.
/// </summary>
public sealed class DocNode
{
    private readonly List<DocNode> _children = [];

    internal DocNode(
        NodeKind kind,
        NodePath path,
        DocNode? parent,
        IReadOnlyList<DocPart> parts,
        IReadOnlyList<DocPart> endParts,
        int start,
        int end,
        JsonValueKind? jsonKind,
        TomlValueKind? tomlKind = null)
    {
        Kind = kind;
        Path = path;
        Parent = parent;
        Parts = parts;
        EndParts = endParts;
        Start = start;
        End = end;
        JsonKind = jsonKind;
        TomlKind = tomlKind;
    }

    /// <summary>The kind of node.</summary>
    public NodeKind Kind { get; }

    /// <summary>The node's path from the document node.</summary>
    public NodePath Path { get; }

    /// <summary>The parent node, or null for the document node.</summary>
    public DocNode? Parent { get; }

    /// <summary>The child nodes, in document order. Whitespace-only XML text is not a child.</summary>
    public IReadOnlyList<DocNode> Children => _children;

    /// <summary>The parts on the node's row, in display order.</summary>
    public IReadOnlyList<DocPart> Parts { get; }

    /// <summary>The parts of an XML element's end row. Empty when there is no end row.</summary>
    public IReadOnlyList<DocPart> EndParts { get; }

    /// <summary>The offset of the first character of the node in the source.</summary>
    public int Start { get; }

    /// <summary>The offset just after the last character of the node in the source.</summary>
    public int End { get; }

    /// <summary>The JSON kind of the node's value. Null for XML nodes, comments and the document node.</summary>
    public JsonValueKind? JsonKind { get; }

    /// <summary>The TOML kind of a TOML scalar. Null for every other node.</summary>
    public TomlValueKind? TomlKind { get; }

    internal void AddChild(DocNode child)
    {
        _children.Add(child);
    }
}
