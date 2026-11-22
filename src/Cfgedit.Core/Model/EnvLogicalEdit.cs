using System.Globalization;
using Cfgedit.Core.Json;

namespace Cfgedit.Core.Model;

/// <summary>
/// Which .env children belong together. A comment line directly above an entry, with no blank line between, is attached to it.
/// A trailing comment (its <c>#</c> comes before the end of the entry, so it stands on the entry's line) is a comment child right
/// after its entry. The planner and the logical application both use these rules.
/// </summary>
internal static class EnvLayout
{
    /// <summary>True for a comment child that stands on the line of the entry before it.</summary>
    public static bool IsTrailing(IReadOnlyList<DocNode> children, int index)
    {
        return children[index].Kind == NodeKind.EnvComment
            && index > 0
            && children[index - 1].Kind == NodeKind.EnvEntry
            && children[index].Start < children[index - 1].End;
    }

    /// <summary>True for a comment child that has a line of its own.</summary>
    public static bool IsCommentLine(IReadOnlyList<DocNode> children, int index)
    {
        return children[index].Kind == NodeKind.EnvComment && !IsTrailing(children, index);
    }

    /// <summary>The key segments of a node's children that are entries or groups, in order. Comments have none.</summary>
    public static IEnumerable<string> Segments(DocNode parent)
    {
        return parent.Children.Select(child => child.Path.Steps[^1]).OfType<MemberStep>().Select(step => step.Key);
    }

    /// <summary>
    /// The segment a copy takes: <c>name_copy</c>, then <c>name_copy2</c> and so on, unique among the segments of the siblings. The
    /// underscore is the one .env names use, so the copy's key stays a valid name.
    /// </summary>
    public static string CopySegment(IEnumerable<string> siblings, string name)
    {
        var taken = siblings.ToHashSet(StringComparer.Ordinal);
        string candidate = name + "_copy";
        for (int n = 2; taken.Contains(candidate); n++)
        {
            candidate = name + "_copy" + n.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    /// <summary>
    /// The children that move, copy or delete with the child at <paramref name="index"/>: for an entry, the comment lines attached
    /// above it and its trailing comment. Any other child stands alone. Groups and comments never take attached comments.
    /// </summary>
    public static (int First, int Last) Block(DocNode parent, int index)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        if (children[index].Kind != NodeKind.EnvEntry)
        {
            return (index, index);
        }

        int first = index;
        while (first > 0 && IsCommentLine(children, first - 1) && children[first - 1].End == children[first].Start)
        {
            first--;
        }

        int last = index;
        while (last + 1 < children.Count && IsTrailing(children, last + 1))
        {
            last++;
        }

        return (first, last);
    }
}

/// <summary>
/// The independent logical application of an edit to a .env document. The logical tree of a .env file is flat: one string scalar
/// per entry, named by its full key, a comment after an entry for its trailing comment, and one comment per comment line, all in
/// file order. Each entry and each comment of the document tree is one flat item, and the flat order is the file order. So a node's
/// flat index is the number of such nodes that start before it, and an insert goes at the number of nodes that start before the
/// offset where its line goes. The planner is not used: this computes what the text should hold from the tree alone.
/// </summary>
internal static class EnvLogicalEdit
{
    private const string Separator = "__";

    /// <summary>Applies an operation to the flat logical tree of a .env document.</summary>
    /// <param name="before">The document the operation applies to.</param>
    /// <param name="root">The logical tree of <paramref name="before"/>.</param>
    /// <param name="op">The operation.</param>
    /// <exception cref="InvalidOperationException">The operation cannot be applied to the tree, or it is not an operation .env has.</exception>
    public static LogicalNode Apply(ConfigDocument before, LogicalNode root, EditOp op)
    {
        return op switch
        {
            SetPart set => SetPartAt(before, root, set),
            InsertNode insert => InsertAt(before, root, insert),
            DeleteNode delete => DeleteAt(before, root, delete.Node),
            MoveNode move => MoveAt(before, root, move),
            DuplicateNode duplicate => DuplicateAt(before, root, duplicate.Node),
            _ => throw new InvalidOperationException("The .env edit " + op.GetType().Name + " is not applied to the logical tree."),
        };
    }

    /// <summary>
    /// The flat index of the item an <see cref="InsertNode"/> creates in the document before the edit: the number of entries and
    /// comments that start before the offset where the new line goes. The offset follows the layout rules in <see cref="EnvLayout"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The parent or the index does not exist.</exception>
    public static int InsertPosition(ConfigDocument before, InsertNode insert)
    {
        DocNode parent = Locate(before, insert.Parent);
        IReadOnlyList<DocNode> children = parent.Children;
        int index = insert.Index;
        if (index < 0 || index > children.Count)
        {
            throw new InvalidOperationException($"Index {index} is outside the children of {insert.Parent}.");
        }

        int offset;
        if (index < children.Count)
        {
            offset = EnvLayout.IsTrailing(children, index)
                ? children[index - 1].End
                : children[EnvLayout.Block(parent, index).First].Start;
        }
        else if (children.Count == 0)
        {
            offset = before.Source.Text.Length;
        }
        else
        {
            int last = EnvLayout.Block(parent, children.Count - 1).Last;
            offset = EnvLayout.IsTrailing(children, last) ? children[last - 1].End : children[last].End;
        }

        return FlatNodes(before).Count(node => node.Start < offset);
    }

    /// <summary>
    /// The focus after an operation on a .env document: the node that the edit put at the same place. An insert focuses the new
    /// item. A set focuses the node at the same index of the same parent. A delete focuses the next sibling, else the previous one,
    /// else the parent, or the nearest ancestor that still exists when the parent is gone.
    /// </summary>
    /// <returns>The path of the node to focus, or null when there is none.</returns>
    public static NodePath? FocusAfter(ConfigDocument before, ConfigDocument after, EditOp op)
    {
        switch (op)
        {
            case InsertNode insert:
            {
                List<DocNode> flat = FlatNodes(after);
                int position = InsertPosition(before, insert);
                return position < flat.Count ? flat[position].Path : null;
            }
            case SetPart set:
            {
                DocNode node = Locate(before, set.Node);
                DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node has no parent.");
                DocNode? survivor = after.Resolve(parent.Path);
                int index = IndexOfChild(parent, node);
                return survivor is not null && index < survivor.Children.Count ? survivor.Children[index].Path : null;
            }
            case DeleteNode delete:
                return FocusAfterDelete(before, after, delete.Node);
            case MoveNode move:
            {
                Placement placement = PlaceMove(before, move.Node, move.NewIndex);
                return FocusAfterPlacement(before, after, move.Node, placement);
            }
            case DuplicateNode duplicate:
                return FocusAfterPlacement(before, after, duplicate.Node, PlaceCopy(before, duplicate.Node));
            default:
                return null;
        }
    }

    /// <summary>
    /// The focus after a move or a duplicate: the node itself, in the new document. Its flat position is the place where its unit
    /// starts, plus the number of unit items before it (its attached comments). A group is found from its first item, by going up to
    /// its child of the parent.
    /// </summary>
    private static NodePath? FocusAfterPlacement(ConfigDocument before, ConfigDocument after, NodePath path, Placement placement)
    {
        DocNode node = Locate(before, path);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node has no parent.");
        Dictionary<DocNode, int> positions = PositionsOf(FlatNodes(before));
        int own = node.Kind is NodeKind.EnvEntry or NodeKind.EnvComment ? placement.Positions.IndexOf(PositionOf(positions, node)) : 0;
        DocNode? survivor = after.Resolve(parent.Path);
        List<DocNode> flat = FlatNodes(after);
        int index = placement.Destination + own;
        if (survivor is null || index < 0 || index >= flat.Count)
        {
            return null;
        }

        DocNode focused = flat[index];
        if (node.Kind != NodeKind.EnvGroup)
        {
            return focused.Path;
        }

        while (focused.Parent is not null && !ReferenceEquals(focused.Parent, survivor))
        {
            focused = focused.Parent;
        }

        return focused.Path;
    }

    /// <summary>
    /// Where a unit goes: its flat positions in the document before the edit, and the position it takes among the other nodes once
    /// the unit is taken out. The destination is the flat count of the other nodes that start before the offset where the unit's
    /// first line goes, which is the first line of the sibling the index names (or the end of the last sibling).
    /// </summary>
    private sealed record Placement(List<int> Positions, int Destination);

    /// <summary>The placement of a move: the unit is the node's sibling group, and the index counts the siblings without it.</summary>
    private static Placement PlaceMove(ConfigDocument before, NodePath path, int newIndex)
    {
        DocNode node = Locate(before, path);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be moved.");
        int index = IndexOfChild(parent, node);
        (int first, int last) = EditLayout.Group(parent, index, before.Source.Text);
        List<DocNode> flat = FlatNodes(before);
        Dictionary<DocNode, int> positions = PositionsOf(flat);
        List<int> unit = UnitPositions(parent, first, last, positions);
        var moved = new HashSet<int>(unit);
        List<DocNode> rest = parent.Children.Where((_, k) => k < first || k > last).ToList();
        int target = Math.Clamp(newIndex, 0, rest.Count);
        int offset = target < rest.Count ? rest[target].Start : rest.Count == 0 ? 0 : rest.Max(child => child.End);
        int destination = 0;
        for (int p = 0; p < flat.Count; p++)
        {
            if (!moved.Contains(p) && flat[p].Start < offset)
            {
                destination++;
            }
        }

        return new Placement(unit, destination);
    }

    /// <summary>The placement of a copy: the copy goes right after the unit, so its first flat position follows the unit's last.</summary>
    private static Placement PlaceCopy(ConfigDocument before, NodePath path)
    {
        DocNode node = Locate(before, path);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be duplicated.");
        (int first, int last) = EditLayout.Group(parent, IndexOfChild(parent, node), before.Source.Text);
        List<int> unit = UnitPositions(parent, first, last, PositionsOf(FlatNodes(before)));
        return new Placement(unit, unit[^1] + 1);
    }

    /// <summary>The flat positions of the entries and comments under the children [first, last] of a parent, in file order.</summary>
    private static List<int> UnitPositions(DocNode parent, int first, int last, Dictionary<DocNode, int> positions)
    {
        var result = new List<int>();
        for (int k = first; k <= last; k++)
        {
            foreach (DocNode item in Descendants(parent.Children[k]).Where(item => item.Kind is NodeKind.EnvEntry or NodeKind.EnvComment))
            {
                result.Add(PositionOf(positions, item));
            }
        }

        result.Sort();
        return result;
    }

    private static NodePath? FocusAfterDelete(ConfigDocument before, ConfigDocument after, NodePath path)
    {
        DocNode node = Locate(before, path);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node has no parent.");
        int index = IndexOfChild(parent, node);
        (int first, int last) = EnvLayout.Block(parent, index);
        int remaining = parent.Children.Count - (last - first + 1);

        NodePath parentPath = parent.Path;
        while (after.Resolve(parentPath) is null && parentPath.Count > 1)
        {
            parentPath = parentPath.Prefix(parentPath.Count - 1);
        }

        DocNode? survivor = after.Resolve(parentPath);
        if (survivor is null)
        {
            return null;
        }

        if (parentPath != parent.Path)
        {
            return survivor.Path;
        }

        if (first < remaining)
        {
            return survivor.Children[first].Path;
        }

        return first > 0 ? survivor.Children[first - 1].Path : survivor.Path;
    }

    // ----- Set, insert and delete on the flat tree -----

    private static LogicalNode SetPartAt(ConfigDocument before, LogicalNode root, SetPart set)
    {
        DocNode target = Locate(before, set.Node);
        List<DocNode> flat = FlatNodes(before);
        Dictionary<DocNode, int> positions = PositionsOf(flat);
        List<LogicalNode> children = ChildrenOf(root, flat.Count);

        switch (set.Part)
        {
            case PartKind.Value when target.Kind == NodeKind.EnvEntry:
            {
                int i = PositionOf(positions, target);
                children[i] = new LogicalNode(LogicalKind.Scalar, children[i].Name, JsonValueKind.String, set.Value, [], []);
                break;
            }
            case PartKind.Key when target.Kind == NodeKind.EnvEntry:
            {
                // The full key ends with the last segment, so only that suffix changes.
                string oldSegment = SegmentOf(target);
                int i = PositionOf(positions, target);
                string name = children[i].Name ?? throw new InvalidOperationException("An entry has no key.");
                children[i] = Renamed(children[i], name[..^oldSegment.Length] + set.Value);
                break;
            }
            case PartKind.Key when target.Kind == NodeKind.EnvGroup:
            {
                // Every entry under the group has the segment at the group's depth, at the same offset in its key.
                string oldSegment = SegmentOf(target);
                int offset = PrefixLength(target.Path);
                foreach (DocNode entry in Descendants(target).Where(node => node.Kind == NodeKind.EnvEntry))
                {
                    int i = PositionOf(positions, entry);
                    string name = children[i].Name ?? throw new InvalidOperationException("An entry has no key.");
                    children[i] = Renamed(children[i], name[..offset] + set.Value + name[(offset + oldSegment.Length)..]);
                }

                break;
            }
            case PartKind.Comment when target.Kind == NodeKind.EnvComment:
            {
                int i = PositionOf(positions, target);
                children[i] = new LogicalNode(LogicalKind.Comment, null, null, set.Value, [], []);
                break;
            }
            default:
                throw new InvalidOperationException("The part " + set.Part + " cannot be set in .env.");
        }

        return WithChildren(root, children);
    }

    private static LogicalNode InsertAt(ConfigDocument before, LogicalNode root, InsertNode insert)
    {
        DocNode parent = Locate(before, insert.Parent);
        if (parent.Kind != NodeKind.EnvGroup)
        {
            throw new InvalidOperationException("The node " + insert.Parent + " cannot have children.");
        }

        LogicalNode created = insert.Node switch
        {
            NewEnvEntry entry => new LogicalNode(
                LogicalKind.Scalar,
                string.Join(Separator, NamesOf(parent.Path).Append(entry.Key)),
                JsonValueKind.String,
                entry.Value,
                [],
                []),
            NewEnvComment comment => new LogicalNode(LogicalKind.Comment, null, null, comment.Text, [], []),
            _ => throw new InvalidOperationException("The node " + insert.Node.GetType().Name + " cannot be inserted into .env."),
        };

        List<LogicalNode> children = ChildrenOf(root, FlatNodes(before).Count);
        children.Insert(InsertPosition(before, insert), created);
        return WithChildren(root, children);
    }

    private static LogicalNode DeleteAt(ConfigDocument before, LogicalNode root, NodePath path)
    {
        DocNode node = Locate(before, path);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be deleted.");
        if (parent.Kind == NodeKind.Document)
        {
            throw new InvalidOperationException("The root of a .env document cannot be deleted.");
        }

        int index = IndexOfChild(parent, node);
        (int first, int last) = EnvLayout.Block(parent, index);
        List<DocNode> flat = FlatNodes(before);
        Dictionary<DocNode, int> positions = PositionsOf(flat);
        var removed = new HashSet<int>();
        for (int k = first; k <= last; k++)
        {
            foreach (DocNode gone in Descendants(parent.Children[k]).Where(item => item.Kind is NodeKind.EnvEntry or NodeKind.EnvComment))
            {
                removed.Add(PositionOf(positions, gone));
            }
        }

        List<LogicalNode> children = ChildrenOf(root, flat.Count);
        return WithChildren(root, children.Where((_, k) => !removed.Contains(k)).ToList());
    }

    private static LogicalNode MoveAt(ConfigDocument before, LogicalNode root, MoveNode move)
    {
        Placement placement = PlaceMove(before, move.Node, move.NewIndex);
        List<LogicalNode> children = ChildrenOf(root, FlatNodes(before).Count);
        var moved = new HashSet<int>(placement.Positions);
        List<LogicalNode> unit = placement.Positions.Select(p => children[p]).ToList();
        List<LogicalNode> rest = children.Where((_, p) => !moved.Contains(p)).ToList();
        rest.InsertRange(placement.Destination, unit);
        return WithChildren(root, rest);
    }

    /// <summary>
    /// A duplicate copies the unit after itself. An entry's copy gets the new last segment (or, for a group, the new segment at the
    /// group's depth in every key under it), and a comment is copied as it is.
    /// </summary>
    private static LogicalNode DuplicateAt(ConfigDocument before, LogicalNode root, NodePath path)
    {
        DocNode node = Locate(before, path);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be duplicated.");
        Placement placement = PlaceCopy(before, path);
        List<DocNode> flat = FlatNodes(before);
        List<LogicalNode> children = ChildrenOf(root, flat.Count);
        string? fresh = node.Kind is NodeKind.EnvEntry or NodeKind.EnvGroup
            ? EnvLayout.CopySegment(EnvLayout.Segments(parent), SegmentOf(node))
            : null;
        int offset = PrefixLength(node.Path);
        string old = fresh is null ? string.Empty : SegmentOf(node);
        var copies = new List<LogicalNode>();
        foreach (int p in placement.Positions)
        {
            LogicalNode copy = children[p];
            if (fresh is not null && copy.Kind == LogicalKind.Scalar)
            {
                string name = copy.Name ?? throw new InvalidOperationException("An entry has no key.");
                copy = Renamed(copy, name[..offset] + fresh + name[(offset + old.Length)..]);
            }

            copies.Add(copy);
        }

        children.InsertRange(placement.Destination, copies);
        return WithChildren(root, children);
    }

    // ----- Tree helpers -----

    /// <summary>Every entry and comment of the document, in file order.</summary>
    public static List<DocNode> FlatNodes(ConfigDocument document)
    {
        var nodes = Descendants(document.Root).Where(node => node.Kind is NodeKind.EnvEntry or NodeKind.EnvComment).ToList();
        nodes.Sort((a, b) => a.Start.CompareTo(b.Start));
        return nodes;
    }

    /// <summary>The node and all its descendants, in no particular order.</summary>
    private static List<DocNode> Descendants(DocNode node)
    {
        var all = new List<DocNode>();
        var pending = new Stack<DocNode>();
        pending.Push(node);
        while (pending.Count > 0)
        {
            DocNode current = pending.Pop();
            all.Add(current);
            foreach (DocNode child in current.Children)
            {
                pending.Push(child);
            }
        }

        return all;
    }

    private static Dictionary<DocNode, int> PositionsOf(List<DocNode> flat)
    {
        var positions = new Dictionary<DocNode, int>(ReferenceEqualityComparer.Instance);
        for (int k = 0; k < flat.Count; k++)
        {
            positions[flat[k]] = k;
        }

        return positions;
    }

    private static int PositionOf(Dictionary<DocNode, int> positions, DocNode node)
    {
        return positions.TryGetValue(node, out int position) ? position : throw new InvalidOperationException("The node is not a .env item.");
    }

    private static List<LogicalNode> ChildrenOf(LogicalNode root, int expected)
    {
        if (root.Children.Count != expected)
        {
            throw new InvalidOperationException("The .env logical tree does not match the document.");
        }

        return root.Children.ToList();
    }

    private static LogicalNode WithChildren(LogicalNode root, List<LogicalNode> children)
    {
        return new LogicalNode(root.Kind, root.Name, root.ScalarKind, root.Value, root.Attributes, children);
    }

    private static LogicalNode Renamed(LogicalNode node, string name)
    {
        return new LogicalNode(node.Kind, name, node.ScalarKind, node.Value, node.Attributes, node.Children);
    }

    private static DocNode Locate(ConfigDocument document, NodePath path)
    {
        return document.Resolve(path) ?? throw new InvalidOperationException($"There is no node at {path}.");
    }

    private static int IndexOfChild(DocNode parent, DocNode child)
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

    /// <summary>The key segment a path names: the last member step of the path.</summary>
    private static string SegmentOf(DocNode node)
    {
        return node.Path.Steps[^1] is MemberStep step ? step.Key : throw new InvalidOperationException("The node has no key.");
    }

    /// <summary>The length of the key text before the segment that a group's path names, separators included.</summary>
    private static int PrefixLength(NodePath path)
    {
        return path.Steps.Take(path.Count - 1).OfType<MemberStep>().Sum(step => step.Key.Length + Separator.Length);
    }

    private static List<string> NamesOf(NodePath path)
    {
        return path.Steps.OfType<MemberStep>().Select(step => step.Key).ToList();
    }
}
