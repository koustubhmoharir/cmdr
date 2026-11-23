using System.Globalization;
using Cfgedit.Core.Text;
using Cfgedit.Core.Yaml;

namespace Cfgedit.Core.Model;

/// <summary>
/// Builds the document tree of a YAML stream from its syntax tree. A stream with one document shows that document's root as the root row
/// (<c>$</c>). A stream with several documents shows each as a row <c>[i]</c> with the root below it. A stream with none is an empty
/// mapping root. A mapping member is a row with its key, its anchor and tag, and its value (<c>{n}</c>, <c>[n]</c>, the decoded scalar, or
/// the alias as written). A sequence item is a row with its index. A comment is a row, placed as <see cref="YamlComments"/> decides.
/// </summary>
internal static class YamlModelBuilder
{
    /// <exception cref="Cfgedit.Core.CfgeditException">A tagged scalar's value does not read as its tag's type.</exception>
    internal static YamlModel Build(YamlDocument syntax)
    {
        var syntaxOf = new Dictionary<DocNode, YamlNode>();
        int length = syntax.Source.Text.Length;
        var tree = new DocNode(NodeKind.Document, NodePath.Empty, null, [], [], 0, length, null);
        new TreeBuilder(syntax, YamlComments.Assign(syntax), syntaxOf).FillDocument(tree, syntax);
        return new YamlModel(syntax, tree, syntaxOf);
    }

    /// <summary>A mapping entry, with its key, its colon (absent in a flow entry with no value), and its value (null when it has none).</summary>
    private readonly record struct EntryView(YamlNode Node, YamlScalar Key, YamlToken? Colon, YamlValueNode? Value);

    /// <summary>A sequence item: its node, its dash (absent in a flow sequence) and its value (null for an empty block item).</summary>
    private readonly record struct ItemView(YamlNode Node, YamlToken? Dash, YamlValueNode? Value);

    private sealed class TreeBuilder(YamlDocument syntax, YamlComments comments, Dictionary<DocNode, YamlNode> syntaxOf)
    {
        private readonly Dictionary<DocNode, int> _commentCounts = new();
        private IReadOnlyDictionary<string, string> _handles = new Dictionary<string, string>();

        /// <summary>Fills the document node: the root row of a single document, the document rows of several, or the empty root of a stream with none.</summary>
        internal void FillDocument(DocNode tree, YamlDocument syntax)
        {
            IReadOnlyList<YamlDocumentNode> documents = YamlDocuments.Counted(syntax);
            if (documents.Count == 0)
            {
                var root = new DocNode(
                    NodeKind.YamlMapping,
                    NodePath.Empty.Append(new ItemStep(0)),
                    tree,
                    [RootPart(0), SummaryPart(0, 0)],
                    [],
                    tree.Start,
                    tree.End,
                    null);
                tree.AddChild(root);
                foreach (YamlComment comment in comments.Stream)
                {
                    root.AddChild(Comment(root, comment));
                }

                return;
            }

            if (documents.Count == 1)
            {
                FillRoot(tree, NodePath.Empty.Append(new ItemStep(0)), documents[0]);
                return;
            }

            for (int i = 0; i < documents.Count; i++)
            {
                YamlDocumentNode document = documents[i];
                NodePath path = NodePath.Empty.Append(new ItemStep(i));
                IReadOnlyList<YamlComment> around = Merge(comments.Before(document), comments.After(document));
                (int start, int end) = Extend(document.Start, document.End, around);
                var row = new DocNode(
                    NodeKind.YamlDocument,
                    path,
                    tree,
                    [IndexPart(i, start)],
                    [],
                    start,
                    end,
                    null);
                syntaxOf[row] = document;
                tree.AddChild(row);
                FillRoot(row, path.Append(new ItemStep(0)), document);
            }
        }

        /// <summary>
        /// The root value of a document. A collection root takes the comments before and after it as its own rows (the first and last
        /// rows of the collection). A scalar root, or an empty document, is a row with the comments before and after it as siblings.
        /// </summary>
        private void FillRoot(DocNode parent, NodePath path, YamlDocumentNode document)
        {
            _handles = YamlTags.HandlesOf(document);
            YamlValueNode? root = document.Root;
            IReadOnlyList<YamlComment> around = Merge(comments.Before(document), comments.After(document));
            if (root is not null && YamlComments.IsCollection(root))
            {
                parent.AddChild(ValueRow(parent, path, root, [RootPart(root.Start)], root, root.Start, root.Start, root.End, around));
                return;
            }

            List<YamlComment> all = Merge(around, comments.Of(document)).ToList();
            int split = root?.Start ?? int.MaxValue;
            foreach (YamlComment comment in all.Where(comment => comment.Start < split))
            {
                parent.AddChild(Comment(parent, comment));
            }

            parent.AddChild(ValueRow(
                parent,
                path,
                root,
                [RootPart(document.Start)],
                root is null ? document : root,
                document.Start,
                document.Start,
                root?.End ?? document.Start,
                []));
            foreach (YamlComment comment in all.Where(comment => comment.Start >= split))
            {
                parent.AddChild(Comment(parent, comment));
            }
        }

        /// <summary>
        /// A row for a value: its lead parts (the root marker, an index, a key), its anchor and tag, then its scalar text, its alias, or its
        /// count. A missing value (null) is a null scalar with an empty value part at <paramref name="nullAt"/>. A collection's rows are
        /// added below it, with <paramref name="extra"/> among its own comments.
        /// </summary>
        private DocNode ValueRow(
            DocNode parent,
            NodePath path,
            YamlValueNode? value,
            List<DocPart> lead,
            YamlNode piece,
            int nullAt,
            int start,
            int end,
            IReadOnlyList<YamlComment> extra)
        {
            var parts = new List<DocPart>(lead);
            if (value is null)
            {
                parts.Add(new DocPart(PartKind.Value, string.Empty, true, false, nullAt, nullAt));
                return Remember(new DocNode(NodeKind.YamlScalar, path, parent, parts, [], start, end, null, yamlKind: YamlValueKind.Null), piece);
            }

            parts.AddRange(PropParts(value));
            switch (value)
            {
                case YamlAlias alias:
                    parts.Add(new DocPart(PartKind.Alias, "*" + alias.Name, false, false, alias.Start, alias.End));
                    return Remember(new DocNode(NodeKind.YamlAlias, path, parent, parts, [], start, end, null), piece);

                case YamlScalar scalar:
                {
                    string? tag = YamlTags.Resolve(scalar.Tag, _handles);
                    YamlValueKind kind = Kind(scalar, tag);
                    (int from, int to) = ContentSpan(scalar);
                    parts.Add(new DocPart(PartKind.Value, scalar.Value, true, false, from, to));
                    return Remember(new DocNode(NodeKind.YamlScalar, path, parent, parts, [], start, end, null, yamlKind: kind), piece);
                }

                case YamlBlockMapping or YamlFlowMapping:
                {
                    IReadOnlyList<YamlComment> own = Merge(comments.Of(value), extra);
                    IReadOnlyList<EntryView> entries = EntriesOf(value);
                    parts.Add(SummaryPart(entries.Count, value.Start));
                    (int spanStart, int spanEnd) = Extend(start, end, own);
                    var node = Remember(new DocNode(NodeKind.YamlMapping, path, parent, parts, [], spanStart, spanEnd, null), piece);
                    FillMapping(node, entries, own);
                    return node;
                }

                default:
                {
                    IReadOnlyList<YamlComment> own = Merge(comments.Of(value), extra);
                    IReadOnlyList<ItemView> items = ItemsOf(value);
                    parts.Add(SummaryPart(items.Count, value.Start));
                    (int spanStart, int spanEnd) = Extend(start, end, own);
                    var node = Remember(new DocNode(NodeKind.YamlSequence, path, parent, parts, [], spanStart, spanEnd, null), piece);
                    FillSequence(node, items, own);
                    return node;
                }
            }
        }

        /// <summary>The rows of a mapping: its members and the comments among them, in source order.</summary>
        private void FillMapping(DocNode node, IReadOnlyList<EntryView> entries, IReadOnlyList<YamlComment> own)
        {
            var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
            int next = 0;
            foreach (EntryView entry in entries)
            {
                next = EmitComments(node, own, next, entry.Node.Start);
                YamlScalar key = entry.Key;
                int ordinal = ordinals.GetValueOrDefault(key.Value);
                ordinals[key.Value] = ordinal + 1;
                List<DocPart> lead = PropParts(key);
                (int from, int to) = ContentSpan(key);
                lead.Add(new DocPart(PartKind.Key, key.Value, true, false, from, to));
                NodePath path = node.Path.Append(new MemberStep(key.Value, ordinal));
                node.AddChild(ValueRow(
                    node,
                    path,
                    entry.Value,
                    lead,
                    entry.Node,
                    entry.Colon?.End ?? key.End,
                    entry.Node.Start,
                    entry.Node.End,
                    []));
            }

            EmitComments(node, own, next, int.MaxValue);
        }

        /// <summary>The rows of a sequence: its items and the comments among them, in source order.</summary>
        private void FillSequence(DocNode node, IReadOnlyList<ItemView> items, IReadOnlyList<YamlComment> own)
        {
            int next = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemView item = items[i];
                next = EmitComments(node, own, next, item.Node.Start);
                var lead = new List<DocPart> { IndexPart(i, item.Node.Start) };
                node.AddChild(ValueRow(
                    node,
                    node.Path.Append(new ItemStep(i)),
                    item.Value,
                    lead,
                    item.Node,
                    item.Dash?.End ?? item.Node.Start,
                    item.Node.Start,
                    item.Node.End,
                    []));
            }

            EmitComments(node, own, next, int.MaxValue);
        }

        /// <summary>Adds the comments that start before <paramref name="limit"/>, from index <paramref name="next"/>; returns the next index.</summary>
        private int EmitComments(DocNode node, IReadOnlyList<YamlComment> own, int next, int limit)
        {
            while (next < own.Count && own[next].Start < limit)
            {
                node.AddChild(Comment(node, own[next]));
                next++;
            }

            return next;
        }

        /// <summary>The kind of a scalar, with the position of the scalar in the error when its tag does not fit its value.</summary>
        private YamlValueKind Kind(YamlScalar scalar, string? tag)
        {
            try
            {
                return YamlKinds.KindOf(scalar.Style == YamlScalarStyle.Plain, tag, scalar.Value);
            }
            catch (CfgeditException ex)
            {
                LinePosition at = syntax.Source.GetPosition(scalar.Start);
                throw new CfgeditException(ex.Message) { Line = at.Line, Column = at.Column };
            }
        }

        private DocNode Comment(DocNode parent, YamlComment comment)
        {
            int index = _commentCounts.GetValueOrDefault(parent);
            _commentCounts[parent] = index + 1;
            var part = new DocPart(PartKind.Comment, comment.Content, true, false, comment.Start + 1, comment.End);
            return Remember(
                new DocNode(NodeKind.YamlComment, parent.Path.Append(new CommentStep(index)), parent, [part], [], comment.Start, comment.End, null),
                comment);
        }

        private DocNode Remember(DocNode node, YamlNode piece)
        {
            syntaxOf[node] = piece;
            return node;
        }

        /// <summary>The comments of a collection and those given, in source order.</summary>
        private static IReadOnlyList<YamlComment> Merge(IReadOnlyList<YamlComment> first, IReadOnlyList<YamlComment> second)
        {
            return first.Concat(second).OrderBy(comment => comment.Start).ToList();
        }

        /// <summary>A span widened to take in the comments that a row owns: a row's span includes its leading and trailing comments.</summary>
        private static (int Start, int End) Extend(int start, int end, IReadOnlyList<YamlComment> comments)
        {
            foreach (YamlComment comment in comments)
            {
                start = Math.Min(start, comment.Start);
                end = Math.Max(end, comment.End);
            }

            return (start, end);
        }

        private static IReadOnlyList<EntryView> EntriesOf(YamlValueNode mapping)
        {
            return mapping switch
            {
                YamlBlockMapping block => block.Entries.Select(e => new EntryView(e, e.Key, e.Colon, e.Value)).ToList(),
                YamlFlowMapping flow => flow.Entries.Select(e => new EntryView(e, e.Key, e.Colon, e.Value)).ToList(),
                _ => throw new InvalidOperationException("The node is not a YAML mapping."),
            };
        }

        private static IReadOnlyList<ItemView> ItemsOf(YamlValueNode sequence)
        {
            return sequence switch
            {
                YamlBlockSequence block => block.Items.Select(i => new ItemView(i, i.Dash, i.Value)).ToList(),
                YamlFlowSequence flow => flow.Items.Select(v => new ItemView(v, null, v)).ToList(),
                _ => throw new InvalidOperationException("The node is not a YAML sequence."),
            };
        }
    }

    /// <summary>The anchor and tag of a node, as read-only parts, in the order they are written.</summary>
    private static List<DocPart> PropParts(YamlValueNode node)
    {
        var parts = new List<DocPart>(2);
        if (node.Anchor is { } anchor)
        {
            parts.Add(new DocPart(PartKind.Anchor, anchor.Text, false, false, anchor.Start, anchor.End));
        }

        if (node.Tag is { } tag)
        {
            parts.Add(new DocPart(PartKind.Tag, tag.Text, false, false, tag.Start, tag.End));
        }

        return parts.OrderBy(part => part.Start).ToList();
    }

    /// <summary>The span of a scalar's text, without its anchor and tag. An empty scalar has an empty span at its end.</summary>
    private static (int Start, int End) ContentSpan(YamlScalar scalar)
    {
        if (scalar.IsEmpty)
        {
            return (scalar.End, scalar.End);
        }

        foreach (YamlNode part in scalar.Parts)
        {
            if (part is YamlToken { Kind: YamlTokenKind.Scalar or YamlTokenKind.BlockHeader } token)
            {
                return (token.Start, scalar.End);
            }
        }

        return (scalar.Start, scalar.End);
    }

    /// <summary>The root marker, shown as <c>$</c>: zero-width, not editable.</summary>
    private static DocPart RootPart(int offset)
    {
        return new DocPart(PartKind.Root, "$", false, false, offset, offset);
    }

    /// <summary>A collection's count, shown as <c>{n}</c> or <c>[n]</c>: zero-width, not editable.</summary>
    private static DocPart SummaryPart(int count, int offset)
    {
        return new DocPart(PartKind.Summary, count.ToString(CultureInfo.InvariantCulture), false, false, offset, offset);
    }

    /// <summary>An index: a zero-width part, not editable.</summary>
    private static DocPart IndexPart(int index, int offset)
    {
        return new DocPart(PartKind.Index, index.ToString(CultureInfo.InvariantCulture), false, false, offset, offset);
    }
}
