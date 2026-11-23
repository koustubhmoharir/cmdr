using System.Text;
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

    /// <summary>
    /// The syntax that carries each value: the key/value, inline member or array item that holds it. An inline table's carrier is the
    /// piece that its own text does not name, so it is looked up here.
    /// </summary>
    private readonly Dictionary<TomlValue, TomlNode> _carriers = [];

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
                    _carriers[keyValue.Value] = keyValue;
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

        CheckCommentText(text);
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
        if (change.Kind == TomlNewKind.Array)
        {
            // A value that is not an array becomes the array's only item, and its text is kept as it is.
            if (!isArray)
            {
                Replace(syntax.Start, syntax.End, "[" + _text[syntax.Start..syntax.End] + "]");
            }

            return;
        }

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

    // ----- MoveNode -----

    /// <summary>
    /// Moves a unit among its siblings: a key/value with its attached and trailing comments, a comment on a line of its own, an item or
    /// member of an array or inline table, or a table with its sections (see <see cref="TomlLayout.Units"/>). The units from the moved one
    /// to its destination are written back as one span. Each unit keeps its text, and the blank lines (or the spaces and line breaks of an
    /// array) between the units stay where they are. In an array or an inline table a comma belongs to the item in front of it, and the
    /// array's last item keeps the trailing comma that the array had.
    /// </summary>
    private void PlanMove(MoveNode op)
    {
        DocNode node = Node(op.Node);
        DocNode parent = Parent(node);
        if (parent.Kind == NodeKind.Document)
        {
            throw new CfgeditException("the root table cannot be moved");
        }

        if (IsInlineMember(parent) && !IsInline(parent))
        {
            throw new CfgeditException("a member of a dotted table inside an inline table cannot move yet");
        }

        int index = IndexOf(parent, node);
        List<TomlUnit> units = TomlLayout.Units(_model, parent, _text, index);
        int moved = units.FindIndex(unit => unit.First <= index && index <= unit.Last);
        TomlUnit mover = units[moved];
        if (mover.Refusal is not null)
        {
            throw new CfgeditException(mover.Refusal);
        }

        int destination = TomlLayout.Destination(units, moved, op.NewIndex, parent.Children.Count);
        if (destination < 0)
        {
            throw new CfgeditException("this cannot move there: it would split an entry from its comments");
        }

        List<int> order = TomlLayout.Order(units.Count, moved, destination);
        (int lo, int hi) = TomlLayout.Span(order);
        if (lo < 0)
        {
            return;
        }

        for (int k = lo; k <= hi; k++)
        {
            if (k != moved && (units[k].Refusal is not null || !SameKind(mover, units[k])))
            {
                throw new CfgeditException(TomlLayout.NeighbourRefusal);
            }
        }

        bool tokens = IsStaticArray(parent) || IsInline(parent);
        List<string> texts;
        if (tokens)
        {
            texts = TokenTexts(parent, units, order, lo, hi);
        }
        else
        {
            RequireNoAttachment(units, order, lo, hi);
            texts = LineTexts(units, lo, hi, order);
        }

        int start = units[lo].Start;
        int end = units[hi].End;
        string span = Assemble(units, lo, hi, texts);
        if (span != _text[start..end])
        {
            Replace(start, end, span);
        }
    }

    /// <summary>A unit can take another's place only when both are of one kind: tables with tables, entries and comments of one section.</summary>
    private static bool SameKind(TomlUnit mover, TomlUnit other)
    {
        if (mover.Kind == TomlUnitKind.Table)
        {
            return other.Kind == TomlUnitKind.Table;
        }

        return (other.Kind is TomlUnitKind.Entry or TomlUnitKind.Comment) && other.Section == mover.Section;
    }

    /// <summary>
    /// The text of the units from <paramref name="lo"/> to <paramref name="hi"/> in their new order, in lines: a unit that is not the last one
    /// gets a line break when it had none (it was the file's last line), and the last one loses its break when the file must end without one.
    /// </summary>
    private List<string> LineTexts(List<TomlUnit> units, int lo, int hi, List<int> order)
    {
        bool atEnd = hi == units.Count - 1 && units[hi].End == _text.Length && !EndsWithBreak(_text);
        var texts = new List<string>();
        for (int p = lo; p <= hi; p++)
        {
            TomlUnit unit = units[order[p]];
            string text = _text[unit.Start..unit.End];
            if (p < hi && !EndsWithBreak(text))
            {
                text += _newline;
            }

            if (p == hi && atEnd)
            {
                text = WithoutBreak(text);
            }

            texts.Add(text);
        }

        return texts;
    }

    /// <summary>
    /// The text of the units from <paramref name="lo"/> to <paramref name="hi"/> in their new order, for an array or an inline table. An item
    /// takes a comma unless it is the last item of the array: that one keeps the comma the array had at its end.
    /// </summary>
    private List<string> TokenTexts(DocNode parent, List<TomlUnit> units, List<int> order, int lo, int hi)
    {
        int lastEntry = units.FindLastIndex(unit => unit.Kind == TomlUnitKind.Entry);
        bool hadTrailing = lastEntry >= 0 && ItemSpans(parent, units[lastEntry]).Comma is not null;
        int newLast = -1;
        if (lastEntry >= lo && lastEntry <= hi)
        {
            for (int p = lo; p <= hi; p++)
            {
                if (units[order[p]].Kind == TomlUnitKind.Entry)
                {
                    newLast = p;
                }
            }
        }

        var texts = new List<string>();
        for (int p = lo; p <= hi; p++)
        {
            TomlUnit unit = units[order[p]];
            string text = _text[unit.Start..unit.End];
            if (unit.Kind == TomlUnitKind.Entry)
            {
                bool needComma = p != newLast || hadTrailing;
                (int valueEnd, TomlSpan? comma) = ItemSpans(parent, unit);
                if (needComma && comma is null)
                {
                    text = text.Insert(valueEnd - unit.Start, ",");
                }
                else if (!needComma && comma is { } span)
                {
                    text = text[..(valueEnd - unit.Start)] + text[(span.End - unit.Start)..];
                }
            }

            texts.Add(text);
        }

        return texts;
    }

    /// <summary>The end of a value and the comma after it, for an item of an array or an inline table.</summary>
    private (int ValueEnd, TomlSpan? Comma) ItemSpans(DocNode parent, TomlUnit unit)
    {
        DocNode child = parent.Children[unit.First];
        return Carrier(child) switch
        {
            TomlInlineMember member => (member.Value.End, member.Comma),
            TomlArrayItem item => (item.Value.End, item.Comma),
            _ => throw Internal("an item has no syntax"),
        };
    }

    /// <summary>
    /// Refuses a move that would leave a comment on the line before a table header with no blank line between: the comment would then belong to
    /// that table. The pairs checked are the new neighbours of the span and the units on either side of it.
    /// </summary>
    private void RequireNoAttachment(List<TomlUnit> units, List<int> order, int lo, int hi)
    {
        if (lo > 0)
        {
            RequireNoAttachment(units[lo - 1], units[order[lo]], units[lo - 1].End, units[lo].Start);
        }

        for (int p = lo; p < hi; p++)
        {
            RequireNoAttachment(units[order[p]], units[order[p + 1]], units[p].End, units[p + 1].Start);
        }

        if (hi + 1 < units.Count)
        {
            RequireNoAttachment(units[order[hi]], units[hi + 1], units[hi].End, units[hi + 1].Start);
        }
    }

    private void RequireNoAttachment(TomlUnit before, TomlUnit after, int gapStart, int gapEnd)
    {
        if (gapStart == gapEnd && after.Kind == TomlUnitKind.Table && EndsWithComment(before))
        {
            throw new CfgeditException("this cannot move there: a comment would then sit on the table header below it");
        }
    }

    private bool EndsWithComment(TomlUnit unit)
    {
        int at = TomlLayout.ItemAt(_model, unit.End - 1);
        return at >= 0 && _model.Syntax.Items[at] is TomlCommentItem;
    }

    /// <summary>The text of a span of units, with the original text between them (blank lines, or the spaces and line breaks of an array).</summary>
    private string Assemble(List<TomlUnit> units, int lo, int hi, List<string> texts)
    {
        var builder = new StringBuilder();
        for (int p = lo; p <= hi; p++)
        {
            builder.Append(texts[p - lo]);
            if (p < hi)
            {
                string gap = _text[units[p].End..units[p + 1].Start];
                if (gap.Any(c => c is not (' ' or '\t' or '\r' or '\n')))
                {
                    throw Internal("the text between two units is not blank");
                }

                builder.Append(gap);
            }
        }

        return builder.ToString();
    }

    private static bool EndsWithBreak(string text)
    {
        return text.Length > 0 && text[^1] is '\r' or '\n';
    }

    private static string WithoutBreak(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        return EndsWithBreak(text) ? text[..^1] : text;
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

    // ----- InsertNode -----

    private void PlanInsert(InsertNode op)
    {
        DocNode parent = Node(op.Parent);
        if (parent.Kind is not (NodeKind.TomlTable or NodeKind.TomlArray))
        {
            throw new CfgeditException("a TOML value has no children");
        }

        int count = parent.Children.Count;
        if (op.Index < 0 || op.Index > count)
        {
            throw new CfgeditException($"Index {op.Index} is outside the children, 0 to {count}");
        }

        switch (op.Node)
        {
            case NewTomlValue value:
                InsertValue(parent, op.Index, value);
                return;
            case NewTomlComment comment:
                CheckCommentText(comment.Text);
                InsertComment(parent, op.Index, comment.Text);
                return;
            default:
                throw new CfgeditException("this kind of node cannot be inserted into TOML");
        }
    }

    private void InsertValue(DocNode parent, int index, NewTomlValue value)
    {
        switch (_model.ShapeOf(parent))
        {
            case TomlContainer.Inline:
                InsertInlineMember(parent, index, value);
                break;
            case TomlContainer.Array:
                InsertArrayItem(parent, index, value);
                break;
            case TomlContainer.ArrayOfTables:
                InsertTableItem(parent, index, value);
                break;
            default:
                InsertTableMember(parent, index, value);
                break;
        }
    }

    private void InsertComment(DocNode parent, int index, string text)
    {
        switch (_model.ShapeOf(parent))
        {
            case TomlContainer.Inline:
                throw new CfgeditException("a comment cannot go into an inline table");
            case TomlContainer.ArrayOfTables:
                throw new CfgeditException("an array of tables holds tables only");
            case TomlContainer.Array:
                InsertArrayComment(parent, index, text);
                break;
            default:
                WriteTableLine(parent, index, format => format.Indent + "#" + text);
                break;
        }
    }

    /// <summary>A key/value or a table in a table: a section for a table in a header, root or implicit table, else a line.</summary>
    private void InsertTableMember(DocNode table, int index, NewTomlValue value)
    {
        if (value.Key is null)
        {
            throw new CfgeditException("a key is needed for an entry of a table");
        }

        CheckKeyFree(table, value.Key);
        TomlContainer shape = _model.ShapeOf(table);
        if (value.Kind == TomlNewKind.Table && shape is TomlContainer.Root or TomlContainer.Header or TomlContainer.Implicit)
        {
            WriteLines(SubtreeOffset(table), [HeaderText(Names(table).Append(value.Key), false)], section: true);
            return;
        }

        string token = ValueToken(value.Kind, value.Value);
        string key = TomlEncoder.EncodeKey(value.Key);
        WriteTableLine(table, index, format => format.Indent + format.Prefix + key + format.Assign + token);
    }

    private void InsertInlineMember(DocNode table, int index, NewTomlValue value)
    {
        if (value.Key is null)
        {
            throw new CfgeditException("a key is needed for a member of an inline table");
        }

        CheckKeyFree(table, value.Key);
        InsertInlineToken(table, index, value.Key, ValueToken(value.Kind, value.Value));
    }

    /// <summary>Writes a member with a key and a value token into an inline table, at an index among its members.</summary>
    private void InsertInlineToken(DocNode table, int index, string key, string token)
    {
        var syntax = (TomlInlineTable)(ValueSyntax(table) ?? throw Internal("an inline table has no syntax"));
        var members = syntax.Entries.OfType<TomlInlineMember>().ToList();
        string assign = members.Count > 0 ? Text(members[0].Assignment) : " = ";
        string member = TomlEncoder.EncodeKey(key) + assign + token;
        if (members.Count == 0)
        {
            string interior = _text[(syntax.Start + 1)..(syntax.End - 1)];
            Replace(syntax.Start + 1, syntax.End - 1, interior.Length == 0 ? member : " " + member + " ");
            return;
        }

        string spacing = CommaSpacing(members.Where(m => m.Comma is not null).Select(m => m.Comma!.Value));
        if (index == members.Count)
        {
            Insert(members[^1].Value.End, "," + spacing + member);
            return;
        }

        Insert(members[index].Start, member + "," + spacing);
    }

    private void InsertArrayItem(DocNode array, int index, NewTomlValue value)
    {
        if (value.Key is not null)
        {
            throw new CfgeditException("an item of an array has no key");
        }

        InsertArrayToken(array, index, ValueToken(value.Kind, value.Value));
    }

    /// <summary>Writes an item with a value token into a static array, at an index among its children.</summary>
    private void InsertArrayToken(DocNode array, int index, string token)
    {
        var syntax = (TomlArray)(ValueSyntax(array) ?? throw Internal("an array has no syntax"));
        IReadOnlyList<DocNode> children = array.Children;
        var items = syntax.Entries.OfType<TomlArrayItem>().ToList();
        if (items.Count == 0)
        {
            if (children.Count > 0)
            {
                throw new CfgeditException("an array with only comments cannot take an item yet");
            }

            Replace(syntax.Start + 1, syntax.End - 1, token);
            return;
        }

        bool multiline = EditLayout.HasLineBreak(_text, syntax.Start, syntax.End);
        if (index < children.Count)
        {
            DocNode site = children[index];
            if (multiline && EditLayout.StartsLine(_text, site.Start))
            {
                Insert(EditLayout.LineStart(_text, site.Start), EditLayout.LineIndent(_text, site.Start) + token + "," + _newline);
                return;
            }

            Insert(site.Start, token + "," + CommaSpacing(items.Where(i => i.Comma is not null).Select(i => i.Comma!.Value)));
            return;
        }

        TomlArrayItem last = items[^1];
        int lastEnd = Math.Max(children[^1].End, TomlLayout.ItemEnd(_model, array, children.Count - 1));
        if (multiline && EditLayout.StartsLine(_text, last.Value.Start) && EditLayout.RestOfLineBlank(_text, lastEnd))
        {
            // One item per line: the new item goes on its own line, after the last one. A trailing comma stays a trailing comma.
            bool trailing = last.Comma is not null;
            if (!trailing)
            {
                Insert(last.Value.End, ",");
            }

            Insert(
                EditLayout.AfterLineBreak(_text, lastEnd),
                EditLayout.LineIndent(_text, last.Value.Start) + token + (trailing ? "," : string.Empty) + _newline);
            return;
        }

        string spacing = CommaSpacing(items.Where(i => i.Comma is not null).Select(i => i.Comma!.Value));
        if (last.Comma is { } comma)
        {
            Insert(comma.End, spacing + token + ",");
        }
        else
        {
            Insert(last.Value.End, "," + spacing + token);
        }
    }

    private void InsertArrayComment(DocNode array, int index, string text)
    {
        IReadOnlyList<DocNode> children = array.Children;
        string line = "#" + text;
        if (index < children.Count && EditLayout.StartsLine(_text, children[index].Start))
        {
            Insert(EditLayout.LineStart(_text, children[index].Start), EditLayout.LineIndent(_text, children[index].Start) + line + _newline);
            return;
        }

        if (index == children.Count && EditLayout.StartsLine(_text, array.End - 1))
        {
            string indent = children.Count > 0 ? EditLayout.LineIndent(_text, children[^1].Start) : EditLayout.LineIndent(_text, array.Start);
            Insert(EditLayout.LineStart(_text, array.End - 1), indent + line + _newline);
            return;
        }

        throw new CfgeditException("a comment in an array needs a line of its own here");
    }

    /// <summary>A new array of tables item: a section with the table's header, placed before the item that follows, else after the last one.</summary>
    private void InsertTableItem(DocNode array, int index, NewTomlValue value)
    {
        if (value.Kind != TomlNewKind.Table)
        {
            throw new CfgeditException("an array of tables holds tables only");
        }

        if (value.Key is not null)
        {
            throw new CfgeditException("an item of an array has no key");
        }

        IReadOnlyList<DocNode> children = array.Children;
        int offset = index < children.Count
            ? EditLayout.LineStart(_text, children[index].Start)
            : children.Count == 0 ? _text.Length : EditLayout.AfterLineBreak(_text, children[^1].End);
        WriteLines(offset, [HeaderText(Names(array), true)], section: true);
    }

    // ----- MoveNodeTo -----

    /// <summary>
    /// Moves a node into another table or array, with its comments. A key/value, an inline member or an array item moves as a line, a
    /// member or an item of the new container (<see cref="PlanMoveEntry"/>). A table with its own header, a whole array of tables, or an
    /// item of one, moves as its header sections (<see cref="PlanMoveSection"/>). The rest is refused with a message.
    /// </summary>
    private void PlanMoveTo(MoveNodeTo op)
    {
        DocNode node = Node(op.Node);
        DocNode parent = Parent(node);
        if (parent.Kind == NodeKind.Document)
        {
            throw new CfgeditException("the root table cannot be moved");
        }

        if (node.Kind == NodeKind.TomlComment)
        {
            throw new CfgeditException("a comment line moves within its own table or array only");
        }

        DocNode destination = Node(op.NewParent);
        if (destination.Kind is not (NodeKind.TomlTable or NodeKind.TomlArray))
        {
            throw new CfgeditException("the new parent must be a TOML table or array");
        }

        if (ReferenceEquals(destination, parent))
        {
            throw new CfgeditException("the node is already in that container");
        }

        if (InSubtree(destination, node))
        {
            throw new CfgeditException("a node cannot be moved into itself");
        }

        int count = destination.Children.Count;
        if (op.NewIndex < 0 || op.NewIndex > count)
        {
            throw new CfgeditException($"Index {op.NewIndex} is outside the children, 0 to {count}");
        }

        switch (_model.ShapeOf(destination))
        {
            case TomlContainer.ArrayOfTables:
                throw new CfgeditException("an array of tables holds tables only; a table can move into one of the array's tables instead");
            case TomlContainer.Implicit or TomlContainer.Dotted:
                throw new CfgeditException("a table defined by dotted keys, or only by its sub-tables, cannot take a moved node yet");
        }

        if (IsSection(node))
        {
            PlanMoveSection(node, destination, op);
        }
        else
        {
            PlanMoveEntry(node, parent, destination, op);
        }
    }

    /// <summary>A table with its own header, a whole array of tables, or an item of one: what moves as header sections.</summary>
    private bool IsSection(DocNode node)
    {
        return node.Kind == NodeKind.TomlTable ? _model.ShapeOf(node) == TomlContainer.Header : IsTableArray(node);
    }

    /// <summary>
    /// Moves a key/value, an inline member or an array item. A key/value of a table keeps its line (with its attached comment lines above
    /// and its trailing comment) and goes to a table as a line at the position of the index. An inline member and an item go as their value
    /// text, and take the destination's key rules. A comment on an entry can only move from a table to a table.
    /// </summary>
    private void PlanMoveEntry(DocNode node, DocNode parent, DocNode destination, MoveNodeTo op)
    {
        TomlContainer parentShape = _model.ShapeOf(parent);
        if (node.Kind == NodeKind.TomlTable && _model.ShapeOf(node) == TomlContainer.Dotted)
        {
            throw new CfgeditException("a table defined by dotted keys cannot move; move its keys instead");
        }

        if (node.Kind == NodeKind.TomlTable && _model.ShapeOf(node) == TomlContainer.Implicit)
        {
            throw new CfgeditException("a table defined only by its sub-tables cannot move yet");
        }

        if (parentShape is TomlContainer.Dotted or TomlContainer.Implicit)
        {
            throw new CfgeditException("a value under dotted keys cannot move to another table yet; move the table that defines it instead");
        }

        bool fromTable = parentShape is TomlContainer.Root or TomlContainer.Header;
        TomlKeyValueItem? keyValue = null;
        string? name = null;
        if (fromTable)
        {
            keyValue = DirectKeyValue(node) ?? throw Internal("an entry of a table has no key/value");
            if (keyValue.Key.Parts.Count != 1)
            {
                throw new CfgeditException("a dotted key cannot move to another table yet; only a key with one name can move");
            }

            name = keyValue.Key.Parts[0].Name;
        }
        else if (parentShape == TomlContainer.Inline)
        {
            name = NameOf(node);
        }

        TomlContainer destShape = _model.ShapeOf(destination);
        bool tableDestination = destShape is TomlContainer.Root or TomlContainer.Header;
        bool tableToTable = fromTable && tableDestination;
        string? key = null;
        if (destShape == TomlContainer.Array)
        {
            if (op.NewKey is not null)
            {
                throw new CfgeditException("an item of an array has no key");
            }
        }
        else
        {
            key = op.NewKey ?? name ?? throw new CfgeditException("a key is needed for an item moving into a table");
            CheckKeyFree(destination, key);
        }

        int index = IndexOf(parent, node);
        (int first, int last) = TomlLayout.Block(parent, index, _text);
        if ((first < index || last > index) && !tableToTable)
        {
            throw new CfgeditException("a comment on an entry cannot move into an array or inline table yet");
        }

        TomlValue value = ValueSyntax(node) ?? throw Internal("an entry has no value");
        string token = _text[value.Start..value.End];
        string suffix = string.Empty;
        string comments = string.Empty;
        if (keyValue is not null)
        {
            // The trailing comment of a line stays with the value; the attached comment lines above it move with it.
            IReadOnlyList<TomlItem> items = _model.Syntax.Items;
            int kvIndex = ItemIndexOf(keyValue);
            int from = kvIndex;
            while (from > 0 && items[from - 1] is TomlCommentItem)
            {
                from--;
            }

            suffix = _text[value.End..EditLayout.TrimBreaks(_text, value.End, keyValue.End)];
            for (int k = from; k < kvIndex; k++)
            {
                comments += _text[items[k].Start..items[k].End];
            }

            for (int k = from; k <= kvIndex; k++)
            {
                Replace(items[k].Start, items[k].End, string.Empty);
            }
        }
        else
        {
            if (parentShape == TomlContainer.Inline)
            {
                DeleteInlineMembers(parent, node);
            }
            else
            {
                DeleteArrayItem(parent, node);
            }
        }

        if (!tableDestination && suffix.Trim(' ', '\t').Length > 0)
        {
            throw new CfgeditException("a trailing comment on an entry cannot move into an array or inline table yet");
        }

        switch (destShape)
        {
            case TomlContainer.Root or TomlContainer.Header:
                string line = TomlEncoder.EncodeKey(key!);
                WriteTableLine(destination, op.NewIndex, format => comments + format.Indent + line + format.Assign + token + suffix);
                break;
            case TomlContainer.Inline:
                InsertInlineToken(destination, op.NewIndex, key!, token);
                break;
            case TomlContainer.Array:
                InsertArrayToken(destination, op.NewIndex, token);
                break;
            default:
                throw Internal("an entry cannot go into a " + destShape);
        }
    }

    /// <summary>
    /// Moves a table with its own header, a whole array of tables, or an item of one, as its header sections: one run of text from its
    /// first line to its last. Every header in the run gets the new prefix: the destination's names and the new name, which is the
    /// <see cref="MoveNodeTo.NewKey"/> or the node's own name. The run goes where <see cref="SectionOffset"/> puts it.
    /// </summary>
    private void PlanMoveSection(DocNode node, DocNode destination, MoveNodeTo op)
    {
        if (_model.ShapeOf(destination) is not (TomlContainer.Root or TomlContainer.Header))
        {
            throw new CfgeditException("a table with its own header can only move into a table with a header, or into the root");
        }

        if (destination.Parent is { Kind: NodeKind.TomlArray } holder && _model.ShapeOf(holder) == TomlContainer.ArrayOfTables)
        {
            throw new CfgeditException("a table with its own header cannot move into an item of an array of tables yet");
        }

        bool item = node.Kind == NodeKind.TomlTable && node.Parent is { Kind: NodeKind.TomlArray };
        DocNode named = item ? node.Parent ?? throw Internal("an array item has no array") : node;
        string name = op.NewKey ?? NameOf(named);
        int depth = Depth(named);
        CheckKeyFree(destination, name);

        int start = EditLayout.LineStart(_text, node.Start);
        int end = EditLayout.AfterLineBreak(_text, node.End);
        RequireContiguous(node, start, end);

        string prefix = string.Join(".", Names(destination).Append(name).Select(TomlEncoder.EncodeKey));
        var renames = new List<(int Start, int End, string Text)>();
        foreach (TomlItem entry in _model.Syntax.Items)
        {
            if (entry is TomlHeaderItem header && entry.Start >= start && entry.End <= end)
            {
                renames.Add((header.Key.Parts[0].Start, header.Key.Parts[depth - 1].End, prefix));
            }
        }

        string copy = Rename(_text[start..end], start, renames);
        int at = SectionOffset(destination, op.NewIndex);

        // The blank line after the run goes with it when the run starts the file or follows a blank line, so that the file keeps one blank line
        // between sections. It stays when the new place is the blank line itself.
        int removeEnd = end;
        if (end < _text.Length && BlankLineAt(end) && (start == 0 || PrecededByBlankLine(start)))
        {
            int after = EditLayout.AfterLineBreak(_text, end);
            if (after > end && (at <= start || at >= after))
            {
                removeEnd = after;
            }
        }

        string block = LinesBody(at, [copy], section: true, copyText: true);
        if (at <= start)
        {
            Replace(at, removeEnd, block + _text[at..start]);
        }
        else
        {
            Replace(start, at, _text[removeEnd..at] + block);
        }
    }

    /// <summary>
    /// The offset where a moved section goes in a table: at the child at the index when that child is a section of its own (before its
    /// comment lines), else after the table's key/values, or after its subtree when the index is past the children.
    /// </summary>
    private int SectionOffset(DocNode table, int index)
    {
        IReadOnlyList<DocNode> children = table.Children;
        for (int k = index; k < children.Count; k++)
        {
            DocNode child = children[k];
            if (EditLayout.IsComment(child))
            {
                continue;
            }

            return IsSectionChild(child) ? HeaderOffset(child) : AfterKeyValuesOffset(table);
        }

        return SubtreeOffset(table);
    }

    /// <summary>True for a child that is a table with its own header, or an array of tables.</summary>
    private bool IsSectionChild(DocNode child)
    {
        return child.Kind == NodeKind.TomlTable && _model.ShapeOf(child) == TomlContainer.Header
            || child.Kind == NodeKind.TomlArray && _model.ShapeOf(child) == TomlContainer.ArrayOfTables;
    }

    /// <summary>The offset of a section's header line, moved up over the comment lines directly above it, which belong to the section.</summary>
    private int HeaderOffset(DocNode table)
    {
        IReadOnlyList<TomlItem> items = _model.Syntax.Items;
        int k = ItemAt(EditLayout.LineStart(_text, table.Start));
        while (k > 0 && items[k - 1] is TomlCommentItem)
        {
            k--;
        }

        return items[k].Start;
    }

    /// <summary>The offset after the last key/value of a table's own section (before its first sub-table header), else after its header.</summary>
    private int AfterKeyValuesOffset(DocNode table)
    {
        IReadOnlyList<TomlItem> items = _model.Syntax.Items;
        (int start, int end) = SectionRange(table);
        for (int k = end - 1; k >= start; k--)
        {
            if (items[k] is TomlKeyValueItem)
            {
                return items[k].End;
            }
        }

        if (_model.ShapeOf(table) == TomlContainer.Root)
        {
            return TopOffset(end);
        }

        return items[start].End;
    }

    // ----- Table lines -----

    /// <summary>The formatting a new line copies, and the dotted prefix of a key in a dotted table.</summary>
    private readonly record struct LineFormat(string Indent, string Assign, string Prefix);

    /// <summary>
    /// Writes a key/value or comment line into a table. In a header or root table it goes before the key/value child at the index, after the
    /// key/value before it, else after the last key/value of the table's section, else at the top of the root or after the header. In a dotted
    /// table it goes after the table's last dotted line, with the same dotted prefix. In an implicit table it is a new section with the
    /// table's header, placed before the table's first deeper header and its comments.
    /// </summary>
    private void WriteTableLine(DocNode table, int index, Func<LineFormat, string> build)
    {
        switch (_model.ShapeOf(table))
        {
            case TomlContainer.Dotted:
            {
                if (_model.PiecesOf(table)[^1] is not TomlKeyValueItem last)
                {
                    throw new CfgeditException("a table inside an inline table cannot take a key yet");
                }

                LineFormat format = FormatOf(last) with { Prefix = DottedPrefix(table, last) };
                WriteLines(last.End, [build(format)], section: false);
                return;
            }

            case TomlContainer.Implicit:
            {
                int offset = ImplicitOffset(table);
                LineFormat format = FormatNear(0, _model.Syntax.Items.Count, ItemAt(offset));
                WriteLines(offset, [HeaderText(Names(table), false), build(format with { Prefix = string.Empty })], section: true);
                return;
            }

            default:
            {
                int offset = KeyValueOffset(table, index);
                (int start, int end) = SectionRange(table);
                WriteLines(offset, [build(FormatNear(start, end, ItemAt(offset)))], section: false);
                return;
            }
        }
    }

    /// <summary>The offset after a table's section and its subtree: after its last line, or for the root, after its last line of content.</summary>
    private int SubtreeOffset(DocNode table)
    {
        if (_model.ShapeOf(table) != TomlContainer.Root)
        {
            return EditLayout.AfterLineBreak(_text, table.End);
        }

        IReadOnlyList<TomlItem> items = _model.Syntax.Items;
        for (int k = items.Count - 1; k >= 0; k--)
        {
            if (items[k] is not TomlBlankItem)
            {
                return items[k].End;
            }
        }

        return _text.Length;
    }

    /// <summary>The offset of the line for a key/value or comment at an index in a root or header table.</summary>
    private int KeyValueOffset(DocNode table, int index)
    {
        IReadOnlyList<TomlItem> items = _model.Syntax.Items;
        IReadOnlyList<DocNode> children = table.Children;
        (int start, int end) = SectionRange(table);
        int next = index;
        while (next < children.Count && children[next].Kind == NodeKind.TomlComment)
        {
            next++;
        }

        // The key/value after the index, when the index starts its group (its own line or the first comment above it).
        if (next < children.Count && DirectKeyValue(children[next]) is { } at && ItemIndexOf(at) is var ki && start <= ki && ki < end)
        {
            int k = ki;
            while (k > 0 && items[k - 1] is TomlCommentItem)
            {
                k--;
            }

            if (next == index || items[k].Start == children[index].Start)
            {
                return items[k].Start;
            }
        }

        if (index > 0)
        {
            int m = index - 1;
            int c = m;
            while (c >= 0 && children[c].Kind == NodeKind.TomlComment)
            {
                c--;
            }

            if (c >= 0 && DirectKeyValue(children[c]) is { } previous && start <= ItemIndexOf(previous) && ItemIndexOf(previous) < end
                && (c == m || (c == m - 1 && previous.Comment is { } trailing && trailing.Start == children[m].Start)))
            {
                return previous.End;
            }
        }

        for (int k = end - 1; k >= start; k--)
        {
            if (items[k] is TomlKeyValueItem)
            {
                return items[k].End;
            }
        }

        if (_model.ShapeOf(table) == TomlContainer.Root)
        {
            return TopOffset(end);
        }

        return items[start].End;
    }

    /// <summary>The offset at the top of the root with no key/values: after the comment lines at the top that are not attached to the first header.</summary>
    private int TopOffset(int end)
    {
        IReadOnlyList<TomlItem> items = _model.Syntax.Items;
        int k = end;
        if (end < items.Count)
        {
            while (k > 0 && items[k - 1] is TomlCommentItem)
            {
                k--;
            }
        }

        for (int j = k - 1; j >= 0; j--)
        {
            if (items[j] is TomlCommentItem)
            {
                return items[j].End;
            }
        }

        return 0;
    }

    /// <summary>The offset of an implicit table's new section: before the first deeper header of the table, and the comments above it.</summary>
    private int ImplicitOffset(DocNode table)
    {
        IReadOnlyList<TomlItem> items = _model.Syntax.Items;
        int k = ItemIndexOf(_model.PiecesOf(table)[0]);
        while (k > 0 && items[k - 1] is TomlCommentItem)
        {
            k--;
        }

        return items[k].Start;
    }

    /// <summary>The item index range of a table's own section: the root's items before its first header, or a header table's header up to the next header.</summary>
    private (int Start, int End) SectionRange(DocNode table)
    {
        IReadOnlyList<TomlItem> items = _model.Syntax.Items;
        bool root = _model.ShapeOf(table) == TomlContainer.Root;
        int start = root ? 0 : ItemIndexOf(_model.OwnHeaderOf(table) ?? throw Internal("a header table has no header"));
        int end = items.Count;
        for (int k = root ? 0 : start + 1; k < items.Count; k++)
        {
            if (items[k] is TomlHeaderItem)
            {
                end = k;
                break;
            }
        }

        return (start, end);
    }

    /// <summary>The formatting of the nearest key/value in a section (before the offset item index first), else in the file, else the defaults.</summary>
    private LineFormat FormatNear(int start, int end, int at)
    {
        IReadOnlyList<TomlItem> items = _model.Syntax.Items;
        TomlKeyValueItem? before = null;
        TomlKeyValueItem? after = null;
        for (int k = start; k < end && k < items.Count; k++)
        {
            if (items[k] is TomlKeyValueItem keyValue)
            {
                if (k < at)
                {
                    before = keyValue;
                }
                else
                {
                    after ??= keyValue;
                }
            }
        }

        TomlKeyValueItem? chosen = before ?? after ?? items.OfType<TomlKeyValueItem>().FirstOrDefault();
        return chosen is null ? new LineFormat(string.Empty, " = ", string.Empty) : FormatOf(chosen);
    }

    private LineFormat FormatOf(TomlKeyValueItem keyValue)
    {
        return new LineFormat(Text(keyValue.Indent), Text(keyValue.Assignment), string.Empty);
    }

    /// <summary>
    /// The dotted prefix of a key in a dotted table: the key of the table's last dotted line, spelled as it is up to the separator after the
    /// table's own name. The table's name is relative to the section it is in, so the prefix is counted from that section's table.
    /// </summary>
    private string DottedPrefix(DocNode table, TomlKeyValueItem last)
    {
        DocNode owner = table.Parent ?? throw Internal("a dotted table has no parent");
        while (_model.ShapeOf(owner) is TomlContainer.Dotted or TomlContainer.Implicit)
        {
            owner = owner.Parent ?? throw Internal("a dotted table has no section");
        }

        if (_model.ShapeOf(owner) == TomlContainer.Inline)
        {
            throw new CfgeditException("a table inside an inline table cannot take a key yet");
        }

        int relative = Depth(table) - Depth(owner);
        return _text[last.Key.Start..last.Key.Separators[relative - 1].End];
    }

    // ----- Duplicating -----

    private void PlanDuplicate(DuplicateNode op)
    {
        DocNode node = Node(op.Node);
        DocNode parent = Parent(node);
        if (parent.Kind == NodeKind.Document)
        {
            throw new CfgeditException("the root table cannot be duplicated");
        }

        if (node.Kind == NodeKind.TomlComment)
        {
            DuplicateComment(parent, node);
            return;
        }

        if (IsInlineMember(parent))
        {
            DuplicateInlineMember(node);
            return;
        }

        switch (_model.ShapeOf(parent))
        {
            case TomlContainer.ArrayOfTables:
                CopyRegion(node, rename: false);
                return;
            case TomlContainer.Array:
                DuplicateItem(parent, node);
                return;
        }

        if (node.Kind == NodeKind.TomlArray && _model.ShapeOf(node) == TomlContainer.ArrayOfTables)
        {
            throw new CfgeditException("an array of tables cannot be duplicated; duplicate one of its items");
        }

        if (node.Kind == NodeKind.TomlTable)
        {
            switch (_model.ShapeOf(node))
            {
                case TomlContainer.Header or TomlContainer.Implicit:
                    CopyRegion(node, rename: true);
                    return;
                case TomlContainer.Dotted:
                    throw new CfgeditException("a table defined by dotted keys cannot be duplicated");
            }
        }

        DuplicateKeyValue(parent, node);
    }

    /// <summary>A comment line is copied as it is. A comment inside an array, or after a value on its line, is not copied alone.</summary>
    private void DuplicateComment(DocNode parent, DocNode comment)
    {
        if (parent.Kind == NodeKind.TomlArray)
        {
            throw new CfgeditException("a comment inside an array cannot be duplicated");
        }

        if (!EditLayout.StartsLine(_text, comment.Start))
        {
            throw new CfgeditException("a trailing comment is duplicated with its entry");
        }

        int start = EditLayout.LineStart(_text, comment.Start);
        int end = EditLayout.AfterLineBreak(_text, comment.End);
        WriteLines(end, [_text[start..end]], section: false, copyText: true);
    }

    /// <summary>
    /// A key/value with its attached and trailing comments, copied right after its group. The copy's last key segment is renamed
    /// (<c>name</c> gives <c>name2</c>, unique among the siblings).
    /// </summary>
    private void DuplicateKeyValue(DocNode parent, DocNode node)
    {
        IReadOnlyList<DocNode> children = parent.Children;
        (int first, int last) = EditLayout.Group(parent, IndexOf(parent, node), _text);
        int start = EditLayout.LineStart(_text, children[first].Start);
        int end = EditLayout.AfterLineBreak(_text, children[last].End);
        TomlKeyValueItem keyValue = DirectKeyValue(node) ?? throw Internal("a key/value has no key/value syntax");
        TomlKeyPart part = keyValue.Key.Parts[^1];
        string copy = Rename(_text[start..end], start, [(part.Start, part.End, TomlEncoder.EncodeKey(FreshName(parent, NameOf(node))))]);
        WriteLines(end, [copy], section: false, copyText: true);
    }

    /// <summary>
    /// A table with its header sections, copied right after its last section, when the table is one contiguous run of text. The copy's
    /// header (and those of its subtables) gets the new name at the table's depth, when <paramref name="rename"/> is set: an array of tables
    /// item keeps its name.
    /// </summary>
    private void CopyRegion(DocNode table, bool rename)
    {
        int start = EditLayout.LineStart(_text, table.Start);
        int end = EditLayout.AfterLineBreak(_text, table.End);
        RequireContiguous(table, start, end);
        string copy = _text[start..end];
        if (rename)
        {
            string fresh = FreshName(table.Parent ?? throw Internal("a table has no parent"), NameOf(table));
            int depth = Depth(table);
            var renames = new List<(int Start, int End, string Text)>();
            foreach (TomlItem item in _model.Syntax.Items)
            {
                if (item is TomlHeaderItem header && item.Start >= start && item.End <= end)
                {
                    TomlKeyPart part = header.Key.Parts[depth - 1];
                    renames.Add((part.Start, part.End, TomlEncoder.EncodeKey(fresh)));
                }
            }

            copy = Rename(copy, start, renames);
        }

        WriteLines(end, [copy], section: true, copyText: true);
    }

    /// <summary>Refuses a region that holds text of another node: a table whose sections are spread over the file cannot be copied as one run.</summary>
    private void RequireContiguous(DocNode table, int start, int end)
    {
        foreach (TomlItem item in _model.Syntax.Items)
        {
            if (item.End <= start || item.Start >= end || item is TomlBlankItem)
            {
                continue;
            }

            DocNode? owner = item is TomlCommentItem comment ? OwnerOf(comment.Comment!) : _leaves.GetValueOrDefault((TomlNode)item);
            if (owner is null || !InSubtree(owner, table))
            {
                throw new CfgeditException("this table is spread over the file and cannot be duplicated");
            }
        }
    }

    private DocNode? OwnerOf(TomlComment comment)
    {
        return _owners.TryGetValue(comment, out List<DocNode>? nodes) ? nodes[0] : null;
    }

    /// <summary>An item of a static array, copied right after itself: as whole lines when it has its own lines, else after its value.</summary>
    private void DuplicateItem(DocNode array, DocNode node)
    {
        IReadOnlyList<DocNode> children = array.Children;
        int index = IndexOf(array, node);
        (int first, int last) = EditLayout.Group(array, index, _text);
        TomlArrayItem item = ArrayItemOf(node);
        if (TomlLayout.IsLineCopy(_model, array, first, last, _text))
        {
            int start = EditLayout.LineStart(_text, children[first].Start);
            int end = EditLayout.AfterLineBreak(_text, TomlLayout.ItemEnd(_model, array, last));
            if (item.Comma is null)
            {
                // The original becomes a middle item, so it takes the comma that the copy does not have.
                Insert(item.Value.End, ",");
            }

            WriteLines(end, [_text[start..end]], section: false, copyText: true);
            return;
        }

        string copy = _text[item.Value.Start..item.Value.End];
        string spacing = CommaSpacing(ArrayCommas(array));
        if (item.Comma is { } comma)
        {
            Insert(comma.End, spacing + copy + ",");
        }
        else
        {
            Insert(item.Value.End, "," + spacing + copy);
        }
    }

    /// <summary>The commas of an array's items, in order.</summary>
    private IEnumerable<TomlSpan> ArrayCommas(DocNode array)
    {
        return ((TomlArray)(ValueSyntax(array) ?? throw Internal("an array has no syntax"))).Entries
            .OfType<TomlArrayItem>()
            .Where(item => item.Comma is not null)
            .Select(item => item.Comma!.Value);
    }

    /// <summary>
    /// A member of an inline table, or a value under a dotted key inside one, copied right after its own member, with its last key segment
    /// renamed. A dotted table inside an inline table has no text of its own, so it cannot be copied.
    /// </summary>
    private void DuplicateInlineMember(DocNode node)
    {
        if (node.Kind == NodeKind.TomlTable && _model.ShapeOf(node) == TomlContainer.Dotted)
        {
            throw new CfgeditException("a table defined by dotted keys cannot be duplicated");
        }

        var member = (TomlInlineMember)(Carrier(node) ?? throw Internal("an inline member has no syntax"));
        TomlKeyPart part = member.Key.Parts[^1];
        string copy = Rename(_text[member.Start..member.Value.End], member.Start, [(part.Start, part.End, TomlEncoder.EncodeKey(FreshName(node.Parent!, NameOf(node))))]);
        string spacing = CommaSpacing(InlineCommas(InlineAncestor(node)));
        if (member.Comma is { } comma)
        {
            Insert(comma.End, spacing + copy + ",");
        }
        else
        {
            Insert(member.Value.End, "," + spacing + copy);
        }
    }

    /// <summary>The inline table that a member's text is in: the nearest ancestor that is an inline table.</summary>
    private DocNode InlineAncestor(DocNode node)
    {
        for (DocNode? n = node.Parent; n is not null; n = n.Parent)
        {
            if (n.Kind == NodeKind.TomlTable && _model.ShapeOf(n) == TomlContainer.Inline)
            {
                return n;
            }
        }

        throw Internal("an inline member is not in an inline table");
    }

    /// <summary>
    /// True for a container whose children are members of an inline table: the inline table itself, or a dotted table inside one (its
    /// members are the inline table's members).
    /// </summary>
    private bool IsInlineMember(DocNode parent)
    {
        TomlContainer shape = _model.ShapeOf(parent);
        return shape == TomlContainer.Inline || (shape == TomlContainer.Dotted && _model.PiecesOf(parent)[0] is TomlInlineMember);
    }

    /// <summary>The commas of an inline table's members, in order.</summary>
    private IEnumerable<TomlSpan> InlineCommas(DocNode table)
    {
        return ((TomlInlineTable)(ValueSyntax(table) ?? throw Internal("an inline table has no syntax"))).Entries
            .OfType<TomlInlineMember>()
            .Where(member => member.Comma is not null)
            .Select(member => member.Comma!.Value);
    }

    // ----- Copy and layout helpers -----

    /// <summary>
    /// Writes text at an offset of a line start. A section takes a blank line before it when the file separates its sections with blank lines
    /// and the line before is not blank, and one after it when the line after is not blank. At the end of a text with no final line break,
    /// the document's line break comes first, and the text keeps its missing final break.
    /// </summary>
    private void WriteLines(int offset, IReadOnlyList<string> lines, bool section, bool copyText = false)
    {
        Insert(offset, LinesBody(offset, lines, section, copyText));
    }

    /// <summary>The text that <see cref="WriteLines"/> writes at an offset: the lines, with the blank lines a section needs around it.</summary>
    private string LinesBody(int offset, IReadOnlyList<string> lines, bool section, bool copyText)
    {
        var block = new StringBuilder();
        bool separated = section && SectionsSeparated();
        if (separated && offset > 0 && !PrecededByBlankLine(offset))
        {
            block.Append(_newline);
        }

        if (copyText)
        {
            block.Append(lines[0]);
        }
        else
        {
            foreach (string line in lines)
            {
                block.Append(line).Append(_newline);
            }
        }

        if (separated && offset < _text.Length && !BlankLineAt(offset))
        {
            block.Append(_newline);
        }

        string body = block.ToString();
        bool atEnd = offset == _text.Length;
        bool endsWithBreak = _text.Length == 0 || _text[^1] is '\r' or '\n';
        if (atEnd && !endsWithBreak)
        {
            body = _newline + (body.EndsWith(_newline, StringComparison.Ordinal) ? body[..^_newline.Length] : body);
        }

        return body;
    }

    private bool SectionsSeparated()
    {
        IReadOnlyList<TomlItem> items = _model.Syntax.Items;
        for (int k = 1; k < items.Count; k++)
        {
            if (items[k - 1] is TomlBlankItem && items[k] is TomlHeaderItem)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the line before an offset (a line start) is blank.</summary>
    private bool PrecededByBlankLine(int offset)
    {
        int contentEnd = offset - 1;
        if (contentEnd < 0 || _text[contentEnd] is not ('\r' or '\n'))
        {
            return false;
        }

        if (_text[contentEnd] == '\n' && contentEnd > 0 && _text[contentEnd - 1] == '\r')
        {
            contentEnd--;
        }

        int lineStart = EditLayout.LineStart(_text, contentEnd);
        for (int i = lineStart; i < contentEnd; i++)
        {
            if (_text[i] is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the line starting at an offset is blank, or the offset is the end of the text.</summary>
    private bool BlankLineAt(int offset)
    {
        int i = offset;
        while (i < _text.Length && _text[i] is ' ' or '\t')
        {
            i++;
        }

        return i >= _text.Length || _text[i] is '\r' or '\n';
    }

    /// <summary>Replaces spans of a copy. The spans are offsets into the source, and <paramref name="start"/> is where the copy starts in it.</summary>
    private static string Rename(string copy, int start, IEnumerable<(int Start, int End, string Text)> renames)
    {
        var builder = new StringBuilder(copy);
        foreach ((int from, int to, string text) in renames.OrderByDescending(r => r.Start))
        {
            builder.Remove(from - start, to - from).Insert(from - start, text);
        }

        return builder.ToString();
    }

    /// <summary>The name of a copy of a keyed node: <c>name2</c>, then the next number up while taken (<see cref="EditLayout.CopyName"/>), unique among the parent's members.</summary>
    private static string FreshName(DocNode parent, string name)
    {
        var taken = parent.Children
            .Where(child => child.Path.Steps[^1] is MemberStep)
            .Select(child => ((MemberStep)child.Path.Steps[^1]).Key)
            .ToHashSet(StringComparer.Ordinal);
        return EditLayout.CopyName(name, taken.Contains);
    }

    /// <summary>The spacing after a comma, copied from the first comma that has spaces after it, else one space.</summary>
    private string CommaSpacing(IEnumerable<TomlSpan> commas)
    {
        foreach (TomlSpan comma in commas)
        {
            int j = comma.End;
            while (j < _text.Length && _text[j] is ' ' or '\t')
            {
                j++;
            }

            if (j < _text.Length && _text[j] is not ('\r' or '\n' or '#' or ']' or '}'))
            {
                return _text[comma.End..j];
            }
        }

        return " ";
    }

    private void CheckKeyFree(DocNode parent, string key)
    {
        foreach (DocNode child in parent.Children)
        {
            if (child.Path.Steps[^1] is MemberStep step && step.Key == key)
            {
                throw new CfgeditException($"a key named '{key}' already exists here");
            }
        }
    }

    private void CheckCommentText(string text)
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

    /// <summary>The token of a new value: a scalar of its kind (the default when none is given), an empty inline table or array.</summary>
    private string ValueToken(TomlNewKind kind, string? value)
    {
        switch (kind)
        {
            case TomlNewKind.Table:
                return "{}";
            case TomlNewKind.Array:
                return "[]";
            case TomlNewKind.String:
                return TomlEncoder.EncodeString(value ?? string.Empty, _newline);
            case TomlNewKind.Integer:
                RequireInteger(value ?? "0");
                return value ?? "0";
            case TomlNewKind.Float:
                RequireFloat(value ?? "0.0");
                return value ?? "0.0";
            case TomlNewKind.Boolean:
                RequireBoolean(value ?? "false");
                return value ?? "false";
            case TomlNewKind.DateTime:
                RequireDateTime(value ?? throw new CfgeditException("a date-time needs a value"));
                return value!;
            default:
                throw new CfgeditException("this type is not supported for TOML");
        }
    }

    /// <summary>The header of a table or of an array of tables, from its names: <c>[a.b]</c> or <c>[[a.b]]</c>, each name encoded as a key.</summary>
    private static string HeaderText(IEnumerable<string> names, bool arrayOfTables)
    {
        string key = string.Join(".", names.Select(TomlEncoder.EncodeKey));
        return arrayOfTables ? "[[" + key + "]]" : "[" + key + "]";
    }

    private static IEnumerable<string> Names(DocNode node)
    {
        return node.Path.Steps.OfType<MemberStep>().Select(step => step.Key);
    }

    private static string NameOf(DocNode node)
    {
        return ((MemberStep)node.Path.Steps[^1]).Key;
    }

    private static int Depth(DocNode node)
    {
        return node.Path.Steps.Count(step => step is MemberStep);
    }

    private string Text(TomlSpan span)
    {
        return _text[span.Start..span.End];
    }

    /// <summary>The first item index at or after an offset (the item the offset is before).</summary>
    private int ItemAt(int offset)
    {
        IReadOnlyList<TomlItem> items = _model.Syntax.Items;
        int k = 0;
        while (k < items.Count && items[k].Start < offset)
        {
            k++;
        }

        return k;
    }

    private int ItemIndexOf(TomlNode item)
    {
        IReadOnlyList<TomlItem> items = _model.Syntax.Items;
        for (int k = 0; k < items.Count; k++)
        {
            if (ReferenceEquals(items[k], item))
            {
                return k;
            }
        }

        throw Internal("a piece is not among the lines");
    }

    /// <summary>The key/value item that a child is, when the child is a key/value of its table: a scalar, a static array or an inline table.</summary>
    private TomlKeyValueItem? DirectKeyValue(DocNode child)
    {
        bool direct = child.Kind == NodeKind.TomlScalar
            || (child.Kind == NodeKind.TomlArray && _model.ShapeOf(child) == TomlContainer.Array)
            || (child.Kind == NodeKind.TomlTable && _model.ShapeOf(child) == TomlContainer.Inline);
        return direct ? Carrier(child) as TomlKeyValueItem : null;
    }

    /// <summary>The syntax that carries a node's value: a key/value, an inline member or an array item.</summary>
    private TomlNode? Carrier(DocNode node)
    {
        IReadOnlyList<TomlNode> pieces = _model.PiecesOf(node);
        if (pieces.Count == 0)
        {
            return null;
        }

        return pieces[0] is TomlInlineTable inline ? _carriers.GetValueOrDefault(inline) : pieces[0];
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
                    _carriers[member.Value] = member;
                    IndexValue(member.Value);
                }

                break;
            case TomlArray array:
                foreach (TomlArrayItem item in array.Entries.OfType<TomlArrayItem>())
                {
                    _arrayItems[item.Value.Start] = item;
                    _carriers[item.Value] = item;
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
        return _model.ValueSyntaxOf(node);
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
