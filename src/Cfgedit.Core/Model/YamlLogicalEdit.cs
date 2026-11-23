using Cfgedit.Core.Yaml;

namespace Cfgedit.Core.Model;

/// <summary>
/// The independent logical application of a YAML edit: the logical tree after the operation, computed from the tree before it. The data
/// (mappings as objects, sequences as arrays, scalars with their kind, plain flag, anchor and tag) is changed at the logical position of the
/// node, which the document tree gives. The <c>#comments</c> array is changed by the rank of each comment in file order. A deleted node takes
/// the comments that it owns: the comments in its layout group and those nested in it, and an emptied collection those in its span. The
/// style of a written scalar comes from the same rules as the planner's (<see cref="YamlEditPlanner.StringToken"/>), so the two agree.
/// </summary>
internal static class YamlLogicalEdit
{
    /// <summary>Applies an operation to a logical tree. Throws <see cref="InvalidOperationException"/> when it cannot be applied.</summary>
    /// <param name="before">The document the operation applies to. Its tree gives the paths their meaning.</param>
    /// <param name="root">The logical tree of <paramref name="before"/>: the data, then the <c>#comments</c> array.</param>
    /// <param name="op">The operation. It is assumed valid.</param>
    public static LogicalNode Apply(ConfigDocument before, LogicalNode root, EditOp op)
    {
        var model = (YamlModel)before.Syntax;
        LogicalNode data = root.Children[0];
        IReadOnlyList<LogicalNode> comments = root.Children[1].Children;
        var removed = new HashSet<int>();

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
                data = Modify(data, IndicesOf(node), 0, n => SetScalar(before, node, n, value.Value)) ?? data;
                break;
            }

            case SetPart { Part: PartKind.Comment } comment:
            {
                DocNode node = Locate(before, comment.Node);
                var replaced = comments.ToList();
                replaced[RankOf(model, node)] = new LogicalNode(LogicalKind.Comment, null, null, comment.Value, [], []);
                return Rebuild(root, data, replaced);
            }

            case ChangeYamlType change:
            {
                DocNode node = Locate(before, change.Node);
                if (node.Kind is NodeKind.YamlMapping or NodeKind.YamlSequence)
                {
                    YamlValueNode value = YamlEditPlanner.ValueOf(model.SyntaxOf(node)) ?? throw new InvalidOperationException("The collection has no syntax.");
                    bool mapping = value is YamlBlockMapping or YamlFlowMapping;
                    bool same = change.Kind == YamlNewKind.Mapping ? mapping : change.Kind == YamlNewKind.Sequence && !mapping;
                    if (same)
                    {
                        break;
                    }

                    (int start, int end) = YamlEditPlanner.CollectionSpan(before.Source.Text, value, YamlEditPlanner.OwnerOf(model.SyntaxOf(node), before.Source.Text));
                    AddRanks(model, removed, start, end);
                }

                data = Modify(data, IndicesOf(node), 0, n => ChangedType(before, node, n, change)) ?? data;
                break;
            }

            case DeleteNode delete:
                return DeleteAt(before, root, data, comments, delete.Node);

            case InsertNode insert:
                return InsertAt(before, root, data, comments, insert);

            case DuplicateNode duplicate:
                return DuplicateAt(before, root, data, comments, duplicate.Node);

            case MoveNode move:
                return MoveAt(before, root, data, comments, move);

            default:
                throw new InvalidOperationException("The operation " + op.GetType().Name + " is not supported for YAML.");
        }

        return Rebuild(root, data, WithoutRanks(comments, removed));
    }

    /// <summary>The focus after an operation: the node the operation changed, or after a delete the neighbour that takes its place.</summary>
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

            case ChangeYamlType change:
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
                var model = (YamlModel)before.Syntax;
                DocNode parent = Locate(before, insert.Parent);
                if (insert.Node is NewYamlComment)
                {
                    return CommentPathAt(after, CommentRankOf(model, before.Source.Text, parent, insert.Index));
                }

                var value = (NewYamlValue)insert.Node;
                NodePath path = value.Key is null
                    ? parent.Path.Append(new ItemStep(EntriesBefore(parent, insert.Index)))
                    : parent.Path.Append(new MemberStep(value.Key, 0));
                return after.Resolve(path) is null ? null : path;
            }

            case DuplicateNode duplicate:
            {
                var model = (YamlModel)before.Syntax;
                DocNode node = Locate(before, duplicate.Node);
                DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node has no parent.");
                YamlEditPlanner.CopyRange range = YamlEditPlanner.CopySpan(before.Source.Text, model, parent, node);
                if (node.Kind == NodeKind.YamlComment)
                {
                    return CommentPathAt(after, AllComments(model).Count(c => c.Start < range.At));
                }

                NodePath path = parent.Kind == NodeKind.YamlMapping
                    ? parent.Path.Append(new MemberStep(CopyNameOf(parent, node), 0))
                    : parent.Path.Append(new ItemStep(CountEntries(parent.Children, node) + 1));
                return after.Resolve(path) is null ? null : path;
            }

            case MoveNode move:
            {
                var model = (YamlModel)before.Syntax;
                MovePlan plan = PlanMove(before, move);
                if (plan.Node.Kind == NodeKind.YamlComment)
                {
                    return CommentPathAt(after, plan.RankLo + plan.RankOrder.IndexOf(RankOf(model, plan.Node)));
                }

                NodePath path = plan.Parent.Kind == NodeKind.YamlMapping
                    ? plan.Parent.Path.Append(new MemberStep(((MemberStep)plan.Node.Path.Steps[^1]).Key, 0))
                    : plan.Parent.Path.Append(new ItemStep(plan.EntryOrder.IndexOf(CountEntries(plan.Parent.Children, plan.Node))));
                return after.Resolve(path) is null ? null : path;
            }

            default:
                return null;
        }
    }

    // ----- Move -----

    /// <summary>
    /// Where a move puts things. The entries of the parent are in <see cref="EntryOrder"/> (old entry indices, in their new order). The
    /// comments of the parent's units are one run of ranks from <see cref="RankLo"/>, and <see cref="RankOrder"/> is that run in its new
    /// order: the comments move with their units, and nothing outside the run moves.
    /// </summary>
    private sealed record MovePlan(DocNode Parent, DocNode Node, List<int> EntryOrder, int RankLo, List<int> RankOrder);

    /// <summary>
    /// The units of the moved node's parent (see <see cref="YamlMoves.Units"/>) and the order they take. The units come from the tree and its
    /// text, as the planner's do, and the logical order is derived from them without the planner's edits.
    /// </summary>
    private static MovePlan PlanMove(ConfigDocument before, MoveNode move)
    {
        var model = (YamlModel)before.Syntax;
        DocNode node = Locate(before, move.Node);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be moved.");
        IReadOnlyList<DocNode> children = parent.Children;
        int index = IndexOf(parent, node);
        List<YamlUnit> units = YamlMoves.Units(model, parent, before.Source.Text, node.Kind == NodeKind.YamlComment ? index : -1);
        int moved = YamlMoves.UnitOf(units, index);
        int destination = YamlMoves.Destination(units, moved, move.NewIndex, children.Count);
        if (destination < 0)
        {
            throw new InvalidOperationException("The move would split a unit.");
        }

        List<int> order = YamlMoves.Order(units.Count, moved, destination);

        var entryIndex = new int[children.Count];
        int entries = 0;
        for (int k = 0; k < children.Count; k++)
        {
            if (!EditLayout.IsComment(children[k]))
            {
                entryIndex[k] = entries++;
            }
        }

        var entryOrder = new List<int>();
        foreach (int u in order)
        {
            for (int k = units[u].First; k <= units[u].Last; k++)
            {
                if (!EditLayout.IsComment(children[k]))
                {
                    entryOrder.Add(entryIndex[k]);
                }
            }
        }

        List<YamlComment> all = AllComments(model);
        var unitRanks = units.Select(unit => RanksIn(all, children[unit.First].Start, children[unit.Last].End)).ToList();
        List<int> oldRanks = unitRanks.SelectMany(ranks => ranks).ToList();
        int rankLo = oldRanks.Count > 0 ? oldRanks[0] : 0;
        for (int i = 0; i < oldRanks.Count; i++)
        {
            if (oldRanks[i] != rankLo + i)
            {
                throw new InvalidOperationException("The comments of a move are not one run.");
            }
        }

        List<int> rankOrder = order.SelectMany(u => unitRanks[u]).ToList();
        return new MovePlan(parent, node, entryOrder, rankLo, rankOrder);
    }

    /// <summary>The ranks of the comments that start in [from, to), in file order.</summary>
    private static List<int> RanksIn(List<YamlComment> all, int from, int to)
    {
        var ranks = new List<int>();
        for (int r = 0; r < all.Count; r++)
        {
            if (all[r].Start >= from && all[r].Start < to)
            {
                ranks.Add(r);
            }
        }

        return ranks;
    }

    /// <summary>
    /// A moved entry or item is reordered among its siblings in the data; a moved comment changes only the order of the comments. The comments
    /// of the moved units keep their text and move with them in the #comments order.
    /// </summary>
    private static LogicalNode MoveAt(ConfigDocument before, LogicalNode root, LogicalNode data, IReadOnlyList<LogicalNode> comments, MoveNode move)
    {
        MovePlan plan = PlanMove(before, move);
        if (plan.Node.Kind != NodeKind.YamlComment)
        {
            IReadOnlyList<LogicalNode> Reorder(IReadOnlyList<LogicalNode> entries) => plan.EntryOrder.Select(k => entries[k]).ToList();
            data = plan.Parent.Kind == NodeKind.Document
                ? WithChildren(data, Reorder(data.Children))
                : Modify(data, IndicesOf(plan.Parent), 0, n => WithChildren(n, Reorder(n.Children))) ?? data;
        }

        var permuted = comments.ToList();
        for (int i = 0; i < plan.RankOrder.Count; i++)
        {
            permuted[plan.RankLo + i] = comments[plan.RankOrder[i]];
        }

        return Rebuild(root, data, permuted);
    }

    // ----- Insert and duplicate -----

    /// <summary>
    /// A new entry or item: its data goes to the position among the entries that the index gives (comments are not entries). Its layout is the
    /// planner's, which does not change the data. A new comment takes the rank that its line has in the file.
    /// </summary>
    private static LogicalNode InsertAt(ConfigDocument before, LogicalNode root, LogicalNode data, IReadOnlyList<LogicalNode> comments, InsertNode insert)
    {
        var model = (YamlModel)before.Syntax;
        DocNode parent = Locate(before, insert.Parent);
        if (insert.Node is NewYamlComment comment)
        {
            var added = comments.ToList();
            added.Insert(CommentRankOf(model, before.Source.Text, parent, insert.Index), new LogicalNode(LogicalKind.Comment, null, null, comment.Text, [], []));
            return Rebuild(root, data, added);
        }

        var value = (NewYamlValue)insert.Node;
        LogicalNode child = NewLogical(value.Key, value.Kind, value.Value);
        int position = EntriesBefore(parent, insert.Index);
        LogicalNode next = Modify(data, IndicesOf(parent), 0, n => WithChildAt(n, position, child)) ?? data;
        return Rebuild(root, next, comments);
    }

    /// <summary>
    /// A copy of a node, right after the node: its data is the node's data with the anchors removed (a copy would define them twice), renamed
    /// when it is a mapping entry. The comments in the copied span are copied after the originals, at the rank of the copy's offset.
    /// </summary>
    private static LogicalNode DuplicateAt(ConfigDocument before, LogicalNode root, LogicalNode data, IReadOnlyList<LogicalNode> comments, NodePath path)
    {
        var model = (YamlModel)before.Syntax;
        DocNode node = Locate(before, path);
        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be duplicated.");
        YamlEditPlanner.CopyRange range = YamlEditPlanner.CopySpan(before.Source.Text, model, parent, node);
        var next = comments.ToList();
        next.InsertRange(
            AllComments(model).Count(c => c.Start < range.At),
            AllComments(model)
                .Where(c => c.Start >= range.From && c.Start < range.To)
                .Select(c => new LogicalNode(LogicalKind.Comment, null, null, c.Content, [], [])));
        if (node.Kind == NodeKind.YamlComment)
        {
            return Rebuild(root, data, next);
        }

        LogicalNode copy = Anchorless(Get(data, IndicesOf(node)));
        if (parent.Kind == NodeKind.YamlMapping)
        {
            copy = Renamed(copy, CopyNameOf(parent, node));
        }

        int position = CountEntries(parent.Children, node) + 1;
        LogicalNode updated = Modify(data, IndicesOf(parent), 0, n => WithChildAt(n, position, copy)) ?? data;
        return Rebuild(root, updated, next);
    }

    /// <summary>The logical child a new value is: a scalar with the text the planner writes, or an empty collection.</summary>
    private static LogicalNode NewLogical(string? name, YamlNewKind kind, string? value)
    {
        switch (kind)
        {
            case YamlNewKind.String:
            {
                string text = value ?? string.Empty;
                return Scalar(name, text, PlainText(YamlEditPlanner.StringToken(text, null, 0, false, "\n")), null, null);
            }

            case YamlNewKind.Number:
                return Scalar(name, value ?? "0", true, null, null);

            case YamlNewKind.Boolean:
                return Scalar(name, value ?? "false", true, null, null);

            case YamlNewKind.Null:
                return Scalar(name, "null", true, null, null);

            case YamlNewKind.Mapping:
                return new LogicalNode(LogicalKind.Object, name, null, null, [], []);

            default:
                return new LogicalNode(LogicalKind.Array, name, null, null, [], []);
        }
    }

    /// <summary>The number of entries (not comments) before an index of the children: the logical position of a new child there.</summary>
    private static int EntriesBefore(DocNode parent, int index)
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

    /// <summary>The rank a new comment takes: the number of comments that start before the line the planner writes it on.</summary>
    private static int CommentRankOf(YamlModel model, string text, DocNode parent, int index)
    {
        int offset = IsFlowParent(model, parent)
            ? YamlEditPlanner.FlowCommentLine(text, YamlEditPlanner.ValueOf(model.SyntaxOf(parent)) ?? throw new InvalidOperationException("The collection has no syntax."), parent.Children, index).Offset
            : YamlEditPlanner.BlockPlace(model, text, parent, index).Offset;
        return AllComments(model).Count(c => c.Start < offset);
    }

    private static bool IsFlowParent(YamlModel model, DocNode parent) => YamlEditPlanner.ValueOf(model.SyntaxOf(parent)) is YamlFlowMapping or YamlFlowSequence;

    /// <summary>The name of a copy of a mapping entry: the first copy name that no sibling has.</summary>
    private static string CopyNameOf(DocNode parent, DocNode node)
    {
        string name = ((MemberStep)node.Path.Steps[^1]).Key;
        var taken = parent.Children
            .Where(child => child.Path.Steps[^1] is MemberStep)
            .Select(child => ((MemberStep)child.Path.Steps[^1]).Key)
            .ToHashSet(StringComparer.Ordinal);
        return EditLayout.CopyName(name, taken.Contains);
    }

    /// <summary>The logical node with its anchors removed, at every depth: the copy of a node in a duplicate has none.</summary>
    private static LogicalNode Anchorless(LogicalNode node)
    {
        return new LogicalNode(
            node.Kind,
            node.Name,
            node.ScalarKind,
            node.Value,
            node.Attributes,
            node.Children.Select(Anchorless).ToList(),
            node.AnyValue,
            node.TomlKind,
            node.YamlKind,
            node.YamlPlain,
            null,
            node.YamlTag);
    }

    /// <summary>The logical node at a path of child indices.</summary>
    private static LogicalNode Get(LogicalNode root, IReadOnlyList<int> indices)
    {
        LogicalNode node = root;
        foreach (int k in indices)
        {
            node = node.Children[k];
        }

        return node;
    }

    private static LogicalNode WithChildAt(LogicalNode node, int position, LogicalNode child)
    {
        var children = node.Children.ToList();
        children.Insert(position, child);
        return WithChildren(node, children);
    }

    /// <summary>The path of the comment at a rank in file order, or null when there is none.</summary>
    private static NodePath? CommentPathAt(ConfigDocument document, int rank)
    {
        var comments = new List<DocNode>();
        CollectComments(document.Root, comments);
        comments.Sort((a, b) => a.Start.CompareTo(b.Start));
        return rank < comments.Count ? comments[rank].Path : null;
    }

    private static void CollectComments(DocNode node, List<DocNode> comments)
    {
        if (node.Kind == NodeKind.YamlComment)
        {
            comments.Add(node);
        }

        foreach (DocNode child in node.Children)
        {
            CollectComments(child, comments);
        }
    }

    // ----- Delete -----

    private static LogicalNode DeleteAt(ConfigDocument before, LogicalNode root, LogicalNode data, IReadOnlyList<LogicalNode> comments, NodePath path)
    {
        var model = (YamlModel)before.Syntax;
        DocNode node = Locate(before, path);
        var removed = new HashSet<int>();
        if (node.Kind == NodeKind.YamlComment)
        {
            removed.Add(RankOf(model, node));
            return Rebuild(root, data, WithoutRanks(comments, removed));
        }

        if (node.Kind == NodeKind.YamlDocument)
        {
            AddRanks(model, removed, node.Start, node.End);
            int index = CountEntries(node.Parent!.Children, node);
            var documents = data.Children.ToList();
            documents.RemoveAt(index);
            // One document left is the stream's root, not an array of one.
            LogicalNode next = documents.Count == 1 ? documents[0] : WithChildren(data, documents);
            return Rebuild(root, next, WithoutRanks(comments, removed));
        }

        DocNode parent = node.Parent ?? throw new InvalidOperationException("The document node cannot be deleted.");
        IReadOnlyList<DocNode> children = parent.Children;
        int position = IndexOf(parent, node);
        (int first, int last) = EditLayout.Group(parent, position, before.Source.Text);
        if (EntryCount(parent) == 1 || (YamlEditPlanner.ValueOf(model.SyntaxOf(parent)) is YamlFlowMapping or YamlFlowSequence && AllEntriesIn(parent, first, last)))
        {
            YamlValueNode container = YamlEditPlanner.ValueOf(model.SyntaxOf(parent)) ?? throw new InvalidOperationException("The collection has no syntax.");
            if (container is YamlBlockMapping or YamlBlockSequence)
            {
                (int start, int end) = YamlEditPlanner.CollectionSpan(
                    before.Source.Text, container, YamlEditPlanner.OwnerOf(model.SyntaxOf(parent), before.Source.Text));
                AddRanks(model, removed, start, end);
            }
            else
            {
                (int open, int close) = BracketsOf(container);
                AddRanks(model, removed, open + 1, close);
            }

            LogicalNode emptied = Modify(data, IndicesOf(parent), 0, n => WithChildren(n, [])) ?? data;
            return Rebuild(root, emptied, WithoutRanks(comments, removed));
        }

        for (int k = first; k <= last; k++)
        {
            if (EditLayout.IsComment(children[k]))
            {
                removed.Add(RankOf(model, children[k]));
            }
        }

        foreach (YamlComment comment in YamlDocuments.CommentsIn(model.SyntaxOf(node) ?? throw new InvalidOperationException("The entry has no syntax.")))
        {
            removed.Add(RankOf(model, comment));
        }

        LogicalNode changed = Modify(data, IndicesOf(node), 0, _ => null) ?? data;
        return Rebuild(root, changed, WithoutRanks(comments, removed));
    }

    /// <summary>True when the entries from <paramref name="first"/> to <paramref name="last"/> are all the entries of the parent.</summary>
    private static bool AllEntriesIn(DocNode parent, int first, int last)
    {
        int removed = 0;
        for (int k = first; k <= last; k++)
        {
            if (!EditLayout.IsComment(parent.Children[k]))
            {
                removed++;
            }
        }

        return removed == EntryCount(parent);
    }

    // ----- Values -----

    /// <summary>The state a scalar's logical node gives: whether it is an empty plain null, the core kind its text keeps, and its tag.</summary>
    private static (bool Empty, YamlCoreKind? Typed, bool AllowsInteger) TypingOf(LogicalNode old)
    {
        string? tag = old.YamlTag;
        if (tag is not null && tag.StartsWith(YamlTags.CorePrefix, StringComparison.Ordinal))
        {
            return tag[YamlTags.CorePrefix.Length..] switch
            {
                "null" => (false, YamlCoreKind.Null, false),
                "bool" => (false, YamlCoreKind.Boolean, false),
                "int" => (false, YamlCoreKind.Integer, false),
                "float" => (false, YamlCoreKind.Float, true),
                _ => (false, null, false),
            };
        }

        if (tag is null && old.YamlPlain)
        {
            if (old.Value == string.Empty)
            {
                return (true, null, false);
            }

            YamlCoreKind kind = YamlEncoder.CoreSchemaKind(old.Value ?? string.Empty);
            return (false, kind == YamlCoreKind.String ? null : kind, false);
        }

        return (false, null, false);
    }

    /// <summary>The text's style as the planner writes it: a plain text is plain when it stays a string, and a text with a line break is never plain.</summary>
    private static bool PlainText(string token)
    {
        return token.Length > 0 && token[0] is not ('\'' or '"' or '|' or '>');
    }

    /// <summary>The logical scalar after its text is set (SetPart Value): the style it keeps, and the kind its text reads as.</summary>
    private static LogicalNode SetScalar(ConfigDocument before, DocNode node, LogicalNode old, string value)
    {
        (bool empty, YamlCoreKind? typed, _) = TypingOf(old);
        bool flow = InFlow(before, node);
        string text;
        bool plain;
        if (empty)
        {
            if (value.Length == 0)
            {
                return old;
            }

            plain = PlainText(YamlEditPlanner.StringToken(value, YamlScalarStyle.Plain, 0, flow, "\n"));
            text = value;
        }
        else if (typed is not null)
        {
            text = value;
            plain = true;
        }
        else
        {
            text = value;
            plain = old.YamlPlain && PlainText(YamlEditPlanner.StringToken(value, YamlScalarStyle.Plain, 0, flow, "\n"));
        }

        return Scalar(old.Name, text, plain, old.YamlAnchor, old.YamlTag);
    }

    /// <summary>The logical node of a scalar changed to another type (ChangeYamlType), with the tag that still fits it.</summary>
    private static LogicalNode ChangedType(ConfigDocument before, DocNode node, LogicalNode old, ChangeYamlType change)
    {
        if (change.Kind is YamlNewKind.Mapping or YamlNewKind.Sequence)
        {
            string? keep = YamlEditPlanner.TagFits(old.YamlTag, null) ? old.YamlTag : null;
            return new LogicalNode(
                change.Kind == YamlNewKind.Mapping ? LogicalKind.Object : LogicalKind.Array,
                old.Name,
                null,
                null,
                [],
                [],
                yamlAnchor: old.YamlAnchor,
                yamlTag: keep);
        }

        // A collection has no scalar state, so no scalar keeps its text when it is changed.
        bool scalar = node.Kind == NodeKind.YamlScalar;
        (bool empty, YamlCoreKind? typed, _) = scalar ? TypingOf(old) : (false, null, false);
        bool flow = InFlow(before, node);
        switch (change.Kind)
        {
            case YamlNewKind.String:
            {
                if (scalar && change.Value is null && !empty && typed is null)
                {
                    return old;
                }

                string text = change.Value ?? string.Empty;
                // The planner has no preferred style for a collection or a missing value (the plainest style), and keeps the quotes of a quoted one.
                YamlScalarStyle preferred = scalar && !old.YamlPlain ? YamlScalarStyle.SingleQuoted : YamlScalarStyle.Plain;
                string token = YamlEditPlanner.StringToken(text, preferred, 0, flow, "\n");
                return WithTag(old, text, PlainText(token), YamlValueKind.String);
            }

            case YamlNewKind.Number:
            {
                if (scalar && change.Value is null && typed is YamlCoreKind.Integer or YamlCoreKind.Float)
                {
                    return old;
                }

                string number = change.Value ?? "0";
                YamlValueKind kind = YamlEncoder.CoreSchemaKind(number) == YamlCoreKind.Integer ? YamlValueKind.Integer : YamlValueKind.Float;
                return WithTag(old, number, true, kind);
            }

            case YamlNewKind.Boolean:
            {
                if (scalar && change.Value is null && typed == YamlCoreKind.Boolean)
                {
                    return old;
                }

                return WithTag(old, change.Value ?? "false", true, YamlValueKind.Boolean);
            }

            case YamlNewKind.Null:
                if (scalar && (empty || typed == YamlCoreKind.Null))
                {
                    return old;
                }

                return WithTag(old, "null", true, YamlValueKind.Null);

            default:
                throw new InvalidOperationException("The type " + change.Kind + " is not a scalar type.");
        }
    }

    /// <summary>A scalar with a new text, whose tag is kept only when it still fits the kind the text reads as.</summary>
    private static LogicalNode WithTag(LogicalNode old, string text, bool plain, YamlValueKind kind)
    {
        string? tag = YamlEditPlanner.TagFits(old.YamlTag, kind) ? old.YamlTag : null;
        return Scalar(old.Name, text, plain, old.YamlAnchor, tag);
    }

    private static LogicalNode Scalar(string? name, string text, bool plain, string? anchor, string? tag)
    {
        YamlValueKind kind = YamlKinds.KindOf(plain, tag, text);
        return new LogicalNode(LogicalKind.Scalar, name, null, text, [], [], yamlKind: kind, yamlPlain: plain, yamlAnchor: anchor, yamlTag: tag);
    }

    // ----- Tree helpers -----

    private static LogicalNode Renamed(LogicalNode node, string name)
    {
        return new LogicalNode(
            node.Kind,
            name,
            node.ScalarKind,
            node.Value,
            node.Attributes,
            node.Children,
            node.AnyValue,
            node.TomlKind,
            node.YamlKind,
            node.YamlPlain,
            node.YamlAnchor,
            node.YamlTag);
    }

    private static LogicalNode WithChildren(LogicalNode node, IReadOnlyList<LogicalNode> children)
    {
        return new LogicalNode(
            node.Kind,
            node.Name,
            node.ScalarKind,
            node.Value,
            node.Attributes,
            children,
            node.AnyValue,
            node.TomlKind,
            node.YamlKind,
            node.YamlPlain,
            node.YamlAnchor,
            node.YamlTag);
    }

    private static LogicalNode Rebuild(LogicalNode root, LogicalNode data, IReadOnlyList<LogicalNode> comments)
    {
        var commentsNode = new LogicalNode(LogicalKind.Array, "#comments", null, null, [], comments);
        return new LogicalNode(root.Kind, root.Name, root.ScalarKind, root.Value, root.Attributes, [data, commentsNode]);
    }

    /// <summary>Changes the node at the logical indices; <paramref name="change"/> returns null to remove it.</summary>
    private static LogicalNode? Modify(LogicalNode node, IReadOnlyList<int> indices, int depth, Func<LogicalNode, LogicalNode?> change)
    {
        if (depth == indices.Count)
        {
            return change(node);
        }

        var children = node.Children.ToList();
        int k = indices[depth];
        LogicalNode? child = Modify(children[k], indices, depth + 1, change);
        if (child is null)
        {
            children.RemoveAt(k);
        }
        else
        {
            children[k] = child;
        }

        return WithChildren(node, children);
    }

    /// <summary>The logical child indices that lead from the document node to a node. A document's root and the single root have none.</summary>
    private static List<int> IndicesOf(DocNode node)
    {
        var chain = new List<DocNode>();
        for (DocNode n = node; n.Parent is not null; n = n.Parent)
        {
            chain.Add(n);
        }

        chain.Reverse();
        var indices = new List<int>();
        foreach (DocNode n in chain)
        {
            DocNode parent = n.Parent!;
            if (parent.Kind == NodeKind.YamlDocument)
            {
                continue;
            }

            if (parent.Kind == NodeKind.Document)
            {
                if (n.Kind == NodeKind.YamlDocument)
                {
                    indices.Add(CountEntries(parent.Children, n));
                }

                continue;
            }

            indices.Add(CountEntries(parent.Children, n));
        }

        return indices;
    }

    /// <summary>The index of a child among its parent's logical children, which are its entries and not its comments.</summary>
    private static int CountEntries(IReadOnlyList<DocNode> children, DocNode child)
    {
        int count = 0;
        foreach (DocNode c in children)
        {
            if (ReferenceEquals(c, child))
            {
                return count;
            }

            if (!EditLayout.IsComment(c))
            {
                count++;
            }
        }

        throw new InvalidOperationException("The node is not among its parent's children.");
    }

    private static int EntryCount(DocNode parent) => parent.Children.Count(child => !EditLayout.IsComment(child));

    private static int IndexOf(DocNode parent, DocNode child)
    {
        for (int c = 0; c < parent.Children.Count; c++)
        {
            if (ReferenceEquals(parent.Children[c], child))
            {
                return c;
            }
        }

        throw new InvalidOperationException("The node is not a child of its parent.");
    }

    /// <summary>True when the node's parent is a flow collection: its scalar is written in flow style.</summary>
    private static bool InFlow(ConfigDocument before, DocNode node)
    {
        var model = (YamlModel)before.Syntax;
        return node.Parent is { } parent && YamlEditPlanner.ValueOf(model.SyntaxOf(parent)) is YamlFlowMapping or YamlFlowSequence;
    }

    /// <summary>The brackets of a flow collection: the positions of its opening and closing bracket.</summary>
    private static (int Open, int Close) BracketsOf(YamlValueNode flow)
    {
        int open = -1;
        int close = -1;
        foreach (YamlNode part in flow.Parts)
        {
            if (part is not YamlToken { Kind: YamlTokenKind.Indicator } token)
            {
                continue;
            }

            if (open < 0 && token.Text is "{" or "[")
            {
                open = token.Start;
            }
            else if (token.Text is "}" or "]")
            {
                close = token.Start;
            }
        }

        return (open, close);
    }

    private static DocNode Locate(ConfigDocument document, NodePath path)
    {
        return document.Resolve(path) ?? throw new InvalidOperationException($"There is no node at {path}.");
    }

    // ----- Comments -----

    /// <summary>Every comment of the stream, in file order: the rank of a comment is its index here, which is its index in the #comments array.</summary>
    private static List<YamlComment> AllComments(YamlModel model)
    {
        return model.Syntax.Items.SelectMany(item => YamlDocuments.CommentsIn(item)).ToList();
    }

    private static int RankOf(YamlModel model, DocNode comment)
    {
        var target = model.SyntaxOf(comment) as YamlComment ?? throw new InvalidOperationException("The comment has no syntax.");
        return RankOf(model, target);
    }

    private static int RankOf(YamlModel model, YamlComment target)
    {
        int rank = AllComments(model).FindIndex(c => ReferenceEquals(c, target));
        return rank >= 0 ? rank : throw new InvalidOperationException("The comment is not in the stream.");
    }

    /// <summary>Adds the ranks of the comments that start in [start, end).</summary>
    private static void AddRanks(YamlModel model, HashSet<int> removed, int start, int end)
    {
        List<YamlComment> all = AllComments(model);
        for (int k = 0; k < all.Count; k++)
        {
            if (all[k].Start >= start && all[k].Start < end)
            {
                removed.Add(k);
            }
        }
    }

    private static IReadOnlyList<LogicalNode> WithoutRanks(IReadOnlyList<LogicalNode> comments, HashSet<int> removed)
    {
        return comments.Where((_, k) => !removed.Contains(k)).ToList();
    }
}
