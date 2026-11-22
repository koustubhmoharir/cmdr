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
            case MoveNode:
                throw new CfgeditException("moving is not supported in .env yet");
            case DuplicateNode:
                throw new CfgeditException("duplicating is not supported in .env yet");
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
        int offset = parentNames.Sum(segment => segment.Length + Separator.Length);
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
        return node.Parent ?? throw new CfgeditException("The document node cannot be edited this way");
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
        return new CfgeditException("This part cannot be edited");
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
}
