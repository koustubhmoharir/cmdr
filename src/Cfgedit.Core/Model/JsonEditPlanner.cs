using System.Globalization;
using Cfgedit.Core.Json;

namespace Cfgedit.Core.Model;

/// <summary>
/// Plans the text edits for an operation on a JSON-family document. It works on the document tree and the source text: the
/// tree says what the nodes are and where their parts are, and the text gives the layout that new text copies. Every edit
/// is a replacement of a span of the old text, and the spans it leaves alone keep their bytes.
/// </summary>
internal sealed class JsonEditPlanner
{
    private readonly ConfigDocument _document;
    private readonly JsonDialect _dialect;
    private readonly string _text;
    private readonly string _newline;
    private readonly string _unit;
    private readonly List<TextEdit> _edits = [];

    /// <summary>Creates a planner for a parsed JSON-family document.</summary>
    public JsonEditPlanner(ConfigDocument document, JsonDialect dialect)
    {
        _document = document;
        _dialect = dialect;
        _text = document.Source.Text;
        _newline = document.Source.Newline;
        _unit = EditLayout.InferIndentUnit(_text);
    }

    /// <summary>Plans an operation. Throws <see cref="CfgeditException"/> when the operation is invalid.</summary>
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
            case ChangeJsonType change:
                PlanChangeType(change);
                break;
            case DuplicateNode duplicate:
                PlanDuplicate(duplicate);
                break;
            case InsertAttribute or DeleteAttribute or MoveAttribute:
                throw new CfgeditException("JSON values have no attributes");
            default:
                throw new CfgeditException("This edit is not supported for JSON");
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
            case PartKind.Comment when node.Kind == NodeKind.JsonComment:
                PlanSetComment(node, op.Value);
                return;
            default:
                throw Uneditable();
        }
    }

    private void PlanRename(DocNode member, string name)
    {
        DocPart key = Part(member, PartKind.Key) ?? throw Uneditable();
        if (name == key.Value)
        {
            return;
        }

        DocNode parent = Parent(member);
        int index = IndexOf(parent, member);
        if (HasMemberNamed(parent, name, member))
        {
            throw new CfgeditException($"a member named '{name}' already exists");
        }

        string text = JsonSyntax.EncodeKey(name, QuoteOf(key), PreferredKeyQuote(parent, index - 1, index + 1), _dialect);
        Replace(key.Start, key.End, text);
    }

    private void PlanSetValue(DocNode node, string value)
    {
        DocPart part = Part(node, PartKind.Value) ?? throw Uneditable();
        if (!part.Editable)
        {
            throw Uneditable();
        }

        string current = _text[part.Start..part.End];
        switch (node.JsonKind)
        {
            case JsonValueKind.String:
                if (value == part.Value)
                {
                    return;
                }

                Replace(part.Start, part.End, JsonSyntax.EncodeString(value, _text[part.Start], _dialect));
                return;
            case JsonValueKind.Number:
                if (value == current)
                {
                    return;
                }

                RequireNumber(value);
                Replace(part.Start, part.End, value);
                return;
            default:
                if (value == current)
                {
                    return;
                }

                throw new CfgeditException($"'{current}' cannot be changed to '{value}'; change its type instead");
        }
    }

    private void PlanSetComment(DocNode comment, string text)
    {
        DocPart part = Part(comment, PartKind.Comment) ?? throw Uneditable();
        if (text == part.Value)
        {
            return;
        }

        ValidateComment(text, IsBlockComment(comment));
        Replace(part.Start, part.End, text);
    }

    private void PlanInsert(InsertNode op)
    {
        DocNode parent = Node(op.Parent);
        int count = parent.Children.Count;
        if (op.Index < 0 || op.Index > count)
        {
            throw new CfgeditException($"Index {op.Index} is outside the children, 0 to {count}");
        }

        if (parent.Kind == NodeKind.Document)
        {
            if (op.Node is not NewJsonComment documentComment)
            {
                throw new CfgeditException("Only comments can be placed at the document level");
            }

            InsertComment(parent, op.Index, documentComment);
            return;
        }

        if (parent.Kind is not (NodeKind.JsonObject or NodeKind.JsonArray))
        {
            throw new CfgeditException(parent.Kind == NodeKind.JsonScalar ? "A scalar has no children" : "This node cannot have children");
        }

        switch (op.Node)
        {
            case NewJsonComment comment:
                InsertComment(parent, op.Index, comment);
                return;
            case NewJsonValue value:
                InsertValue(parent, op.Index, value);
                return;
            default:
                throw new CfgeditException("This kind of node cannot be inserted into JSON");
        }
    }

    private void PlanDelete(DeleteNode op)
    {
        DocNode node = Node(op.Node);
        DocNode parent = Parent(node);
        if (parent.Kind == NodeKind.Document && !IsComment(node))
        {
            throw new CfgeditException("The root value cannot be deleted");
        }

        int index = IndexOf(parent, node);
        (int first, int last) = EditLayout.Group(parent, index, _text);
        if (IsJsonContainer(parent) && first == 0 && last == parent.Children.Count - 1)
        {
            // The container's only entries go, so it becomes {} or [] on its opening line.
            Replace(OpenOf(parent) + 1, parent.End - 1, string.Empty);
            return;
        }

        bool removeComma = CommaRemovedByDelete(parent, first, last);
        RemoveGroup(parent, first, last, removeComma);
    }

    private void PlanMove(MoveNode op)
    {
        DocNode node = Node(op.Node);
        DocNode parent = Parent(node);
        if (parent.Kind == NodeKind.Document && !IsComment(node))
        {
            throw new CfgeditException("The root value cannot be moved");
        }

        IReadOnlyList<DocNode> children = parent.Children;
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), _text);
        var rest = new List<DocNode>();
        for (int k = 0; k < children.Count; k++)
        {
            if (k < first || k > last)
            {
                rest.Add(children[k]);
            }
        }

        int target = Math.Clamp(op.NewIndex, 0, rest.Count);
        if (target == first)
        {
            return;
        }

        bool json = IsJsonContainer(parent);
        int main = json ? MainEntry(parent, first, last) : -1;
        bool trailing = json && HasTrailingComma(parent);
        int previousEntry = json ? LastEntryBefore(parent, first) : -1;
        bool hasAfter = AnyEntryAfter(parent, last);
        bool insertedAfterPrevious = previousEntry >= 0 && target > previousEntry;
        bool removeComma = main >= 0 && previousEntry >= 0 && !hasAfter && !trailing && !insertedAfterPrevious;
        bool entryAfter = false;
        for (int r = target; r < rest.Count; r++)
        {
            entryAfter |= !IsComment(rest[r]);
        }

        bool needsComma = main >= 0 && (entryAfter || trailing);
        DocNode? previousInsert = null;
        for (int r = target - 1; r >= 0; r--)
        {
            if (!IsComment(rest[r]))
            {
                previousInsert = rest[r];
                break;
            }
        }

        if (main >= 0 && previousInsert is not null && EditLayout.CommaAfter(parent, IndexOf(parent, previousInsert), _text) is null)
        {
            Replace(previousInsert.End, previousInsert.End, ",");
        }

        int start = children[first].Start;
        int end = EditLayout.Extent(parent, last, _text);
        string core = MovedText(parent, main, needsComma, start, end);
        bool lineComment = IsLineComment(children[last]);
        RemoveGroup(parent, first, last, removeComma);
        DocNode? neighbour = target > 0 ? rest[target - 1] : null;
        if (target > first && neighbour is not null && !IsLineComment(neighbour) && !EditLayout.HasLineBreak(_text, end, neighbour.Start))
        {
            // Moved down past a neighbour that was on the group's line: the group stays on that line, after the neighbour.
            int at = EditLayout.Extent(parent, IndexOf(parent, neighbour), _text);
            if (!lineComment || EditLayout.RestOfLineBlank(_text, at))
            {
                Replace(at, at, (main >= 0 ? AfterComma(parent) : " ") + core);
                return;
            }
        }

        if (target < rest.Count)
        {
            // Moved up to a site that was on the group's line: the group goes in front of the site, on that line.
            bool sameLine = target < first && !EditLayout.HasLineBreak(_text, rest[target].End, start);
            PlaceBefore(parent, rest[target], neighbour, core, lineComment, needsComma, sameLine);
        }
        else
        {
            PlaceAfter(parent, rest[^1], core, lineComment, main >= 0);
        }
    }

    private void PlanChangeType(ChangeJsonType op)
    {
        DocNode node = Node(op.Node);
        if (IsComment(node) || node.JsonKind is null)
        {
            throw new CfgeditException("only a value has a type");
        }

        DocNode parent = Parent(node);
        int index = IndexOf(parent, node);
        (int start, int end) = ValueSpan(node);
        string text;
        if (op.Kind == JsonValueKind.String && node.JsonKind == JsonValueKind.String)
        {
            text = JsonSyntax.EncodeString(op.Value ?? string.Empty, _text[start], _dialect);
        }
        else
        {
            text = ValueText(op.Kind, op.Value, parent, index - 1, index + 1);
        }

        if (_text[start..end] == text)
        {
            return;
        }

        Replace(start, end, text);
    }

    private void PlanDuplicate(DuplicateNode op)
    {
        DocNode node = Node(op.Node);
        DocNode parent = Parent(node);
        if (parent.Kind == NodeKind.Document && !IsComment(node))
        {
            throw new CfgeditException("The root value cannot be duplicated");
        }

        IReadOnlyList<DocNode> children = parent.Children;
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), _text);
        int main = IsJsonContainer(parent) ? MainEntry(parent, first, last) : -1;
        int start = children[first].Start;
        int end = EditLayout.Extent(parent, last, _text);
        string core = _text[start..end];
        if (main >= 0)
        {
            DocNode entry = children[main];
            DocPart? key = Part(entry, PartKind.Key);
            if (key is not null)
            {
                string name = FreshKey(parent, key.Value);
                string newKey = JsonSyntax.EncodeKey(name, QuoteOf(key), PreferredKeyQuote(parent, main - 1, main + 1), _dialect);
                core = _text[start..key.Start] + newKey + _text[key.End..end];
            }

            // The copy follows the original, so the original needs a comma.
            if (EditLayout.CommaAfter(parent, main, _text) is null)
            {
                Replace(entry.End, entry.End, ",");
            }
        }

        PlaceAfter(parent, children[last], core, IsLineComment(children[last]), main >= 0);
    }

    // ----- Inserting -----

    private void InsertComment(DocNode parent, int index, NewJsonComment comment)
    {
        ValidateComment(comment.Text, comment.Block);
        string core = comment.Block ? "/*" + comment.Text + "*/" : "//" + comment.Text;
        if (parent.Children.Count == 0)
        {
            EmptyContainerInsert(parent, core, !comment.Block);
            return;
        }

        PlaceAt(parent, index, core, !comment.Block, isEntry: false, commaFollows: false);
    }

    private void InsertValue(DocNode parent, int index, NewJsonValue value)
    {
        bool isObject = parent.Kind == NodeKind.JsonObject;
        if (isObject && value.Key is null)
        {
            throw new CfgeditException("A member needs a key");
        }

        if (!isObject && value.Key is not null)
        {
            throw new CfgeditException("Items of an array have no key");
        }

        if (isObject && HasMemberNamed(parent, value.Key!, null))
        {
            throw new CfgeditException($"a member named '{value.Key}' already exists");
        }

        string valueText = ValueText(value.Kind, value.Value, parent, index - 1, index);
        string text = valueText;
        if (isObject)
        {
            text = NewKeyText(value.Key!, parent, index - 1, index) + KeySeparator(parent, index - 1, index) + valueText;
        }

        bool comma = ExistsEntryFrom(parent, index) || HasTrailingComma(parent);
        int previous = LastEntryBefore(parent, index);
        if (previous >= 0 && EditLayout.CommaAfter(parent, previous, _text) is null)
        {
            DocNode previousNode = parent.Children[previous];
            Replace(previousNode.End, previousNode.End, ",");
        }

        string core = text + (comma ? "," : string.Empty);
        if (parent.Children.Count == 0)
        {
            EmptyContainerInsert(parent, core, lineComment: false);
            return;
        }

        PlaceAt(parent, index, core, lineComment: false, isEntry: true, commaFollows: comma);
    }

    /// <summary>Inserts text into a container with no entries: <c>{}</c> becomes multi-line when the document has a line break.</summary>
    private void EmptyContainerInsert(DocNode container, string core, bool lineComment)
    {
        int open = OpenOf(container);
        int close = container.End - 1;
        string text;
        if (EditLayout.CountLineBreaks(_text, 0, _text.Length) > 0)
        {
            string closingIndent = EditLayout.LineIndent(_text, open);
            text = _newline + closingIndent + _unit + core + _newline + closingIndent;
        }
        else
        {
            text = core + (lineComment ? _newline : string.Empty);
        }

        Replace(open + 1, close, text);
    }

    /// <summary>Places a new piece before the child at <paramref name="index"/>, or after the last child when it is the end.</summary>
    private void PlaceAt(DocNode parent, int index, string core, bool lineComment, bool isEntry, bool commaFollows)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        if (index < children.Count)
        {
            PlaceBefore(parent, children[index], index > 0 ? children[index - 1] : null, core, lineComment, commaFollows);
        }
        else
        {
            PlaceAfter(parent, children[^1], core, lineComment, isEntry);
        }
    }

    /// <summary>
    /// Places a piece before a child. On its own line, the piece takes the line above the child, with the previous sibling's
    /// indentation. Otherwise it goes in front of the child, followed by a comma and the separator (or a line break after a
    /// line comment). <paramref name="sameLine"/> keeps the piece on the child's line even when the child starts a line.
    /// </summary>
    private void PlaceBefore(DocNode parent, DocNode site, DocNode? previous, string core, bool lineComment, bool commaFollows, bool sameLine = false)
    {
        int at = site.Start;
        bool document = parent.Kind == NodeKind.Document;
        if (document || (!sameLine && EditLayout.StartsLine(_text, at)))
        {
            string indent = document ? string.Empty : IndentFor(previous, site, parent);
            int lineStart = EditLayout.LineStart(_text, at);
            Replace(lineStart, lineStart, indent + core + _newline);
            return;
        }

        string tail = lineComment
            ? _newline + EditLayout.LineIndent(_text, at)
            : commaFollows ? AfterComma(parent) : " ";
        Replace(at, at, core + tail);
    }

    /// <summary>
    /// Places a piece after a child, and after its trailing comments. In a multi-line container (and at the document level) the
    /// piece starts a new line; otherwise it follows a separator.
    /// </summary>
    private void PlaceAfter(DocNode parent, DocNode anchor, string core, bool lineComment, bool isEntry)
    {
        int at = EditLayout.Extent(parent, IndexOf(parent, anchor), _text);
        string suffix = lineComment && !EditLayout.RestOfLineBlank(_text, at) ? _newline : string.Empty;
        if (parent.Kind == NodeKind.Document || IsMultiLine(parent))
        {
            string indent = parent.Kind == NodeKind.Document ? string.Empty : IndentFor(anchor, null, parent);
            Replace(at, at, _newline + indent + core + suffix);
            return;
        }

        string prefix = IsLineComment(anchor)
            ? _newline + EditLayout.LineIndent(_text, at)
            : isEntry ? AfterComma(parent) : " ";
        Replace(at, at, prefix + core + suffix);
    }

    // ----- Removing -----

    /// <summary>
    /// True when deleting the entries [first, last] of a container leaves the previous entry as the last one, so that its
    /// comma goes too. A trailing comma style keeps it.
    /// </summary>
    private bool CommaRemovedByDelete(DocNode parent, int first, int last)
    {
        if (!IsJsonContainer(parent))
        {
            return false;
        }

        return MainEntry(parent, first, last) >= 0
            && LastEntryBefore(parent, first) >= 0
            && !AnyEntryAfter(parent, last)
            && !HasTrailingComma(parent);
    }

    /// <summary>
    /// Removes the children [first, last] with their layout: their whole lines when they stand on lines of their own, else the
    /// text from them to the next sibling, or from the previous entry when they are last. A removed previous comma is in
    /// <paramref name="removePreviousComma"/>.
    /// </summary>
    private void RemoveGroup(DocNode parent, int first, int last, bool removePreviousComma)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        int start = children[first].Start;
        int end = EditLayout.Extent(parent, last, _text);
        int previous = IsJsonContainer(parent) ? LastEntryBefore(parent, first) : -1;
        if (EditLayout.StartsLine(_text, start) && EditLayout.RestOfLineBlank(_text, end))
        {
            Replace(EditLayout.LineStart(_text, start), EditLayout.AfterLineBreak(_text, end), string.Empty);
            if (removePreviousComma && previous >= 0)
            {
                int comma = EditLayout.CommaAfter(parent, previous, _text) ?? throw new InvalidOperationException("The previous entry has no comma.");
                Replace(comma, comma + 1, string.Empty);
            }

            return;
        }

        if (AnyEntryAfter(parent, last))
        {
            int next = children[last + 1].Start;
            if (EditLayout.HasLineBreak(_text, end, next))
            {
                // The group ends its line and the next sibling starts another one: the line break stays, so the spaces that
                // separated the group from what comes before it go instead.
                Replace(HorizontalStart(start), end, string.Empty);
                return;
            }

            Replace(start, next, string.Empty);
            return;
        }

        if (previous >= 0)
        {
            int comma = EditLayout.CommaAfter(parent, previous, _text) ?? throw new InvalidOperationException("The previous entry has no comma.");
            if (previous == first - 1)
            {
                if (removePreviousComma)
                {
                    Replace(children[previous].End, end, string.Empty);
                }
                else
                {
                    Replace(comma + 1, end, string.Empty);
                }

                return;
            }

            if (removePreviousComma)
            {
                Replace(comma, comma + 1, string.Empty);
            }

            Replace(children[first - 1].End, end, string.Empty);
            return;
        }

        if (last + 1 < children.Count)
        {
            Replace(start, children[last + 1].Start, string.Empty);
            return;
        }

        Replace(start, end, string.Empty);
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

    /// <summary>The text of a moved group, with its entry's comma set to what the new place needs.</summary>
    private string MovedText(DocNode parent, int main, bool needsComma, int start, int end)
    {
        if (main < 0)
        {
            return _text[start..end];
        }

        DocNode entry = parent.Children[main];
        int? comma = EditLayout.CommaAfter(parent, main, _text);
        int restStart = comma is { } position ? position + 1 : entry.End;
        return _text[start..entry.End] + (needsComma ? "," : string.Empty) + _text[restStart..end];
    }

    // ----- Layout -----

    /// <summary>
    /// The text after a comma that separates entries: copied from the container's own commas, else from the document's, else
    /// one space.
    /// </summary>
    private string AfterComma(DocNode parent)
    {
        string? separator = FindInContainer(parent) ?? FirstInDocument(node => IsJsonContainer(node) ? FindInContainer(node) : null);
        return separator ?? " ";
    }

    private string? FindInContainer(DocNode parent)
    {
        for (int k = 0; k + 1 < parent.Children.Count; k++)
        {
            if (SeparatorAfterComma(parent, k) is { } separator)
            {
                return separator;
            }
        }

        return null;
    }

    /// <summary>The spaces after the comma that follows child <paramref name="k"/>, when a child follows and only spaces are between.</summary>
    private string? SeparatorAfterComma(DocNode parent, int k)
    {
        int? comma = EditLayout.CommaAfter(parent, k, _text);
        if (comma is not { } position || k + 1 >= parent.Children.Count)
        {
            return null;
        }

        int next = parent.Children[k + 1].Start;
        if (next < position + 1)
        {
            return null;
        }

        string gap = _text[(position + 1)..next];
        return gap.All(c => c is ' ' or '\t') ? gap : null;
    }

    private string KeySeparator(DocNode parent, int backFrom, int forwardFrom)
    {
        DocNode? sibling = NearestSibling(parent, backFrom, forwardFrom, n => SeparatorOf(n) is not null);
        if (sibling is not null)
        {
            return SeparatorOf(sibling)!;
        }

        return FirstInDocument(n => SeparatorOf(n)) ?? ": ";
    }

    /// <summary>The text between a member's key and its value, when it is spaces around one colon.</summary>
    private string? SeparatorOf(DocNode node)
    {
        if (!IsMember(node))
        {
            return null;
        }

        DocPart key = Part(node, PartKind.Key)!;
        int valueStart = ValueSpan(node).Start;
        if (valueStart < key.End)
        {
            return null;
        }

        string gap = _text[key.End..valueStart];
        return gap.Count(c => c == ':') == 1 && gap.All(c => c == ':' || char.IsWhiteSpace(c)) ? gap : null;
    }

    private string NewKeyText(string name, DocNode parent, int backFrom, int forwardFrom)
    {
        if (_dialect == JsonDialect.Json5
            && JsonSyntax.IsIdentifierName(name)
            && NearestSibling(parent, backFrom, forwardFrom, IsMember) is { } sibling
            && QuoteOf(Part(sibling, PartKind.Key)!) is null)
        {
            return name;
        }

        return JsonSyntax.EncodeString(name, PreferredKeyQuote(parent, backFrom, forwardFrom), _dialect);
    }

    private char PreferredKeyQuote(DocNode parent, int backFrom, int forwardFrom)
    {
        DocNode? sibling = NearestSibling(parent, backFrom, forwardFrom, n => IsMember(n) && QuoteOf(Part(n, PartKind.Key)!) is not null);
        if (sibling is not null)
        {
            return QuoteOf(Part(sibling, PartKind.Key)!)!.Value;
        }

        DocNode? first = FirstNodeInDocument(n => IsMember(n) && QuoteOf(Part(n, PartKind.Key)!) is not null);
        return first is null ? '"' : QuoteOf(Part(first, PartKind.Key)!)!.Value;
    }

    private char PreferredValueQuote(DocNode parent, int backFrom, int forwardFrom)
    {
        DocNode? sibling = NearestSibling(parent, backFrom, forwardFrom, n => n.JsonKind == JsonValueKind.String);
        DocNode? node = sibling ?? FirstNodeInDocument(n => n.JsonKind == JsonValueKind.String);
        return node is null ? '"' : _text[ValueSpan(node).Start];
    }

    private char? QuoteOf(DocPart key)
    {
        char first = _text[key.Start];
        return first is '"' or '\'' ? first : null;
    }

    /// <summary>The indentation of a new line for an entry: that of the previous sibling, else the next, else one unit deeper than the container.</summary>
    private string IndentFor(DocNode? previous, DocNode? next, DocNode parent)
    {
        if (previous is not null)
        {
            return EditLayout.LineIndent(_text, previous.Start);
        }

        if (next is not null)
        {
            return EditLayout.LineIndent(_text, next.Start);
        }

        return EditLayout.LineIndent(_text, OpenOf(parent)) + _unit;
    }

    /// <summary>True when a line break separates the container's opening bracket from its first child.</summary>
    private bool IsMultiLine(DocNode container)
    {
        int first = container.Children.Count > 0 ? container.Children[0].Start : container.End - 1;
        return EditLayout.HasLineBreak(_text, OpenOf(container) + 1, first);
    }

    private string ValueText(JsonValueKind kind, string? value, DocNode parent, int backFrom, int forwardFrom)
    {
        switch (kind)
        {
            case JsonValueKind.Object:
                return "{}";
            case JsonValueKind.Array:
                return "[]";
            case JsonValueKind.String:
                return JsonSyntax.EncodeString(value ?? string.Empty, PreferredValueQuote(parent, backFrom, forwardFrom), _dialect);
            case JsonValueKind.Number:
                if (value is null)
                {
                    throw new CfgeditException("A number needs a value");
                }

                RequireNumber(value);
                return value;
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            case JsonValueKind.Null:
                return "null";
            default:
                throw new CfgeditException("Unknown JSON type");
        }
    }

    private void RequireNumber(string text)
    {
        if (!JsonSyntax.IsValidNumber(text, _dialect))
        {
            throw new CfgeditException($"'{text}' is not a valid number");
        }
    }

    private void ValidateComment(string text, bool block)
    {
        if (_dialect == JsonDialect.Json)
        {
            throw new CfgeditException("comments are not allowed in JSON");
        }

        if (block)
        {
            if (text.Contains("*/", StringComparison.Ordinal))
            {
                throw new CfgeditException("a block comment cannot contain */");
            }
        }
        else if (text.IndexOfAny(['\r', '\n', JsonSyntax.LineSeparator, JsonSyntax.ParagraphSeparator]) >= 0)
        {
            throw new CfgeditException("a line comment cannot contain a line break");
        }
    }

    // ----- Tree and text helpers -----

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

        throw new CfgeditException("The node is not a child of its parent");
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

    private static CfgeditException Uneditable()
    {
        return new CfgeditException("this part cannot be edited");
    }

    private static bool IsComment(DocNode node) => EditLayout.IsComment(node);

    private static bool IsJsonContainer(DocNode node) => EditLayout.IsJsonContainer(node);

    private static bool IsMember(DocNode node) => node.Path.Count > 0 && node.Path.Steps[^1] is MemberStep;

    private bool IsBlockComment(DocNode comment) => _text[comment.Start + 1] == '*';

    private bool IsLineComment(DocNode node) => node.Kind == NodeKind.JsonComment && !IsBlockComment(node);

    private int OpenOf(DocNode container)
    {
        return (Part(container, PartKind.Summary) ?? throw new InvalidOperationException("A container has no summary part")).Start;
    }

    /// <summary>The span of a value: a container from its opening bracket to its end, a scalar from its value part.</summary>
    private (int Start, int End) ValueSpan(DocNode node)
    {
        if (node.JsonKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return (OpenOf(node), node.End);
        }

        DocPart value = Part(node, PartKind.Value) ?? throw new InvalidOperationException("A value has no value part");
        return (value.Start, value.End);
    }

    private static DocNode? NearestSibling(DocNode parent, int backFrom, int forwardFrom, Func<DocNode, bool> predicate)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        for (int k = Math.Min(backFrom, children.Count - 1); k >= 0; k--)
        {
            if (predicate(children[k]))
            {
                return children[k];
            }
        }

        for (int k = Math.Max(forwardFrom, 0); k < children.Count; k++)
        {
            if (predicate(children[k]))
            {
                return children[k];
            }
        }

        return null;
    }

    private bool HasMemberNamed(DocNode parent, string name, DocNode? except)
    {
        foreach (DocNode child in parent.Children)
        {
            if (!ReferenceEquals(child, except) && IsMember(child) && Part(child, PartKind.Key)!.Value == name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A copy's key: <c>name-copy</c>, then <c>name-copy2</c> and so on, unique among the siblings.</summary>
    private string FreshKey(DocNode parent, string name)
    {
        string candidate = name + "-copy";
        for (int n = 2; HasMemberNamed(parent, candidate, null); n++)
        {
            candidate = name + "-copy" + n.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    /// <summary>The index of the entry (not a comment) among children [first, last], or -1.</summary>
    private static int MainEntry(DocNode parent, int first, int last)
    {
        for (int k = first; k <= last; k++)
        {
            if (!IsComment(parent.Children[k]))
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>The index of the nearest entry before <paramref name="before"/>, or -1.</summary>
    private static int LastEntryBefore(DocNode parent, int before)
    {
        for (int k = before - 1; k >= 0; k--)
        {
            if (!IsComment(parent.Children[k]))
            {
                return k;
            }
        }

        return -1;
    }

    private static bool AnyEntryAfter(DocNode parent, int after)
    {
        for (int k = after + 1; k < parent.Children.Count; k++)
        {
            if (!IsComment(parent.Children[k]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExistsEntryFrom(DocNode parent, int from)
    {
        for (int k = Math.Max(from, 0); k < parent.Children.Count; k++)
        {
            if (!IsComment(parent.Children[k]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the container's last entry is followed by a comma.</summary>
    private bool HasTrailingComma(DocNode parent)
    {
        if (!IsJsonContainer(parent))
        {
            return false;
        }

        for (int k = parent.Children.Count - 1; k >= 0; k--)
        {
            if (!IsComment(parent.Children[k]))
            {
                return EditLayout.CommaAfter(parent, k, _text) is not null;
            }
        }

        return false;
    }

    private DocNode? FirstNodeInDocument(Func<DocNode, bool> predicate)
    {
        var stack = new Stack<DocNode>();
        stack.Push(_document.Root);
        while (stack.Count > 0)
        {
            DocNode node = stack.Pop();
            if (predicate(node))
            {
                return node;
            }

            for (int k = node.Children.Count - 1; k >= 0; k--)
            {
                stack.Push(node.Children[k]);
            }
        }

        return null;
    }

    private string? FirstInDocument(Func<DocNode, string?> select)
    {
        var stack = new Stack<DocNode>();
        stack.Push(_document.Root);
        while (stack.Count > 0)
        {
            DocNode node = stack.Pop();
            if (select(node) is { } found)
            {
                return found;
            }

            for (int k = node.Children.Count - 1; k >= 0; k--)
            {
                stack.Push(node.Children[k]);
            }
        }

        return null;
    }

    private void Replace(int start, int end, string text)
    {
        _edits.Add(new TextEdit(start, end, text));
    }
}
