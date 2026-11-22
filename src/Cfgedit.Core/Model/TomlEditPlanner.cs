using System.Text.RegularExpressions;
using Cfgedit.Core.Toml;

namespace Cfgedit.Core.Model;

/// <summary>
/// Plans the text edits for an operation on a TOML document. It works on the document tree, the TOML model (the pieces that define
/// each node) and the source text. Every edit replaces a span of the old text, and the spans it leaves alone keep their bytes.
/// </summary>
/// <remarks>
/// Renames and deletes find the key parts and the lines that belong to a node through the key-bearing syntax (headers, key/value
/// lines and inline members). A key part belongs to the node of the key segment it names: each key part is matched to the node at
/// its position, the named ancestors of the node that holds the key (the leaf). That finds every header, dotted key and inline key
/// that names a table, including deeper headers that only pass through it.
/// </remarks>
internal sealed class TomlEditPlanner
{
    private static readonly Regex CommaGap = new(@"\A[ \t]*,[ \t]*\z", RegexOptions.CultureInvariant);

    private readonly ConfigDocument _document;
    private readonly TomlModel _model;
    private readonly string _text;
    private readonly string _newline;
    private readonly DocNode _rootTable;
    private readonly List<TextEdit> _edits = [];

    /// <summary>The document nodes that each piece of text belongs to: a piece can define more than one node.</summary>
    private readonly Dictionary<TomlNode, List<DocNode>> _owners = [];

    /// <summary>The node that a top-level header or key/value, or an inline member, defines (its leaf).</summary>
    private readonly Dictionary<TomlNode, DocNode> _leaves = [];

    /// <summary>The key parts that name each node.</summary>
    private readonly Dictionary<DocNode, List<TomlKeyPart>> _keyParts = [];

    /// <summary>The array items of every static array, by the offset where their value starts.</summary>
    private readonly Dictionary<int, TomlArrayItem> _arrayItems = [];

    /// <summary>Creates a planner for a parsed TOML document.</summary>
    public TomlEditPlanner(ConfigDocument document)
    {
        _document = document;
        _model = (TomlModel)document.Syntax;
        _text = document.Source.Text;
        _newline = document.Source.Newline;
        _rootTable = document.Root.Children.Count > 0 ? document.Root.Children[0] : throw Internal("the document has no root table");
        IndexOwners(document.Root);
        foreach (TomlItem item in _model.Syntax.Items)
        {
            switch (item)
            {
                case TomlHeaderItem header:
                    AddKeyed(header, header.Key, null);
                    break;
                case TomlKeyValueItem keyValue:
                    AddKeyed(keyValue, keyValue.Key, keyValue.Value);
                    IndexValue(keyValue.Value);
                    break;
            }
        }
    }

    /// <summary>Plans an operation. Throws <see cref="CfgeditException"/> when the operation is invalid or not supported.</summary>
    public IReadOnlyList<TextEdit> Plan(EditOp op)
    {
        switch (op)
        {
            case SetPart set:
                PlanSetPart(set);
                break;
            case DeleteNode delete:
                PlanDelete(delete);
                break;
            case ChangeTomlType change:
                PlanChangeType(change);
                break;
            case InsertNode:
                throw new CfgeditException("adding is not supported in TOML yet");
            case DuplicateNode:
                throw new CfgeditException("duplicating is not supported in TOML yet");
            case MoveNode:
                throw new CfgeditException("moving is not supported in TOML yet");
            case ChangeJsonType:
                throw new CfgeditException("use the TOML type change");
            case InsertAttribute or DeleteAttribute or MoveAttribute:
                throw new CfgeditException("TOML values have no attributes");
            default:
                throw new CfgeditException("this edit is not supported for TOML");
        }

        return _edits;
    }

    // ----- Operations -----

    private void PlanSetPart(SetPart op)
    {
        DocNode node = Node(op.Node);
        switch (op.Part)
        {
            case PartKind.Key:
                PlanRename(node, op.Value);
                return;
            case PartKind.Value:
                PlanSetValue(node, op.Value);
                return;
            case PartKind.Comment when node.Kind == NodeKind.TomlComment:
                PlanSetComment(node, op.Value);
                return;
            default:
                throw Uneditable();
        }
    }

    private void PlanRename(DocNode node, string name)
    {
        DocPart key = Part(node, PartKind.Key) ?? throw Uneditable();
        if (name == key.Value)
        {
            return;
        }

        DocNode parent = Parent(node);
        foreach (DocNode sibling in parent.Children)
        {
            if (!ReferenceEquals(sibling, node) && Part(sibling, PartKind.Key) is { } other && other.Value == name)
            {
                throw new CfgeditException($"a key named '{name}' already exists here");
            }
        }

        string token = TomlEncoder.EncodeKey(name);
        if (!_keyParts.TryGetValue(node, out List<TomlKeyPart>? parts) || !parts.Any(part => part.Start == key.Start && part.End == key.End))
        {
            throw Internal("the key of " + node.Path + " is not among the key parts");
        }

        foreach (TomlKeyPart part in parts)
        {
            Replace(part.Start, part.End, token);
        }
    }

    private void PlanSetValue(DocNode node, string value)
    {
        if (node.Kind != NodeKind.TomlScalar)
        {
            throw Uneditable();
        }

        TomlValue syntax = ValueSyntax(node) ?? throw Uneditable();
        string current = _text[syntax.Start..syntax.End];
        string token;
        switch (node.TomlKind)
        {
            case TomlValueKind.String:
                token = TomlEncoder.EncodeStringKeeping(value, ((TomlString)syntax).Form, _newline);
                break;
            case TomlValueKind.Integer:
                RequireInteger(value);
                token = value;
                break;
            case TomlValueKind.Float:
                RequireFloat(value);
                token = value;
                break;
            case TomlValueKind.Boolean:
                RequireBoolean(value);
                token = value;
                break;
            case TomlValueKind.OffsetDateTime or TomlValueKind.LocalDateTime or TomlValueKind.LocalDate or TomlValueKind.LocalTime:
                RequireDateTime(value);
                token = value;
                break;
            default:
                throw Uneditable();
        }

        if (token != current)
        {
            Replace(syntax.Start, syntax.End, token);
        }
    }

    private void PlanSetComment(DocNode comment, string text)
    {
        DocPart part = Part(comment, PartKind.Comment) ?? throw Uneditable();
        if (text == part.Value)
        {
            return;
        }

        foreach (char c in text)
        {
            if (c is '\r' or '\n')
            {
                throw new CfgeditException("a comment cannot contain a line break");
            }

            if ((c < 0x20 && c != '\t') || c == 0x7F)
            {
                throw new CfgeditException("a comment cannot contain control characters other than tab");
            }
        }

        Replace(part.Start, part.End, text);
    }

    private void PlanChangeType(ChangeTomlType change)
    {
        DocNode node = Node(change.Node);
        if (node.Kind == NodeKind.Document || node.Parent?.Kind == NodeKind.Document)
        {
            throw new CfgeditException("the root table cannot change type");
        }

        TomlValueKind? scalarKind = null;
        bool isTable = false;
        bool isArray = false;
        switch (node.Kind)
        {
            case NodeKind.TomlScalar:
                scalarKind = node.TomlKind;
                break;
            case NodeKind.TomlTable when IsInline(node):
                isTable = true;
                break;
            case NodeKind.TomlArray when !IsTableArray(node):
                isArray = true;
                break;
            case NodeKind.TomlTable:
                throw new CfgeditException("a table defined with [headers] or dotted keys cannot change type; delete it instead");
            case NodeKind.TomlArray:
                throw new CfgeditException("an array of tables cannot change type; delete it instead");
            default:
                throw new CfgeditException("only a value has a type");
        }

        TomlValue syntax = ValueSyntax(node) ?? throw Uneditable();
        string text;
        switch (change.Kind)
        {
            case TomlNewKind.String:
                if (scalarKind == TomlValueKind.String && change.Value is null)
                {
                    return;
                }

                text = TomlEncoder.EncodeString(change.Value ?? string.Empty, _newline);
                break;
            case TomlNewKind.Integer:
                if (scalarKind == TomlValueKind.Integer && change.Value is null)
                {
                    return;
                }

                text = change.Value ?? "0";
                RequireInteger(text);
                break;
            case TomlNewKind.Float:
                if (scalarKind == TomlValueKind.Float && change.Value is null)
                {
                    return;
                }

                text = change.Value ?? "0.0";
                RequireFloat(text);
                break;
            case TomlNewKind.Boolean:
                if (scalarKind == TomlValueKind.Boolean && change.Value is null)
                {
                    return;
                }

                text = change.Value ?? "false";
                RequireBoolean(text);
                break;
            case TomlNewKind.DateTime:
                if (IsDateTime(scalarKind) && change.Value is null)
                {
                    return;
                }

                text = change.Value ?? throw new CfgeditException("a date-time needs a value");
                RequireDateTime(text);
                break;
            case TomlNewKind.Table:
                if (isTable)
                {
                    return;
                }

                text = "{}";
                break;
            case TomlNewKind.Array:
                if (isArray)
                {
                    return;
                }

                text = "[]";
                break;
            default:
                throw new CfgeditException("this type is not supported for TOML");
        }

        if (_text[syntax.Start..syntax.End] != text)
        {
            Replace(syntax.Start, syntax.End, text);
        }
    }

    private void PlanDelete(DeleteNode op)
    {
        DocNode node = Node(op.Node);
        DocNode parent = Parent(node);
        if (parent.Kind == NodeKind.Document)
        {
            throw new CfgeditException("the root table cannot be deleted");
        }

        if (node.Kind == NodeKind.TomlComment)
        {
            DeleteComment(node);
        }
        else if (IsStaticArray(parent))
        {
            DeleteArrayItem(parent, node);
        }
        else if (IsInline(parent))
        {
            DeleteInlineMembers(parent, node);
        }
        else
        {
            DeleteLines(node);
        }
    }

    // ----- Deleting -----

    /// <summary>A comment alone on its line goes with its line. A trailing comment goes with the spaces before it.</summary>
    private void DeleteComment(DocNode comment)
    {
        if (EditLayout.StartsLine(_text, comment.Start) && EditLayout.RestOfLineBlank(_text, comment.End))
        {
            Replace(EditLayout.LineStart(_text, comment.Start), EditLayout.AfterLineBreak(_text, comment.End), string.Empty);
        }
        else
        {
            Replace(HorizontalStart(comment.Start), comment.End, string.Empty);
        }
    }

    /// <summary>
    /// Deletes the lines of a node in a table, or of an array of tables: every header and key/value line whose leaf is the node or
    /// lies below it, with the comment lines attached above them, and the standalone comment lines of the sections below it.
    /// </summary>
    private void DeleteLines(DocNode node)
    {
        var doomed = new List<TomlItem>();
        var pending = new List<TomlItem>();
        DocNode section = _rootTable;
        foreach (TomlItem item in _model.Syntax.Items)
        {
            if (item is TomlCommentItem)
            {
                pending.Add(item);
                continue;
            }

            if (item is TomlHeaderItem header)
            {
                section = _leaves[header];
            }

            if (item is TomlHeaderItem or TomlKeyValueItem)
            {
                if (InSubtree(_leaves[item], node))
                {
                    doomed.AddRange(pending);
                    doomed.Add(item);
                }
            }
            else if (InSubtree(section, node))
            {
                // A run of comments followed by a blank line stands alone in the section it is in.
                doomed.AddRange(pending);
            }

            pending.Clear();
        }

        if (InSubtree(section, node))
        {
            doomed.AddRange(pending);
        }

        foreach (TomlItem item in doomed)
        {
            Replace(item.Start, item.End, string.Empty);
        }
    }

    /// <summary>
    /// Deletes an item of a static array with its comments: the lines it stands on when it has lines of its own, else the text from it
    /// to the next item, or from the previous item's comma when it is last. Its comma goes with it.
    /// </summary>
    private void DeleteArrayItem(DocNode array, DocNode element)
    {
        IReadOnlyList<DocNode> children = array.Children;
        int index = IndexOf(array, element);
        (int first, int last) = EditLayout.Group(array, index, _text);
        TomlArrayItem item = ArrayItemOf(element);
        int start = children[first].Start;
        int end = Math.Max(children[last].End, item.Comma?.End ?? item.Value.End);

        if (EditLayout.StartsLine(_text, start) && EditLayout.RestOfLineBlank(_text, end))
        {
            Replace(EditLayout.LineStart(_text, start), EditLayout.AfterLineBreak(_text, end), string.Empty);
            return;
        }

        int next = last + 1;
        while (next < children.Count && EditLayout.IsComment(children[next]))
        {
            next++;
        }

        int previous = first - 1;
        while (previous >= 0 && EditLayout.IsComment(children[previous]))
        {
            previous--;
        }

        if (next < children.Count)
        {
            int nextStart = children[next].Start;
            if (IsHorizontalSpace(end, nextStart))
            {
                Replace(start, nextStart, string.Empty);
            }
            else
            {
                Replace(HorizontalStart(start), end, string.Empty);
            }

            return;
        }

        if (item.Comma is not null || previous < 0)
        {
            // A trailing comma stays with the array, and a sole item leaves the brackets.
            Replace(HorizontalStart(start), end, string.Empty);
            return;
        }

        TomlArrayItem before = ArrayItemOf(children[previous]);
        TomlSpan comma = before.Comma ?? throw Internal("an array item before the last has no comma");
        if (CommaGap.IsMatch(_text[before.Value.End..start]))
        {
            Replace(before.Value.End, end, string.Empty);
        }
        else
        {
            // Comments stand between the previous item and this one, so they stay: only the two commas and this item go.
            Replace(comma.Start, comma.End, string.Empty);
            Replace(start, end, string.Empty);
        }
    }

    /// <summary>
    /// Deletes the members of an inline table that a node names: the node's member and the members below it. A run of removed members
    /// goes with the text up to the next kept member, or from the comma of the kept member before it when it is last.
    /// </summary>
    private void DeleteInlineMembers(DocNode table, DocNode node)
    {
        TomlInlineTable syntax = ValueSyntax(table) as TomlInlineTable ?? throw Internal("an inline table has no syntax");
        var members = syntax.Entries.OfType<TomlInlineMember>().ToList();
        var removed = new bool[members.Count];
        bool any = false;
        for (int i = 0; i < members.Count; i++)
        {
            removed[i] = InSubtree(_leaves[members[i]], node);
            any |= removed[i];
        }

        if (!any)
        {
            throw Internal("no member of the inline table is the node");
        }

        int k = 0;
        while (k < members.Count)
        {
            if (!removed[k])
            {
                k++;
                continue;
            }

            int first = k;
            while (k + 1 < members.Count && removed[k + 1])
            {
                k++;
            }

            int last = k;
            k++;
            if (last + 1 < members.Count)
            {
                Replace(members[first].Start, members[last + 1].Start, string.Empty);
            }
            else if (first > 0)
            {
                TomlSpan comma = members[first - 1].Comma ?? throw Internal("an inline member before the last has no comma");
                Replace(comma.Start, members[last].End, string.Empty);
            }
            else
            {
                Replace(members[first].Start, members[last].End, string.Empty);
            }
        }
    }

    // ----- Index of the syntax and the document tree -----

    private void IndexOwners(DocNode root)
    {
        var stack = new Stack<DocNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            DocNode node = stack.Pop();
            foreach (TomlNode piece in _model.PiecesOf(node))
            {
                if (!_owners.TryGetValue(piece, out List<DocNode>? nodes))
                {
                    nodes = [];
                    _owners[piece] = nodes;
                }

                nodes.Add(node);
            }

            foreach (DocNode child in node.Children)
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>Records the array items and the inline members below a value, which are key-bearing items of their own.</summary>
    private void IndexValue(TomlValue value)
    {
        switch (value)
        {
            case TomlInlineTable inline:
                foreach (TomlInlineMember member in inline.Entries.OfType<TomlInlineMember>())
                {
                    AddKeyed(member, member.Key, member.Value);
                    IndexValue(member.Value);
                }

                break;
            case TomlArray array:
                foreach (TomlArrayItem item in array.Entries.OfType<TomlArrayItem>())
                {
                    _arrayItems[item.Value.Start] = item;
                    IndexValue(item.Value);
                }

                break;
        }
    }

    /// <summary>
    /// Finds the leaf of a key-bearing item and records the key parts that name each node. The leaf is the deepest node that the
    /// item or its value belongs to. The parts are matched to the named nodes from the leaf upward.
    /// </summary>
    private void AddKeyed(TomlNode item, TomlKey key, TomlValue? value)
    {
        DocNode leaf = Deepest(item, value);
        _leaves[item] = leaf;
        int count = key.Parts.Count;
        var chain = new List<DocNode>();
        for (DocNode? node = leaf; node is not null && chain.Count < count; node = node.Parent)
        {
            if (node.Path.Count > 0 && node.Path.Steps[^1] is MemberStep)
            {
                chain.Add(node);
            }
        }

        if (chain.Count < count)
        {
            throw Internal("a key has fewer named nodes than parts");
        }

        for (int j = 0; j < count; j++)
        {
            DocNode target = chain[count - 1 - j];
            TomlKeyPart part = key.Parts[j];
            if (((MemberStep)target.Path.Steps[^1]).Key != part.Name)
            {
                throw Internal("a key part does not match its node");
            }

            if (!_keyParts.TryGetValue(target, out List<TomlKeyPart>? parts))
            {
                parts = [];
                _keyParts[target] = parts;
            }

            parts.Add(part);
        }
    }

    private DocNode Deepest(TomlNode item, TomlValue? value)
    {
        DocNode? best = null;
        TomlNode[] pieces = value is null ? [item] : [item, value];
        foreach (TomlNode piece in pieces)
        {
            if (!_owners.TryGetValue(piece, out List<DocNode>? nodes))
            {
                continue;
            }

            foreach (DocNode node in nodes)
            {
                if (best is null || node.Path.Count > best.Path.Count)
                {
                    best = node;
                }
            }
        }

        return best ?? throw Internal("a key has no node");
    }

    private TomlArrayItem ArrayItemOf(DocNode element)
    {
        return _arrayItems.TryGetValue(element.Start, out TomlArrayItem? item) ? item : throw Internal("an array element has no item");
    }

    /// <summary>The syntax of a node's value: its key/value's value, an inline member's value, an array item's value, or the inline table itself.</summary>
    private TomlValue? ValueSyntax(DocNode node)
    {
        IReadOnlyList<TomlNode> pieces = _model.PiecesOf(node);
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

    private bool IsInline(DocNode table)
    {
        IReadOnlyList<TomlNode> pieces = _model.PiecesOf(table);
        return pieces.Count > 0 && pieces[0] is TomlInlineTable;
    }

    private bool IsTableArray(DocNode array)
    {
        IReadOnlyList<TomlNode> pieces = _model.PiecesOf(array);
        return array.Kind == NodeKind.TomlArray && pieces.Count > 0 && pieces[0] is TomlHeaderItem;
    }

    private bool IsStaticArray(DocNode node)
    {
        return node.Kind == NodeKind.TomlArray && !IsTableArray(node);
    }

    private static bool IsDateTime(TomlValueKind? kind)
    {
        return kind is TomlValueKind.OffsetDateTime or TomlValueKind.LocalDateTime or TomlValueKind.LocalDate or TomlValueKind.LocalTime;
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

    // ----- Value checks -----

    private static void RequireInteger(string text)
    {
        if (!TomlEncoder.IsValidInteger(text))
        {
            throw new CfgeditException($"'{text}' is not a valid integer");
        }
    }

    private static void RequireFloat(string text)
    {
        if (!TomlEncoder.IsValidFloat(text))
        {
            throw new CfgeditException($"'{text}' is not a valid float");
        }
    }

    private static void RequireBoolean(string text)
    {
        if (text is not ("true" or "false"))
        {
            throw new CfgeditException($"'{text}' is not a valid boolean (true or false)");
        }
    }

    private static void RequireDateTime(string text)
    {
        if (!TomlEncoder.TryParseDateTime(text, out _))
        {
            throw new CfgeditException($"'{text}' is not a valid date-time");
        }
    }

    // ----- Text and tree helpers -----

    private DocNode Node(NodePath path)
    {
        return _document.Resolve(path) ?? throw new CfgeditException($"There is no node at {path}");
    }

    private static DocNode Parent(DocNode node)
    {
        return node.Parent ?? throw new CfgeditException("the document node cannot be edited this way");
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

        throw Internal("a node is not a child of its parent");
    }

    private static DocPart? Part(DocNode node, PartKind kind)
    {
        foreach (DocPart part in node.Parts)
        {
            if (part.Kind == kind)
            {
                return part;
            }
        }

        return null;
    }

    /// <summary>The offset of the first space or tab that precedes an offset on its line, or the offset itself.</summary>
    private int HorizontalStart(int offset)
    {
        int i = offset;
        while (i > 0 && _text[i - 1] is ' ' or '\t')
        {
            i--;
        }

        return i;
    }

    /// <summary>True when the text in [from, to) holds only spaces and tabs.</summary>
    private bool IsHorizontalSpace(int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (_text[i] is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    private void Replace(int start, int end, string text)
    {
        _edits.Add(new TextEdit(start, end, text));
    }

    private static CfgeditException Uneditable()
    {
        return new CfgeditException("this part cannot be edited");
    }

    private static CfgeditException Internal(string detail)
    {
        return new CfgeditException("internal edit error: " + detail + " (please report this)");
    }
}
