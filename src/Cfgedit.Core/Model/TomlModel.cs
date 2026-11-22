using Cfgedit.Core.Toml;

namespace Cfgedit.Core.Model;

/// <summary>The shape of a TOML table or array node: which text can add to it, as its pieces define it.</summary>
internal enum TomlContainer
{
    /// <summary>The document's root table. Its key/values come before its first header.</summary>
    Root,

    /// <summary>A table defined by its own header of its own depth, or an item of an array of tables.</summary>
    Header,

    /// <summary>A table that only deeper headers make (<c>[a.b]</c> makes <c>a</c>). It has no section of its own.</summary>
    Implicit,

    /// <summary>A table that only dotted keys make (<c>a.b = 1</c> makes <c>a</c>).</summary>
    Dotted,

    /// <summary>An inline table.</summary>
    Inline,

    /// <summary>An array of tables (<c>[[x]]</c>).</summary>
    ArrayOfTables,

    /// <summary>A static array (<c>[...]</c>).</summary>
    Array,
}

/// <summary>
/// A parsed TOML document, held as the format's syntax: the syntax tree, the merged table model, the document tree that the editor
/// shows, and the pieces of text that define each node of the tree. A model is immutable.
/// </summary>
internal sealed class TomlModel
{
    private readonly MergedTable _data;
    private readonly IReadOnlyDictionary<DocNode, IReadOnlyList<TomlNode>> _pieces;

    internal TomlModel(
        TomlDocument syntax,
        DocNode tree,
        MergedTable data,
        IReadOnlyDictionary<DocNode, IReadOnlyList<TomlNode>> pieces)
    {
        Syntax = syntax;
        Tree = tree;
        _data = data;
        _pieces = pieces;
    }

    /// <summary>The syntax tree of the text.</summary>
    internal TomlDocument Syntax { get; }

    /// <summary>The document node of the document tree.</summary>
    internal DocNode Tree { get; }

    /// <summary>
    /// The text that defines a node. A table's pieces are its headers, the key/value lines that name it or pass through it, and its
    /// inline table value; a scalar's or an array's piece is its key/value line or its array item; an array of tables' pieces are its
    /// headers; a comment's piece is the comment. The document node has none.
    /// </summary>
    internal IReadOnlyList<TomlNode> PiecesOf(DocNode node)
    {
        return _pieces.TryGetValue(node, out IReadOnlyList<TomlNode>? pieces) ? pieces : [];
    }

    /// <summary>
    /// The shape of a table or array node, from the pieces that define it. A table with a header of its own depth is a header table;
    /// one with only deeper headers is implicit; one with key/value pieces only is dotted. An array is an array of tables when its
    /// pieces are headers.
    /// </summary>
    internal TomlContainer ShapeOf(DocNode node)
    {
        IReadOnlyList<TomlNode> pieces = PiecesOf(node);
        if (node.Kind == NodeKind.TomlArray)
        {
            return pieces.Count > 0 && pieces[0] is TomlHeaderItem ? TomlContainer.ArrayOfTables : TomlContainer.Array;
        }

        if (node.Kind != NodeKind.TomlTable)
        {
            throw new InvalidOperationException("The node is not a TOML table or array.");
        }

        if (node.Parent?.Kind == NodeKind.Document)
        {
            return TomlContainer.Root;
        }

        if (pieces.Count == 0)
        {
            throw new InvalidOperationException("A TOML table has no pieces.");
        }

        if (pieces[0] is TomlInlineTable)
        {
            return TomlContainer.Inline;
        }

        if (OwnHeaderOf(node) is not null)
        {
            return TomlContainer.Header;
        }

        return pieces[0] is TomlHeaderItem ? TomlContainer.Implicit : TomlContainer.Dotted;
    }

    /// <summary>The header that defines a header table (one with a header of its own depth), or an item of an array of tables; null otherwise.</summary>
    internal TomlHeaderItem? OwnHeaderOf(DocNode node)
    {
        int depth = node.Path.Steps.Count(step => step is MemberStep);
        return PiecesOf(node).OfType<TomlHeaderItem>().FirstOrDefault(header => header.Key.Parts.Count == depth);
    }

    /// <summary>
    /// The syntax of a node's value: a key/value's value, an inline member's value, an array item's value, or the inline table itself.
    /// Null for a node with no pieces, such as a comment or the root.
    /// </summary>
    internal TomlValue? ValueSyntaxOf(DocNode node)
    {
        IReadOnlyList<TomlNode> pieces = PiecesOf(node);
        if (pieces.Count == 0)
        {
            return null;
        }

        return pieces[0] switch
        {
            TomlKeyValueItem keyValue => keyValue.Value,
            TomlInlineMember member => member.Value,
            TomlArrayItem item => item.Value,
            TomlInlineTable inline => inline,
            _ => null,
        };
    }

    /// <summary>The item of a static array's syntax at an item index (the index among the array's items, comments not counted).</summary>
    internal TomlArrayItem ArrayItemSyntaxOf(DocNode array, int itemIndex)
    {
        TomlArray syntax = ValueSyntaxOf(array) as TomlArray ?? throw new InvalidOperationException("The node is not a TOML array.");
        return syntax.Entries.OfType<TomlArrayItem>().ElementAt(itemIndex);
    }

    /// <summary>
    /// The data of the document: tables as objects, arrays and arrays of tables as arrays, and scalars with their kind and canonical
    /// value. The members of an object are in order of first appearance. Comments are not in it.
    /// </summary>
    internal LogicalNode DataLogical()
    {
        return Table(_data, null);
    }

    /// <summary>
    /// The logical tree of the document: its data, then an array named <c>#comments</c> with one comment per comment in the file, in
    /// file order (comment lines, trailing comments and comments inside arrays).
    /// </summary>
    internal LogicalNode ToLogical()
    {
        var comments = new List<LogicalNode>();
        foreach (TomlComment comment in CommentsOf(Syntax))
        {
            comments.Add(new LogicalNode(LogicalKind.Comment, null, null, comment.Content, [], []));
        }

        var commentArray = new LogicalNode(LogicalKind.Array, "#comments", null, null, [], comments);
        return new LogicalNode(LogicalKind.Document, null, null, null, [], [DataLogical(), commentArray]);
    }

    private static LogicalNode Table(MergedTable table, string? name)
    {
        var children = new List<LogicalNode>();
        foreach (MergedMember member in table.Members)
        {
            children.Add(Value(member.Value, member.Name));
        }

        return new LogicalNode(LogicalKind.Object, name, null, null, [], children);
    }

    private static LogicalNode Value(MergedValue value, string? name)
    {
        switch (value)
        {
            case MergedTable table:
                return Table(table, name);
            case MergedArray array:
            {
                var children = new List<LogicalNode>();
                foreach (MergedValue item in array.Items)
                {
                    children.Add(Value(item, null));
                }

                return new LogicalNode(LogicalKind.Array, name, null, null, [], children);
            }
            case MergedScalar scalar:
            {
                (TomlValueKind kind, string text) = TomlValues.Canonical(scalar.Value);
                return new LogicalNode(LogicalKind.Scalar, name, null, text, [], [], tomlKind: kind);
            }
            default:
                throw new InvalidOperationException("Unknown TOML value type: " + value.GetType().Name);
        }
    }

    /// <summary>Every comment of the text in file order: comment lines, trailing comments, and comments inside arrays.</summary>
    private static List<TomlComment> CommentsOf(TomlDocument document)
    {
        var comments = new List<TomlComment>();
        foreach (TomlItem item in document.Items)
        {
            if (item.Comment is { } comment)
            {
                comments.Add(comment);
            }

            if (item is TomlKeyValueItem keyValue)
            {
                CollectValueComments(keyValue.Value, comments);
            }
        }

        return comments.OrderBy(comment => comment.Start).ToList();
    }

    private static void CollectValueComments(TomlValue value, List<TomlComment> comments)
    {
        switch (value)
        {
            case TomlArray array:
                foreach (TomlNode entry in array.Entries)
                {
                    switch (entry)
                    {
                        case TomlComment comment:
                            comments.Add(comment);
                            break;
                        case TomlArrayItem item:
                            foreach (TomlNode between in item.BeforeComma)
                            {
                                if (between is TomlComment betweenComment)
                                {
                                    comments.Add(betweenComment);
                                }
                            }

                            CollectValueComments(item.Value, comments);
                            break;
                    }
                }

                break;
            case TomlInlineTable inline:
                foreach (TomlNode entry in inline.Entries)
                {
                    if (entry is TomlInlineMember member)
                    {
                        CollectValueComments(member.Value, comments);
                    }
                }

                break;
        }
    }
}
