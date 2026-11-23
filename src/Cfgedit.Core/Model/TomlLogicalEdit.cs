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

                if (change.Kind == TomlNewKind.Array)
                {
                    // The value keeps its text and comments (an inline value holds none), as the array's only item.
                    data = Modify(data, IndicesOf(node), 0, Wrapped) ?? data;
                    break;
                }

                CollectComments(node, Ranks(), removed);
                data = Modify(data, IndicesOf(node), 0, n => NewValue(n.Name, change.Kind, change.Value)) ?? data;
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

            case InsertNode insert:
            {
                DocNode parent = Locate(before, insert.Parent);
                switch (insert.Node)
                {
                    case NewTomlComment newComment:
                    {
                        int offset = CommentOffset(before, parent, insert.Index);
                        comments.Insert(CommentsBefore(before, offset), CommentNode(newComment.Text));
                        break;
                    }

                    case NewTomlValue value:
                    {
                        var model = (TomlModel)before.Syntax;
                        bool tableLine = model.ShapeOf(parent) is not (TomlContainer.Array or TomlContainer.ArrayOfTables or TomlContainer.Inline);
                        int offset = tableLine ? MemberOffset(before, parent, insert.Index, value.Kind) : -1;
                        int position = tableLine
                            ? MembersBefore(model, parent, offset)
                            : ChildrenBefore(parent, insert.Index);
                        LogicalNode added = NewValue(value.Key, value.Kind, value.Value);
                        data = Modify(data, IndicesOf(parent), 0, n => WithChild(n, position, added)) ?? data;
                        break;
                    }

                    default:
                        throw new InvalidOperationException("Unknown TOML node to insert: " + insert.Node.GetType().Name);
                }

                break;
            }

            case DuplicateNode duplicate:
            {
                DocNode node = Locate(before, duplicate.Node);
                DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be duplicated.");
                int offset = CopyOffset(before, node);
                List<DocNode> copied = CopiedComments(before, node);
                comments.InsertRange(CommentsBefore(before, offset), copied.Select(c => CommentNode(CommentText(c))));
                if (node.Kind == NodeKind.TomlComment)
                {
                    break;
                }

                var model = (TomlModel)before.Syntax;
                LogicalNode original = NodeAt(data, IndicesOf(node));
                bool keyed = IsKeyed(model, parent);
                LogicalNode copy = keyed ? Renamed(original, CopyNameOf(parent, node)) : original;
                int position = keyed && !InlineMemberOf(model, node) ? MembersBefore(model, parent, offset) : LogicalIndex(parent, node) + 1;
                data = Modify(data, IndicesOf(parent), 0, n => WithChild(n, position, copy)) ?? data;
                break;
            }

            case MoveNodeTo moveTo:
            {
                (data, comments) = MoveTo(before, data, comments, moveTo);
                break;
            }

            case MoveNode move:
            {
                MoveLayout layout = LayoutOf(before, move);
                if (layout.Node.Kind != NodeKind.TomlComment)
                {
                    int from = LogicalIndex(layout.Parent, layout.Node);
                    int to = layout.Position;
                    data = Modify(data, IndicesOf(layout.Parent), 0, n => WithMoved(n, from, to)) ?? data;
                }

                List<LogicalNode> old = comments;
                comments = CommentPermutation(before, layout).Select(rank => old[rank]).ToList();
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

            case InsertNode insert:
            {
                DocNode parent = Locate(before, insert.Parent);
                if (insert.Node is NewTomlComment)
                {
                    return CommentPathAt(after, CommentsBefore(before, CommentOffset(before, parent, insert.Index)));
                }

                var value = (NewTomlValue)insert.Node;
                NodePath path = value.Key is null
                    ? parent.Path.Append(new ItemStep(ChildrenBefore(parent, insert.Index)))
                    : parent.Path.Append(new MemberStep(value.Key, 0));
                return after.Resolve(path) is null ? null : path;
            }

            case DuplicateNode duplicate:
            {
                DocNode node = Locate(before, duplicate.Node);
                DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node has no parent.");
                if (node.Kind == NodeKind.TomlComment)
                {
                    return CommentPathAt(after, CommentsBefore(before, CopyOffset(before, node)));
                }

                NodePath path = IsKeyed((TomlModel)before.Syntax, parent)
                    ? parent.Path.Append(new MemberStep(CopyNameOf(parent, node), 0))
                    : parent.Path.Append(new ItemStep(LogicalIndex(parent, node) + 1));
                return after.Resolve(path) is null ? null : path;
            }

            case MoveNodeTo moveTo:
                return MovedFocus(before, after, moveTo);

            case MoveNode move:
            {
                MoveLayout layout = LayoutOf(before, move);
                DocNode node = layout.Node;
                if (node.Kind == NodeKind.TomlComment)
                {
                    return CommentPathAt(after, Array.IndexOf(CommentPermutation(before, layout), CommentRanks(before)[node]));
                }

                NodePath path = IsKeyed((TomlModel)before.Syntax, layout.Parent)
                    ? node.Path
                    : layout.Parent.Path.Append(new ItemStep(layout.Position));
                return after.Resolve(path) is null ? null : path;
            }

            default:
                return null;
        }
    }

    // ----- Move into another container: where the moved node goes, and its comments -----

    /// <summary>
    /// The data and the comments after a MoveNodeTo. The node leaves its parent, with the containers that only it kept alive. It enters the
    /// destination at the position its text takes: a section at <see cref="SectionOffset"/>, a line at <see cref="LineOffset"/>, or a member
    /// or item at its index. The indexes are read after the removal (the removed nodes do not count). The moved comments take their rank in
    /// file order at the offset where the text goes.
    /// </summary>
    private static (LogicalNode Data, List<LogicalNode> Comments) MoveTo(ConfigDocument before, LogicalNode data, List<LogicalNode> comments, MoveNodeTo move)
    {
        var model = (TomlModel)before.Syntax;
        string text = before.Source.Text;
        DocNode node = Locate(before, move.Node);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be moved.");
        DocNode destination = Locate(before, move.NewParent);
        bool section = IsSectionNode(model, node);

        LogicalNode moved = NodeAt(data, IndicesOf(node));
        var movedComments = new List<DocNode>();
        var removed = new HashSet<DocNode>(ReferenceEqualityComparer.Instance);
        if (!section)
        {
            // A key/value takes the comment lines attached above it and its trailing comment: the children of its group.
            (int first, int last) = TomlLayout.Block(parent, IndexOf(parent, node), text);
            for (int k = first; k <= last; k++)
            {
                if (EditLayout.IsComment(parent.Children[k]))
                {
                    movedComments.Add(parent.Children[k]);
                }
            }
        }

        CollectComments(node, movedComments);
        removed.Add(node);
        data = Modify(data, IndicesOf(node), 0, _ => null) ?? throw new InvalidOperationException("The node cannot be removed.");
        if (section)
        {
            // A table that only the moved node kept alive goes too, as its text does (see ShouldPrune).
            DocNode container = parent;
            DocNode child = node;
            while (ShouldPrune(model, container, child))
            {
                removed.Add(container);
                data = Modify(data, IndicesOf(container), 0, _ => null) ?? throw new InvalidOperationException("The root table cannot be pruned.");
                child = container;
                container = container.Parent ?? throw new InvalidOperationException("The pruned table has no parent.");
            }
        }

        List<int> destinationIndices = IndicesExcept(destination, removed);
        LogicalNode added;
        int position;
        int at;
        if (section)
        {
            at = SectionOffset(before, destination, move.NewIndex);
            position = MembersBeforeExcept(model, destination, at, removed);
            string name = move.NewKey ?? NodeNameOf(node);
            added = node.Kind == NodeKind.TomlTable && parent.Kind == NodeKind.TomlArray
                ? new LogicalNode(LogicalKind.Array, name, null, null, [], [moved])
                : Renamed(moved, name);
        }
        else if (model.ShapeOf(destination) is TomlContainer.Root or TomlContainer.Header)
        {
            at = LineOffset(before, destination, move.NewIndex);
            position = MembersBeforeExcept(model, destination, at, removed);
            added = Renamed(moved, move.NewKey ?? moved.Name);
        }
        else
        {
            at = TokenOffset(destination, move.NewIndex);
            position = ChildrenBeforeExcept(destination, move.NewIndex, removed);
            added = model.ShapeOf(destination) == TomlContainer.Array ? Renamed(moved, null) : Renamed(moved, move.NewKey ?? moved.Name);
        }

        data = Modify(data, destinationIndices, 0, n => WithChild(n, position, added)) ?? data;
        return (data, PlaceComments(before, comments, movedComments, at));
    }

    /// <summary>
    /// The #comments array after the moved comments take their new place: they leave their ranks, and go in at the rank that their offset
    /// has among the comments that stay (the staying comments before the offset come first).
    /// </summary>
    private static List<LogicalNode> PlaceComments(ConfigDocument before, List<LogicalNode> comments, List<DocNode> moved, int offset)
    {
        Dictionary<DocNode, int> ranks = CommentRanks(before);
        List<int> movedRanks = moved.Where(comment => comment.Kind == NodeKind.TomlComment).Select(comment => ranks[comment]).Distinct().Order().ToList();
        if (movedRanks.Count == 0)
        {
            return comments;
        }

        var all = new List<DocNode>();
        CollectComments(before.Root, all);
        all.Sort((a, b) => a.Start.CompareTo(b.Start));
        List<int> staying = Enumerable.Range(0, comments.Count).Where(rank => !movedRanks.Contains(rank)).ToList();
        int insertAt = staying.Count(rank => all[rank].Start < offset);
        var result = new List<LogicalNode>();
        result.AddRange(staying.Take(insertAt).Select(rank => comments[rank]));
        result.AddRange(movedRanks.Select(rank => comments[rank]));
        result.AddRange(staying.Skip(insertAt).Select(rank => comments[rank]));
        return result;
    }

    /// <summary>The focus after a MoveNodeTo: the moved node under its new parent, by its name (or its index in an array).</summary>
    private static NodePath? MovedFocus(ConfigDocument before, ConfigDocument after, MoveNodeTo move)
    {
        var model = (TomlModel)before.Syntax;
        DocNode node = Locate(before, move.Node);
        DocNode destination = Locate(before, move.NewParent);
        NodePath container = ShiftedPath(destination, node);
        NodePath path = model.ShapeOf(destination) == TomlContainer.Array
            ? container.Append(new ItemStep(ChildrenBefore(destination, move.NewIndex)))
            : container.Append(new MemberStep(move.NewKey ?? NodeNameOf(node), 0));
        return after.Resolve(path) is null ? null : path;
    }

    /// <summary>
    /// The path of a destination after a node is removed from its parent: an item index after the removed item moves down by one. A
    /// key path does not change.
    /// </summary>
    private static NodePath ShiftedPath(DocNode destination, DocNode removed)
    {
        var chain = new List<DocNode>();
        for (DocNode? n = destination; n is not null && n.Parent is not null; n = n.Parent)
        {
            chain.Add(n);
        }

        chain.Reverse();
        var steps = new List<PathStep>();
        foreach (DocNode n in chain)
        {
            PathStep step = n.Path.Steps[^1];
            if (step is ItemStep at
                && ReferenceEquals(n.Parent, removed.Parent)
                && removed.Path.Steps[^1] is ItemStep gone
                && gone.Index < at.Index)
            {
                step = new ItemStep(at.Index - 1);
            }

            steps.Add(step);
        }

        return new NodePath(steps);
    }

    /// <summary>The name of a node in its container: its own key, or the key of the array that holds it, for an item of an array of tables.</summary>
    private static string NodeNameOf(DocNode node)
    {
        if (node.Path.Steps[^1] is MemberStep member)
        {
            return member.Key;
        }

        return ((MemberStep)node.Parent!.Path.Steps[^1]).Key;
    }

    /// <summary>True for a table with its own header, a whole array of tables, or an item of one (what moves as header sections).</summary>
    private static bool IsSectionNode(TomlModel model, DocNode node)
    {
        return node.Kind == NodeKind.TomlTable
            ? model.ShapeOf(node) == TomlContainer.Header
            : node.Kind == NodeKind.TomlArray && model.ShapeOf(node) == TomlContainer.ArrayOfTables;
    }

    /// <summary>The offset where a section moved into a table goes: as the planner places it (see the TOML edit planner).</summary>
    private static int SectionOffset(ConfigDocument before, DocNode table, int index)
    {
        var model = (TomlModel)before.Syntax;
        IReadOnlyList<DocNode> children = table.Children;
        for (int k = index; k < children.Count; k++)
        {
            DocNode child = children[k];
            if (EditLayout.IsComment(child))
            {
                continue;
            }

            bool sectionChild = child.Kind == NodeKind.TomlTable && model.ShapeOf(child) == TomlContainer.Header
                || child.Kind == NodeKind.TomlArray && model.ShapeOf(child) == TomlContainer.ArrayOfTables;
            return sectionChild ? HeaderStart(before, child) : AfterKeyValues(before, table);
        }

        return SubtreeOffset(before, table);
    }

    /// <summary>The start of a section's header line, moved up over the comment lines above it (which belong to the section).</summary>
    private static int HeaderStart(ConfigDocument before, DocNode table)
    {
        IReadOnlyList<TomlItem> items = ((TomlModel)before.Syntax).Syntax.Items;
        int line = EditLayout.LineStart(before.Source.Text, table.Start);
        int k = 0;
        while (k < items.Count && items[k].Start < line)
        {
            k++;
        }

        while (k > 0 && items[k - 1] is TomlCommentItem)
        {
            k--;
        }

        return items[k].Start;
    }

    /// <summary>The offset after the last key/value of a table's own section, or after its header, or at the top of the root.</summary>
    private static int AfterKeyValues(ConfigDocument before, DocNode table)
    {
        var model = (TomlModel)before.Syntax;
        IReadOnlyList<TomlItem> items = model.Syntax.Items;
        bool root = model.ShapeOf(table) == TomlContainer.Root;
        int start = 0;
        if (!root)
        {
            TomlHeaderItem header = model.OwnHeaderOf(table) ?? throw new InvalidOperationException("A header table has no header.");
            start = Array.FindIndex(items.ToArray(), item => ReferenceEquals(item, header));
        }

        int end = items.Count;
        for (int k = root ? 0 : start + 1; k < items.Count; k++)
        {
            if (items[k] is TomlHeaderItem)
            {
                end = k;
                break;
            }
        }

        for (int k = end - 1; k >= start; k--)
        {
            if (items[k] is TomlKeyValueItem)
            {
                return items[k].End;
            }
        }

        if (!root)
        {
            return items[start].End;
        }

        int top = end;
        if (end < items.Count)
        {
            while (top > 0 && items[top - 1] is TomlCommentItem)
            {
                top--;
            }
        }

        for (int j = top - 1; j >= 0; j--)
        {
            if (items[j] is TomlCommentItem)
            {
                return items[j].End;
            }
        }

        return 0;
    }

    /// <summary>The offset where a token (an inline member or an array item) goes: before the child at the index, else before the closing bracket.</summary>
    private static int TokenOffset(DocNode destination, int index)
    {
        return index < destination.Children.Count ? destination.Children[index].Start : destination.End - 1;
    }

    /// <summary>The logical child indices that lead to a node, with the removed nodes left out of the counts.</summary>
    private static List<int> IndicesExcept(DocNode node, HashSet<DocNode> removed)
    {
        var indices = new List<int>();
        DocNode child = node;
        while (child.Parent is not null && child.Parent.Parent is not null)
        {
            indices.Add(ChildIndexExcept(child.Parent, child, removed));
            child = child.Parent;
        }

        indices.Reverse();
        return indices;
    }

    private static int ChildIndexExcept(DocNode parent, DocNode child, HashSet<DocNode> removed)
    {
        int index = 0;
        foreach (DocNode sibling in parent.Children)
        {
            if (ReferenceEquals(sibling, child))
            {
                return index;
            }

            if (!EditLayout.IsComment(sibling) && !removed.Contains(sibling))
            {
                index++;
            }
        }

        throw new InvalidOperationException("The node is not a child of its parent.");
    }

    /// <summary>The number of members of a table whose text starts before an offset, not counting the removed nodes.</summary>
    private static int MembersBeforeExcept(TomlModel model, DocNode parent, int offset, HashSet<DocNode> removed)
    {
        int count = 0;
        foreach (DocNode child in parent.Children)
        {
            if (!EditLayout.IsComment(child) && !removed.Contains(child) && model.PiecesOf(child).Min(piece => piece.Start) < offset)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The number of non-comment children before an index, not counting the removed nodes.</summary>
    private static int ChildrenBeforeExcept(DocNode parent, int index, HashSet<DocNode> removed)
    {
        int count = 0;
        for (int k = 0; k < index; k++)
        {
            if (!EditLayout.IsComment(parent.Children[k]) && !removed.Contains(parent.Children[k]))
            {
                count++;
            }
        }

        return count;
    }

    // ----- Moves: where a unit goes, and the comments in their new order -----

    /// <summary>
    /// A move on the layout of its parent: the units of the parent (see <see cref="TomlLayout.Units"/>), the order of the units after the move,
    /// and the position of the moved node among the parent's data members (its comments are not data).
    /// </summary>
    private sealed record MoveLayout(DocNode Parent, DocNode Node, List<TomlUnit> Units, List<int> Order, int Position);

    private static MoveLayout LayoutOf(ConfigDocument before, MoveNode move)
    {
        DocNode node = Locate(before, move.Node);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be moved.");
        int index = IndexOf(parent, node);
        List<TomlUnit> units = TomlLayout.Units((TomlModel)before.Syntax, parent, before.Source.Text, index);
        int moved = units.FindIndex(unit => unit.First <= index && index <= unit.Last);
        int destination = TomlLayout.Destination(units, moved, move.NewIndex, parent.Children.Count);
        if (destination < 0)
        {
            throw new InvalidOperationException("A move cannot go inside a unit.");
        }

        List<int> order = TomlLayout.Order(units.Count, moved, destination);

        // The data members before the moved unit, in the new order, are the members that come before it in the data.
        int position = 0;
        foreach (int k in order)
        {
            if (k == moved)
            {
                break;
            }

            position += DataCount(parent, units[k]);
        }

        return new MoveLayout(parent, node, units, order, position);
    }

    /// <summary>The number of data members (children that are not comments) in a unit.</summary>
    private static int DataCount(DocNode parent, TomlUnit unit)
    {
        int count = 0;
        for (int k = unit.First; k <= unit.Last; k++)
        {
            if (parent.Children[k].Kind != NodeKind.TomlComment)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The old rank of the comment at each new rank after a move. A move rewrites the span of its units: the comments of each unit (the comments
    /// whose text lies in the unit) move with it, in the order the units are in. The comments outside the span keep their ranks.
    /// </summary>
    private static int[] CommentPermutation(ConfigDocument before, MoveLayout layout)
    {
        var comments = new List<DocNode>();
        CollectComments(before.Root, comments);
        comments.Sort((a, b) => a.Start.CompareTo(b.Start));
        var permutation = Enumerable.Range(0, comments.Count).ToArray();
        (int lo, int hi) = TomlLayout.Span(layout.Order);
        if (lo < 0)
        {
            return permutation;
        }

        int from = comments.FindIndex(comment => comment.Start >= layout.Units[lo].Start);
        if (from < 0)
        {
            from = comments.Count;
        }

        int to = Math.Max(from, comments.FindLastIndex(comment => comment.Start < layout.Units[hi].End) + 1);
        var block = new List<int>();
        for (int p = lo; p <= hi; p++)
        {
            TomlUnit unit = layout.Units[layout.Order[p]];
            for (int rank = from; rank < to; rank++)
            {
                if (comments[rank].Start >= unit.Start && comments[rank].Start < unit.End)
                {
                    block.Add(rank);
                }
            }
        }

        if (block.Count != to - from)
        {
            throw new InvalidOperationException("A comment is not in any unit of a move.");
        }

        for (int k = 0; k < block.Count; k++)
        {
            permutation[from + k] = block[k];
        }

        return permutation;
    }

    /// <summary>The children with one moved from a position to another, as a data member list.</summary>
    private static LogicalNode WithMoved(LogicalNode node, int from, int to)
    {
        var children = node.Children.ToList();
        LogicalNode child = children[from];
        children.RemoveAt(from);
        children.Insert(to, child);
        return new LogicalNode(node.Kind, node.Name, node.ScalarKind, node.Value, node.Attributes, children, false, node.TomlKind);
    }

    // ----- Insert and duplicate: where a new node or a copy goes -----

    /// <summary>
    /// The offset of the line (or section) where a new node goes, and of the copies of a node. These rules follow the planner's, but
    /// they are read off the document tree and the layout rules, not off the planner's text edits, so the check compares two readings.
    /// </summary>
    private static int CommentOffset(ConfigDocument before, DocNode parent, int index)
    {
        string text = before.Source.Text;
        switch (((TomlModel)before.Syntax).ShapeOf(parent))
        {
            case TomlContainer.Array:
                IReadOnlyList<DocNode> children = parent.Children;
                if (index < children.Count && EditLayout.StartsLine(text, children[index].Start))
                {
                    return EditLayout.LineStart(text, children[index].Start);
                }

                if (index == children.Count && EditLayout.StartsLine(text, parent.End - 1))
                {
                    return EditLayout.LineStart(text, parent.End - 1);
                }

                throw new InvalidOperationException("A comment in an array needs a line of its own.");
            case TomlContainer.Inline:
            case TomlContainer.ArrayOfTables:
                throw new InvalidOperationException("No comment goes into this container.");
            default:
                return LineOffset(before, parent, index);
        }
    }

    /// <summary>The offset of a new member of a table: a section for a new table in a header or root table, else a line.</summary>
    private static int MemberOffset(ConfigDocument before, DocNode parent, int index, TomlNewKind kind)
    {
        TomlContainer shape = ((TomlModel)before.Syntax).ShapeOf(parent);
        bool section = kind == TomlNewKind.Table && shape is TomlContainer.Root or TomlContainer.Header or TomlContainer.Implicit;
        return section ? SubtreeOffset(before, parent) : LineOffset(before, parent, index);
    }

    /// <summary>The offset where a copy of a node goes: right after the node's group, or after the node itself when it is a table, a comment or an item.</summary>
    private static int CopyOffset(ConfigDocument before, DocNode node)
    {
        string text = before.Source.Text;
        var model = (TomlModel)before.Syntax;
        if (node.Kind == NodeKind.TomlComment)
        {
            return EditLayout.AfterLineBreak(text, node.End);
        }

        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be copied.");
        TomlContainer shape = model.ShapeOf(parent);
        if (InlineMemberOf(model, node))
        {
            return node.End;
        }

        if (shape == TomlContainer.ArrayOfTables)
        {
            return EditLayout.AfterLineBreak(text, node.End);
        }

        if (shape == TomlContainer.Array)
        {
            int index = IndexOf(parent, node);
            (int first, int last) = TomlLayout.Block(parent, index, text);
            return TomlLayout.IsLineCopy(model, parent, first, last, text)
                ? EditLayout.AfterLineBreak(text, TomlLayout.ItemEnd(model, parent, last))
                : TomlLayout.ItemEnd(model, parent, index);
        }

        if (shape == TomlContainer.Inline)
        {
            return node.End;
        }

        if (node.Kind == NodeKind.TomlTable && model.ShapeOf(node) is TomlContainer.Header or TomlContainer.Implicit)
        {
            return EditLayout.AfterLineBreak(text, node.End);
        }

        (int groupFirst, int groupLast) = TomlLayout.Block(parent, IndexOf(parent, node), text);
        return EditLayout.AfterLineBreak(text, parent.Children[groupLast].End);
    }

    /// <summary>The comment nodes that a copy of a node repeats, in file order.</summary>
    private static List<DocNode> CopiedComments(ConfigDocument before, DocNode node)
    {
        var model = (TomlModel)before.Syntax;
        string text = before.Source.Text;
        var roots = new List<DocNode>();
        if (node.Kind == NodeKind.TomlComment || InlineMemberOf(model, node))
        {
            roots.Add(node);
        }
        else
        {
            DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be copied.");
            TomlContainer shape = model.ShapeOf(parent);
            if (shape == TomlContainer.Array)
            {
                (int first, int last) = TomlLayout.Block(parent, IndexOf(parent, node), text);
                if (TomlLayout.IsLineCopy(model, parent, first, last, text))
                {
                    roots.AddRange(parent.Children.Skip(first).Take(last - first + 1));
                }
                else
                {
                    roots.Add(node);
                }
            }
            else if (shape is TomlContainer.ArrayOfTables or TomlContainer.Inline
                || (node.Kind == NodeKind.TomlTable && model.ShapeOf(node) is TomlContainer.Header or TomlContainer.Implicit))
            {
                roots.Add(node);
            }
            else
            {
                (int first, int last) = TomlLayout.Block(parent, IndexOf(parent, node), text);
                roots.AddRange(parent.Children.Skip(first).Take(last - first + 1));
            }
        }

        var comments = new List<DocNode>();
        foreach (DocNode root in roots)
        {
            CollectComments(root, comments);
        }

        comments.Sort((a, b) => a.Start.CompareTo(b.Start));
        return comments;
    }

    /// <summary>The offset of the line where a key/value or a comment goes in a table, as the tree shows it.</summary>
    private static int LineOffset(ConfigDocument before, DocNode table, int index)
    {
        string text = before.Source.Text;
        var model = (TomlModel)before.Syntax;
        IReadOnlyList<DocNode> children = table.Children;
        TomlContainer shape = model.ShapeOf(table);
        switch (shape)
        {
            case TomlContainer.Dotted:
                return model.PiecesOf(table)[^1].End;
            case TomlContainer.Implicit:
                return LeafOf(model, table, model.PiecesOf(table)[0]) is { } leaf ? EditLayout.LineStart(text, leaf.Start) : throw new InvalidOperationException("No leaf.");
        }

        // A root or header table: the new line goes before a key/value child, after the key/value before it, else after the last key/value of
        // the section, else at the top of the root or after the header.
        // The key/value after the index, when the index starts its group: the new line goes before the group.
        int next = index;
        while (next < children.Count && EditLayout.IsComment(children[next]))
        {
            next++;
        }

        if (next < children.Count && IsDirectKeyValue(model, children[next]))
        {
            int first = TomlLayout.Block(table, next, text).First;
            if (next == index || first == index)
            {
                return EditLayout.LineStart(text, children[first].Start);
            }
        }

        if (index > 0)
        {
            int m = index - 1;
            int k = m;
            while (k >= 0 && EditLayout.IsComment(children[k]))
            {
                k--;
            }

            if (k >= 0 && IsDirectKeyValue(model, children[k]) && TomlLayout.Block(table, k, text).Last == m)
            {
                return EditLayout.AfterLineBreak(text, children[m].End);
            }
        }

        int end = -1;
        for (int c = 0; c < children.Count; c++)
        {
            DocNode child = children[c];
            if (EditLayout.IsComment(child))
            {
                continue;
            }

            if (IsDirectKeyValue(model, child))
            {
                end = Math.Max(end, EditLayout.AfterLineBreak(text, children[TomlLayout.Block(table, c, text).Last].End));
            }
            else if (child.Kind == NodeKind.TomlTable && model.ShapeOf(child) == TomlContainer.Dotted)
            {
                end = Math.Max(end, model.PiecesOf(child)[^1].End);
            }
        }

        if (end >= 0)
        {
            return end;
        }

        if (model.ShapeOf(table) == TomlContainer.Root)
        {
            int top = 0;
            while (top < children.Count && EditLayout.IsComment(children[top]))
            {
                top++;
            }

            return top == 0 ? 0 : EditLayout.AfterLineBreak(text, children[top - 1].End);
        }

        return model.OwnHeaderOf(table)!.End;
    }

    /// <summary>The offset after a table's section and its subtree: after the last line of the table, or the end of the text for the root.</summary>
    private static int SubtreeOffset(ConfigDocument before, DocNode table)
    {
        string text = before.Source.Text;
        if (((TomlModel)before.Syntax).ShapeOf(table) != TomlContainer.Root)
        {
            return EditLayout.AfterLineBreak(text, table.End);
        }

        if (table.Children.Count == 0)
        {
            return text.Length;
        }

        return EditLayout.AfterLineBreak(text, table.Children.Max(child => child.End));
    }

    /// <summary>The deepest node of a table's subtree that a piece defines (its leaf), or null when none does.</summary>
    private static DocNode? LeafOf(TomlModel model, DocNode table, TomlNode piece)
    {
        DocNode? best = null;
        var stack = new Stack<DocNode>();
        stack.Push(table);
        while (stack.Count > 0)
        {
            DocNode node = stack.Pop();
            if (model.PiecesOf(node).Contains(piece) && (best is null || node.Path.Count > best.Path.Count))
            {
                best = node;
            }

            foreach (DocNode child in node.Children)
            {
                stack.Push(child);
            }
        }

        return best;
    }

    /// <summary>True for a child that is a key/value of its table: a scalar, a static array or an inline table.</summary>
    private static bool IsDirectKeyValue(TomlModel model, DocNode child)
    {
        return child.Kind == NodeKind.TomlScalar
            || (child.Kind == NodeKind.TomlArray && model.ShapeOf(child) == TomlContainer.Array)
            || (child.Kind == NodeKind.TomlTable && model.ShapeOf(child) == TomlContainer.Inline);
    }

    /// <summary>
    /// True for a node whose text is a member of an inline table: a child of an inline table, or a child of a dotted table inside one. Its copy
    /// is an inline member, right after it, with no lines.
    /// </summary>
    private static bool InlineMemberOf(TomlModel model, DocNode node)
    {
        DocNode? parent = node.Parent;
        if (parent is null || parent.Kind == NodeKind.Document)
        {
            return false;
        }

        TomlContainer shape = model.ShapeOf(parent);
        return shape == TomlContainer.Inline || (shape == TomlContainer.Dotted && model.PiecesOf(parent)[0] is TomlInlineMember);
    }

    /// <summary>True for a container whose children are named members: a table, or an inline table. An array's items have no names.</summary>
    private static bool IsKeyed(TomlModel model, DocNode parent)
    {
        return model.ShapeOf(parent) is not (TomlContainer.Array or TomlContainer.ArrayOfTables);
    }

    /// <summary>The number of members of a table whose text starts before an offset: the position of a node added there.</summary>
    private static int MembersBefore(TomlModel model, DocNode parent, int offset)
    {
        int count = 0;
        foreach (DocNode child in parent.Children)
        {
            if (!EditLayout.IsComment(child) && model.PiecesOf(child).Min(piece => piece.Start) < offset)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The number of non-comment children before an index: the logical position of the node at that index.</summary>
    private static int ChildrenBefore(DocNode parent, int index)
    {
        int count = 0;
        for (int k = 0; k < index; k++)
        {
            if (!EditLayout.IsComment(parent.Children[k]))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The number of comments whose text starts before an offset: the rank of a comment added there.</summary>
    private static int CommentsBefore(ConfigDocument before, int offset)
    {
        var comments = new List<DocNode>();
        CollectComments(before.Root, comments);
        return comments.Count(comment => comment.Start < offset);
    }

    /// <summary>The path of the comment at a rank in file order, in a document, or null when there is none.</summary>
    private static NodePath? CommentPathAt(ConfigDocument document, int rank)
    {
        var comments = new List<DocNode>();
        CollectComments(document.Root, comments);
        comments.Sort((a, b) => a.Start.CompareTo(b.Start));
        return rank < comments.Count ? comments[rank].Path : null;
    }

    /// <summary>The name of a copy of a keyed node: the first <see cref="EditLayout.CopyName"/> name that no sibling has.</summary>
    private static string CopyNameOf(DocNode parent, DocNode node)
    {
        string name = ((MemberStep)node.Path.Steps[^1]).Key;
        var taken = parent.Children
            .Where(child => child.Path.Steps[^1] is MemberStep)
            .Select(child => ((MemberStep)child.Path.Steps[^1]).Key)
            .ToHashSet(StringComparer.Ordinal);
        return EditLayout.CopyName(name, taken.Contains);
    }

    private static string CommentText(DocNode comment)
    {
        return comment.Parts.First(part => part.Kind == PartKind.Comment).Value;
    }

    private static LogicalNode CommentNode(string text)
    {
        return new LogicalNode(LogicalKind.Comment, null, null, text, [], []);
    }

    private static LogicalNode NodeAt(LogicalNode root, IReadOnlyList<int> indices)
    {
        LogicalNode node = root;
        foreach (int index in indices)
        {
            node = node.Children[index];
        }

        return node;
    }

    private static LogicalNode WithChild(LogicalNode node, int position, LogicalNode child)
    {
        var children = node.Children.ToList();
        children.Insert(position, child);
        return new LogicalNode(node.Kind, node.Name, node.ScalarKind, node.Value, node.Attributes, children, false, node.TomlKind);
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

    private static LogicalNode Renamed(LogicalNode node, string? name)
    {
        return new LogicalNode(node.Kind, name, node.ScalarKind, node.Value, node.Attributes, node.Children, false, node.TomlKind);
    }

    /// <summary>An array whose only item is the node, which loses its name.</summary>
    private static LogicalNode Wrapped(LogicalNode node)
    {
        if (node.Kind == LogicalKind.Array)
        {
            return node;
        }

        var item = new LogicalNode(node.Kind, null, node.ScalarKind, node.Value, node.Attributes, node.Children, false, node.TomlKind);
        return new LogicalNode(LogicalKind.Array, node.Name, null, null, [], [item]);
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

    /// <summary>The logical node of a new value: an empty table or array, or a scalar of a kind, with the default value when none is given.</summary>
    private static LogicalNode NewValue(string? name, TomlNewKind kind, string? value)
    {
        switch (kind)
        {
            case TomlNewKind.Table:
                return new LogicalNode(LogicalKind.Object, name, null, null, [], []);
            case TomlNewKind.Array:
                return new LogicalNode(LogicalKind.Array, name, null, null, [], []);
            case TomlNewKind.String:
                return Scalar(name, TomlValueKind.String, value ?? string.Empty);
            case TomlNewKind.Integer:
            {
                (TomlValueKind valueKind, string text) = Canonical(TomlValueKind.Integer, value ?? "0");
                return Scalar(name, valueKind, text);
            }

            case TomlNewKind.Float:
            {
                (TomlValueKind valueKind, string text) = Canonical(TomlValueKind.Float, value ?? "0.0");
                return Scalar(name, valueKind, text);
            }

            case TomlNewKind.Boolean:
            {
                (TomlValueKind valueKind, string text) = Canonical(TomlValueKind.Boolean, value ?? "false");
                return Scalar(name, valueKind, text);
            }

            default:
            {
                (TomlValueKind valueKind, string text) = Canonical(TomlValueKind.OffsetDateTime, value ?? string.Empty);
                return Scalar(name, valueKind, text);
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

/// <summary>
/// The layout of TOML text around a node. A group is the node with the comments that belong to it: the comment lines directly above a
/// key/value line (no blank line between), and the comment on the node's last line. The merger makes that attachment (see
/// <see cref="TomlMerger"/>), and the tree shows it: a comment child of a table that is directly followed by a key/value child belongs
/// to it, and a comment child that starts on the line where the child before it ends is that child's trailing comment. A comment above
/// a header is a child of the table that header defines, never of the table above, so it is not in a group here. Array items take their
/// trailing comments only: a comment line in an array stands alone.
/// </summary>
internal static class TomlLayout
{
    /// <summary>The group of a child of a table or array: the child and its attached comments, as the child range [First, Last].</summary>
    public static (int First, int Last) Block(DocNode parent, int index, string text)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        if (EditLayout.IsComment(children[index]))
        {
            return (index, index);
        }

        int first = index;
        if (parent.Kind == NodeKind.TomlTable)
        {
            int next = index;
            for (int k = index - 1; k >= 0 && children[k].Kind == NodeKind.TomlComment && StandaloneAbove(text, children[k], children[next].Start); k--)
            {
                first = k;
                next = k;
            }
        }

        int last = index;
        int after = index + 1;
        if (after < children.Count && children[after].Kind == NodeKind.TomlComment && !EditLayout.HasLineBreak(text, children[index].End, children[after].Start))
        {
            last = after;
        }

        return (first, last);
    }

    /// <summary>
    /// The end of an array's child, with its comma: a comment's end, or an item's comma end when it has one, else the child's end. The
    /// item is found through the array's syntax.
    /// </summary>
    public static int ItemEnd(TomlModel model, DocNode array, int index)
    {
        DocNode child = array.Children[index];
        if (child.Kind == NodeKind.TomlComment)
        {
            return child.End;
        }

        int itemIndex = 0;
        for (int k = 0; k < index; k++)
        {
            if (array.Children[k].Kind != NodeKind.TomlComment)
            {
                itemIndex++;
            }
        }

        return model.ArrayItemSyntaxOf(array, itemIndex).Comma is { } comma ? comma.End : child.End;
    }

    /// <summary>
    /// True when a group of an array's children is copied as whole lines: it starts a line, it lies on one line, and nothing follows it on
    /// that line. Otherwise a copy goes in the line, after the item.
    /// </summary>
    public static bool IsLineCopy(TomlModel model, DocNode array, int first, int last, string text)
    {
        int end = ItemEnd(model, array, last);
        return EditLayout.StartsLine(text, array.Children[first].Start)
            && !EditLayout.HasLineBreak(text, array.Children[first].Start, end)
            && EditLayout.RestOfLineBlank(text, end);
    }

    /// <summary>True when the comment at the start of the line is followed by a single line break and then the next child, with no blank line.</summary>
    private static bool StandaloneAbove(string text, DocNode comment, int nextStart)
    {
        if (!EditLayout.StartsLine(text, comment.Start))
        {
            return false;
        }

        int i = comment.End;
        if (i < text.Length && text[i] == '\r')
        {
            i++;
        }

        if (i >= text.Length || text[i] != '\n')
        {
            return false;
        }

        i++;
        while (i < nextStart && text[i] is ' ' or '\t')
        {
            i++;
        }

        return i == nextStart;
    }

    /// <summary>
    /// The units of a parent's children, in order, as a move sees them. Each child with its attached comments is a unit, and each other comment
    /// is a unit alone. A table child is a unit of its own region: its header, its attached comments and every line of its subtree. A table
    /// that the header of its own does not define (a dotted or implicit table), or whose region holds lines of another node, cannot move, and
    /// neither can an array of tables among its siblings. In a table the units are whole lines of text. In an array or an inline table a unit
    /// is the text of its item (its comma and its trailing comment included).
    /// </summary>
    /// <param name="model">The model of the document.</param>
    /// <param name="parent">The table or array whose children are the units.</param>
    /// <param name="text">The source text.</param>
    /// <param name="split">The index of a comment child that moves alone, out of the unit it sits in; -1 for none.</param>
    public static List<TomlUnit> Units(TomlModel model, DocNode parent, string text, int split = -1)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        IReadOnlyList<TomlItem> items = model.Syntax.Items;
        bool inline = parent.Kind == NodeKind.TomlTable && model.ShapeOf(parent) == TomlContainer.Inline;
        bool tokens = inline || (parent.Kind == NodeKind.TomlArray && model.ShapeOf(parent) == TomlContainer.Array);
        List<(int First, int Last)> groups = Groups(parent, text);
        if (split >= 0 && children[split].Kind == NodeKind.TomlComment)
        {
            groups = Split(groups, split);
        }

        var units = new List<TomlUnit>();
        Dictionary<TomlNode, DocNode>? owners = null;
        foreach ((int first, int last) in groups)
        {
            int main = first;
            while (main <= last && children[main].Kind == NodeKind.TomlComment)
            {
                main++;
            }

            if (main > last)
            {
                // A comment alone. A comment that shares its line with a value (a table's own header comment) cannot move.
                if (!EditLayout.StartsLine(text, children[first].Start))
                {
                    units.Add(new TomlUnit(TomlUnitKind.Refused, first, last, 0, 0, -1, TrailingRefusal));
                }
                else if (tokens)
                {
                    units.Add(new TomlUnit(TomlUnitKind.Comment, first, last, children[first].Start, children[last].End, -1, null));
                }
                else
                {
                    int start = EditLayout.LineStart(text, children[first].Start);
                    int end = EditLayout.AfterLineBreak(text, children[last].End);
                    units.Add(new TomlUnit(TomlUnitKind.Comment, first, last, start, end, SectionOf(items, start), null));
                }

                continue;
            }

            DocNode child = children[main];
            if (child.Kind == NodeKind.TomlTable && model.ShapeOf(child) is TomlContainer.Implicit or TomlContainer.Dotted)
            {
                units.Add(new TomlUnit(TomlUnitKind.Refused, first, last, 0, 0, -1, SpreadRefusal));
            }
            else if (child.Kind == NodeKind.TomlTable && model.ShapeOf(child) == TomlContainer.Header && !tokens)
            {
                int start = EditLayout.LineStart(text, child.Start);
                int end = EditLayout.AfterLineBreak(text, child.End);
                owners ??= Owners(model);
                bool whole = Owned(model, items, owners, child, start, end);
                units.Add(whole
                    ? new TomlUnit(TomlUnitKind.Table, first, last, start, end, -1, null)
                    : new TomlUnit(TomlUnitKind.Refused, first, last, 0, 0, -1, SpreadRefusal));
            }
            else if (child.Kind == NodeKind.TomlArray && !tokens && model.ShapeOf(child) == TomlContainer.ArrayOfTables)
            {
                units.Add(new TomlUnit(TomlUnitKind.Refused, first, last, 0, 0, -1, "an array of tables cannot move among its siblings yet"));
            }
            else if (inline)
            {
                TomlInlineMember member = MemberOf(model, parent, child);
                int end = member.Comma?.End ?? member.Value.End;
                units.Add(new TomlUnit(TomlUnitKind.Entry, first, last, member.Key.Start, end, -1, null));
            }
            else if (tokens)
            {
                int index = 0;
                for (int k = 0; k < main; k++)
                {
                    index += children[k].Kind == NodeKind.TomlComment ? 0 : 1;
                }

                TomlArrayItem item = model.ArrayItemSyntaxOf(parent, index);
                if (item.BeforeComma.Any(node => node is TomlComment))
                {
                    units.Add(new TomlUnit(TomlUnitKind.Refused, first, last, 0, 0, -1, "a comment between an item and its comma cannot move"));
                }
                else
                {
                    units.Add(new TomlUnit(TomlUnitKind.Entry, first, last, children[first].Start, ItemEnd(model, parent, last), -1, null));
                }
            }
            else
            {
                int start = EditLayout.LineStart(text, children[first].Start);
                int end = EditLayout.AfterLineBreak(text, children[last].End);
                units.Add(new TomlUnit(TomlUnitKind.Entry, first, last, start, end, SectionOf(items, start), null));
            }
        }

        return units;
    }

    /// <summary>The message for a unit that cannot move, or that a move would pass.</summary>
    public const string NeighbourRefusal = "this cannot move: its neighbour is in another part of the file";

    /// <summary>The message for a table whose lines are not one run, as a dotted, implicit or spread table is.</summary>
    public const string SpreadRefusal = "this table is spread over the file and cannot move";

    /// <summary>The message for a comment that shares its line with a value, which moves with that value.</summary>
    public const string TrailingRefusal = "a trailing comment moves with its entry";

    /// <summary>
    /// The unit that a move puts the moved unit before, as an index into the units, or the number of units for the end of the list. It is -1
    /// when the position is inside a unit (between a comment and the entry it is attached to, say). <paramref name="newIndex"/> counts the
    /// children that stay, as <see cref="MoveNode.NewIndex"/> does, and is clamped to them.
    /// </summary>
    public static int Destination(IReadOnlyList<TomlUnit> units, int moved, int newIndex, int childCount)
    {
        TomlUnit unit = units[moved];
        int size = unit.Last - unit.First + 1;
        int restCount = childCount - size;
        int target = Math.Clamp(newIndex, 0, restCount);
        if (target == unit.First)
        {
            return moved;
        }

        if (target == restCount)
        {
            return units.Count;
        }

        int childDestination = target < unit.First ? target : target + size;
        for (int k = 0; k < units.Count; k++)
        {
            if (units[k].First == childDestination)
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>The units in their order after the moved unit goes before the unit at <paramref name="destination"/> (see <see cref="Destination"/>).</summary>
    public static List<int> Order(int count, int moved, int destination)
    {
        int position = destination > moved ? destination - 1 : destination;
        var order = Enumerable.Range(0, count).Where(k => k != moved).ToList();
        order.Insert(position, moved);
        return order;
    }

    /// <summary>The first and last positions that an order changes, or (-1, -1) when it changes nothing.</summary>
    public static (int First, int Last) Span(IReadOnlyList<int> order)
    {
        int first = 0;
        while (first < order.Count && order[first] == first)
        {
            first++;
        }

        if (first == order.Count)
        {
            return (-1, -1);
        }

        int last = order.Count - 1;
        while (order[last] == last)
        {
            last--;
        }

        return (first, last);
    }

    /// <summary>The index of the item that holds an offset, or -1 before the first item.</summary>
    public static int ItemAt(TomlModel model, int offset)
    {
        IReadOnlyList<TomlItem> items = model.Syntax.Items;
        int found = -1;
        int low = 0;
        int high = items.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (items[mid].Start <= offset)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }

    /// <summary>The group of a parent's children: each child with the comments attached to it, and each comment alone (see <see cref="Block"/>).</summary>
    private static List<(int First, int Last)> Groups(DocNode parent, string text)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        var groups = new List<(int First, int Last)>();
        int count = children.Count;
        int k = 0;
        while (k < count)
        {
            int entry = k;
            while (entry < count && children[entry].Kind == NodeKind.TomlComment)
            {
                entry++;
            }

            (int first, int last) = entry < count ? Block(parent, entry, text) : (count, count - 1);
            first = Math.Max(first, k);
            for (; k < first; k++)
            {
                groups.Add((k, k));
            }

            if (entry == count)
            {
                break;
            }

            groups.Add((first, last));
            k = last + 1;
        }

        return groups;
    }

    /// <summary>A group with a comment taken out of it: the comment becomes a group alone, and the comments before and after it stay together.</summary>
    private static List<(int First, int Last)> Split(List<(int First, int Last)> groups, int split)
    {
        var result = new List<(int First, int Last)>();
        foreach ((int first, int last) in groups)
        {
            if (split < first || split > last || first == last)
            {
                result.Add((first, last));
                continue;
            }

            if (first < split)
            {
                result.Add((first, split - 1));
            }

            result.Add((split, split));
            if (split < last)
            {
                result.Add((split + 1, last));
            }
        }

        return result;
    }

    /// <summary>The item of an inline table that a child of it is: the member whose value starts where the child's value starts.</summary>
    private static TomlInlineMember MemberOf(TomlModel model, DocNode table, DocNode child)
    {
        int valueStart = (model.ValueSyntaxOf(child) ?? throw new InvalidOperationException("An inline member has no value.")).Start;
        var syntax = (TomlInlineTable)(model.ValueSyntaxOf(table) ?? throw new InvalidOperationException("An inline table has no syntax."));
        return syntax.Entries.OfType<TomlInlineMember>().First(member => member.Value.Start == valueStart);
    }

    /// <summary>The section that holds an offset: the offset of its header, or -1 for the root's own section.</summary>
    private static int SectionOf(IReadOnlyList<TomlItem> items, int offset)
    {
        int at = -1;
        int low = 0;
        int high = items.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (items[mid].Start <= offset)
            {
                at = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        for (int k = at; k >= 0; k--)
        {
            if (items[k] is TomlHeaderItem)
            {
                return items[k].Start;
            }
        }

        return -1;
    }

    /// <summary>The deepest node that each piece of text belongs to: the node that a piece of a table's lines defines or passes through.</summary>
    private static Dictionary<TomlNode, DocNode> Owners(TomlModel model)
    {
        var owners = new Dictionary<TomlNode, DocNode>();
        var stack = new Stack<DocNode>();
        stack.Push(model.Tree);
        while (stack.Count > 0)
        {
            DocNode node = stack.Pop();
            foreach (TomlNode piece in model.PiecesOf(node))
            {
                if (!owners.TryGetValue(piece, out DocNode? seen) || node.Path.Count > seen.Path.Count)
                {
                    owners[piece] = node;
                }
            }

            foreach (DocNode child in node.Children)
            {
                stack.Push(child);
            }
        }

        return owners;
    }

    /// <summary>True when every line of a region that is not blank belongs to the table, so that the region is the table's and nothing else's.</summary>
    private static bool Owned(TomlModel model, IReadOnlyList<TomlItem> items, Dictionary<TomlNode, DocNode> owners, DocNode table, int start, int end)
    {
        for (int k = Math.Max(0, ItemAt(model, start)); k < items.Count && items[k].Start < end; k++)
        {
            TomlItem item = items[k];
            if (item is TomlBlankItem)
            {
                continue;
            }

            TomlNode piece = item is TomlCommentItem comment ? comment.Comment! : item;
            if (!owners.TryGetValue(piece, out DocNode? owner) || !InSubtree(owner, table))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InSubtree(DocNode node, DocNode ancestor)
    {
        for (DocNode? n = node; n is not null; n = n.Parent)
        {
            if (ReferenceEquals(n, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>The kinds of unit that a TOML move works on (see <see cref="TomlLayout.Units"/>).</summary>
internal enum TomlUnitKind
{
    /// <summary>A key/value, a static array or inline table value, an array item or an inline member, with its attached comments.</summary>
    Entry,

    /// <summary>A comment on a line of its own, or in an array.</summary>
    Comment,

    /// <summary>A table defined by a header: its region is its header, its attached comments and every line of its subtree.</summary>
    Table,

    /// <summary>A child that cannot move, with the reason in <see cref="TomlUnit.Refusal"/>.</summary>
    Refused,
}

/// <summary>
/// A unit of a parent's children (see <see cref="TomlLayout.Units"/>): the children <see cref="First"/> to <see cref="Last"/>, the text
/// <see cref="Start"/> to <see cref="End"/>, the section it is in (the offset of its header, or -1 for the root), and why it cannot move.
/// </summary>
internal sealed record TomlUnit(TomlUnitKind Kind, int First, int Last, int Start, int End, int Section, string? Refusal);
