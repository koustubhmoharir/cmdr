using System.Text;
using Cfgedit.Core.Env;
using Cfgedit.Core.Json;

namespace Cfgedit.Core.Model;

/// <summary>
/// Plans the text edits for an operation on a .env document. It works on the document tree and the concrete syntax: the tree says
/// which entries and comments the nodes are, and the syntax gives the spans of their keys, values and comments. Every edit is a
/// replacement of a span of the old text, or an insertion of whole lines; the text the edit leaves alone keeps its bytes.
/// </summary>
internal sealed class EnvEditPlanner
{
    private const string Separator = "__";
    private const string MovedNotContiguous = "this cannot move: its lines are not next to each other in the file";
    private const string NeighbourNotContiguous = "this cannot move: the lines of the sibling it goes next to are not next to each other in the file";
    private const string PassesOtherLines = "this cannot move there: a line that is not part of the siblings it passes lies between them";
    private const string BlankLineWouldGo = "this cannot move up: the blank line before the last line would have to go, and the file must keep ending as it does";

    private readonly ConfigDocument _document;
    private readonly EnvDocument _syntax;
    private readonly string _text;
    private readonly string _newline;
    private readonly Dictionary<int, EnvEntry> _entryAt;
    private readonly List<TextEdit> _edits = [];

    /// <summary>Creates a planner for a parsed .env document.</summary>
    public EnvEditPlanner(ConfigDocument document)
    {
        _document = document;
        _syntax = document.Syntax as EnvDocument ?? throw new ArgumentException("The document was not parsed as .env.", nameof(document));
        _text = document.Source.Text;
        _newline = document.Source.Newline;
        _entryAt = _syntax.Items.OfType<EnvEntry>().ToDictionary(entry => entry.Start);
    }

    /// <summary>Plans an operation. Throws <see cref="CfgeditException"/> when the operation is invalid or not supported yet.</summary>
    public IReadOnlyList<TextEdit> Plan(EditOp op)
    {
        switch (op)
        {
            case SetPart set:
                PlanSetPart(set);
                break;
            case InsertNode insert:
                PlanInsert(insert);
                break;
            case DeleteNode delete:
                PlanDelete(delete);
                break;
            case MoveNode move:
                PlanMove(move);
                break;
            case DuplicateNode duplicate:
                PlanDuplicate(duplicate);
                break;
            case ChangeJsonType:
                throw new CfgeditException("`.env` values are all strings");
            case InsertAttribute or DeleteAttribute or MoveAttribute:
                throw new CfgeditException("attributes are not part of .env");
            default:
                throw new CfgeditException("This edit is not supported for .env");
        }

        return _edits;
    }

    // ----- SetPart -----

    private void PlanSetPart(SetPart op)
    {
        DocNode node = Node(op.Node);
        switch (op.Part)
        {
            case PartKind.Value when node.Kind == NodeKind.EnvEntry:
                PlanSetValue(node, op.Value);
                return;
            case PartKind.Key when node.Kind is NodeKind.EnvEntry or NodeKind.EnvGroup && node.Parts.Any(part => part.Kind == PartKind.Key):
                PlanRename(node, op.Value);
                return;
            case PartKind.Comment when node.Kind == NodeKind.EnvComment:
                PlanSetComment(node, op.Value);
                return;
            case PartKind.AttributeName or PartKind.AttributeValue:
                throw new CfgeditException("attributes are not part of .env");
            default:
                throw Uneditable();
        }
    }

    private void PlanSetValue(DocNode node, string value)
    {
        EnvEntry entry = Entry(node);
        if (value == entry.Value)
        {
            return;
        }

        string token = EnvEncoder.EncodeKeeping(value, entry.Quote, _newline);
        if (entry.Comment is { } comment && comment.Start == entry.ValueSpan.End)
        {
            // The comment starts right where the value ends. A '#' that follows a value without a space would be read as part of
            // an unquoted value, so a space keeps the comment apart.
            token += " ";
        }

        Replace(entry.ValueSpan.Start, entry.ValueSpan.End, token);
    }

    private void PlanRename(DocNode node, string name)
    {
        DocNode parent = Parent(node);
        string oldSegment = SegmentOf(node);
        if (name == oldSegment)
        {
            return;
        }

        IReadOnlyList<string> parentNames = NamesOf(parent.Path);
        ValidateSegment(parentNames, name);
        if (parent.Children.Any(child => !ReferenceEquals(child, node) && SegmentOrNull(child) == name))
        {
            throw new CfgeditException($"a key named {name} already exists here");
        }

        if (node.Kind == NodeKind.EnvEntry)
        {
            // The last segment is the end of the key.
            EnvEntry entry = Entry(node);
            Replace(entry.KeySpan.End - oldSegment.Length, entry.KeySpan.End, name);
            return;
        }

        // A group's segment sits at the same offset in the key of every entry under it.
        int offset = SegmentOffset(parent);
        foreach (EnvEntry entry in ItemsOf(node).OfType<EnvEntry>())
        {
            int start = entry.KeySpan.Start + offset;
            Replace(start, start + oldSegment.Length, name);
        }
    }

    private void PlanSetComment(DocNode node, string text)
    {
        DocPart part = node.Parts.FirstOrDefault(p => p.Kind == PartKind.Comment) ?? throw Uneditable();
        if (text == part.Value)
        {
            return;
        }

        if (text.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new CfgeditException("a comment cannot contain a line break");
        }

        Replace(part.Start, part.End, text);
    }

    // ----- InsertNode -----

    private void PlanInsert(InsertNode op)
    {
        DocNode parent = Node(op.Parent);
        if (parent.Kind != NodeKind.EnvGroup)
        {
            throw new CfgeditException(parent.Kind == NodeKind.EnvEntry ? "an entry has no children" : "This node cannot have children");
        }

        int count = parent.Children.Count;
        if (op.Index < 0 || op.Index > count)
        {
            throw new CfgeditException($"Index {op.Index} is outside the children, 0 to {count}");
        }

        switch (op.Node)
        {
            case NewEnvEntry entry:
                InsertEntry(parent, op.Index, entry);
                return;
            case NewEnvComment comment:
                InsertComment(parent, op.Index, comment);
                return;
            default:
                throw new CfgeditException("This kind of node cannot be inserted into .env");
        }
    }

    private void InsertEntry(DocNode parent, int index, NewEnvEntry entry)
    {
        string full = ValidateInsertKey(NamesOf(parent.Path), entry.Key);
        if (_syntax.Items.OfType<EnvEntry>().Any(existing => existing.Key == full))
        {
            throw new CfgeditException($"a key named {full} already exists");
        }

        Template template = TemplateAt(parent, index);
        string token = EnvEncoder.Encode(entry.Value, _newline);
        string line = template.Indent + template.Export + full + template.SpaceBefore + "=" + template.SpaceAfter + token;
        InsertLine(line, InsertOffset(parent, index));
    }

    private void InsertComment(DocNode parent, int index, NewEnvComment comment)
    {
        if (comment.Text.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new CfgeditException("a comment cannot contain a line break");
        }

        Template template = TemplateAt(parent, index);
        InsertLine(template.Indent + "#" + comment.Text, InsertOffset(parent, index));
    }

    /// <summary>
    /// The offset where a new line goes for an insert at an index. Before a child, that is before the first line of the child's
    /// block; a trailing comment is on its entry's line, so the line goes after that entry. After the last child, it goes after the
    /// last line of its block.
    /// </summary>
    private int InsertOffset(DocNode parent, int index)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        if (index < children.Count)
        {
            return EnvLayout.IsTrailing(children, index)
                ? children[index - 1].End
                : children[EnvLayout.Block(parent, index).First].Start;
        }

        if (children.Count == 0)
        {
            return _text.Length;
        }

        int last = EnvLayout.Block(parent, children.Count - 1).Last;
        return EnvLayout.IsTrailing(children, last) ? children[last - 1].End : children[last].End;
    }

    /// <summary>
    /// Writes a whole line at an offset. At the end of a text that has no final line break, the line break comes first, and the new
    /// line gets none, so the file keeps its way of ending.
    /// </summary>
    private void InsertLine(string line, int offset)
    {
        bool atEnd = offset == _text.Length;
        bool endsWithBreak = _text.Length == 0 || _text[^1] is '\r' or '\n';
        if (atEnd && !endsWithBreak)
        {
            Insert(offset, _newline + line);
            return;
        }

        Insert(offset, line + _newline);
    }

    /// <summary>
    /// The formatting a new line copies from a neighbour: the previous sibling entry, else the next one, else the first entry of the
    /// file. A file with no entries gets none.
    /// </summary>
    private Template TemplateAt(DocNode parent, int index)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        EnvEntry? source = null;
        for (int k = index - 1; k >= 0 && source is null; k--)
        {
            if (children[k].Kind == NodeKind.EnvEntry)
            {
                source = _entryAt[children[k].Start];
            }
        }

        for (int k = index; k < children.Count && source is null; k++)
        {
            if (children[k].Kind == NodeKind.EnvEntry)
            {
                source = _entryAt[children[k].Start];
            }
        }

        source ??= _syntax.Items.OfType<EnvEntry>().FirstOrDefault();
        if (source is null)
        {
            return Template.Plain;
        }

        int indentEnd = source.Start;
        while (indentEnd < _text.Length && IsBlank(_text[indentEnd]))
        {
            indentEnd++;
        }

        string export = source.Export is { } keyword ? _text[keyword.Start..source.KeySpan.Start] : string.Empty;
        return new Template(
            _text[source.Start..indentEnd],
            export,
            _text[source.KeySpan.End..source.EqualsStart],
            _text[(source.EqualsStart + 1)..source.ValueSpan.Start]);
    }

    /// <summary>
    /// Checks the key of an insert, relative to the parent. The full key must be a valid .env key, and it must split into the
    /// parent's segments followed by at least one more, by the rule the document tree uses.
    /// </summary>
    private static string ValidateInsertKey(IReadOnlyList<string> parentNames, string key)
    {
        string full = string.Join(Separator, parentNames.Append(key));
        if (!EnvKey.IsValid(full))
        {
            throw new CfgeditException($"invalid key '{key}'");
        }

        string[] names = SplitKey(full);
        if (names.Length <= parentNames.Count || !names.Take(parentNames.Count).SequenceEqual(parentNames))
        {
            throw new CfgeditException("a key part must be a valid name without __ and must not start or end with _");
        }

        return full;
    }

    /// <summary>
    /// Checks one segment of a key that is renamed, under the parent's segments. A segment is a valid name with no <c>__</c> and no
    /// leading or trailing underscore, so renaming it never changes where the key splits.
    /// </summary>
    private static void ValidateSegment(IReadOnlyList<string> parentNames, string segment)
    {
        if (segment.Length == 0 || segment.Contains(Separator, StringComparison.Ordinal) || segment[0] == '_' || segment[^1] == '_')
        {
            throw new CfgeditException("a key part must be a valid name without __ and must not start or end with _");
        }

        if (!EnvKey.IsValid(string.Join(Separator, parentNames.Append(segment))))
        {
            throw new CfgeditException($"invalid key '{segment}'");
        }

        if (parentNames.Count + 1 > JsonDocument.MaxDepth)
        {
            throw new CfgeditException($"a key can have at most {JsonDocument.MaxDepth} parts");
        }
    }

    private static string[] SplitKey(string key) => EnvModelBuilder.SplitKey(key);

    // ----- DeleteNode -----

    private void PlanDelete(DeleteNode op)
    {
        DocNode node = Node(op.Node);
        DocNode parent = node.Parent ?? throw new CfgeditException("the root cannot be deleted");
        if (parent.Kind == NodeKind.Document)
        {
            throw new CfgeditException("the root cannot be deleted");
        }

        int index = IndexOf(parent, node);
        switch (node.Kind)
        {
            case NodeKind.EnvEntry:
            {
                // The entry's block: its attached comment lines above it, and its line with the trailing comment.
                int start = parent.Children[EnvLayout.Block(parent, index).First].Start;
                Replace(start, node.End, string.Empty);
                return;
            }
            case NodeKind.EnvComment when EnvLayout.IsTrailing(parent.Children, index):
            {
                // A trailing comment goes with the spaces before its '#'; the entry's line break stays.
                int start = node.Start;
                while (start > 0 && IsBlank(_text[start - 1]))
                {
                    start--;
                }

                Replace(start, node.End, string.Empty);
                return;
            }
            case NodeKind.EnvComment:
                Replace(node.Start, node.End, string.Empty);
                return;
            case NodeKind.EnvGroup:
                DeleteGroup(node);
                return;
            default:
                throw Uneditable();
        }
    }

    /// <summary>Deletes every line of a group, one edit for each run of lines that touch each other.</summary>
    private void DeleteGroup(DocNode group)
    {
        List<EnvItem> items = ItemsOf(group).OrderBy(item => item.Start).ToList();
        for (int i = 0; i < items.Count;)
        {
            int start = items[i].Start;
            int end = items[i].End;
            i++;
            while (i < items.Count && items[i].Start == end)
            {
                end = items[i].End;
                i++;
            }

            Replace(start, end, string.Empty);
        }
    }

    // ----- MoveNode -----

    /// <summary>
    /// Moves a unit: the node's sibling group (its attached comment lines, its entry and its trailing comment; for a group, all its
    /// lines). Its lines are cut and put back as one block before the first line of the sibling at the destination, or after the last
    /// line of the last sibling. The moved lines keep their text. The file keeps its way of ending: a block that becomes the last
    /// one loses its line break, and a block that was the last one and moves up gets one.
    /// </summary>
    private void PlanMove(MoveNode op)
    {
        DocNode node = Node(op.Node);
        DocNode parent = Parent(node);
        if (parent.Kind == NodeKind.Document)
        {
            throw new CfgeditException("the root cannot be moved");
        }

        int index = IndexOf(parent, node);
        if (node.Kind == NodeKind.EnvComment && EnvLayout.IsTrailing(parent.Children, index))
        {
            throw new CfgeditException("a trailing comment moves with its entry");
        }

        (int first, int last) = EditLayout.Group(parent, index, _text);
        LineSpan unit = Contiguous(parent, first, last, MovedNotContiguous);
        List<DocNode> rest = parent.Children.Where((_, k) => k < first || k > last).ToList();
        int target = Math.Clamp(op.NewIndex, 0, rest.Count);
        if (target == first)
        {
            return;
        }

        int destinationIndex = target < rest.Count ? IndexOf(parent, rest[target]) : parent.Children.Count;
        int destination = target < rest.Count ? BeforeSibling(parent, rest, target) : AfterSiblings(parent, rest[^1]);
        if (destinationIndex > last)
        {
            RequireNextTo(parent, last + 1, destinationIndex - 1, unit, destination);
        }
        else
        {
            RequireNextTo(parent, destinationIndex, first - 1, unit, destination);
        }

        if (destination == unit.Start || destination == unit.End)
        {
            return;
        }

        string text = _text;
        int length = text.Length;
        bool lacksFinalBreak = unit.End == length && !EndsWithBreak(text);
        string block = text[unit.Start..unit.End];
        if (destination < unit.Start)
        {
            if (lacksFinalBreak)
            {
                // The block was the file's last line. It gets a line break, and the break that ended the line before it goes, so
                // the file still ends without one. A blank line before it would be the last line after that, which would end the
                // file with a break; the blank line would have to go, so the move is refused instead.
                int cut = unit.Start - BreakLengthBefore(unit.Start);
                if (cut > 0 && IsBreak(text[cut - 1]))
                {
                    throw new CfgeditException(BlankLineWouldGo);
                }

                Insert(destination, block + _newline);
                Replace(unit.Start - BreakLengthBefore(unit.Start), unit.End, string.Empty);
                return;
            }

            Insert(destination, block);
            Replace(unit.Start, unit.End, string.Empty);
            return;
        }

        // Moved down: the block lands after the destination's line. At the end of a file without a final line break, it goes after
        // a line break and gives up its own.
        Insert(destination, destination == length && !EndsWithBreak(text) ? _newline + WithoutBreak(block) : block);
        Replace(unit.Start, unit.End, string.Empty);
    }

    /// <summary>
    /// A move passes only the siblings that are next to the unit in the file: every line between the unit and the destination must
    /// belong to a passed sibling. Blank lines between them do not count.
    /// </summary>
    private void RequireNextTo(DocNode parent, int passedFirst, int passedLast, LineSpan unit, int destination)
    {
        var passed = new HashSet<EnvItem>();
        for (int k = passedFirst; k <= passedLast; k++)
        {
            foreach (EnvItem item in ItemsOf(parent.Children[k]))
            {
                passed.Add(item);
            }
        }

        int from = destination < unit.Start ? destination : unit.End;
        int to = destination < unit.Start ? unit.Start : destination;
        bool clear = _syntax.Items.All(item => item is EnvBlankLine || item.Start < from || item.Start >= to || passed.Contains(item));
        if (!clear)
        {
            throw new CfgeditException(PassesOtherLines);
        }
    }

    /// <summary>
    /// The offset of the first line of the sibling at <paramref name="target"/> of the list without the moved unit, after the checks
    /// that the move does not separate a comment from the entry it is attached to.
    /// </summary>
    private int BeforeSibling(DocNode parent, List<DocNode> rest, int target)
    {
        DocNode sibling = rest[target];
        int siblingIndex = IndexOf(parent, sibling);
        if (EnvLayout.IsTrailing(parent.Children, siblingIndex))
        {
            throw new CfgeditException("this cannot move there: it would come between an entry and its trailing comment");
        }

        if (target > 0)
        {
            DocNode above = rest[target - 1];
            bool attached = above.Kind == NodeKind.EnvComment
                && !EnvLayout.IsTrailing(parent.Children, IndexOf(parent, above))
                && above.End == sibling.Start;
            if (attached)
            {
                throw new CfgeditException("this cannot move there: it would come between a comment and the entry it is attached to");
            }
        }

        (int first, int last) = EditLayout.Group(parent, siblingIndex, _text);
        Contiguous(parent, first, last, NeighbourNotContiguous);
        return sibling.Start;
    }

    /// <summary>The offset just after the lines of the last sibling, when the move goes to the end.</summary>
    private int AfterSiblings(DocNode parent, DocNode sibling)
    {
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, sibling), _text);
        return Contiguous(parent, first, last, NeighbourNotContiguous).End;
    }

    // ----- DuplicateNode -----

    /// <summary>
    /// Copies a unit (see <see cref="PlanMove"/>) right after itself. The copy's last key segment gets the suffix <c>_copy</c>, then
    /// <c>_copy2</c> and so on, unique among the siblings; for a group, the segment is renamed in every key under it. A comment line
    /// is copied as it is. A file without a final line break gets one after the original, and the copy ends without one.
    /// </summary>
    private void PlanDuplicate(DuplicateNode op)
    {
        DocNode node = Node(op.Node);
        DocNode parent = Parent(node);
        if (parent.Kind == NodeKind.Document)
        {
            throw new CfgeditException("the root cannot be duplicated");
        }

        int index = IndexOf(parent, node);
        if (node.Kind == NodeKind.EnvComment && EnvLayout.IsTrailing(parent.Children, index))
        {
            throw new CfgeditException("a trailing comment is duplicated with its entry");
        }

        (int first, int last) = EditLayout.Group(parent, index, _text);
        LineSpan unit = Contiguous(parent, first, last, "this cannot be copied: its lines are not next to each other in the file");
        var renames = new List<(int Start, int End, string Text)>();
        if (node.Kind is NodeKind.EnvEntry or NodeKind.EnvGroup)
        {
            string segment = SegmentOf(node);
            string fresh = EnvLayout.CopySegment(EnvLayout.Segments(parent), segment);
            int offset = SegmentOffset(parent);
            foreach (EnvEntry entry in ItemsOf(node).OfType<EnvEntry>())
            {
                string copyKey = entry.Key[..offset] + fresh + entry.Key[(offset + segment.Length)..];
                if (!EnvKey.IsValid(copyKey))
                {
                    throw new CfgeditException($"invalid key '{copyKey}'");
                }

                int start = entry.KeySpan.Start + offset;
                renames.Add((start, start + segment.Length, fresh));
            }
        }

        string copy = WithRenames(unit, renames);
        if (unit.End == _text.Length && !EndsWithBreak(_text))
        {
            Insert(_text.Length, _newline + copy);
            return;
        }

        Insert(unit.End, copy);
    }

    // ----- Move and copy helpers -----

    /// <summary>
    /// The lines of the children [first, last] of a parent, as one span. The span must hold no line of another node, blank lines
    /// included: a unit whose lines are not next to each other cannot move or copy.
    /// </summary>
    private LineSpan Contiguous(DocNode parent, int first, int last, string message)
    {
        var lines = new HashSet<EnvItem>();
        int start = int.MaxValue;
        int end = int.MinValue;
        for (int k = first; k <= last; k++)
        {
            foreach (EnvItem item in ItemsOf(parent.Children[k]))
            {
                lines.Add(item);
                start = Math.Min(start, item.Start);
                end = Math.Max(end, item.End);
            }
        }

        bool whole = _syntax.Items.All(item => item.Start < start || item.Start >= end || lines.Contains(item));
        return whole ? new LineSpan(start, end) : throw new CfgeditException(message);
    }

    /// <summary>The text of a span with the given replacements applied. The replacements lie inside the span, in any order.</summary>
    private string WithRenames(LineSpan span, List<(int Start, int End, string Text)> renames)
    {
        var builder = new StringBuilder();
        int at = span.Start;
        foreach ((int start, int end, string replacement) in renames.OrderBy(rename => rename.Start))
        {
            builder.Append(_text, at, start - at).Append(replacement);
            at = end;
        }

        builder.Append(_text, at, span.End - at);
        return builder.ToString();
    }

    /// <summary>The length of the key text before the segments of a node's own parent, separators included.</summary>
    private static int SegmentOffset(DocNode parent)
    {
        return NamesOf(parent.Path).Sum(segment => segment.Length + Separator.Length);
    }

    /// <summary>The length of the line break that ends the line before an offset: two for CRLF, otherwise one.</summary>
    private int BreakLengthBefore(int offset)
    {
        return offset >= 2 && _text[offset - 1] == '\n' && _text[offset - 2] == '\r' ? 2 : 1;
    }

    private static bool EndsWithBreak(string text)
    {
        return text.Length == 0 || IsBreak(text[^1]);
    }

    private static bool IsBreak(char c)
    {
        return c is '\r' or '\n';
    }

    private static string WithoutBreak(string block)
    {
        return block.EndsWith("\r\n", StringComparison.Ordinal) ? block[..^2] : block[..^1];
    }

    // ----- Helpers -----

    private IReadOnlyList<EnvItem> ItemsOf(DocNode node)
    {
        return EnvModelBuilder.ItemsOf(_syntax, node);
    }

    private DocNode Node(NodePath path)
    {
        return _document.Resolve(path) ?? throw new CfgeditException($"There is no node at {path}");
    }

    private static DocNode Parent(DocNode node)
    {
        return node.Parent ?? throw new CfgeditException("the document node cannot be edited this way");
    }

    private EnvEntry Entry(DocNode node)
    {
        return _entryAt.GetValueOrDefault(node.Start) ?? throw new CfgeditException("The node is not an entry");
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

        throw new CfgeditException("The node is not a child of its parent");
    }

    /// <summary>The key segments of a group or entry path, from the root down: every member step's key.</summary>
    private static List<string> NamesOf(NodePath path)
    {
        return path.Steps.OfType<MemberStep>().Select(step => step.Key).ToList();
    }

    private static string SegmentOf(DocNode node)
    {
        return SegmentOrNull(node) ?? throw new CfgeditException("The node has no key");
    }

    private static string? SegmentOrNull(DocNode node)
    {
        return node.Path.Steps[^1] is MemberStep step ? step.Key : null;
    }

    private static bool IsBlank(char c)
    {
        return c is ' ' or '\t';
    }

    private static CfgeditException Uneditable()
    {
        return new CfgeditException("this part cannot be edited");
    }

    private void Replace(int start, int end, string text)
    {
        _edits.Add(new TextEdit(start, end, text));
    }

    private void Insert(int offset, string text)
    {
        _edits.Add(new TextEdit(offset, offset, text));
    }

    /// <summary>The formatting a new line copies: its leading spaces, an <c>export</c> keyword with its spaces, and the spaces around <c>=</c>.</summary>
    private sealed record Template(string Indent, string Export, string SpaceBefore, string SpaceAfter)
    {
        public static Template Plain { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
    }

    /// <summary>The offsets of a unit's lines: from the start of its first line to the end of its last one.</summary>
    private readonly record struct LineSpan(int Start, int End);
}
