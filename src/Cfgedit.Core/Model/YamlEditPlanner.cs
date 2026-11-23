using Cfgedit.Core.Yaml;

namespace Cfgedit.Core.Model;

/// <summary>
/// Plans the text edits for an operation on a YAML document. It works on the document tree, the YAML syntax that each row was built from
/// (<see cref="YamlModel.SyntaxOf"/>) and the source text. Every edit replaces a span of the old text, and the text it leaves alone keeps
/// its bytes.
/// </summary>
/// <remarks>
/// A value's owner is the colon or dash that introduces it. A new scalar text goes after the owner, and a block collection that becomes flow
/// goes on the owner's line. The static helpers are shared with <see cref="YamlLogicalEdit"/>, so that the two agree on the style of a
/// scalar, on the tag that stays, and on the span of a collection that is replaced.
/// </remarks>
internal sealed class YamlEditPlanner
{
    private static readonly IReadOnlyDictionary<string, string> DefaultHandles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["!"] = "!",
        ["!!"] = YamlTags.CorePrefix,
    };

    private readonly YamlModel _model;
    private readonly ConfigDocument _document;
    private readonly string _text;
    private readonly string _newline;
    private readonly List<TextEdit> _edits = [];

    /// <summary>Creates a planner for a parsed YAML document.</summary>
    public YamlEditPlanner(ConfigDocument document)
    {
        _model = (YamlModel)document.Syntax;
        _document = document;
        _text = document.Source.Text;
        _newline = document.Source.Newline;
    }

    /// <summary>The position where a value's text starts after its owner, and the indentation its owner has.</summary>
    /// <param name="End">The end of the colon or dash; for a flow entry with no colon, the end of its key.</param>
    /// <param name="Column">The column of the owning entry or item, which a literal block is indented from. -1 in a flow.</param>
    /// <param name="NoColon">True for a flow entry with a key and no colon, which takes a colon when it gets a value.</param>
    internal readonly record struct Owner(int End, int Column, bool NoColon);

    /// <summary>
    /// The scalar a row shows: its syntax (null when the value is missing), the span of its text, and the kind the file gives it. Empty is an
    /// untagged plain scalar with no text, which reads as null. Typed is the core kind its text must keep, and null for a string.
    /// </summary>
    private sealed record ScalarState(YamlScalar? Syntax, int From, int To, bool Empty, YamlCoreKind? Typed, bool AllowsInteger, string? Tag)
    {
        public YamlScalarStyle? Style => Syntax?.Style;

        public bool HasProps => Syntax is { } scalar && (scalar.Anchor is not null || scalar.Tag is not null);
    }

    /// <summary>Plans an operation. Throws <see cref="CfgeditException"/> when the operation is invalid or not supported.</summary>
    public IReadOnlyList<TextEdit> Plan(EditOp op)
    {
        switch (op)
        {
            case SetPart set:
                PlanSetPart(set);
                break;
            case ChangeYamlType change:
                PlanChangeType(change);
                break;
            case DeleteNode delete:
                PlanDelete(delete);
                break;
            case InsertNode:
                throw new CfgeditException("adding is not supported in YAML yet");
            case DuplicateNode:
                throw new CfgeditException("duplicating a node is not supported in YAML yet");
            case MoveNode:
                throw new CfgeditException("moving a node is not supported in YAML yet");
            case ChangeJsonType or ChangeTomlType:
                throw new CfgeditException("use the YAML type change");
            case InsertAttribute or DeleteAttribute or MoveAttribute:
                throw new CfgeditException("YAML values have no attributes");
            default:
                throw new CfgeditException("this edit is not supported for YAML");
        }

        return _edits;
    }

    // ----- Shared rules (also used by the logical edit) -----

    /// <summary>The value a row's syntax holds: a mapping entry's, an item's, or the value itself. Null when there is none.</summary>
    internal static YamlValueNode? ValueOf(YamlNode? piece)
    {
        return piece switch
        {
            YamlMappingEntry entry => entry.Value,
            YamlSequenceItem item => item.Value,
            YamlFlowMappingEntry entry => entry.Value,
            YamlValueNode value => value,
            _ => null,
        };
    }

    /// <summary>The owner of a value: the colon or dash in front of it, or null for a root value or a flow sequence item.</summary>
    internal static Owner? OwnerOf(YamlNode? piece, string text)
    {
        return piece switch
        {
            YamlMappingEntry entry => new Owner(entry.Colon.End, Column(text, entry.Start), false),
            YamlSequenceItem item => new Owner(item.Dash.End, Column(text, item.Dash.Start), false),
            YamlFlowMappingEntry { Colon: { } colon } => new Owner(colon.End, -1, false),
            YamlFlowMappingEntry flow => new Owner(flow.Key.End, -1, true),
            _ => null,
        };
    }

    /// <summary>The text of a collection that is replaced: from its owner (or its start) to the end of its last entry, or of the flow brackets.</summary>
    internal static (int Start, int End) CollectionSpan(string text, YamlValueNode value, Owner? owner)
    {
        int start = owner is { } at ? at.End : value.Start;
        int end = value switch
        {
            YamlBlockMapping { Entries.Count: > 0 } block => EditLayout.TrimBreaks(text, value.Start, block.Entries[^1].End),
            YamlBlockSequence { Items.Count: > 0 } block => EditLayout.TrimBreaks(text, value.Start, block.Items[^1].End),
            _ => value.End,
        };
        return (start, end);
    }

    /// <summary>
    /// The text of a string written in place of a scalar. The style is kept when it can hold the value; a plain text is kept plain only when it
    /// stays a string. In a flow a literal block cannot be written, so a value with a line break is double-quoted.
    /// </summary>
    internal static string StringToken(string value, YamlScalarStyle? preferred, int column, bool flow, string newline)
    {
        if (flow)
        {
            if (value.Contains('\n'))
            {
                preferred = YamlScalarStyle.DoubleQuoted;
            }
            else if (preferred is YamlScalarStyle.Literal or YamlScalarStyle.Folded)
            {
                preferred = null;
            }
        }

        return YamlEncoder.EncodeScalar(value, preferred, Math.Max(column, 0), newline, mustStayString: true);
    }

    /// <summary>
    /// True when a tag still fits a value of a kind: the core scalar tags fit only their own type (a float also takes an integer), a
    /// non-core tag fits a string and a collection, and the core collection tags fit a collection. <paramref name="kind"/> is null for a collection.
    /// </summary>
    internal static bool TagFits(string? tag, YamlValueKind? kind)
    {
        if (tag is null)
        {
            return true;
        }

        bool collection = kind is null;
        if (tag.StartsWith(YamlTags.CorePrefix, StringComparison.Ordinal))
        {
            string name = tag[YamlTags.CorePrefix.Length..];
            if (collection)
            {
                return name is not ("str" or "null" or "bool" or "int" or "float");
            }

            return name switch
            {
                "null" => kind == YamlValueKind.Null,
                "bool" => kind == YamlValueKind.Boolean,
                "int" => kind == YamlValueKind.Integer,
                "float" => kind is YamlValueKind.Integer or YamlValueKind.Float,
                _ => kind == YamlValueKind.String,
            };
        }

        return collection || kind == YamlValueKind.String;
    }

    /// <summary>The column of an offset in its line.</summary>
    private static int Column(string text, int offset)
    {
        return offset - EditLayout.LineStart(text, offset);
    }

    private static bool IsLineBreak(char c) => c is '\r' or '\n';

    // ----- SetPart -----

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
            case PartKind.Comment when node.Kind == NodeKind.YamlComment:
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

        Replace(key.Start, key.End, YamlEncoder.EncodeKey(name));
    }

    private void PlanSetValue(DocNode node, string value)
    {
        ScalarState cur = StateOf(node);
        RequireNoBlockComment(cur);
        Owner? owner = OwnerOf(Piece(node), _text);
        string? token;
        if (cur.Empty)
        {
            // A null with no text: any text makes it a string, and an empty text leaves it null.
            token = value.Length == 0 ? null : ScalarText(value, null, owner, IsFlow(Parent(node)));
        }
        else if (cur.Typed is { } kind)
        {
            RequireTyped(kind, cur.AllowsInteger, value);
            token = value;
        }
        else
        {
            token = ScalarText(value, cur.Style, owner, IsFlow(Parent(node)));
        }

        if (token is not null)
        {
            WriteValue(cur.From, cur.To, token, owner, cur.HasProps);
        }
    }

    private void PlanSetComment(DocNode comment, string text)
    {
        DocPart part = Part(comment, PartKind.Comment) ?? throw Uneditable();
        if (text == part.Value)
        {
            return;
        }

        CheckCommentText(text);
        Replace(part.Start, part.End, text);
    }

    // ----- ChangeYamlType -----

    private void PlanChangeType(ChangeYamlType change)
    {
        DocNode node = Node(change.Node);
        switch (node.Kind)
        {
            case NodeKind.YamlScalar:
                ChangeScalar(node, change);
                return;
            case NodeKind.YamlMapping or NodeKind.YamlSequence:
                ChangeCollection(node, change);
                return;
            case NodeKind.YamlAlias:
                throw new CfgeditException("an alias cannot change type");
            default:
                throw new CfgeditException("only a value has a type");
        }
    }

    private void ChangeScalar(DocNode node, ChangeYamlType change)
    {
        ScalarState cur = StateOf(node);
        RequireNoBlockComment(cur);
        Owner? owner = OwnerOf(Piece(node), _text);
        if (change.Kind is YamlNewKind.Mapping or YamlNewKind.Sequence)
        {
            string brackets = change.Kind == YamlNewKind.Mapping ? "{}" : "[]";
            DropTagIfUnfit(cur, null);
            WriteValue(cur.From, cur.To, brackets, owner, cur.HasProps);
            return;
        }

        string? token = ScalarTypeToken(change, cur, owner, IsFlow(Parent(node)), out YamlValueKind result);
        if (token is null)
        {
            return;
        }

        DropTagIfUnfit(cur, result);
        WriteValue(cur.From, cur.To, token, owner, cur.HasProps);
    }

    private void ChangeCollection(DocNode node, ChangeYamlType change)
    {
        YamlNode? piece = Piece(node);
        YamlValueNode value = ValueOf(piece) ?? throw new CfgeditException("a new file has no value to change yet");
        bool isMapping = value is YamlBlockMapping or YamlFlowMapping;
        if ((change.Kind == YamlNewKind.Mapping && isMapping) || (change.Kind == YamlNewKind.Sequence && !isMapping))
        {
            return;
        }

        if (change.Kind is YamlNewKind.Mapping or YamlNewKind.Sequence)
        {
            ReplaceCollection(node, change.Kind == YamlNewKind.Mapping ? "{}" : "[]", TagFits(TagOf(value), null));
            return;
        }

        Owner? owner = OwnerOf(piece, _text);
        string? token = ScalarTypeToken(change, null, owner, IsFlow(Parent(node)), out YamlValueKind result);
        if (token is not null)
        {
            ReplaceCollection(node, token, TagFits(TagOf(value), result));
        }
    }

    /// <summary>
    /// The text of a scalar of the new type, or null when the scalar already has it and no value is given. The kind is the one the text reads
    /// as: a number is an integer or a float by its text.
    /// </summary>
    private string? ScalarTypeToken(ChangeYamlType change, ScalarState? cur, Owner? owner, bool flow, out YamlValueKind result)
    {
        switch (change.Kind)
        {
            case YamlNewKind.String:
            {
                result = YamlValueKind.String;
                if (change.Value is null && cur is { Empty: false, Typed: null })
                {
                    return null;
                }

                return ScalarText(change.Value ?? string.Empty, cur is { Empty: false } ? cur.Style : null, owner, flow);
            }

            case YamlNewKind.Number:
            {
                YamlCoreKind? known = cur?.Typed;
                if (change.Value is null && known is YamlCoreKind.Integer or YamlCoreKind.Float)
                {
                    result = known == YamlCoreKind.Integer ? YamlValueKind.Integer : YamlValueKind.Float;
                    return null;
                }

                string number = change.Value ?? "0";
                YamlCoreKind kind = YamlEncoder.CoreSchemaKind(number);
                if (kind is not (YamlCoreKind.Integer or YamlCoreKind.Float))
                {
                    throw new CfgeditException($"'{number}' is not a valid number");
                }

                result = kind == YamlCoreKind.Integer ? YamlValueKind.Integer : YamlValueKind.Float;
                return number;
            }

            case YamlNewKind.Boolean:
            {
                result = YamlValueKind.Boolean;
                if (change.Value is null && cur?.Typed == YamlCoreKind.Boolean)
                {
                    return null;
                }

                string flag = change.Value ?? "false";
                if (YamlEncoder.CoreSchemaKind(flag) != YamlCoreKind.Boolean)
                {
                    throw new CfgeditException($"'{flag}' is not a valid boolean");
                }

                return flag;
            }

            case YamlNewKind.Null:
            {
                result = YamlValueKind.Null;
                return cur is { } present && (present.Empty || present.Typed == YamlCoreKind.Null) ? null : "null";
            }

            default:
                throw new CfgeditException("this type is not supported for YAML");
        }
    }

    /// <summary>
    /// The text of a string for a scalar: the style it has, unless it is missing or a plain text that the new value cannot keep. A top-level
    /// literal whose first line starts with a space needs an indentation indicator, which YAML counts from one column left of the document;
    /// it is double-quoted instead, which reads the same everywhere.
    /// </summary>
    private string ScalarText(string value, YamlScalarStyle? preferred, Owner? owner, bool flow)
    {
        string token = StringToken(value, preferred, owner?.Column ?? 0, flow, _newline);
        if (owner is null && !flow && token.StartsWith("|2", StringComparison.Ordinal))
        {
            token = YamlEncoder.EncodeScalar(value, YamlScalarStyle.DoubleQuoted, 0, _newline, mustStayString: true);
        }

        return token;
    }

    /// <summary>A value of a core scalar type must read as that type. A tag that no longer fits the new type is removed.</summary>
    private void DropTagIfUnfit(ScalarState cur, YamlValueKind? kind)
    {
        if (cur.Syntax?.Tag is { } tag && !TagFits(cur.Tag, kind))
        {
            Replace(HorizontalStart(tag.Start), tag.End, string.Empty);
        }
    }

    /// <summary>
    /// Replaces a collection, with its owner's line, by a scalar or an empty flow collection. Its anchor stays, and its tag stays when
    /// <paramref name="keepTag"/> holds. Its contents go with it, and so do the comments among them.
    /// </summary>
    private void ReplaceCollection(DocNode node, string token, bool keepTag)
    {
        YamlNode? piece = Piece(node);
        YamlValueNode value = ValueOf(piece) ?? throw Internal("a collection has no syntax");
        Owner? owner = OwnerOf(piece, _text);
        (int start, int end) = CollectionSpan(_text, value, owner);
        var parts = new List<string>();
        if (value.Anchor is { } anchor)
        {
            parts.Add(anchor.Text);
        }

        if (keepTag && value.Tag is { } tag)
        {
            parts.Add(tag.Text);
        }

        parts.Add(token);
        Put(start, end, (owner is null ? string.Empty : " ") + string.Join(" ", parts));
    }

    /// <summary>The state of a scalar row: its syntax, the span of its text, and the kind its text must keep.</summary>
    private ScalarState StateOf(DocNode node)
    {
        if (node.Kind != NodeKind.YamlScalar)
        {
            throw Uneditable();
        }

        YamlNode? piece = Piece(node);
        DocPart part = Part(node, PartKind.Value) ?? throw Internal("a scalar has no value part");
        YamlValueNode? value = ValueOf(piece);
        if (value is null)
        {
            // A missing value sits after the colon or dash; a root one after the document's marker.
            int at = piece is YamlDocumentNode document ? (document.StartMarker?.End ?? document.Start) : part.Start;
            return new ScalarState(null, at, at, true, null, false, null);
        }

        if (value is not YamlScalar scalar)
        {
            throw Internal("a scalar row has no scalar syntax");
        }

        string? tag = TagOf(scalar);
        YamlCoreKind? typed = null;
        bool allowsInteger = false;
        bool empty = false;
        if (tag is not null && tag.StartsWith(YamlTags.CorePrefix, StringComparison.Ordinal))
        {
            switch (tag[YamlTags.CorePrefix.Length..])
            {
                case "null":
                    typed = YamlCoreKind.Null;
                    break;
                case "bool":
                    typed = YamlCoreKind.Boolean;
                    break;
                case "int":
                    typed = YamlCoreKind.Integer;
                    break;
                case "float":
                    typed = YamlCoreKind.Float;
                    allowsInteger = true;
                    break;
            }
        }
        else if (tag is null && scalar.Style == YamlScalarStyle.Plain)
        {
            if (scalar.IsEmpty)
            {
                empty = true;
            }
            else
            {
                YamlCoreKind kind = YamlEncoder.CoreSchemaKind(scalar.Value);
                typed = kind == YamlCoreKind.String ? null : kind;
            }
        }

        return new ScalarState(scalar, part.Start, part.End, empty, typed, allowsInteger, tag);
    }

    private static void RequireNoBlockComment(ScalarState cur)
    {
        if (cur.Syntax is { Style: YamlScalarStyle.Literal or YamlScalarStyle.Folded } block && YamlDocuments.CommentsIn(block).Any())
        {
            throw new CfgeditException("a block scalar with a comment on its header cannot be edited here");
        }
    }

    private static void RequireTyped(YamlCoreKind kind, bool allowsInteger, string value)
    {
        YamlCoreKind actual = YamlEncoder.CoreSchemaKind(value);
        if (actual == kind || (allowsInteger && kind == YamlCoreKind.Float && actual == YamlCoreKind.Integer))
        {
            return;
        }

        throw new CfgeditException(kind switch
        {
            YamlCoreKind.Integer => $"'{value}' is not a valid integer",
            YamlCoreKind.Float => $"'{value}' is not a valid float",
            YamlCoreKind.Boolean => $"'{value}' is not a valid boolean",
            _ => $"'{value}' is not a valid null",
        });
    }

    /// <summary>
    /// Writes a value's text in place of the old one at [<paramref name="from"/>, <paramref name="to"/>). A missing value gets a space after its
    /// owner, or the colon a flow entry lacks. A value that is written on its owner's line stays there, and one on a later line with no
    /// property, comment or line break in between moves up to the owner's line.
    /// </summary>
    private void WriteValue(int from, int to, string token, Owner? owner, bool hasProps)
    {
        if (owner is { NoColon: true })
        {
            if (token.Length > 0)
            {
                Insert(owner.Value.End, ": " + token);
            }

            return;
        }

        if (from != to && _text[from..to] == token)
        {
            return;
        }

        if (token.Length == 0)
        {
            if (from < to)
            {
                Put(HorizontalStart(from), to, string.Empty);
            }

            return;
        }

        int start = from;
        string prefix = string.Empty;
        if (from == to)
        {
            prefix = from > 0 && !char.IsWhiteSpace(_text[from - 1]) ? " " : string.Empty;
        }
        else if (owner is { } at && !hasProps && from > at.End && EditLayout.HasLineBreak(_text, at.End, from) && _text[at.End..from].IndexOf('#') < 0)
        {
            start = at.End;
            prefix = " ";
        }

        string text = prefix + token;
        if (text.Contains('\n'))
        {
            WriteLines(start, to, text);
        }
        else
        {
            Put(start, to, text);
        }
    }

    /// <summary>
    /// Writes a text with line breaks (a literal block). When the span ends with a line break, the new text ends with one too. A trailing comment
    /// on the value's line moves onto the header line, so that it does not become part of the block.
    /// </summary>
    private void WriteLines(int start, int to, string text)
    {
        if (to > start && IsLineBreak(_text[to - 1]))
        {
            Put(start, to, text);
            return;
        }

        int tail = to;
        while (tail < _text.Length && _text[tail] is ' ' or '\t')
        {
            tail++;
        }

        if (tail < _text.Length && _text[tail] == '#')
        {
            int lineEnd = LineEnd(tail);
            int lineBreak = text.IndexOfAny(['\r', '\n']);
            Replace(start, to, text[..lineBreak] + " " + _text[tail..lineEnd] + text[lineBreak..]);
            Replace(to, lineEnd, string.Empty);
            return;
        }

        Put(start, tail, text);
    }

    /// <summary>
    /// Replaces [start, end) with text. A span that ends with a line break keeps one: the text gets the document's line break, since the
    /// break belongs to the span and the new text must end the line too.
    /// </summary>
    private void Put(int start, int end, string text)
    {
        if (end > start && IsLineBreak(_text[end - 1]))
        {
            text += _newline;
        }

        if (_text[start..end] == text)
        {
            return;
        }

        Replace(start, end, text);
    }

    private int LineEnd(int offset)
    {
        int i = offset;
        while (i < _text.Length && !IsLineBreak(_text[i]))
        {
            i++;
        }

        return i;
    }

    // ----- Comments -----

    private static void CheckCommentText(string text)
    {
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
    }

    // ----- DeleteNode -----

    private void PlanDelete(DeleteNode op)
    {
        DocNode node = Node(op.Node);
        if (node.Kind == NodeKind.YamlComment)
        {
            DeleteComment(node);
            return;
        }

        if (node.Kind == NodeKind.YamlDocument)
        {
            DeleteDocument(node);
            return;
        }

        DocNode parent = Parent(node);
        if (parent.Kind is NodeKind.Document or NodeKind.YamlDocument)
        {
            throw new CfgeditException("the root value cannot be deleted");
        }

        RequireNoReferencedAnchors(Piece(node) ?? throw Internal("an entry has no syntax"));
        if (IsFlow(parent))
        {
            DeleteFlow(parent, node);
        }
        else
        {
            DeleteBlock(parent, node);
        }
    }

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
    /// Deletes an entry or item of a block collection with its lines: its attached comments, and what is nested in it. The last entry of a
    /// collection leaves the empty collection on its owner's line.
    /// </summary>
    private void DeleteBlock(DocNode parent, DocNode node)
    {
        if (EntryCount(parent) == 1)
        {
            ReplaceCollection(parent, IsMapping(parent) ? "{}" : "[]", TagFits(TagOf(ValueOf(Piece(parent))!), null));
            return;
        }

        IReadOnlyList<DocNode> children = parent.Children;
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), _text);
        int start = children[first].Start;
        int end = EditLayout.TrimBreaks(_text, start, children[last].End);
        if (EditLayout.StartsLine(_text, start) && EditLayout.RestOfLineBlank(_text, end))
        {
            Replace(EditLayout.LineStart(_text, start), EditLayout.AfterLineBreak(_text, end), string.Empty);
        }
        else if (last + 1 < children.Count)
        {
            Replace(start, children[last + 1].Start, string.Empty);
        }
        else
        {
            Replace(HorizontalStart(start), end, string.Empty);
        }
    }

    /// <summary>
    /// Deletes an entry or item of a flow collection with its comma. The last entry of a flow collection leaves the brackets empty.
    /// </summary>
    private void DeleteFlow(DocNode parent, DocNode node)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        YamlValueNode container = ValueOf(Piece(parent)) ?? throw Internal("a flow collection has no syntax");
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), _text);
        int removed = 0;
        for (int k = first; k <= last; k++)
        {
            if (!EditLayout.IsComment(children[k]))
            {
                removed++;
            }
        }

        if (removed == EntryCount(parent))
        {
            (int open, int close) = BracketsOf(container);
            Replace(open + 1, close, string.Empty);
            return;
        }

        int lastEntry = last;
        while (EditLayout.IsComment(children[lastEntry]))
        {
            lastEntry--;
        }

        int start = children[first].Start;
        YamlNode lastSyntax = Piece(children[lastEntry]) ?? throw Internal("an item has no syntax");
        YamlToken? comma = CommaAfter(container, lastSyntax);
        // The group's comments on its last line go with it, after the comma.
        int end = Math.Max(comma?.End ?? EditLayout.TrimBreaks(_text, start, lastSyntax.End), EditLayout.TrimBreaks(_text, start, children[last].End));
        if (EditLayout.StartsLine(_text, start) && EditLayout.RestOfLineBlank(_text, end))
        {
            Replace(EditLayout.LineStart(_text, start), EditLayout.AfterLineBreak(_text, end), string.Empty);
            return;
        }

        if (last + 1 < children.Count)
        {
            int next = children[last + 1].Start;
            if (next < end)
            {
                throw new CfgeditException("this entry cannot be deleted here: a comment stands between it and its comma");
            }

            if (EditLayout.HasLineBreak(_text, end, next))
            {
                Replace(HorizontalStart(start), end, string.Empty);
            }
            else
            {
                Replace(start, next, string.Empty);
            }

            return;
        }

        int before = EditLayout.TrimBreaks(_text, children[first - 1].Start, children[first - 1].End);
        if (EditLayout.HasLineBreak(_text, before, start))
        {
            Replace(HorizontalStart(start), end, string.Empty);
        }
        else
        {
            Replace(before, end, string.Empty);
        }
    }

    /// <summary>Deletes a document of a stream with its lines, its marker and its comments.</summary>
    private void DeleteDocument(DocNode row)
    {
        int end = EditLayout.TrimBreaks(_text, row.Start, row.End);
        Replace(EditLayout.LineStart(_text, row.Start), EditLayout.AfterLineBreak(_text, end), string.Empty);
    }

    /// <summary>
    /// Refuses a delete that would leave an alias with no anchor: an anchor in the removed text that an alias elsewhere in the same document
    /// names.
    /// </summary>
    private void RequireNoReferencedAnchors(YamlNode removed)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (YamlNode part in Walk(removed))
        {
            if (part is YamlValueNode { Anchor: { } anchor })
            {
                names.Add(anchor.Text[1..]);
            }
        }

        if (names.Count == 0)
        {
            return;
        }

        YamlDocumentNode? document = _model.Syntax.Documents.FirstOrDefault(d => d.Start <= removed.Start && removed.Start < d.End);
        if (document is null)
        {
            return;
        }

        foreach (YamlNode part in Walk(document))
        {
            if (part is YamlAlias alias && names.Contains(alias.Name) && (alias.Start < removed.Start || alias.End > removed.End))
            {
                throw new CfgeditException($"*{alias.Name} refers to it");
            }
        }
    }

    private static IEnumerable<YamlNode> Walk(YamlNode node)
    {
        yield return node;
        foreach (YamlNode part in node.Parts)
        {
            foreach (YamlNode inner in Walk(part))
            {
                yield return inner;
            }
        }
    }

    /// <summary>The positions of the opening and closing brackets of a flow collection.</summary>
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

    /// <summary>The comma that follows an entry of a flow collection, or null when it has none.</summary>
    private static YamlToken? CommaAfter(YamlValueNode container, YamlNode entry)
    {
        IReadOnlyList<YamlNode> parts = container.Parts;
        int at = -1;
        for (int k = 0; k < parts.Count; k++)
        {
            if (ReferenceEquals(parts[k], entry))
            {
                at = k;
                break;
            }
        }

        for (int k = at + 1; at >= 0 && k < parts.Count; k++)
        {
            if (parts[k] is YamlComment || parts[k] is YamlToken { Kind: YamlTokenKind.Whitespace or YamlTokenKind.LineBreak })
            {
                continue;
            }

            return parts[k] is YamlToken { Kind: YamlTokenKind.Indicator, Text: "," } comma ? comma : null;
        }

        return null;
    }

    // ----- Document lookups -----

    /// <summary>The tag handles of the document a node is in, to resolve its tag.</summary>
    private string? TagOf(YamlValueNode value)
    {
        return YamlTags.Resolve(value.Tag, HandlesAt(value.Start));
    }

    private IReadOnlyDictionary<string, string> HandlesAt(int offset)
    {
        YamlDocumentNode? document = _model.Syntax.Documents.FirstOrDefault(d => d.Start <= offset && offset < d.End);
        return document is null ? DefaultHandles : YamlTags.HandlesOf(document);
    }

    // ----- Text and tree helpers -----

    private int EntryCount(DocNode parent) => parent.Children.Count(child => !EditLayout.IsComment(child));

    private bool IsFlow(DocNode parent) => ValueOf(Piece(parent)) is YamlFlowMapping or YamlFlowSequence;

    private bool IsMapping(DocNode parent) => ValueOf(Piece(parent)) is YamlBlockMapping or YamlFlowMapping;

    private YamlNode? Piece(DocNode node) => _model.SyntaxOf(node);

    private int HorizontalStart(int offset)
    {
        int i = offset;
        while (i > 0 && _text[i - 1] is ' ' or '\t')
        {
            i--;
        }

        return i;
    }

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

    private void Replace(int start, int end, string text)
    {
        _edits.Add(new TextEdit(start, end, text));
    }

    private void Insert(int offset, string text)
    {
        _edits.Add(new TextEdit(offset, offset, text));
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
