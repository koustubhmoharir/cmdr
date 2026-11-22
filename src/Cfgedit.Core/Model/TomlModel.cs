using Cfgedit.Core.Toml;

namespace Cfgedit.Core.Model;

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
