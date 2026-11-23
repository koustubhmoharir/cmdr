using System.Text;
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
    /// How a copy of a node is written. <see cref="Lines"/>: the source lines, after the group's last line (a block group, or a flow entry
    /// on its own line). <see cref="Dash"/>: a block entry that shares its first line with a dash, copied as a line of its own at its column.
    /// <see cref="Inline"/>: a flow entry, copied after its comma.
    /// </summary>
    internal enum CopyForm
    {
        Lines,
        Dash,
        Inline,
    }

    /// <summary>
    /// The source span of a copy: [<paramref name="From"/>, <paramref name="To"/>) is copied, and the copy goes at <paramref name="At"/>. The
    /// comments that start in the span are the copied comments, and the copy's rank starts at the comments that start before <c>At</c>.
    /// </summary>
    internal readonly record struct CopyRange(int From, int To, int At, CopyForm Form);

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
            case InsertNode insert:
                PlanInsert(insert);
                break;
            case DuplicateNode duplicate:
                PlanDuplicate(duplicate);
                break;
            case MoveNode move:
                PlanMove(move);
                break;
            case MoveNodeTo moveTo:
                PlanMoveTo(moveTo);
                break;
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
            // An empty string is quoted only because it is empty ('' as written by an add), so its style is not kept for a new text.
            YamlScalarStyle? preferred = cur.Syntax is { Value.Length: 0 } ? null : cur.Style;
            token = ScalarText(value, preferred, owner, IsFlow(Parent(node)));
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
                if (change.Kind != YamlNewKind.Sequence)
                {
                    throw new CfgeditException("an alias can only be wrapped in a sequence");
                }

                WrapInSequence(node);
                return;
            default:
                throw new CfgeditException("only a value has a type");
        }
    }

    private void ChangeScalar(DocNode node, ChangeYamlType change)
    {
        ScalarState cur = StateOf(node);
        RequireNoBlockComment(cur);
        Owner? owner = OwnerOf(Piece(node), _text);
        if (change.Kind == YamlNewKind.Sequence)
        {
            if (cur.Syntax is null || cur.Empty || cur.Typed == YamlCoreKind.Null)
            {
                // A null has nothing to keep: it becomes an empty sequence, its anchor and a tag that still fits staying.
                DropTagIfUnfit(cur, null);
                WriteValue(cur.From, cur.To, "[]", owner, cur.HasProps);
                return;
            }

            WrapInSequence(node);
            return;
        }

        if (change.Kind == YamlNewKind.Mapping)
        {
            DropTagIfUnfit(cur, null);
            WriteValue(cur.From, cur.To, "{}", owner, cur.HasProps);
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
        if (change.Kind == YamlNewKind.Sequence)
        {
            // A sequence stays as it is. A mapping is wrapped as the only item of a new sequence, its entries kept.
            if (isMapping)
            {
                WrapInSequence(node);
            }

            return;
        }

        if (change.Kind == YamlNewKind.Mapping)
        {
            if (!isMapping)
            {
                ReplaceCollection(node, "{}", TagFits(TagOf(value), null));
            }

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
    /// Wraps a value that is not a sequence as the only item of a new sequence. The value keeps its text, its anchor and its tag, which stay on
    /// the item. In a flow collection, or when the value is a flow collection, the value is put in brackets. In a block context the item is
    /// written on a line of its own under its owner: a mapping key gets a dash one unit deeper (or at the key's column when the file's
    /// sequences are not indented), a sequence item gets a dash two columns right of its own, and a root value gets a dash where it is.
    /// The lines of the value after its first are moved to keep their place under the new dash.
    /// </summary>
    private void WrapInSequence(DocNode node)
    {
        YamlNode? piece = Piece(node);
        YamlValueNode value = ValueOf(piece) ?? throw Internal("a value to wrap has no syntax");
        Owner? owner = OwnerOf(piece, _text);
        if (IsFlow(Parent(node)) || value is YamlFlowMapping or YamlFlowSequence)
        {
            Insert(value.Start, "[");
            Insert(value.End, "]");
            return;
        }

        int valueStart = value.Start;
        if (value is YamlScalar { Style: YamlScalarStyle.Literal or YamlScalarStyle.Folded })
        {
            RequireNoIndentIndicator(valueStart);
        }

        if (owner is null)
        {
            if (!EditLayout.StartsLine(_text, valueStart))
            {
                throw new CfgeditException("a root value on the document's marker line cannot be wrapped in a sequence yet");
            }

            int rootDash = Column(_text, valueStart);
            int rootEnd = CollectionSpan(_text, value, null).End;
            bool rootCollection = value is YamlBlockMapping or YamlBlockSequence;
            // A root scalar's lines move two columns, so that they stay right of the new dash; a collection's entries align two right of it.
            int rootDelta = rootCollection ? rootDash + 2 - Column(_text, FirstEntryStart(value)) : 2;
            Replace(valueStart, rootEnd, "- " + Reindent(valueStart, rootEnd, rootDelta, rootDash, !rootCollection));
            return;
        }

        Owner at = owner.Value;
        if (_text[at.End..valueStart].Contains('#'))
        {
            throw new CfgeditException("a comment between a key and its value cannot be wrapped in a sequence yet");
        }

        bool sameLine = !EditLayout.HasLineBreak(_text, at.End, valueStart);
        int dash = piece is YamlSequenceItem ? at.Column + 2 : at.Column + (SequencesIndented() ? IndentUnit().Length : 0);
        int valueEnd = CollectionSpan(_text, value, owner).End;
        bool collection = value is YamlBlockMapping or YamlBlockSequence;
        // A collection's entries align two columns right of the new dash. A scalar's lines move with its owner, so that they keep their place
        // under it.
        int delta = collection ? dash + 2 - Column(_text, FirstEntryStart(value)) : dash - at.Column;
        string body = Reindent(valueStart, valueEnd, delta, dash, !collection);
        string lead;
        if (sameLine && piece is YamlSequenceItem)
        {
            lead = " ";
        }
        else if (sameLine)
        {
            lead = _newline + new string(' ', dash);
        }
        else
        {
            int lastBreak = valueStart - 1;
            while (!IsLineBreak(_text[lastBreak]))
            {
                lastBreak--;
            }

            lead = _text[at.End..(lastBreak + 1)] + new string(' ', dash);
        }

        Replace(at.End, valueEnd, lead + "- " + body);
    }

    private static int FirstEntryStart(YamlValueNode collection)
    {
        return collection switch
        {
            YamlBlockMapping { Entries.Count: > 0 } mapping => mapping.Entries[0].Start,
            YamlBlockSequence { Items.Count: > 0 } sequence => sequence.Items[0].Start,
            _ => throw new InvalidOperationException("A block collection has no entries."),
        };
    }

    /// <summary>
    /// Refuses a block scalar whose header has an explicit indentation indicator (a digit): its content is counted from the indentation of its
    /// parent, which a wrap changes, so it cannot be re-indented without changing its value.
    /// </summary>
    private void RequireNoIndentIndicator(int valueStart, string? refusal = null)
    {
        int header = valueStart;
        while (header < _text.Length && _text[header] is not ('|' or '>'))
        {
            header++;
        }

        int end = LineEnd(header);
        if (_text[header..end].Any(c => c is >= '1' and <= '9'))
        {
            throw new CfgeditException(refusal ?? "a block scalar with an explicit indentation indicator cannot be wrapped in a sequence yet");
        }
    }

    /// <summary>
    /// The text of [start, end) with each line after the first moved by <paramref name="delta"/> columns (spaces are added, or removed). A line
    /// that is empty stays empty. A scalar's line must stay right of <paramref name="minColumn"/>, else the wrap is refused.
    /// </summary>
    private string Reindent(int start, int end, int delta, int minColumn, bool scalar)
    {
        var builder = new StringBuilder();
        int lineStart = start;
        bool first = true;
        while (true)
        {
            int lineEnd = lineStart;
            while (lineEnd < end && !IsLineBreak(_text[lineEnd]))
            {
                lineEnd++;
            }

            builder.Append(first ? _text[lineStart..lineEnd] : Shifted(lineStart, lineEnd, delta, minColumn, scalar));
            first = false;
            if (lineEnd >= end)
            {
                return builder.ToString();
            }

            int breakEnd = _text[lineEnd] == '\r' && lineEnd + 1 < end && _text[lineEnd + 1] == '\n' ? lineEnd + 2 : lineEnd + 1;
            builder.Append(_text, lineEnd, breakEnd - lineEnd);
            lineStart = breakEnd;
        }
    }

    private string Shifted(int lineStart, int lineEnd, int delta, int minColumn, bool scalar)
    {
        int length = lineEnd - lineStart;
        if (length == 0)
        {
            return string.Empty;
        }

        int spaces = 0;
        while (spaces < length && _text[lineStart + spaces] == ' ')
        {
            spaces++;
        }

        bool blank = spaces == length;
        if (delta >= 0)
        {
            if (scalar && !blank && spaces + delta <= minColumn)
            {
                throw new CfgeditException("the value cannot be re-indented safely under a new sequence item");
            }

            return new string(' ', delta) + _text[lineStart..lineEnd];
        }

        int remove = blank ? Math.Min(-delta, spaces) : -delta;
        if (!blank && (spaces < remove || (scalar && spaces - remove <= minColumn)))
        {
            throw new CfgeditException("the value cannot be re-indented safely under a new sequence item");
        }

        return _text[(lineStart + remove)..lineEnd];
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

    // ----- MoveNode -----

    /// <summary>
    /// Moves a unit among its siblings (see <see cref="YamlMoves.Units"/>): an entry or item with its attached comments, its nested lines and
    /// its trailing comment; a comment on a line of its own; a flow entry with its comma; or a document with its markers. The units from the
    /// moved one to its destination are written back as one span. Each unit keeps its text, and the text between the units (blank lines, or
    /// the spaces and line breaks of a flow collection) stays where it is.
    /// </summary>
    private void PlanMove(MoveNode op)
    {
        DocNode node = Node(op.Node);
        DocNode parent = Parent(node);
        if (parent.Kind == NodeKind.Document && node.Kind != NodeKind.YamlDocument)
        {
            throw new CfgeditException("the root value cannot be moved");
        }

        int index = IndexOf(parent, node);
        List<YamlUnit> units = YamlMoves.Units(_model, parent, _text, node.Kind == NodeKind.YamlComment ? index : -1);
        int moved = YamlMoves.UnitOf(units, index);
        if (units[moved].Refusal is { } refusal)
        {
            throw new CfgeditException(refusal);
        }

        int destination = YamlMoves.Destination(units, moved, op.NewIndex, parent.Children.Count);
        if (destination < 0)
        {
            throw new CfgeditException(YamlMoves.SplitRefusal);
        }

        List<int> order = YamlMoves.Order(units.Count, moved, destination);
        (int lo, int hi) = YamlMoves.Span(order);
        if (lo < 0)
        {
            return;
        }

        for (int k = lo; k <= hi; k++)
        {
            if (units[k].Refusal is { } neighbour)
            {
                throw new CfgeditException(neighbour);
            }
        }

        YamlValueNode? container = parent.Kind == NodeKind.Document ? null : ValueOf(Piece(parent));
        List<string> texts = container is YamlFlowMapping or YamlFlowSequence
            ? TokenTexts(container, units, order, lo, hi)
            : LineTexts(units, order, lo, hi);
        string span = string.Concat(texts.Select((text, p) => p + lo < hi ? text + GapAfter(units, p + lo) : text));
        if (parent.Kind != NodeKind.Document)
        {
            RequireAliasOrder(units, order, lo, hi, texts, span.Length);
        }

        int start = units[lo].Start;
        int end = units[hi].End;
        if (span != _text[start..end])
        {
            Replace(start, end, span);
        }
    }

    /// <summary>
    /// The text of the units in their new order, in lines. A unit that is not the last one gets a line break when it had none (it was the
    /// file's last line), and the span's last unit loses its break when the file must end without one. Either change would alter a block
    /// scalar that keeps its line breaks, so that is refused.
    /// </summary>
    private List<string> LineTexts(List<YamlUnit> units, List<int> order, int lo, int hi)
    {
        bool atEnd = hi == units.Count - 1 && units[hi].End == _text.Length && !EndsWithBreak(_text);
        var texts = new List<string>();
        for (int p = lo; p <= hi; p++)
        {
            YamlUnit unit = units[order[p]];
            string text = _text[unit.Start..unit.End];
            if (p < hi && !EndsWithBreak(text))
            {
                RequireNoKeptBreak(unit);
                text += _newline;
            }

            if (p == hi && atEnd && EndsWithBreak(text))
            {
                RequireNoKeptBreak(unit);
                text = WithoutBreak(text);
            }

            texts.Add(text);
        }

        return texts;
    }

    /// <summary>
    /// The text of the units of a flow collection in their new order. An entry takes a comma unless it is the last entry of the collection,
    /// which keeps the trailing comma the collection had. A comment (alone, or ending an entry's line) must still be followed by a line break
    /// where it lands, or the next entry would be commented out.
    /// </summary>
    private List<string> TokenTexts(YamlValueNode container, List<YamlUnit> units, List<int> order, int lo, int hi)
    {
        int lastEntry = units.FindLastIndex(unit => !unit.IsComment);
        bool hadTrailing = lastEntry >= 0 && units[lastEntry].Comma is not null;
        int newLast = -1;
        if (lastEntry >= lo && lastEntry <= hi)
        {
            for (int p = lo; p <= hi; p++)
            {
                if (!units[order[p]].IsComment)
                {
                    newLast = p;
                }
            }
        }

        int close = BracketsOf(container).Close;
        var texts = new List<string>();
        for (int p = lo; p <= hi; p++)
        {
            YamlUnit unit = units[order[p]];
            string gap = p + 1 < units.Count ? GapAfter(units, p) : _text[units[p].End..close];
            if ((unit.IsComment || unit.EndsInComment) && gap.IndexOfAny(['\r', '\n']) < 0)
            {
                throw new CfgeditException(YamlMoves.CommentRunRefusal);
            }

            string text = _text[unit.Start..unit.End];
            if (!unit.IsComment)
            {
                bool needComma = p != newLast || hadTrailing;
                if (needComma && unit.Comma is null)
                {
                    text = text.Insert(unit.ValueEnd - unit.Start, ",");
                }
                else if (!needComma && unit.Comma is { } comma)
                {
                    text = text[..(unit.ValueEnd - unit.Start)] + text[(comma.End - unit.Start)..];
                }
            }

            texts.Add(text);
        }

        return texts;
    }

    /// <summary>The text between a unit and the one after it. It must be blank: anything else is not a run of units.</summary>
    private string GapAfter(List<YamlUnit> units, int k)
    {
        int from = units[k].End;
        int to = units[k + 1].Start;
        string gap = to >= from ? _text[from..to] : throw new CfgeditException(YamlMoves.NotContiguousRefusal);
        if (gap.Any(c => c is not (' ' or '\t' or '\r' or '\n')))
        {
            throw new CfgeditException(YamlMoves.NotContiguousRefusal);
        }

        return gap;
    }

    private void RequireNoKeptBreak(YamlUnit unit)
    {
        if (unit.KeepsBreak)
        {
            throw new CfgeditException(YamlMoves.KeptBreakRefusal);
        }
    }

    /// <summary>
    /// Refuses a move that would change which anchor an alias names, or put an alias before its anchor. Each alias names the nearest anchor
    /// of its name before it. The offsets of the document's anchors and aliases are mapped into the text after the move, and the nearest
    /// anchor of each alias must be the same one.
    /// </summary>
    private void RequireAliasOrder(List<YamlUnit> units, List<int> order, int lo, int hi, List<string> texts, int spanLength)
    {
        int spanStart = units[lo].Start;
        int spanEnd = units[hi].End;
        foreach (YamlDocumentNode document in _model.Syntax.Documents.Where(d => d.Start < spanEnd && spanStart < d.End))
        {
            var anchors = new List<(string Name, int At)>();
            var aliases = new List<(string Name, int At)>();
            foreach (YamlNode part in Walk(document))
            {
                if (part is YamlValueNode { Anchor: { } anchor })
                {
                    anchors.Add((anchor.Text[1..], anchor.Start));
                }

                if (part is YamlAlias alias)
                {
                    aliases.Add((alias.Name, alias.Start));
                }
            }

            if (aliases.Count == 0 || anchors.Count == 0)
            {
                continue;
            }

            var newStart = new int[units.Count];
            int at = spanStart;
            for (int p = lo; p <= hi; p++)
            {
                newStart[order[p]] = at;
                at += texts[p - lo].Length + (p < hi ? GapAfter(units, p).Length : 0);
            }

            int delta = spanLength - (spanEnd - spanStart);
            int Map(int x)
            {
                if (x < spanStart)
                {
                    return x;
                }

                if (x >= spanEnd)
                {
                    return x + delta;
                }

                for (int u = lo; u <= hi; u++)
                {
                    if (units[u].Start <= x && x < units[u].End)
                    {
                        return newStart[u] + (x - units[u].Start);
                    }
                }

                throw Internal("an alias or anchor is not in a unit");
            }

            foreach ((string name, int aliasAt) in aliases)
            {
                if (!anchors.Any(a => a.Name == name && a.At < aliasAt))
                {
                    continue;
                }

                int target = anchors.Where(a => a.Name == name && a.At < aliasAt).Max(a => a.At);
                int newAlias = Map(aliasAt);
                int nearest = -1;
                foreach ((string anchorName, int anchorAt) in anchors)
                {
                    if (anchorName == name && Map(anchorAt) < newAlias)
                    {
                        nearest = Math.Max(nearest, Map(anchorAt));
                    }
                }

                if (nearest != Map(target))
                {
                    throw new CfgeditException(Map(target) > newAlias
                        ? $"*{name} would come before &{name}"
                        : $"*{name} would refer to another &{name}");
                }
            }
        }
    }

    private static bool EndsWithBreak(string text) => text.Length > 0 && text[^1] is '\r' or '\n';

    private static string WithoutBreak(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        return EndsWithBreak(text) ? text[..^1] : text;
    }

    // ----- MoveNodeTo -----

    /// <summary>Writes an entry into a flow collection, given the destination's mapping and flow flags and the column it would be indented to.</summary>
    private delegate string EntryRender(bool mapping, bool flow, int column);

    /// <summary>
    /// A node that moves into another container: its key (or item) and value, the text of its key as it is written there, and the copy
    /// range of its group (its attached comments, its lines).
    /// </summary>
    private sealed record MovingEntry(
        DocNode Node,
        DocNode Parent,
        YamlNode Piece,
        bool Member,
        YamlValueNode? Value,
        string? KeyToken,
        int KeyStart,
        int KeyEnd,
        CopyRange Range);

    /// <summary>An anchor or an alias, at its offset, with the name it has and an identity (the anchor's token, or the alias).</summary>
    private sealed record AliasMark(int At, string Name, object Id, bool Alias);

    /// <summary>
    /// The offset in the text where a node moved to <paramref name="index"/> of a container goes: the start of the child there (before its
    /// attached comments in a block), or the end of the container's entries. Used by the logical edit to place the moved comments in order.
    /// </summary>
    internal static int InsertionOffset(YamlModel model, string text, DocNode destination, int index)
    {
        YamlValueNode? container = ValueOf(model.SyntaxOf(destination));
        if (container is YamlFlowMapping or YamlFlowSequence)
        {
            IReadOnlyList<DocNode> children = destination.Children;
            return index < children.Count ? children[index].Start : BracketsOf(container).Close;
        }

        return BlockPlace(model, text, destination, index).Offset;
    }

    /// <summary>
    /// Moves a node, with its attached comments, out of its parent and into a mapping or sequence, at an index among that container's
    /// children counted before the move. The removal is the one a delete makes. The new text is written where an insert at that index
    /// would put it, in the destination's layout: a member keeps its key (or takes <see cref="MoveNodeTo.NewKey"/>) in a mapping and loses
    /// it in a sequence; an item takes a key to enter a mapping; a compact item (<c>- a: 1</c>) becomes a key with its collection below.
    /// The lines after the first move with their entry, so that they stay under it. An alias keeps the anchor it names.
    /// </summary>
    private void PlanMoveTo(MoveNodeTo op)
    {
        DocNode node = Node(op.Node);
        DocNode destination = Node(op.NewParent);
        RequireMoveTarget(node, destination);
        int count = destination.Children.Count;
        if (op.NewIndex < 0 || op.NewIndex > count)
        {
            throw new CfgeditException($"index {op.NewIndex} is outside the children, 0 to {count}");
        }

        if (node.Kind == NodeKind.YamlComment)
        {
            PlanMoveCommentTo(node, destination, op);
            return;
        }

        if (node.Kind == NodeKind.YamlDocument)
        {
            throw new CfgeditException("a document cannot move into a value");
        }

        DocNode parent = Parent(node);
        if (parent.Kind is NodeKind.Document or NodeKind.YamlDocument)
        {
            throw new CfgeditException("the root value cannot be moved");
        }

        YamlNode piece = Piece(node) ?? throw Internal("an entry has no syntax");
        bool member = piece is YamlMappingEntry or YamlFlowMappingEntry;
        bool mapping = destination.Kind == NodeKind.YamlMapping;
        string? oldKey = member ? ((MemberStep)node.Path.Steps[^1]).Key : null;
        if (!mapping && op.NewKey is not null)
        {
            throw new CfgeditException("an item of a sequence has no key");
        }

        if (mapping && op.NewKey is null && !member)
        {
            throw new CfgeditException("a key is needed for an entry of a mapping");
        }

        if (!mapping && oldKey == "<<")
        {
            throw new CfgeditException("a merge key cannot move into a sequence");
        }

        if (mapping)
        {
            RequireFreeKey(destination, op.NewKey ?? oldKey!);
        }

        bool flowSource = IsFlow(parent);
        if (!flowSource && !EditLayout.StartsLine(_text, piece.Start))
        {
            throw new CfgeditException(member ? YamlMoves.FirstKeyRefusal : YamlMoves.FirstItemRefusal);
        }

        (int keyStart, int keyEnd) = piece switch
        {
            YamlMappingEntry entry => (entry.Key.Start, entry.Key.End),
            YamlFlowMappingEntry entry => (entry.Key.Start, entry.Key.End),
            _ => (piece.Start, piece.Start),
        };
        string? keyToken = !mapping ? null : op.NewKey is { } renamed ? YamlEncoder.EncodeKey(renamed) : _text[keyStart..keyEnd];
        var moving = new MovingEntry(node, parent, piece, member, ValueOf(piece), keyToken, keyStart, keyEnd, CopySpan(_text, _model, parent, node));
        RequireAliasOrderAfterMove(moving, destination, op.NewIndex);
        if (IsFlow(destination))
        {
            RequireFlowable(moving);
            string valueToken = _text[moving.Value!.Start..moving.Value.End];
            InsertFlowEntry(destination, op.NewIndex, (m, f, column) => EntryTokens(m, f, keyToken, valueToken));
        }
        else
        {
            int column = BlockColumn(destination);
            string text = BlockEntryText(moving, mapping, column);
            (int at, bool onDashLine) = BlockPlace(_model, _text, destination, op.NewIndex);
            if (onDashLine)
            {
                // The child at this index shares its line with a dash: the moved entry takes that line, and the child follows it.
                Insert(at, text[column..] + new string(' ', column));
            }
            else
            {
                InsertText(at, text);
            }
        }

        if (flowSource)
        {
            DeleteFlow(parent, node);
        }
        else
        {
            DeleteBlock(parent, node);
        }
    }

    /// <summary>
    /// Refuses a move that the document cannot hold: the destination is not a mapping or sequence, it is the node's own container, it is
    /// inside the node, or it is in another document (a node never moves between documents).
    /// </summary>
    private static void RequireMoveTarget(DocNode node, DocNode destination)
    {
        if (destination.Kind is not (NodeKind.YamlMapping or NodeKind.YamlSequence))
        {
            throw new CfgeditException("a YAML node can only move into a mapping or a sequence");
        }

        if (ReferenceEquals(destination, node.Parent))
        {
            throw new CfgeditException("the node is already in that container");
        }

        for (DocNode? up = destination; up is not null; up = up.Parent)
        {
            if (ReferenceEquals(up, node))
            {
                throw new CfgeditException("a node cannot be moved into itself");
            }
        }

        if (!ReferenceEquals(DocumentOf(destination), DocumentOf(node)))
        {
            throw new CfgeditException("a node cannot move into another document");
        }
    }

    /// <summary>The document row (of a stream with several documents) that holds a node, or null when there is none.</summary>
    private static DocNode? DocumentOf(DocNode node)
    {
        for (DocNode? up = node; up is not null; up = up.Parent)
        {
            if (up.Kind == NodeKind.YamlDocument)
            {
                return up;
            }
        }

        return null;
    }

    /// <summary>
    /// A comment that moves alone: it must start its line, and it keeps its text. It is deleted where it is, and inserted at the index as a
    /// new comment would be.
    /// </summary>
    private void PlanMoveCommentTo(DocNode node, DocNode destination, MoveNodeTo op)
    {
        if (op.NewKey is not null)
        {
            throw new CfgeditException("a comment has no key");
        }

        if (!EditLayout.StartsLine(_text, node.Start) || !EditLayout.RestOfLineBlank(_text, node.End))
        {
            throw new CfgeditException(YamlMoves.TrailingRefusal);
        }

        string content = (Piece(node) as YamlComment ?? throw Internal("a comment has no syntax")).Content;
        DeleteComment(node);
        InsertComment(destination, op.NewIndex, content);
    }

    /// <summary>
    /// Refuses a value that cannot be written in a flow collection as it is: a block value or a block scalar, a value that spans lines, a
    /// value with no text, a comment in the group, and a plain scalar (or key) with a flow indicator or a comment start in it.
    /// </summary>
    private void RequireFlowable(MovingEntry moving)
    {
        if (CommentsIn(moving.Range).Any())
        {
            throw new CfgeditException("a comment cannot move into a flow collection yet");
        }

        switch (moving.Value)
        {
            case null:
                throw new CfgeditException("a value with no text cannot move into a flow collection yet");
            case YamlBlockMapping or YamlBlockSequence:
                throw new CfgeditException("a block value cannot go into a flow collection");
            case YamlScalar { Style: YamlScalarStyle.Literal or YamlScalarStyle.Folded }:
                throw new CfgeditException("a block scalar cannot go into a flow collection");
        }

        string valueText = _text[moving.Value.Start..moving.Value.End];
        if (valueText.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new CfgeditException("a multi-line value cannot go into a flow collection yet");
        }

        if (moving.Value is YamlScalar { Style: YamlScalarStyle.Plain, IsEmpty: false } && !FlowPlain(valueText))
        {
            throw new CfgeditException("a plain scalar with a flow indicator cannot go into a flow collection as it is written: quote it first");
        }

        if (moving.KeyToken is { Length: > 0 } key && key[0] is not ('"' or '\'') && !FlowPlain(key))
        {
            throw new CfgeditException("a plain key with a flow indicator cannot go into a flow collection as it is written: quote it first");
        }
    }

    /// <summary>True when a plain scalar's text reads the same in a flow collection: no flow indicator, no ": " or " #", and no trailing colon.</summary>
    private static bool FlowPlain(string text)
    {
        return text.IndexOfAny([',', '[', ']', '{', '}']) < 0
            && !text.Contains(": ", StringComparison.Ordinal)
            && !text.Contains(":\t", StringComparison.Ordinal)
            && !text.Contains(" #", StringComparison.Ordinal)
            && !text.EndsWith(':');
    }

    /// <summary>
    /// The text of a moved entry for a block destination at <paramref name="column"/>: the group's leading comments, then the entry with
    /// its shape changed for the destination, then its lines after the first moved by the change in the entry's column (checked, see
    /// <see cref="ShiftLines"/>). Every line ends with a line break.
    /// </summary>
    private string BlockEntryText(MovingEntry moving, bool mapping, int column)
    {
        if (IsFlow(moving.Parent))
        {
            return FlowEntryBlockText(moving, mapping, column);
        }

        DocNode parent = moving.Parent;
        IReadOnlyList<DocNode> children = parent.Children;
        int position = IndexOf(parent, moving.Node);
        int first = EditLayout.Group(parent, position, _text).First;
        var builder = new StringBuilder();
        for (int k = first; k < position; k++)
        {
            builder.Append(' ', column).Append(_text, children[k].Start, children[k].End - children[k].Start).Append(_newline);
        }

        YamlNode piece = moving.Piece;
        int ownerEnd = piece is YamlMappingEntry owned ? owned.Colon.End : ((YamlSequenceItem)piece).Dash.End;
        int lineEnd = LineEnd(piece.Start);
        int s = Column(_text, piece.Start);
        YamlValueNode? value = moving.Value;
        bool collection = value is YamlBlockMapping or YamlBlockSequence;
        int entryStart = collection ? FirstEntryStart(value!) : piece.Start;
        int from = EditLayout.AfterLineBreak(_text, lineEnd);
        string head;
        int delta;
        if (mapping)
        {
            if (moving.Member)
            {
                head = new string(' ', column) + _text[piece.Start..moving.KeyStart] + moving.KeyToken + _text[moving.KeyEnd..lineEnd];
                delta = column - s;
            }
            else
            {
                string key = moving.KeyToken + ":";
                if (collection && !EditLayout.HasLineBreak(_text, ownerEnd, entryStart))
                {
                    // A compact item: its first entry is on the dash's line, and the collection moves below the key.
                    if (value!.Anchor is not null || value.Tag is not null)
                    {
                        throw new CfgeditException("a compact item with properties cannot move into a mapping yet");
                    }

                    int childColumn = ChildColumn(value, column);
                    head = new string(' ', column) + key + _newline + new string(' ', childColumn) + _text[entryStart..lineEnd];
                    delta = childColumn - Column(_text, entryStart);
                }
                else if (collection)
                {
                    head = new string(' ', column) + key + _text[ownerEnd..lineEnd];
                    delta = ChildColumn(value!, column) - Column(_text, entryStart);
                }
                else
                {
                    head = new string(' ', column) + key + _text[ownerEnd..lineEnd];
                    delta = column - s;
                }
            }
        }
        else if (moving.Member && collection)
        {
            bool compact = value!.Anchor is null && value.Tag is null && _text[ownerEnd..entryStart].All(c => c is ' ' or '\t' or '\r' or '\n');
            if (compact)
            {
                // The collection's first entry takes the dash's line, and the others align with it, two columns right of the dash.
                int entryLineEnd = LineEnd(entryStart);
                head = new string(' ', column) + "- " + _text[entryStart..entryLineEnd];
                from = EditLayout.AfterLineBreak(_text, entryLineEnd);
                delta = column + 2 - Column(_text, entryStart);
            }
            else
            {
                head = new string(' ', column) + "-" + _text[ownerEnd..lineEnd];
                delta = column + 2 - Column(_text, entryStart);
            }
        }
        else
        {
            head = new string(' ', column) + "-" + _text[ownerEnd..lineEnd];
            delta = column - s;
        }

        RequireNoIndentIndicators(piece);
        builder.Append(head).Append(_newline).Append(ShiftLines(from, moving.Range.To, delta, column));
        return builder.ToString();
    }

    /// <summary>The column of the entries of a block value under a key or dash at <paramref name="column"/>: one unit deeper, and a sequence at the same column when the file's sequences are not indented.</summary>
    private int ChildColumn(YamlValueNode value, int column)
    {
        return column + (value is YamlBlockSequence && !SequencesIndented() ? 0 : IndentUnit().Length);
    }

    /// <summary>The text of a flow entry (single line) written as a block entry at <paramref name="column"/>.</summary>
    private string FlowEntryBlockText(MovingEntry moving, bool mapping, int column)
    {
        string? valueText = moving.Value is null ? null : _text[moving.Value.Start..moving.Value.End];
        if (valueText is not null && valueText.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new CfgeditException("a multi-line value cannot move out of a flow collection yet");
        }

        string lead = mapping ? moving.KeyToken + ":" : "-";
        return new string(' ', column) + lead + (valueText is null ? string.Empty : " " + valueText) + _newline;
    }

    /// <summary>
    /// The lines of [<paramref name="from"/>, <paramref name="to"/>) moved by <paramref name="delta"/> columns, each ending with a line break.
    /// A line that is not a comment must stay more indented than <paramref name="minColumn"/> (its new parent's column), else the move is
    /// refused. A comment keeps its text, and its indentation stops at the line's start.
    /// </summary>
    private string ShiftLines(int from, int to, int delta, int minColumn)
    {
        HashSet<int> comments = CommentStarts();
        var builder = new StringBuilder();
        for (int pos = from; pos < to;)
        {
            int end = Math.Min(LineEnd(pos), to);
            int spaces = 0;
            while (pos + spaces < end && _text[pos + spaces] == ' ')
            {
                spaces++;
            }

            bool blank = pos + spaces == end;
            bool comment = !blank && comments.Contains(pos + spaces);
            int width = spaces + delta;
            if (!blank && !comment && width <= minColumn)
            {
                throw new CfgeditException("the value cannot be re-indented safely under its new parent");
            }

            builder.Append(' ', Math.Max(width, 0)).Append(_text, pos + spaces, end - pos - spaces).Append(_newline);
            pos = EditLayout.AfterLineBreak(_text, end);
        }

        return builder.ToString();
    }

    /// <summary>The offsets of every comment's '#' in the document.</summary>
    private HashSet<int> CommentStarts()
    {
        return _model.Syntax.Items.SelectMany(YamlDocuments.CommentsIn).Select(comment => comment.Start).ToHashSet();
    }

    /// <summary>The comments that start in a copy range.</summary>
    private IEnumerable<YamlComment> CommentsIn(CopyRange range)
    {
        return _model.Syntax.Items.SelectMany(YamlDocuments.CommentsIn).Where(comment => comment.Start >= range.From && comment.Start < range.To);
    }

    /// <summary>Refuses a block scalar with an explicit indentation indicator in the moved entry: its content is counted from its parent's indentation.</summary>
    private void RequireNoIndentIndicators(YamlNode piece)
    {
        foreach (YamlNode part in Walk(piece))
        {
            if (part is YamlScalar { Style: YamlScalarStyle.Literal or YamlScalarStyle.Folded })
            {
                RequireNoIndentIndicator(part.Start, "a block scalar with an explicit indentation indicator cannot move yet");
            }
        }
    }

    /// <summary>
    /// Refuses a move that would change which anchor an alias names, or put an alias before its anchor. The anchors and aliases of the
    /// document are put in their order after the move (the moved ones at the destination's offset), and each alias must name the same
    /// anchor as before, the nearest one above it. An alias with no anchor above it before the move is left alone.
    /// </summary>
    private void RequireAliasOrderAfterMove(MovingEntry moving, DocNode destination, int index)
    {
        YamlDocumentNode? document = _model.Syntax.Documents.FirstOrDefault(d => d.Start <= moving.Range.From && moving.Range.From < d.End);
        if (document is null)
        {
            return;
        }

        var marks = new List<AliasMark>();
        foreach (YamlNode part in Walk(document))
        {
            if (part is YamlValueNode { Anchor: { } anchor })
            {
                marks.Add(new AliasMark(anchor.Start, anchor.Text[1..], anchor, false));
            }

            if (part is YamlAlias alias)
            {
                marks.Add(new AliasMark(alias.Start, alias.Name, alias, true));
            }
        }

        if (!marks.Any(mark => mark.Alias))
        {
            return;
        }

        marks.Sort((a, b) => a.At.CompareTo(b.At));
        int insertAt = InsertionOffset(_model, _text, destination, index);
        List<AliasMark> groupMarks = marks.Where(mark => mark.At >= moving.Range.From && mark.At < moving.Range.To).ToList();
        var order = new List<AliasMark>();
        bool placed = false;
        foreach (AliasMark mark in marks.Where(mark => mark.At < moving.Range.From || mark.At >= moving.Range.To))
        {
            if (!placed && mark.At >= insertAt)
            {
                order.AddRange(groupMarks);
                placed = true;
            }

            order.Add(mark);
        }

        if (!placed)
        {
            order.AddRange(groupMarks);
        }

        Dictionary<object, object?> before = TargetsOf(marks);
        Dictionary<object, object?> after = TargetsOf(order);
        foreach (AliasMark alias in marks.Where(mark => mark.Alias))
        {
            object? old = before[alias.Id];
            if (old is null || ReferenceEquals(old, after[alias.Id]))
            {
                continue;
            }

            throw new CfgeditException(after[alias.Id] is null
                ? $"the alias *{alias.Name} would come before its anchor &{alias.Name}"
                : $"the alias *{alias.Name} would no longer refer to the same anchor");
        }
    }

    /// <summary>For each alias in an order of anchors and aliases, the anchor it names: the nearest anchor of its name before it, or null.</summary>
    private static Dictionary<object, object?> TargetsOf(IEnumerable<AliasMark> order)
    {
        var latest = new Dictionary<string, object>(StringComparer.Ordinal);
        var targets = new Dictionary<object, object?>(ReferenceEqualityComparer.Instance);
        foreach (AliasMark mark in order)
        {
            if (mark.Alias)
            {
                targets[mark.Id] = latest.GetValueOrDefault(mark.Name);
            }
            else
            {
                latest[mark.Name] = mark.Id;
            }
        }

        return targets;
    }

    /// <summary>The text of a flow entry, rendered as a token: <c>key: value</c> in a mapping, <c>- value</c> in a block sequence, else <c>value</c>.</summary>
    private static string EntryTokens(bool mapping, bool flow, string? keyToken, string token)
    {
        string head = mapping ? keyToken + ": " : flow ? string.Empty : "- ";
        return head + token;
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

    internal static IEnumerable<YamlNode> Walk(YamlNode node)
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
    internal static YamlToken? CommaAfter(YamlValueNode container, YamlNode entry)
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

    // ----- InsertNode -----

    private void PlanInsert(InsertNode op)
    {
        DocNode parent = Node(op.Parent);
        if (parent.Kind is NodeKind.Document or NodeKind.YamlDocument)
        {
            throw new CfgeditException(op.Node switch
            {
                NewYamlValue when parent.Kind == NodeKind.Document => "adding a document is not supported",
                NewYamlValue => "a document has one root value",
                _ => "a comment cannot be added here",
            });
        }

        if (parent.Kind is not (NodeKind.YamlMapping or NodeKind.YamlSequence))
        {
            throw new CfgeditException("a YAML value has no children");
        }

        int count = parent.Children.Count;
        if (op.Index < 0 || op.Index > count)
        {
            throw new CfgeditException($"index {op.Index} is outside the children, 0 to {count}");
        }

        switch (op.Node)
        {
            case NewYamlValue value:
                InsertValue(parent, op.Index, value);
                return;
            case NewYamlComment comment:
                CheckCommentText(comment.Text);
                InsertComment(parent, op.Index, comment.Text);
                return;
            default:
                throw new CfgeditException("this kind of node cannot be inserted into YAML");
        }
    }

    private void InsertValue(DocNode parent, int index, NewYamlValue value)
    {
        bool mapping = parent.Kind == NodeKind.YamlMapping;
        if (mapping && value.Key is null)
        {
            throw new CfgeditException("a key is needed for an entry of a mapping");
        }

        if (!mapping && value.Key is not null)
        {
            throw new CfgeditException("an item of a sequence has no key");
        }

        if (value.Key is { } key)
        {
            RequireFreeKey(parent, key);
        }

        if (ValueOf(Piece(parent)) is YamlFlowMapping or YamlFlowSequence)
        {
            InsertFlowValue(parent, index, value);
            return;
        }

        int column = BlockColumn(parent);
        InsertBlockLine(parent, index, EntryText(mapping, false, value, column), column);
    }

    private void InsertComment(DocNode parent, int index, string text)
    {
        if (ValueOf(Piece(parent)) is { } container and (YamlFlowMapping or YamlFlowSequence))
        {
            (int offset, string indent) = FlowCommentLine(_text, container, parent.Children, index);
            Insert(offset, indent + "#" + text + _newline);
            return;
        }

        int column = BlockColumn(parent);
        InsertBlockLine(parent, index, "#" + text, column);
    }

    /// <summary>
    /// Writes a line of a block collection before the child at <paramref name="index"/>, or after the last child. The line is indented to
    /// <paramref name="column"/>. When the child shares its first line with a dash (the collection is the first thing in a sequence item), the
    /// line takes the dash's line, and the child follows it on a line of its own at the same column.
    /// </summary>
    private void InsertBlockLine(DocNode parent, int index, string line, int column)
    {
        (int at, bool onDashLine) = BlockPlace(_model, _text, parent, index);
        if (onDashLine)
        {
            Insert(at, line + _newline + new string(' ', column));
            return;
        }

        InsertText(at, new string(' ', column) + line + _newline);
    }

    /// <summary>The text of a new entry: <c>key: value</c> in a mapping, <c>- value</c> in a block sequence, and <c>value</c> in a flow sequence.</summary>
    private string EntryText(bool mapping, bool flow, NewYamlValue value, int column)
    {
        string token = ValueToken(value.Kind, value.Value, column, flow);
        string head = mapping ? YamlEncoder.EncodeKey(value.Key!) + ": " : flow ? string.Empty : "- ";
        return head + token;
    }

    /// <summary>
    /// The text of a new value: a string as YAML writes it and stays a string, a number or boolean as given (checked against the core schema),
    /// null, or an empty flow collection. A literal block is indented from <paramref name="column"/>.
    /// </summary>
    private string ValueToken(YamlNewKind kind, string? value, int column, bool flow)
    {
        switch (kind)
        {
            case YamlNewKind.String:
                return StringToken(value ?? string.Empty, null, column, flow, _newline);
            case YamlNewKind.Number:
            {
                string number = value ?? "0";
                if (YamlEncoder.CoreSchemaKind(number) is not (YamlCoreKind.Integer or YamlCoreKind.Float))
                {
                    throw new CfgeditException($"'{number}' is not a valid number");
                }

                return number;
            }
            case YamlNewKind.Boolean:
            {
                string flag = value ?? "false";
                if (YamlEncoder.CoreSchemaKind(flag) != YamlCoreKind.Boolean)
                {
                    throw new CfgeditException($"'{flag}' is not a valid boolean");
                }

                return flag;
            }
            case YamlNewKind.Null:
                return "null";
            case YamlNewKind.Mapping:
                return "{}";
            case YamlNewKind.Sequence:
                return "[]";
            default:
                throw new CfgeditException("this type is not supported for YAML");
        }
    }

    /// <summary>The column that the entries of a block collection are indented to: the column of its first entry, or 0 for an empty stream.</summary>
    private int BlockColumn(DocNode parent)
    {
        DocNode? entry = parent.Children.FirstOrDefault(child => !EditLayout.IsComment(child));
        return entry is null ? 0 : Column(_text, Piece(entry)?.Start ?? entry.Start);
    }

    /// <summary>A key must not name a sibling: a mapping's keys are unique.</summary>
    private static void RequireFreeKey(DocNode parent, string key)
    {
        foreach (DocNode child in parent.Children)
        {
            if (Part(child, PartKind.Key) is { } part && part.Value == key)
            {
                throw new CfgeditException($"a key named '{key}' already exists here");
            }
        }
    }

    /// <summary>
    /// Writes an entry into a flow collection with entries. Before a child, the entry takes its own line when the child has one, else its
    /// place before the child with a comma. After the last entry, a one-per-line collection gets a line, a trailing comma stays a trailing
    /// comma, and a one-line collection gets the entry after the last comma (or after the last entry).
    /// </summary>
    private void InsertFlowValue(DocNode parent, int index, NewYamlValue value)
    {
        InsertFlowEntry(parent, index, (mapping, flow, column) => EntryText(mapping, flow, value, column));
    }

    /// <summary>
    /// Writes an entry, rendered by <paramref name="render"/>, into a flow collection at an index. <see cref="InsertFlowValue"/> renders a new
    /// value, and a move renders the text of the moved entry.
    /// </summary>
    private void InsertFlowEntry(DocNode parent, int index, EntryRender render)
    {
        bool mapping = parent.Kind == NodeKind.YamlMapping;
        YamlValueNode container = ValueOf(Piece(parent)) ?? throw Internal("a flow collection has no syntax");
        IReadOnlyList<DocNode> children = parent.Children;
        if (children.Count == 0)
        {
            InsertIntoEmptyFlow(parent, container, render);
            return;
        }

        if (children.All(EditLayout.IsComment))
        {
            throw new CfgeditException("a flow collection with only comments cannot take an entry yet");
        }

        string entry = render(mapping, true, 0);
        string spacing = FlowSpacing(container);
        if (index < children.Count)
        {
            DocNode site = children[index];
            if (EditLayout.StartsLine(_text, site.Start))
            {
                Insert(EditLayout.LineStart(_text, site.Start), EditLayout.LineIndent(_text, site.Start) + entry + "," + _newline);
            }
            else
            {
                Insert(site.Start, entry + "," + spacing);
            }

            return;
        }

        YamlNode last = Piece(children.Last(child => !EditLayout.IsComment(child))) ?? throw Internal("an entry has no syntax");
        YamlToken? comma = CommaAfter(container, last);
        int end = comma?.End ?? last.End;
        (int open, int close) = BracketsOf(container);
        if (EditLayout.StartsLine(_text, last.Start) && EditLayout.HasLineBreak(_text, open, close) && EditLayout.RestOfLineBlank(_text, end))
        {
            if (comma is null)
            {
                Insert(last.End, ",");
            }

            Insert(
                EditLayout.AfterLineBreak(_text, end),
                EditLayout.LineIndent(_text, last.Start) + entry + (comma is null ? string.Empty : ",") + _newline);
            return;
        }

        if (comma is { } present)
        {
            Insert(present.End, spacing + entry + ",");
        }
        else
        {
            Insert(last.End, "," + spacing + entry);
        }
    }

    /// <summary>
    /// The first entry of an empty flow collection. On the line of its owner (a block mapping entry or sequence item), the collection becomes
    /// block style: a mapping that is a sequence item takes the dash's line (compact), and any other entry goes on the next line, one level
    /// deeper than the owner. A root collection becomes block at column 0. Any other
    /// (a flow inside a flow, or a collection on a later line) gets the entry in its brackets.
    /// </summary>
    private void InsertIntoEmptyFlow(DocNode parent, YamlValueNode container, EntryRender render)
    {
        (int open, int close) = BracketsOf(container);
        bool mapping = parent.Kind == NodeKind.YamlMapping;
        YamlNode? piece = Piece(parent);
        Owner? owner = piece is YamlMappingEntry or YamlSequenceItem ? OwnerOf(piece, _text) : null;
        if (owner is { } at && !EditLayout.HasLineBreak(_text, at.End, open))
        {
            if (mapping && piece is YamlSequenceItem)
            {
                // A mapping that is a sequence item is written compact: its first key follows the dash on the dash's line, two columns
                // right of the dash, and the other members align with it.
                Replace(HorizontalStart(open), close + 1, " " + render(true, false, at.Column + 2));
                return;
            }

            int column = at.Column + IndentUnit().Length;
            if (!mapping && piece is YamlMappingEntry && !SequencesIndented())
            {
                column = at.Column;
            }

            Replace(HorizontalStart(open), close + 1, _newline + new string(' ', column) + render(mapping, false, column));
            return;
        }

        if (parent.Parent is { Kind: NodeKind.Document or NodeKind.YamlDocument })
        {
            int start = HorizontalStart(open);
            string prefix = start > EditLayout.LineStart(_text, start) ? _newline : string.Empty;
            Replace(start, close + 1, prefix + render(mapping, false, 0));
            return;
        }

        string interior = _text[(open + 1)..close];
        string flowEntry = render(mapping, true, 0);
        Replace(open + 1, close, interior.Length == 0 ? flowEntry : " " + flowEntry + " ");
    }

    /// <summary>The spacing after a comma in a flow collection: that of the first comma with spaces after it, else one space.</summary>
    private static string FlowSpacing(YamlValueNode container)
    {
        IReadOnlyList<YamlNode> parts = container.Parts;
        for (int k = 0; k + 1 < parts.Count; k++)
        {
            if (parts[k] is YamlToken { Kind: YamlTokenKind.Indicator, Text: "," } && parts[k + 1] is YamlToken { Kind: YamlTokenKind.Whitespace } space && space.Text.Length > 0)
            {
                return space.Text;
            }
        }

        return " ";
    }

    /// <summary>
    /// The indentation step of the document: the file's dominant step, which is spaces. The default is two spaces, which is also used when the
    /// file's step would be a tab.
    /// </summary>
    private string IndentUnit()
    {
        string unit = EditLayout.InferIndentUnit(_text);
        return unit.Length > 0 && unit.All(c => c == ' ') ? unit : "  ";
    }

    /// <summary>
    /// True when a sequence under a mapping key is indented under its key, as the file's first such sequence is. The default is indented.
    /// </summary>
    private bool SequencesIndented()
    {
        foreach (YamlNode item in _model.Syntax.Items)
        {
            foreach (YamlNode part in Walk(item))
            {
                if (part is YamlMappingEntry { Value: YamlBlockSequence { Items.Count: > 0 } sequence } entry)
                {
                    return Column(_text, sequence.Items[0].Dash.Start) > Column(_text, entry.Start);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// The line of a comment in a flow collection: the line of the child at <paramref name="index"/>, or the closing bracket's line, when that
    /// line starts with the child (or the bracket). Otherwise a comment would share a line with an entry, and it is refused.
    /// </summary>
    internal static (int Offset, string Indent) FlowCommentLine(string text, YamlValueNode container, IReadOnlyList<DocNode> children, int index)
    {
        (int open, int close) = BracketsOf(container);
        if (index < children.Count && EditLayout.StartsLine(text, children[index].Start))
        {
            int at = children[index].Start;
            return (EditLayout.LineStart(text, at), EditLayout.LineIndent(text, at));
        }

        if (index == children.Count && EditLayout.StartsLine(text, close))
        {
            string indent = children.Count > 0 ? EditLayout.LineIndent(text, children[^1].Start) : EditLayout.LineIndent(text, open);
            return (EditLayout.LineStart(text, close), indent);
        }

        throw new CfgeditException("a comment in a flow collection needs a line of its own here");
    }

    /// <summary>
    /// Where a new child of a block collection goes: the start of the line of the child at <paramref name="index"/> (its group's first line),
    /// or the line after the last child's group. A child that shares its line with a dash gives the dash-line form (the offset is the child's
    /// start). A comment that sits after an entry on its line goes after that line. The root collection of a document stays between the
    /// document's <c>---</c> line and its <c>...</c> line, since comments outside the document are children of its root too.
    /// </summary>
    internal static (int Offset, bool OnDashLine) BlockPlace(YamlModel model, string text, DocNode parent, int index)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        if (children.Count == 0)
        {
            return (text.Length, false);
        }

        int at;
        if (index < children.Count)
        {
            DocNode site = children[EditLayout.Group(parent, index, text).First];
            if (EditLayout.StartsLine(text, site.Start))
            {
                at = EditLayout.LineStart(text, site.Start);
            }
            else if (EditLayout.IsComment(site))
            {
                at = EditLayout.AfterLineBreak(text, site.End);
            }
            else
            {
                return (site.Start, true);
            }
        }
        else
        {
            int last = EditLayout.Group(parent, children.Count - 1, text).Last;
            at = LinesEnd(text, children[last].End);
        }

        if (ValueOf(model.SyntaxOf(parent)) is { } root && model.Syntax.Documents.FirstOrDefault(d => ReferenceEquals(d.Root, root)) is { } document)
        {
            if (document.StartMarker is { } start && at < EditLayout.AfterLineBreak(text, start.End))
            {
                at = EditLayout.AfterLineBreak(text, start.End);
            }

            if (document.EndMarker is { } end && at > EditLayout.LineStart(text, end.Start))
            {
                at = EditLayout.LineStart(text, end.Start);
            }
        }

        return (at, false);
    }

    /// <summary>
    /// The end of the lines that a span covers. A span that ends after a line break already ends a line: a block scalar with kept blank lines
    /// ends after them, and they are part of its value. Any other span ends at the end of its last line.
    /// </summary>
    internal static int LinesEnd(string text, int end)
    {
        return end > 0 && IsLineBreak(text[end - 1]) ? end : EditLayout.AfterLineBreak(text, end);
    }

    /// <summary>
    /// Writes text at an offset that starts a line, or at the end of the text. At the end of a text with no final line break, the document's
    /// line break comes first and the text keeps its missing final break.
    /// </summary>
    private void InsertText(int offset, string block)
    {
        if (offset == _text.Length && _text.Length > 0 && !IsLineBreak(_text[^1]))
        {
            string body = block.EndsWith(_newline, StringComparison.Ordinal) ? block[..^_newline.Length] : block;
            Insert(offset, _newline + body);
            return;
        }

        Insert(offset, block);
    }

    // ----- DuplicateNode -----

    private void PlanDuplicate(DuplicateNode op)
    {
        DocNode node = Node(op.Node);
        if (node.Kind == NodeKind.YamlComment)
        {
            DuplicateComment(node);
            return;
        }

        if (node.Kind == NodeKind.YamlDocument)
        {
            throw new CfgeditException("a document cannot be duplicated");
        }

        DocNode parent = Parent(node);
        if (parent.Kind is NodeKind.Document or NodeKind.YamlDocument)
        {
            throw new CfgeditException("the root value cannot be duplicated");
        }

        CopyRange range = CopySpan(_text, _model, parent, node);
        var edits = new List<(int Start, int End, string Text)>();
        if (parent.Kind == NodeKind.YamlMapping && Part(node, PartKind.Key) is { } key)
        {
            edits.Add((key.Start, key.End, YamlEncoder.EncodeKey(FreshName(parent, key.Value))));
        }

        AnchorRemovals(range.From, range.To, edits);
        string copy = CopyText(range.From, range.To, edits);
        switch (range.Form)
        {
            case CopyForm.Inline:
            {
                YamlValueNode container = ValueOf(Piece(parent)) ?? throw Internal("a flow collection has no syntax");
                YamlToken? comma = CommaAfter(container, Piece(node) ?? throw Internal("an entry has no syntax"));
                string spacing = FlowSpacing(container);
                if (comma is { } present)
                {
                    Insert(present.End, spacing + copy + ",");
                }
                else
                {
                    Insert(range.To, "," + spacing + copy);
                }

                return;
            }

            case CopyForm.Dash:
                // The copy's first line takes the column of the entry, and the copied text ends with its line break.
                InsertText(range.At, new string(' ', Column(_text, range.From)) + copy);
                return;

            default:
            {
                if (ValueOf(Piece(parent)) is YamlFlowMapping or YamlFlowSequence)
                {
                    YamlValueNode container = ValueOf(Piece(parent))!;
                    YamlNode piece = Piece(node) ?? throw Internal("an entry has no syntax");
                    if (CommaAfter(container, piece) is null)
                    {
                        // The original becomes a middle entry, so it takes the comma that the copy does not have.
                        Insert(piece.End, ",");
                    }
                }

                InsertText(range.At, copy);
                return;
            }
        }
    }

    /// <summary>A comment line is copied after its line, with the indentation of that line. A trailing comment is copied as a line of its own.</summary>
    private void DuplicateComment(DocNode comment)
    {
        string indent = EditLayout.LineIndent(_text, comment.Start);
        InsertText(EditLayout.AfterLineBreak(_text, comment.End), indent + _text[comment.Start..comment.End] + _newline);
    }

    /// <summary>
    /// Removes the anchors of the nodes in [<paramref name="from"/>, <paramref name="to"/>) from a copy: a copy with an anchor would define it
    /// twice. The spaces before an anchor go with it, or the spaces after it when it starts the copy. Aliases in the copy stay.
    /// </summary>
    private void AnchorRemovals(int from, int to, List<(int Start, int End, string Text)> edits)
    {
        foreach (YamlNode item in _model.Syntax.Items)
        {
            foreach (YamlNode part in Walk(item))
            {
                if (part is not YamlValueNode { Anchor: { } anchor } || anchor.Start < from || anchor.End > to)
                {
                    continue;
                }

                int start = anchor.Start;
                int end = anchor.End;
                if (start > from && _text[start - 1] is ' ' or '\t')
                {
                    while (start > from && _text[start - 1] is ' ' or '\t')
                    {
                        start--;
                    }
                }
                else
                {
                    while (end < to && _text[end] is ' ' or '\t')
                    {
                        end++;
                    }
                }

                edits.Add((start, end, string.Empty));
            }
        }
    }

    /// <summary>The key of a copy of a mapping entry: <c>name2</c>, then the next number up while taken (<see cref="EditLayout.CopyName"/>), unique among the entries' keys.</summary>
    private static string FreshName(DocNode parent, string name)
    {
        var taken = parent.Children
            .Where(child => child.Path.Steps[^1] is MemberStep)
            .Select(child => ((MemberStep)child.Path.Steps[^1]).Key)
            .ToHashSet(StringComparer.Ordinal);
        return EditLayout.CopyName(name, taken.Contains);
    }

    /// <summary>The text of a copy: [from, to) with its edits applied. The edits are offsets into the source, and they do not overlap.</summary>
    private string CopyText(int from, int to, List<(int Start, int End, string Text)> edits)
    {
        var builder = new StringBuilder(_text[from..to]);
        foreach ((int start, int end, string text) in edits.OrderByDescending(edit => edit.Start))
        {
            builder.Remove(start - from, end - start).Insert(start - from, text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The span of a copy of a node, and the offset it goes at, shared with the logical edit. A comment is copied after its line. A flow entry
    /// on a line of its own is copied with its line, and any other flow entry after its comma. A block entry is copied with its group (its
    /// attached comments and what is nested in it), after the group's last line.
    /// </summary>
    internal static CopyRange CopySpan(string text, YamlModel model, DocNode parent, DocNode node)
    {
        if (node.Kind == NodeKind.YamlComment)
        {
            return new CopyRange(node.Start, node.End, EditLayout.AfterLineBreak(text, node.End), CopyForm.Lines);
        }

        YamlNode piece = model.SyntaxOf(node) ?? throw Internal("an entry has no syntax");
        if (ValueOf(model.SyntaxOf(parent)) is { } container and (YamlFlowMapping or YamlFlowSequence))
        {
            YamlToken? comma = CommaAfter(container, piece);
            int end = comma?.End ?? piece.End;
            (int open, int close) = BracketsOf(container);
            if (EditLayout.HasLineBreak(text, open, close) && EditLayout.StartsLine(text, piece.Start) && EditLayout.RestOfLineBlank(text, end))
            {
                int after = EditLayout.AfterLineBreak(text, end);
                return new CopyRange(EditLayout.LineStart(text, piece.Start), after, after, CopyForm.Lines);
            }

            return new CopyRange(piece.Start, piece.End, comma?.End ?? piece.End, CopyForm.Inline);
        }

        IReadOnlyList<DocNode> children = parent.Children;
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), text);
        int start = children[first].Start;
        int stop = LinesEnd(text, children[last].End);
        if (EditLayout.StartsLine(text, start))
        {
            return new CopyRange(EditLayout.LineStart(text, start), stop, stop, CopyForm.Lines);
        }

        return new CopyRange(start, stop, stop, CopyForm.Dash);
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

/// <summary>
/// A unit of a YAML move: what moves as a whole. [<paramref name="First"/>, <paramref name="Last"/>] are the child indices of the unit, and
/// [<paramref name="Start"/>, <paramref name="End"/>) is its span of text. <paramref name="IsComment"/>: a comment alone. <paramref name="ValueEnd"/>
/// and <paramref name="Comma"/>: a flow entry's value end and its comma (null when it has none). <paramref name="EndsInComment"/>: the unit's last
/// child is a comment. <paramref name="KeepsBreak"/>: a block scalar that keeps its line breaks ends the span. <paramref name="Refusal"/>: the
/// message when the unit cannot move.
/// </summary>
internal sealed record YamlUnit(
    int First,
    int Last,
    int Start,
    int End,
    bool IsComment,
    int ValueEnd,
    YamlToken? Comma,
    bool EndsInComment,
    bool KeepsBreak,
    string? Refusal);

/// <summary>
/// The units that a YAML move carries, and the arithmetic of a move over them. The planner and the logical edit both use them, so that they
/// agree on what moves together. A block unit is whole lines: its indentation, its entry or item with the nested lines, and its attached and
/// trailing comments. A flow unit runs from its first token to its last, with the comma of its entry. A comment that is moved alone is split
/// out of its group. A document unit is its lines, with its markers.
/// </summary>
internal static class YamlMoves
{
    /// <summary>The refusal for the first key of a mapping that is the value of a dash, which shares the dash's line.</summary>
    public const string FirstKeyRefusal = "the first key of a '- key:' item cannot move; move the item";

    /// <summary>The refusal for the first item of a sequence that is the value of a dash, which shares the dash's line.</summary>
    public const string FirstItemRefusal = "the first item of a '- -' item cannot move; move the item";

    /// <summary>The refusal for a comment that ends a line with a value: it moves with that entry.</summary>
    public const string TrailingRefusal = "a trailing comment moves with its entry";

    /// <summary>The refusal for a flow entry with a comment between its value and its comma.</summary>
    public const string CommentCommaRefusal = "a comment between an item and its comma cannot move";

    /// <summary>The refusal for a document that moves without its own marker, which would merge with its neighbour.</summary>
    public const string DocumentMarkerRefusal = "only documents with a '---' marker can move";

    /// <summary>The refusal for a document with directives, which must stay before its own marker.</summary>
    public const string DocumentDirectiveRefusal = "a document with directives cannot move";

    /// <summary>The refusal for a move into the middle of a unit (between a comment and the entry it is attached to, say).</summary>
    public const string SplitRefusal = "this cannot move there: it would split an entry from its comments";

    /// <summary>The refusal for units that are not one run of text, with other text between them.</summary>
    public const string NotContiguousRefusal = "this cannot move: its units are not contiguous";

    /// <summary>The refusal for a comment that would land where the next entry follows it on the same line.</summary>
    public const string CommentRunRefusal = "this cannot move there: a comment would run into the next entry";

    /// <summary>The refusal for a block scalar that keeps its line breaks when a break is added or removed at its end.</summary>
    public const string KeptBreakRefusal = "this cannot move: a block scalar keeps the line breaks at the end of the file";

    /// <summary>The units of a parent's children, in order. Together they cover every child once.</summary>
    /// <param name="model">The document's model.</param>
    /// <param name="parent">A collection, or the document node whose children are documents.</param>
    /// <param name="text">The source text.</param>
    /// <param name="split">The index of a comment child that moves alone, out of the unit it sits in; -1 for none.</param>
    public static List<YamlUnit> Units(YamlModel model, DocNode parent, string text, int split)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        List<(int First, int Last)> groups = parent.Kind == NodeKind.Document
            ? children.Select((_, k) => (k, k)).ToList()
            : Groups(parent, text);
        if (split >= 0)
        {
            groups = Split(groups, split);
        }

        YamlValueNode? container = parent.Kind == NodeKind.Document ? null : YamlEditPlanner.ValueOf(model.SyntaxOf(parent));
        return groups.Select(group => Describe(model, parent, container, text, group.First, group.Last)).ToList();
    }

    /// <summary>The unit that holds a child index.</summary>
    public static int UnitOf(IReadOnlyList<YamlUnit> units, int index)
    {
        for (int k = 0; k < units.Count; k++)
        {
            if (units[k].First <= index && index <= units[k].Last)
            {
                return k;
            }
        }

        throw new InvalidOperationException("No unit holds the child.");
    }

    /// <summary>
    /// The unit that a move puts the moved unit before, as an index into the units, or the number of units for the end of the list. It is -1
    /// when the position is inside a unit. <paramref name="newIndex"/> counts the children that stay, as <see cref="MoveNode.NewIndex"/> does,
    /// and is clamped to them.
    /// </summary>
    public static int Destination(IReadOnlyList<YamlUnit> units, int moved, int newIndex, int childCount)
    {
        YamlUnit unit = units[moved];
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

    /// <summary>The units in their order after the moved unit goes before the unit at <paramref name="destination"/>.</summary>
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

    /// <summary>The child groups of a collection: each entry with its attached comments, and each other comment alone.</summary>
    private static List<(int First, int Last)> Groups(DocNode parent, string text)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        var groups = new List<(int First, int Last)>();
        int count = children.Count;
        int k = 0;
        while (k < count)
        {
            int entry = k;
            while (entry < count && EditLayout.IsComment(children[entry]))
            {
                entry++;
            }

            if (entry == count)
            {
                for (; k < count; k++)
                {
                    groups.Add((k, k));
                }

                break;
            }

            (int first, int last) = EditLayout.Group(parent, entry, text);
            first = Math.Max(first, k);
            for (; k < first; k++)
            {
                groups.Add((k, k));
            }

            groups.Add((first, last));
            k = last + 1;
        }

        return groups;
    }

    /// <summary>A comment that moves alone: its group is cut so that it is a group of its own.</summary>
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

    private static YamlUnit Describe(YamlModel model, DocNode parent, YamlValueNode? container, string text, int first, int last)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        if (parent.Kind == NodeKind.Document)
        {
            var document = model.SyntaxOf(children[first]) as YamlDocumentNode ?? throw new InvalidOperationException("The document has no syntax.");
            string? refusal = document.StartMarker is null
                ? DocumentMarkerRefusal
                : document.Directives.Count > 0 ? DocumentDirectiveRefusal : null;
            int start = EditLayout.LineStart(text, children[first].Start);
            int end = LineEnd(text, children[last].End);
            return new YamlUnit(first, last, start, end, false, -1, null, false, KeepsBreak(model, children, first, last, end), refusal);
        }

        int main = first;
        while (main <= last && EditLayout.IsComment(children[main]))
        {
            main++;
        }

        if (main > last)
        {
            // A comment alone. A comment that shares its line with a value moves with that value.
            if (!EditLayout.StartsLine(text, children[first].Start))
            {
                return new YamlUnit(first, last, children[first].Start, children[last].End, true, -1, null, true, false, TrailingRefusal);
            }

            bool flowComment = container is YamlFlowMapping or YamlFlowSequence;
            int start = flowComment ? children[first].Start : EditLayout.LineStart(text, children[first].Start);
            int end = flowComment ? children[last].End : LineEnd(text, children[last].End);
            return new YamlUnit(first, last, start, end, true, -1, null, true, false, null);
        }

        DocNode child = children[main];
        YamlNode piece = model.SyntaxOf(child) ?? throw new InvalidOperationException("The entry has no syntax.");
        if (container is null or YamlBlockMapping or YamlBlockSequence)
        {
            // An entry that does not start its line shares the dash line of its item.
            string? refusal = EditLayout.StartsLine(text, child.Start)
                ? null
                : parent.Kind == NodeKind.YamlMapping ? FirstKeyRefusal : FirstItemRefusal;
            int start = EditLayout.LineStart(text, children[first].Start);
            int end = LineEnd(text, children[last].End);
            bool keeps = KeepsBreak(model, children, first, last, end);
            return new YamlUnit(first, last, start, end, false, -1, null, EditLayout.IsComment(children[last]), keeps, refusal);
        }

        YamlToken? comma = YamlEditPlanner.CommaAfter(container, piece);
        int valueEnd = piece.End;
        string? flowRefusal = null;
        if (comma is { } at)
        {
            for (int k = first; k <= last; k++)
            {
                if (EditLayout.IsComment(children[k]) && children[k].Start >= valueEnd && children[k].End <= at.Start)
                {
                    flowRefusal = CommentCommaRefusal;
                }
            }
        }

        int unitEnd = children[first].End;
        for (int k = first; k <= last; k++)
        {
            unitEnd = Math.Max(unitEnd, children[k].End);
        }

        if (comma is { } tail)
        {
            unitEnd = Math.Max(unitEnd, tail.End);
        }

        bool endsInComment = EditLayout.IsComment(children[last]);
        return new YamlUnit(first, last, children[first].Start, unitEnd, false, valueEnd, comma, endsInComment, false, flowRefusal);
    }

    /// <summary>The end of a span that ends with its line: a span that already ends after a line break keeps it.</summary>
    private static int LineEnd(string text, int end)
    {
        return end > 0 && text[end - 1] is ('\r' or '\n') ? end : EditLayout.AfterLineBreak(text, end);
    }

    /// <summary>True when a block scalar that keeps its line breaks ends exactly at the end of the span.</summary>
    private static bool KeepsBreak(YamlModel model, IReadOnlyList<DocNode> children, int first, int last, int end)
    {
        for (int k = first; k <= last; k++)
        {
            if (model.SyntaxOf(children[k]) is { } piece
                && YamlEditPlanner.Walk(piece).Any(part => part is YamlScalar scalar && scalar.End == end && HasKeep(scalar)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasKeep(YamlScalar scalar)
    {
        return scalar.Parts.Any(part =>
            part is YamlToken { Kind: YamlTokenKind.BlockHeader } header && header.Text.Contains('+', StringComparison.Ordinal));
    }
}
