using System.Globalization;
using Cfgedit.Core.Env;
using Cfgedit.Core.Json;

namespace Cfgedit.Core.Model;

/// <summary>
/// Builds the document tree and the logical tree of a .env file. A key is split on <c>__</c> into segments, and each prefix of
/// segments is a virtual group. A comment line belongs to the entry below it, when no blank line or the end of the file comes
/// between them. A trailing comment (<c>A=1 # note</c>) is a comment row right after its entry.
/// </summary>
internal static class EnvModelBuilder
{
    private const string Separator = "__";

    // ----- Document tree -----

    /// <summary>
    /// Builds the document node. Its only child is the root group, which holds the file's top-level entries and comments. The
    /// walk is iterative: a key with many segments makes a deep tree, but not a deep call stack.
    /// </summary>
    public static DocNode BuildTree(EnvDocument document)
    {
        int length = document.Source.Text.Length;
        List<Unit> units = CollectUnits(document);
        var root = new DocNode(NodeKind.Document, NodePath.Empty, null, [], [], 0, length, null);
        var rootGroup = new DocNode(
            NodeKind.EnvGroup,
            NodePath.Empty.Append(new ItemStep(0)),
            root,
            [new DocPart(PartKind.Root, "$", false, false, 0, 0), SummaryPart(CountChildren(units, 0), 0)],
            [],
            0,
            length,
            null);
        root.AddChild(rootGroup);

        var pending = new Queue<Job>();
        pending.Enqueue(new Job(rootGroup, units, 0));
        while (pending.Count > 0)
        {
            AddChildren(pending.Dequeue(), pending);
        }

        return root;
    }

    /// <summary>
    /// The items a node covers, in source order. An entry covers its own line. A comment line covers its own line. A trailing
    /// comment covers the line of its entry, so it returns that entry's item: its node's <c>Start</c> is the <c>#</c>, not the
    /// item's start, which is how a planner tells the two apart. A group covers the entries and comments below it, each item once,
    /// and the document node covers every non-blank item. Blank lines belong to no node.
    /// </summary>
    internal static IReadOnlyList<EnvItem> ItemsOf(EnvDocument document, DocNode node)
    {
        List<EnvItem> lines = document.Items.Where(item => item is not EnvBlankLine).ToList();
        var items = new List<EnvItem>();
        var stack = new Stack<DocNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            DocNode current = stack.Pop();
            if (current.Kind is NodeKind.EnvEntry or NodeKind.EnvComment)
            {
                items.Add(ItemAt(lines, current.Start));
            }

            for (int i = current.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(current.Children[i]);
            }
        }

        return items.Distinct().ToList();
    }

    /// <summary>The last item that starts at or before an offset. Node starts always fall inside a non-blank item.</summary>
    private static EnvItem ItemAt(List<EnvItem> lines, int offset)
    {
        int low = 0;
        int high = lines.Count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (lines[middle].Start <= offset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (lines.Count == 0 || lines[low].Start > offset)
        {
            throw new InvalidOperationException("No .env item covers offset " + offset.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return lines[low];
    }

    /// <summary>Groups the items into units in source order. A comment block that ends at an entry is attached to it.</summary>
    private static List<Unit> CollectUnits(EnvDocument document)
    {
        var units = new List<Unit>();
        var pending = new List<EnvCommentLine>();
        foreach (EnvItem item in document.Items)
        {
            switch (item)
            {
                case EnvCommentLine comment:
                    pending.Add(comment);
                    break;
                case EnvEntry entry:
                {
                    (string[] names, EnvSpan[] spans) = Segments(entry);
                    units.Add(new EntryUnit(entry, names, spans, pending.ToArray()));
                    pending.Clear();
                    break;
                }
                case EnvBlankLine:
                    AddStandalone(units, pending);
                    break;
                default:
                    throw new InvalidOperationException("Unknown .env item type: " + item.GetType().Name);
            }
        }

        AddStandalone(units, pending);
        return units;
    }

    /// <summary>Adds the comment lines that no entry takes as units of their own, at the root.</summary>
    private static void AddStandalone(List<Unit> units, List<EnvCommentLine> pending)
    {
        foreach (EnvCommentLine comment in pending)
        {
            units.Add(new CommentUnit(comment));
        }

        pending.Clear();
    }

    /// <summary>
    /// The segments of an entry's key, with their spans in the source. The key is split on <c>__</c> unless a split would not
    /// round-trip: an empty segment, a segment before the last that ends with <c>_</c>, or a segment after the first that starts
    /// with <c>_</c>. A key with more segments than the JSON nesting cap (<see cref="JsonDocument.MaxDepth"/>) is not split either.
    /// Such a key is one segment.
    /// </summary>
    private static (string[] Names, EnvSpan[] Spans) Segments(EnvEntry entry)
    {
        string[] names = SplitKey(entry.Key);
        if (names.Length == 1)
        {
            return (names, new[] { entry.KeySpan });
        }

        var spans = new EnvSpan[names.Length];
        int start = entry.KeySpan.Start;
        for (int i = 0; i < names.Length; i++)
        {
            spans[i] = new EnvSpan(start, start + names[i].Length);
            start += names[i].Length + Separator.Length;
        }

        return (names, spans);
    }

    /// <summary>
    /// The segments of a key, as the tree splits it (see <see cref="Segments"/>): on <c>__</c>, unless a split would not
    /// round-trip or the key has more segments than the nesting cap. Such a key is one segment. The planner validates new keys
    /// with it, so that the tree and the edits agree.
    /// </summary>
    internal static string[] SplitKey(string key)
    {
        string[] names = key.Split(Separator);
        return names.Length == 1 || names.Length > JsonDocument.MaxDepth || !CanSplit(names) ? [key] : names;
    }

    private static bool CanSplit(string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i].Length == 0
                || (i < names.Length - 1 && names[i].EndsWith('_'))
                || (i > 0 && names[i].StartsWith('_')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The children of a group, one level down: its leaves, comments and subgroups, in order.</summary>
    private static List<Slot> Slots(IReadOnlyList<Unit> units, int depth)
    {
        var slots = new List<Slot>();
        var buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        foreach (Unit unit in units)
        {
            switch (unit)
            {
                case CommentUnit standalone:
                    slots.Add(new CommentSlot(standalone.Line.Comment, standalone.Line.Start, standalone.Line.End));
                    break;
                case EntryUnit leaf when leaf.Names.Length == depth + 1:
                    foreach (EnvCommentLine line in leaf.Comments)
                    {
                        slots.Add(new CommentSlot(line.Comment, line.Start, line.End));
                    }

                    slots.Add(new LeafSlot(leaf));
                    if (leaf.Entry.Comment is { } trailing)
                    {
                        slots.Add(new CommentSlot(trailing, trailing.Start, trailing.End));
                    }

                    break;
                case EntryUnit member:
                {
                    string name = member.Names[depth];
                    if (!buckets.TryGetValue(name, out Bucket? bucket))
                    {
                        bucket = new Bucket(name);
                        buckets.Add(name, bucket);
                        slots.Add(new GroupSlot(bucket));
                    }

                    bucket.Entries.Add(member);
                    break;
                }
                default:
                    throw new InvalidOperationException("Unknown .env unit type: " + unit.GetType().Name);
            }
        }

        return slots;
    }

    /// <summary>The count a group shows: its children that are not comments. It matches the non-comment slots of <see cref="Slots"/>.</summary>
    private static int CountChildren(IReadOnlyList<Unit> units, int depth)
    {
        int leaves = 0;
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (Unit unit in units)
        {
            if (unit is EntryUnit entry)
            {
                if (entry.Names.Length == depth + 1)
                {
                    leaves++;
                }
                else
                {
                    groups.Add(entry.Names[depth]);
                }
            }
        }

        return leaves + groups.Count;
    }

    /// <summary>Adds the children of a group job to its node, and queues the groups among them.</summary>
    private static void AddChildren(Job job, Queue<Job> pending)
    {
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        int comments = 0;
        foreach (Slot slot in Slots(job.Units, job.Depth))
        {
            switch (slot)
            {
                case CommentSlot comment:
                    job.Node.AddChild(CommentNode(job.Node, comment, comments++));
                    break;
                case LeafSlot leafSlot:
                    job.Node.AddChild(EntryNode(job.Node, leafSlot.Entry, NextOrdinal(ordinals, leafSlot.Entry.Names[job.Depth]), job.Depth));
                    break;
                case GroupSlot groupSlot:
                {
                    DocNode group = GroupNode(job.Node, groupSlot.Bucket, NextOrdinal(ordinals, groupSlot.Bucket.Name), job.Depth);
                    job.Node.AddChild(group);
                    pending.Enqueue(new Job(group, groupSlot.Bucket.Entries, job.Depth + 1));
                    break;
                }
                default:
                    throw new InvalidOperationException("Unknown .env slot type: " + slot.GetType().Name);
            }
        }
    }

    private static DocNode GroupNode(DocNode parent, Bucket bucket, int ordinal, int depth)
    {
        EntryUnit first = bucket.Entries[0];
        EntryUnit last = bucket.Entries[^1];
        EnvSpan key = first.Spans[depth];
        int start = first.Comments.Length > 0 ? first.Comments[0].Start : first.Entry.Start;
        var parts = new[]
        {
            new DocPart(PartKind.Key, bucket.Name, true, false, key.Start, key.End),
            SummaryPart(CountChildren(bucket.Entries, depth + 1), key.End),
        };
        return new DocNode(
            NodeKind.EnvGroup,
            parent.Path.Append(new MemberStep(bucket.Name, ordinal)),
            parent,
            parts,
            [],
            start,
            last.Entry.End,
            null);
    }

    private static DocNode EntryNode(DocNode parent, EntryUnit unit, int ordinal, int depth)
    {
        EnvEntry entry = unit.Entry;
        string name = unit.Names[depth];
        EnvSpan key = unit.Spans[depth];
        var parts = new[]
        {
            new DocPart(PartKind.Key, name, true, false, key.Start, key.End),
            new DocPart(PartKind.Value, entry.Value, true, false, entry.ValueSpan.Start, entry.ValueSpan.End),
        };
        return new DocNode(
            NodeKind.EnvEntry,
            parent.Path.Append(new MemberStep(name, ordinal)),
            parent,
            parts,
            [],
            entry.Start,
            entry.End,
            null);
    }

    /// <summary>
    /// A comment row. For a comment line, the node spans the line. For a trailing comment, it spans from its <c>#</c to the end of
    /// the entry's line. Either way the part is the text after the <c>#</c>.
    /// </summary>
    private static DocNode CommentNode(DocNode parent, CommentSlot slot, int index)
    {
        EnvComment comment = slot.Comment;
        var part = new DocPart(PartKind.Comment, comment.Content, true, false, comment.Start + 1, comment.End);
        return new DocNode(
            NodeKind.EnvComment,
            parent.Path.Append(new CommentStep(index)),
            parent,
            [part],
            [],
            slot.Start,
            slot.End,
            null);
    }

    /// <summary>The summary part of a group: its count, shown as <c>{…} (n)</c>. It has no text of its own, so it sits at a point.</summary>
    private static DocPart SummaryPart(int count, int position)
    {
        return new DocPart(PartKind.Summary, count.ToString(CultureInfo.InvariantCulture), false, false, position, position);
    }

    /// <summary>The ordinal of the next sibling with this name: the number of same-named siblings before it.</summary>
    private static int NextOrdinal(Dictionary<string, int> ordinals, string name)
    {
        int ordinal = ordinals.GetValueOrDefault(name);
        ordinals[name] = ordinal + 1;
        return ordinal;
    }

    // ----- Logical tree -----

    /// <summary>
    /// The flat logical tree: one string scalar per entry, named by its full key, followed by its trailing comment if it has one,
    /// and one comment per comment line, all in file order. Blank lines, <c>export</c>, quote styles and spacing are not in it.
    /// </summary>
    public static LogicalNode BuildLogical(EnvDocument document)
    {
        var children = new List<LogicalNode>();
        foreach (EnvItem item in document.Items)
        {
            switch (item)
            {
                case EnvEntry entry:
                    children.Add(new LogicalNode(LogicalKind.Scalar, entry.Key, JsonValueKind.String, entry.Value, [], []));
                    if (entry.Comment is { } trailing)
                    {
                        children.Add(new LogicalNode(LogicalKind.Comment, null, null, trailing.Content, [], []));
                    }

                    break;
                case EnvCommentLine comment:
                    children.Add(new LogicalNode(LogicalKind.Comment, null, null, comment.Comment.Content, [], []));
                    break;
            }
        }

        return new LogicalNode(LogicalKind.Document, null, null, null, [], children);
    }

    // ----- Working types -----

    /// <summary>A unit of the tree: an entry with the comment lines attached to it, or a comment line that stands alone.</summary>
    private abstract record Unit;

    private sealed record EntryUnit(EnvEntry Entry, string[] Names, EnvSpan[] Spans, EnvCommentLine[] Comments) : Unit;

    private sealed record CommentUnit(EnvCommentLine Line) : Unit;

    /// <summary>The entries that share a first segment, in file order.</summary>
    private sealed class Bucket(string name)
    {
        public string Name { get; } = name;

        public List<EntryUnit> Entries { get; } = [];
    }

    /// <summary>One child of a group, before its node is made.</summary>
    private abstract record Slot;

    /// <summary>A comment row: a comment line, or a trailing comment. Start and End are the span of the row.</summary>
    private sealed record CommentSlot(EnvComment Comment, int Start, int End) : Slot;

    private sealed record LeafSlot(EntryUnit Entry) : Slot;

    private sealed record GroupSlot(Bucket Bucket) : Slot;

    /// <summary>A group node whose children are still to be added, with the units below it and its depth in the key.</summary>
    private sealed record Job(DocNode Node, IReadOnlyList<Unit> Units, int Depth);
}
