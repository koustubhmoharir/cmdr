using System.Globalization;
using Cfgedit.Core.Toml;

namespace Cfgedit.Core.Model;

/// <summary>
/// The independent logical application of a TOML edit: the logical tree after the operation, computed from the tree before it. The
/// data (tables as objects, arrays as arrays, scalars with their kind and canonical value) is changed at the logical position of the
/// node, which the document tree gives. The <c>#comments</c> array is changed by the rank of each comment in file order. A deleted node
/// takes its comments with it: the comments below it, the comments in its layout group, and the tables that only it kept alive (an
/// implicit or dotted table with no members left), as the text does.
/// </summary>
internal static class TomlLogicalEdit
{
    /// <summary>Applies an operation to a logical tree. Throws <see cref="InvalidOperationException"/> when it cannot be applied.</summary>
    /// <param name="before">The document the operation applies to. Its tree gives the paths their meaning.</param>
    /// <param name="root">The logical tree of <paramref name="before"/>: the data, then the <c>#comments</c> array.</param>
    /// <param name="op">The operation. It is assumed valid.</param>
    public static LogicalNode Apply(ConfigDocument before, LogicalNode root, EditOp op)
    {
        LogicalNode data = root.Children[0];
        List<LogicalNode> comments = root.Children[1].Children.ToList();
        Dictionary<DocNode, int>? ranks = null;
        var removed = new HashSet<int>();
        Dictionary<DocNode, int> Ranks() => ranks ??= CommentRanks(before);

        switch (op)
        {
            case SetPart { Part: PartKind.Key } rename:
            {
                DocNode node = Locate(before, rename.Node);
                data = Modify(data, IndicesOf(node), 0, n => Renamed(n, rename.Value)) ?? data;
                break;
            }

            case SetPart { Part: PartKind.Value } value:
            {
                DocNode node = Locate(before, value.Node);
                TomlValueKind kind = node.TomlKind ?? throw new InvalidOperationException("The node is not a TOML scalar.");
                (TomlValueKind newKind, string text) = Canonical(kind, value.Value);
                data = Modify(data, IndicesOf(node), 0, n => Scalar(n.Name, newKind, text)) ?? data;
                break;
            }

            case SetPart { Part: PartKind.Comment } comment:
            {
                DocNode node = Locate(before, comment.Node);
                int rank = Ranks()[node];
                comments[rank] = new LogicalNode(LogicalKind.Comment, null, null, comment.Value, [], []);
                break;
            }

            case ChangeTomlType change:
            {
                DocNode node = Locate(before, change.Node);
                if (SameKind(node, change))
                {
                    break;
                }

                CollectComments(node, Ranks(), removed);
                data = Modify(data, IndicesOf(node), 0, n => NewValue(n.Name, change)) ?? data;
                break;
            }

            case DeleteNode delete:
            {
                DocNode node = Locate(before, delete.Node);
                if (node.Kind == NodeKind.TomlComment)
                {
                    removed.Add(Ranks()[node]);
                    break;
                }

                DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be deleted.");
                (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), before.Source.Text);
                for (int k = first; k <= last; k++)
                {
                    if (EditLayout.IsComment(parent.Children[k]))
                    {
                        removed.Add(Ranks()[parent.Children[k]]);
                    }
                }

                CollectComments(node, Ranks(), removed);
                List<int> indices = IndicesOf(node);
                data = Modify(data, indices, 0, _ => null) ?? throw new InvalidOperationException("The root table cannot be deleted.");

                // Tables that only the deleted node kept alive go too, as their text does.
                var model = (TomlModel)before.Syntax;
                DocNode child = node;
                DocNode? container = parent;
                while (container is not null && ShouldPrune(model, container, child))
                {
                    CollectComments(container, Ranks(), removed);
                    data = Modify(data, IndicesOf(container), 0, _ => null) ?? throw new InvalidOperationException("The root table cannot be pruned.");
                    child = container;
                    container = container.Parent;
                }

                break;
            }

            default:
                throw new InvalidOperationException("Unknown TOML edit operation: " + op.GetType().Name);
        }

        var kept = new List<LogicalNode>();
        for (int i = 0; i < comments.Count; i++)
        {
            if (!removed.Contains(i))
            {
                kept.Add(comments[i]);
            }
        }

        var commentArray = new LogicalNode(LogicalKind.Array, "#comments", null, null, [], kept);
        return new LogicalNode(LogicalKind.Document, null, null, null, [], [data, commentArray]);
    }

    /// <summary>
    /// The node to focus after an operation. A rename focuses the renamed node under its parent. A value, comment or type change keeps
    /// the node's path. A delete focuses the next sibling, else the previous one, else the parent; when the parent was pruned, the
    /// nearest surviving ancestor.
    /// </summary>
    public static NodePath? FocusAfter(ConfigDocument before, ConfigDocument after, EditOp op)
    {
        switch (op)
        {
            case SetPart { Part: PartKind.Key } rename:
            {
                DocNode node = Locate(before, rename.Node);
                NodePath renamed = (node.Parent ?? throw new InvalidOperationException("The document node has no parent.")).Path
                    .Append(new MemberStep(rename.Value, 0));
                return after.Resolve(renamed) is null ? null : renamed;
            }

            case SetPart set:
                return after.Resolve(set.Node) is null ? null : set.Node;

            case ChangeTomlType change:
                return after.Resolve(change.Node) is null ? null : change.Node;

            case DeleteNode delete:
            {
                DocNode node = Locate(before, delete.Node);
                DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node has no parent.");
                int first = EditLayout.Group(parent, IndexOf(parent, node), before.Source.Text).First;
                NodePath parentPath = parent.Path;
                DocNode? survivor = after.Resolve(parentPath);
                if (survivor is null)
                {
                    while (after.Resolve(parentPath) is null && parentPath.Count > 1)
                    {
                        parentPath = parentPath.Prefix(parentPath.Count - 1);
                    }

                    return parentPath;
                }

                if (first < survivor.Children.Count)
                {
                    return survivor.Children[first].Path;
                }

                return first > 0 ? survivor.Children[first - 1].Path : survivor.Path;
            }

            default:
                return null;
        }
    }

    // ----- Paths and ranks -----

    private static DocNode Locate(ConfigDocument document, NodePath path)
    {
        return document.Resolve(path) ?? throw new InvalidOperationException($"There is no node at {path}.");
    }

    /// <summary>
    /// The logical child indices that lead from the data root to a node. Comments are not in the data, so the index of a node is the
    /// number of non-comment children before it. The data root has no indices.
    /// </summary>
    private static List<int> IndicesOf(DocNode node)
    {
        var indices = new List<int>();
        DocNode child = node;
        while (child.Parent is not null && child.Parent.Parent is not null)
        {
            indices.Add(LogicalIndex(child.Parent, child));
            child = child.Parent;
        }

        indices.Reverse();
        return indices;
    }

    private static int LogicalIndex(DocNode parent, DocNode child)
    {
        int index = 0;
        foreach (DocNode sibling in parent.Children)
        {
            if (ReferenceEquals(sibling, child))
            {
                return index;
            }

            if (!EditLayout.IsComment(sibling))
            {
                index++;
            }
        }

        throw new InvalidOperationException("The node is not a child of its parent.");
    }

    private static int IndexOf(DocNode parent, DocNode child)
    {
        for (int k = 0; k < parent.Children.Count; k++)
        {
            if (ReferenceEquals(parent.Children[k], child))
            {
                return k;
            }
        }

        throw new InvalidOperationException("The node is not a child of its parent.");
    }

    /// <summary>The rank of each comment node in file order: its index in the <c>#comments</c> array.</summary>
    private static Dictionary<DocNode, int> CommentRanks(ConfigDocument document)
    {
        var comments = new List<DocNode>();
        CollectComments(document.Root, comments);
        comments.Sort((a, b) => a.Start.CompareTo(b.Start));
        var ranks = new Dictionary<DocNode, int>();
        for (int i = 0; i < comments.Count; i++)
        {
            ranks[comments[i]] = i;
        }

        return ranks;
    }

    private static void CollectComments(DocNode node, Dictionary<DocNode, int> ranks, HashSet<int> removed)
    {
        var comments = new List<DocNode>();
        CollectComments(node, comments);
        foreach (DocNode comment in comments)
        {
            removed.Add(ranks[comment]);
        }
    }

    private static void CollectComments(DocNode node, List<DocNode> comments)
    {
        var stack = new Stack<DocNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            DocNode current = stack.Pop();
            if (current.Kind == NodeKind.TomlComment)
            {
                comments.Add(current);
            }

            foreach (DocNode child in current.Children)
            {
                stack.Push(child);
            }
        }
    }

    // ----- Data changes -----

    /// <summary>
    /// Applies a change to the node at a path of logical indices. The change returns the replacement, or null to remove the node from its
    /// parent. The path must not be empty: the root is changed in place by the caller.
    /// </summary>
    private static LogicalNode? Modify(LogicalNode node, IReadOnlyList<int> path, int depth, Func<LogicalNode, LogicalNode?> change)
    {
        if (depth == path.Count)
        {
            return change(node);
        }

        var children = node.Children.ToList();
        int k = path[depth];
        if (depth == path.Count - 1)
        {
            LogicalNode? replaced = change(children[k]);
            if (replaced is null)
            {
                children.RemoveAt(k);
            }
            else
            {
                children[k] = replaced;
            }
        }
        else
        {
            children[k] = Modify(children[k], path, depth + 1, change) ?? throw new InvalidOperationException("Only a leaf can be removed.");
        }

        return new LogicalNode(node.Kind, node.Name, node.ScalarKind, node.Value, node.Attributes, children, false, node.TomlKind);
    }

    private static LogicalNode Renamed(LogicalNode node, string name)
    {
        return new LogicalNode(node.Kind, name, node.ScalarKind, node.Value, node.Attributes, node.Children, false, node.TomlKind);
    }

    /// <summary>True when a change keeps the node's kind, so that it changes nothing: the same type with no value given.</summary>
    private static bool SameKind(DocNode node, ChangeTomlType change)
    {
        switch (change.Kind)
        {
            case TomlNewKind.Table:
                return node.Kind == NodeKind.TomlTable;
            case TomlNewKind.Array:
                return node.Kind == NodeKind.TomlArray;
            default:
                return node.Kind == NodeKind.TomlScalar && change.Value is null && NewKindOf(node.TomlKind) == change.Kind;
        }
    }

    private static TomlNewKind? NewKindOf(TomlValueKind? kind)
    {
        return kind switch
        {
            TomlValueKind.String => TomlNewKind.String,
            TomlValueKind.Integer => TomlNewKind.Integer,
            TomlValueKind.Float => TomlNewKind.Float,
            TomlValueKind.Boolean => TomlNewKind.Boolean,
            TomlValueKind.OffsetDateTime or TomlValueKind.LocalDateTime or TomlValueKind.LocalDate or TomlValueKind.LocalTime => TomlNewKind.DateTime,
            _ => null,
        };
    }

    private static LogicalNode NewValue(string? name, ChangeTomlType change)
    {
        switch (change.Kind)
        {
            case TomlNewKind.Table:
                return new LogicalNode(LogicalKind.Object, name, null, null, [], []);
            case TomlNewKind.Array:
                return new LogicalNode(LogicalKind.Array, name, null, null, [], []);
            case TomlNewKind.String:
                return Scalar(name, TomlValueKind.String, change.Value ?? string.Empty);
            case TomlNewKind.Integer:
            {
                (TomlValueKind kind, string text) = Canonical(TomlValueKind.Integer, change.Value ?? "0");
                return Scalar(name, kind, text);
            }

            case TomlNewKind.Float:
            {
                (TomlValueKind kind, string text) = Canonical(TomlValueKind.Float, change.Value ?? "0.0");
                return Scalar(name, kind, text);
            }

            case TomlNewKind.Boolean:
            {
                (TomlValueKind kind, string text) = Canonical(TomlValueKind.Boolean, change.Value ?? "false");
                return Scalar(name, kind, text);
            }

            default:
            {
                (TomlValueKind kind, string text) = Canonical(TomlValueKind.OffsetDateTime, change.Value ?? string.Empty);
                return Scalar(name, kind, text);
            }
        }
    }

    private static LogicalNode Scalar(string? name, TomlValueKind kind, string text)
    {
        return new LogicalNode(LogicalKind.Scalar, name, null, text, [], [], false, kind);
    }

    /// <summary>
    /// The kind and canonical text of a scalar of the given kind, written as typed. A date-time of any of the four forms is given
    /// as <see cref="TomlValueKind.OffsetDateTime"/>, and its kind is the one the text has. The text was checked by the planner.
    /// </summary>
    private static (TomlValueKind Kind, string Text) Canonical(TomlValueKind kind, string text)
    {
        switch (kind)
        {
            case TomlValueKind.String:
                return (kind, text);
            case TomlValueKind.Integer:
                if (TomlScalars.ValidateInteger(text, out long integer, out _) is null)
                {
                    return (kind, integer.ToString(CultureInfo.InvariantCulture));
                }

                break;
            case TomlValueKind.Float:
                if (TomlScalars.ValidateFloat(text, out double number) is null)
                {
                    return (kind, TomlValues.FloatText(number));
                }

                break;
            case TomlValueKind.Boolean:
                return (kind, text);
            default:
                if (TomlScalars.ValidateDateTime(text, out TomlDateTimeKind dateTimeKind) is null)
                {
                    return (TomlValues.KindOf(dateTimeKind), TomlValues.DateTimeText(text));
                }

                break;
        }

        throw new InvalidOperationException($"'{text}' is not a valid {kind} text.");
    }

    // ----- Pruning -----

    /// <summary>
    /// True when a table or an array of tables has no children left once the child is removed, and the text would drop it too: a table
    /// that is not inline and is not defined by its own header (an implicit or dotted table), or an array of tables with no items.
    /// The root is never pruned.
    /// </summary>
    private static bool ShouldPrune(TomlModel model, DocNode container, DocNode child)
    {
        if (container.Parent is null || container.Parent.Kind == NodeKind.Document)
        {
            return false;
        }

        if (container.Children.Any(c => !EditLayout.IsComment(c) && !ReferenceEquals(c, child)))
        {
            return false;
        }

        IReadOnlyList<TomlNode> pieces = model.PiecesOf(container);
        if (container.Kind == NodeKind.TomlArray)
        {
            return pieces.Count > 0 && pieces[0] is TomlHeaderItem;
        }

        if (container.Kind != NodeKind.TomlTable || pieces.Count == 0 || pieces[0] is TomlInlineTable)
        {
            return false;
        }

        // A table defined by its own header has a header with as many keys as the table has names from the root.
        int depth = container.Path.Steps.Count(step => step is MemberStep);
        return !pieces.Any(piece => piece is TomlHeaderItem header && header.Key.Parts.Count == depth);
    }
}
