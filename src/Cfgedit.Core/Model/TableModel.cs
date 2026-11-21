namespace Cfgedit.Core.Model;

/// <summary>The kinds of table column.</summary>
public enum TableColumnKind
{
    /// <summary>A JSON member key of the row objects.</summary>
    Key,

    /// <summary>The value of each item of an array of scalars.</summary>
    Value,

    /// <summary>An XML attribute of the rows.</summary>
    Attribute,

    /// <summary>An XML child element that is text-only or empty, present at most once in every row.</summary>
    ChildElement,

    /// <summary>The text of a text-only row.</summary>
    Text,
}

/// <summary>
/// A column of a table. <see cref="Name"/> is the JSON key, the attribute or child element name, "value" for an array of
/// scalars, or "text" for the text column.
/// </summary>
public sealed record TableColumn(TableColumnKind Kind, string Name);

/// <summary>
/// One cell of a table. <see cref="Node"/> is the member, the attribute's row element, the child element, or the row element for
/// the text column; null when a JSON member or a child element is absent. <see cref="Part"/> is the value, attribute value or text
/// part, or null for a missing or nested cell. <see cref="Display"/> is the logical value, <c>{n}</c> or <c>[n]</c> for a nested
/// JSON container, <c>&lt;...&gt;</c> for a nested XML element, and empty for a missing cell.
/// </summary>
public sealed record TableCell(DocNode? Node, DocPart? Part, bool IsMissing, bool IsNested, string Display);

/// <summary>
/// A table view of an array of objects, an array of scalars, or an XML element's same-named child elements. The model is built
/// from a <see cref="ConfigDocument"/> and never changes it: each edit method returns the <see cref="EditOp"/> that would make the
/// change, and the caller applies it.
/// </summary>
public sealed class TableModel
{
    private static readonly string Ellipsis = ((char)0x2026).ToString();

    private readonly DocNode _container;
    private readonly TableShape _shape;
    private readonly IReadOnlyList<DocNode> _rowNodes;
    private readonly IReadOnlyList<NodePath> _rowPaths;

    private TableModel(
        ConfigDocument document, DocNode container, TableShape shape, IReadOnlyList<DocNode> rows, IReadOnlyList<TableColumn> columns)
    {
        Document = document;
        _container = container;
        _shape = shape;
        _rowNodes = rows;
        _rowPaths = rows.Select(row => row.Path).ToList();
        Columns = columns;
    }

    private enum TableShape
    {
        JsonObjects,
        JsonScalars,
        XmlElements,
    }

    /// <summary>The document the table was built from.</summary>
    public ConfigDocument Document { get; }

    /// <summary>The path of the array or element whose items or children are the rows.</summary>
    public NodePath Container => _container.Path;

    /// <summary>The paths of the rows, in order.</summary>
    public IReadOnlyList<NodePath> Rows => _rowPaths;

    /// <summary>The columns, in display order.</summary>
    public IReadOnlyList<TableColumn> Columns { get; }

    /// <summary>
    /// Builds the table for a node, or returns null when the node is not a table. The node may be the container or one of its rows.
    /// A JSON array is a table when its items (ignoring comments) are all objects, or all scalars, with at least one item. An XML
    /// element is a table when it has at least two child elements of the same name. For a row, the rows are its siblings of its name.
    /// For a container, the name is the one with the most elements, ties going to the name seen first.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="node">The path of the container or of one of its rows.</param>
    /// <returns>The table, or null when the node is not a table or there is no node at the path.</returns>
    public static TableModel? TryCreate(ConfigDocument document, NodePath node)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(node);
        DocNode? target = document.Resolve(node);
        if (target is null)
        {
            return null;
        }

        if (target.Kind == NodeKind.JsonArray)
        {
            return JsonTable(document, target);
        }

        if ((target.Kind == NodeKind.JsonObject || target.Kind == NodeKind.JsonScalar) && target.Parent?.Kind == NodeKind.JsonArray)
        {
            return JsonTable(document, target.Parent);
        }

        if (target.Kind == NodeKind.XmlElement)
        {
            return XmlTable(document, target);
        }

        return null;
    }

    /// <summary>Returns the cell at a row and a column. Indices are zero-based.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The row or the column is outside the table.</exception>
    public TableCell Cell(int row, int column)
    {
        if ((uint)row >= (uint)_rowNodes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        if ((uint)column >= (uint)Columns.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        DocNode rowNode = _rowNodes[row];
        TableColumn col = Columns[column];
        switch (_shape)
        {
            case TableShape.JsonScalars:
            {
                DocPart value = ValuePart(rowNode);
                return new TableCell(rowNode, value, false, false, value.Value);
            }
            case TableShape.JsonObjects:
                return JsonCell(rowNode, col);
            default:
                return XmlCell(rowNode, col);
        }
    }

    /// <summary>
    /// The edit that sets a cell. An existing part is set. A missing JSON member is inserted into the row object, a missing
    /// attribute is inserted into the row element, and a missing child element is inserted among the row's children in column order.
    /// A missing JSON cell takes the column's most common scalar kind (String on a tie or when there is none); the value text is
    /// ignored for True, False and Null. The text column of a row that is <c>&lt;a/&gt;</c> gets its text.
    /// </summary>
    /// <param name="row">The row index.</param>
    /// <param name="column">The column index.</param>
    /// <param name="value">The new value.</param>
    /// <exception cref="CfgeditException">The request is invalid: the cell is nested, or the row cannot take text.</exception>
    public EditOp EditCell(int row, int column, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckRow(row);
        CheckColumn(column);
        TableColumn col = Columns[column];
        DocNode rowNode = _rowNodes[row];
        if (col.Kind == TableColumnKind.Text && HasElementChildren(rowNode))
        {
            throw new CfgeditException("this row has child elements; edit its text in the tree");
        }

        TableCell cell = Cell(row, column);
        if (cell.IsNested)
        {
            throw new CfgeditException("this cell holds a nested value and cannot be edited in the table; edit it in the tree");
        }

        if (cell.Part is { } part)
        {
            return new SetPart(cell.Node!.Path, part.Kind, part.AttributeIndex, value);
        }

        switch (col.Kind)
        {
            case TableColumnKind.Key:
                return InsertMember(row, column, value);
            case TableColumnKind.Attribute:
                return InsertAttribute(rowNode, column, value);
            case TableColumnKind.ChildElement:
                return cell.Node is { } child
                    ? new SetPart(child.Path, PartKind.Text, -1, value)
                    : InsertChild(rowNode, column, value);
            case TableColumnKind.Text:
                if (rowNode.Children.Count == 0 && rowNode.EndParts.Count == 0)
                {
                    return new SetPart(rowNode.Path, PartKind.Text, -1, value);
                }

                throw new CfgeditException("this row holds content other than text; edit it in the tree");
            default:
                throw new InvalidOperationException("A scalar array's cells always have a value part.");
        }
    }

    /// <summary>
    /// The edit that clears a cell: deletes its member, attribute or child element, or sets the text of the row to empty. A
    /// missing cell needs no change and gives null.
    /// </summary>
    /// <param name="row">The row index.</param>
    /// <param name="column">The column index.</param>
    /// <returns>The edit, or null when the cell is missing.</returns>
    /// <exception cref="CfgeditException">The request is invalid: the cell is nested, or it is a value in an array of scalars.</exception>
    public EditOp? ClearCell(int row, int column)
    {
        CheckRow(row);
        CheckColumn(column);
        if (_shape == TableShape.JsonScalars)
        {
            throw new CfgeditException("an array value cannot be cleared; delete its row instead");
        }

        TableColumn col = Columns[column];
        DocNode rowNode = _rowNodes[row];
        if (col.Kind == TableColumnKind.Text && HasElementChildren(rowNode))
        {
            throw new CfgeditException("this row has child elements; edit its text in the tree");
        }

        TableCell cell = Cell(row, column);
        if (cell.IsNested)
        {
            throw new CfgeditException("this cell holds a nested value and cannot be cleared in the table; edit it in the tree");
        }

        if (cell.IsMissing)
        {
            return null;
        }

        switch (col.Kind)
        {
            case TableColumnKind.Attribute:
                return new DeleteAttribute(rowNode.Path, cell.Part!.AttributeIndex);
            case TableColumnKind.Text:
                return new SetPart(rowNode.Path, PartKind.Text, -1, string.Empty);
            default:
                return new DeleteNode(cell.Node!.Path);
        }
    }

    /// <summary>
    /// The edit that adds a row after a row, or before the first row when <paramref name="afterRow"/> is -1. The new row has the
    /// shape of the row at <c>max(afterRow, 0)</c>, with empty values. The steps are a composite: one undo step.
    /// </summary>
    /// <param name="afterRow">The row to add after, or -1 for the start.</param>
    /// <exception cref="CfgeditException">The row index is outside the table.</exception>
    public CompositeEdit AddRow(int afterRow)
    {
        if (afterRow < -1 || afterRow >= _rowNodes.Count)
        {
            throw new CfgeditException("there is no row " + afterRow + " to add after");
        }

        DocNode source = _rowNodes[Math.Max(afterRow, 0)];
        int index = afterRow < 0
            ? Document.InsertionIndexBefore(_rowPaths[0])
            : Document.InsertionIndexAfter(_rowPaths[afterRow]);
        var steps = new List<EditOp>();
        switch (_shape)
        {
            case TableShape.JsonScalars:
            {
                (JsonValueKind kind, string? text) = EmptyValue(source.JsonKind!.Value);
                steps.Add(new InsertNode(Container, index, new NewJsonValue(null, kind, text)));
                break;
            }
            case TableShape.JsonObjects:
            {
                steps.Add(new InsertNode(Container, index, new NewJsonValue(null, JsonValueKind.Object, null)));
                NodePath row = Container.Append(new ItemStep(CountBefore(index, child => child.Path.Steps[^1] is ItemStep)));
                var seen = new HashSet<string>(StringComparer.Ordinal);
                int position = 0;
                foreach (DocNode member in source.Children)
                {
                    if (member.Path.Steps[^1] is not MemberStep step || !seen.Add(step.Key))
                    {
                        continue;
                    }

                    (JsonValueKind memberKind, string? memberText) = EmptyValue(member.JsonKind!.Value);
                    steps.Add(new InsertNode(row, position++, new NewJsonValue(step.Key, memberKind, memberText)));
                }

                break;
            }
            default:
            {
                string name = ElementName(source);
                List<(string Name, string Value)> attributes = source.Parts
                    .Where(part => part.Kind == PartKind.AttributeName)
                    .Select(part => (Name: part.Value, Value: string.Empty))
                    .ToList();
                string? text = TextPart(source) is not null ? string.Empty : null;
                steps.Add(new InsertNode(Container, index, new NewXmlElement(name, attributes, text)));
                NodePath row = Container.Append(new ElementStep(
                    name, CountBefore(index, child => child.Kind == NodeKind.XmlElement && ElementName(child) == name)));
                int position = 0;
                foreach (DocNode child in source.Children)
                {
                    if (child.Kind == NodeKind.XmlElement && ColumnIndex(TableColumnKind.ChildElement, ElementName(child)) >= 0)
                    {
                        steps.Add(new InsertNode(row, position++, new NewXmlElement(ElementName(child), [], string.Empty)));
                    }
                }

                break;
            }
        }

        return new CompositeEdit(steps);
    }

    /// <summary>The edit that deletes a row with its attached comments.</summary>
    /// <exception cref="CfgeditException">The row index is outside the table.</exception>
    public EditOp DeleteRow(int row)
    {
        CheckRow(row);
        return new DeleteNode(_rowPaths[row]);
    }

    /// <summary>
    /// The edit that moves a row so that it ends at index <paramref name="newRow"/> among the rows. Other siblings and comments keep
    /// their places relative to the rows: the row goes just before the row that is at <paramref name="newRow"/> after the removal, or
    /// after the last row.
    /// </summary>
    /// <exception cref="CfgeditException">A row index is outside the table.</exception>
    public EditOp MoveRow(int row, int newRow)
    {
        CheckRow(row);
        CheckRow(newRow);
        DocNode moved = _rowNodes[row];
        int groupStart = Document.InsertionIndexBefore(moved.Path);
        int groupEnd = Document.InsertionIndexAfter(moved.Path);
        int target;
        if (newRow < row)
        {
            target = Document.InsertionIndexBefore(_rowPaths[newRow]);
        }
        else if (newRow + 1 < _rowPaths.Count)
        {
            target = Document.InsertionIndexBefore(_rowPaths[newRow + 1]);
        }
        else
        {
            target = Document.InsertionIndexAfter(_rowPaths[^1]);
        }

        int newIndex = target <= groupStart ? target : target - (groupEnd - groupStart);
        return new MoveNode(moved.Path, newIndex);
    }

    // ----- Building -----

    private static TableModel? JsonTable(ConfigDocument document, DocNode array)
    {
        List<DocNode> items = array.Children.Where(child => child.Kind != NodeKind.JsonComment).ToList();
        if (items.Count == 0)
        {
            return null;
        }

        if (items.All(item => item.JsonKind == JsonValueKind.Object))
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DocNode item in items)
            {
                foreach (DocNode member in item.Children)
                {
                    if (member.Path.Steps[^1] is MemberStep step && seen.Add(step.Key))
                    {
                        keys.Add(step.Key);
                    }
                }
            }

            return new TableModel(
                document, array, TableShape.JsonObjects, items, keys.Select(key => new TableColumn(TableColumnKind.Key, key)).ToList());
        }

        if (items.All(item => IsScalar(item.JsonKind)))
        {
            return new TableModel(
                document, array, TableShape.JsonScalars, items, [new TableColumn(TableColumnKind.Value, "value")]);
        }

        return null;
    }

    private static TableModel? XmlTable(ConfigDocument document, DocNode element)
    {
        if (element.Parent is { Kind: NodeKind.XmlElement } parent)
        {
            string name = ElementName(element);
            List<DocNode> siblings = ElementChildren(parent).Where(child => ElementName(child) == name).ToList();
            if (siblings.Count >= 2)
            {
                return XmlModel(document, parent, siblings, name);
            }
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (DocNode child in ElementChildren(element))
        {
            string name = ElementName(child);
            if (!counts.ContainsKey(name))
            {
                counts[name] = 0;
                order.Add(name);
            }

            counts[name]++;
        }

        string? chosen = null;
        int best = 1;
        foreach (string name in order)
        {
            if (counts[name] > best)
            {
                chosen = name;
                best = counts[name];
            }
        }

        if (chosen is null)
        {
            return null;
        }

        List<DocNode> rows = ElementChildren(element).Where(child => ElementName(child) == chosen).ToList();
        return XmlModel(document, element, rows, chosen);
    }

    private static TableModel XmlModel(ConfigDocument document, DocNode container, List<DocNode> rows, string name)
    {
        var columns = new List<TableColumn>();

        var attributeNames = new List<string>();
        var seenAttributes = new HashSet<string>(StringComparer.Ordinal);
        foreach (DocNode row in rows)
        {
            foreach (DocPart part in row.Parts)
            {
                if (part.Kind == PartKind.AttributeName && seenAttributes.Add(part.Value))
                {
                    attributeNames.Add(part.Value);
                }
            }
        }

        columns.AddRange(attributeNames.Select(attribute => new TableColumn(TableColumnKind.Attribute, attribute)));

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (DocNode row in rows)
        {
            var perRow = new HashSet<string>(StringComparer.Ordinal);
            foreach (DocNode child in ElementChildren(row))
            {
                string childName = ElementName(child);
                if (!IsSimple(child) || !perRow.Add(childName))
                {
                    excluded.Add(childName);
                }
            }
        }

        var childNames = new List<string>();
        var seenChildren = new HashSet<string>(StringComparer.Ordinal);
        foreach (DocNode row in rows)
        {
            foreach (DocNode child in ElementChildren(row))
            {
                string childName = ElementName(child);
                if (IsSimple(child) && !excluded.Contains(childName) && seenChildren.Add(childName))
                {
                    childNames.Add(childName);
                }
            }
        }

        columns.AddRange(childNames.Select(child => new TableColumn(TableColumnKind.ChildElement, child)));

        if (rows.Any(row => TextPart(row) is { Value.Length: > 0 }))
        {
            columns.Add(new TableColumn(TableColumnKind.Text, "text"));
        }

        return new TableModel(document, container, TableShape.XmlElements, rows, columns);
    }

    // ----- Cells -----

    private DocNode? JsonMember(DocNode obj, string key)
    {
        return obj.Children.FirstOrDefault(child => child.Path.Steps[^1] is MemberStep step && step.Key == key);
    }

    private TableCell JsonCell(DocNode rowNode, TableColumn col)
    {
        DocNode? member = JsonMember(rowNode, col.Name);
        if (member is null)
        {
            return new TableCell(null, null, true, false, string.Empty);
        }

        if (member.JsonKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            string count = member.Parts.First(part => part.Kind == PartKind.Summary).Value;
            string display = member.JsonKind == JsonValueKind.Object ? "{" + count + "}" : "[" + count + "]";
            return new TableCell(member, null, false, true, display);
        }

        DocPart value = ValuePart(member);
        return new TableCell(member, value, false, false, value.Value);
    }

    private TableCell XmlCell(DocNode rowNode, TableColumn col)
    {
        switch (col.Kind)
        {
            case TableColumnKind.Attribute:
            {
                DocPart? value = AttributeValue(rowNode, col.Name);
                return value is null
                    ? new TableCell(rowNode, null, true, false, string.Empty)
                    : new TableCell(rowNode, value, false, false, value.Value);
            }
            case TableColumnKind.ChildElement:
            {
                DocNode? child = ElementChildren(rowNode).FirstOrDefault(candidate => ElementName(candidate) == col.Name);
                if (child is null)
                {
                    return new TableCell(null, null, true, false, string.Empty);
                }

                if (!IsSimple(child))
                {
                    return new TableCell(child, null, false, true, "<" + Ellipsis + ">");
                }

                DocPart? text = TextPart(child);
                return new TableCell(child, text, false, false, text?.Value ?? string.Empty);
            }
            default:
            {
                DocPart? text = TextPart(rowNode);
                if (text is not null)
                {
                    return new TableCell(rowNode, text, false, false, text.Value);
                }

                if (HasElementChildren(rowNode))
                {
                    return new TableCell(rowNode, null, false, true, "<" + Ellipsis + ">");
                }

                return new TableCell(rowNode, null, true, false, string.Empty);
            }
        }
    }

    // ----- Edit helpers -----

    private EditOp InsertMember(int row, int column, string value)
    {
        DocNode rowNode = _rowNodes[row];
        JsonValueKind kind = MostCommonScalarKind(column);
        int index = rowNode.Children.Count;
        foreach (DocNode child in rowNode.Children)
        {
            if (child.Path.Steps[^1] is MemberStep member && ColumnIndex(TableColumnKind.Key, member.Key) > column)
            {
                index = Document.InsertionIndexBefore(child.Path);
                break;
            }
        }

        string? text = kind is JsonValueKind.String or JsonValueKind.Number ? value : null;
        return new InsertNode(rowNode.Path, index, new NewJsonValue(Columns[column].Name, kind, text));
    }

    private JsonValueKind MostCommonScalarKind(int column)
    {
        var counts = new Dictionary<JsonValueKind, int>();
        for (int r = 0; r < _rowNodes.Count; r++)
        {
            if (Cell(r, column).Node?.JsonKind is { } kind && IsScalar(kind))
            {
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
            }
        }

        if (counts.Count == 0)
        {
            return JsonValueKind.String;
        }

        int max = counts.Values.Max();
        var top = counts.Where(pair => pair.Value == max).ToList();
        return top.Count == 1 ? top[0].Key : JsonValueKind.String;
    }

    private EditOp InsertAttribute(DocNode rowNode, int column, string value)
    {
        int index = rowNode.Parts.Count(part => part.Kind == PartKind.AttributeName);
        foreach (DocPart part in rowNode.Parts)
        {
            if (part.Kind == PartKind.AttributeName && ColumnIndex(TableColumnKind.Attribute, part.Value) > column)
            {
                index = part.AttributeIndex;
                break;
            }
        }

        return new InsertAttribute(rowNode.Path, index, Columns[column].Name, value);
    }

    private EditOp InsertChild(DocNode rowNode, int column, string value)
    {
        DocNode? previous = null;
        int previousColumn = -1;
        DocNode? next = null;
        int nextColumn = int.MaxValue;
        foreach (DocNode child in ElementChildren(rowNode))
        {
            int position = ColumnIndex(TableColumnKind.ChildElement, ElementName(child));
            if (position < 0)
            {
                continue;
            }

            if (position < column && position > previousColumn)
            {
                previous = child;
                previousColumn = position;
            }
            else if (position > column && position < nextColumn)
            {
                next = child;
                nextColumn = position;
            }
        }

        int index = previous is not null
            ? Document.InsertionIndexAfter(previous.Path)
            : next is not null
                ? Document.InsertionIndexBefore(next.Path)
                : rowNode.Children.Count;
        return new InsertNode(rowNode.Path, index, new NewXmlElement(Columns[column].Name, [], value));
    }

    private int CountBefore(int index, Func<DocNode, bool> predicate)
    {
        int count = 0;
        for (int k = 0; k < index; k++)
        {
            if (predicate(_container.Children[k]))
            {
                count++;
            }
        }

        return count;
    }

    private int ColumnIndex(TableColumnKind kind, string name)
    {
        for (int k = 0; k < Columns.Count; k++)
        {
            if (Columns[k].Kind == kind && Columns[k].Name == name)
            {
                return k;
            }
        }

        return -1;
    }

    private void CheckRow(int row)
    {
        if (row < 0 || row >= _rowNodes.Count)
        {
            throw new CfgeditException("row " + row + " is outside the table");
        }
    }

    private void CheckColumn(int column)
    {
        if (column < 0 || column >= Columns.Count)
        {
            throw new CfgeditException("column " + column + " is outside the table");
        }
    }

    // ----- Shared helpers -----

    private static (JsonValueKind Kind, string? Text) EmptyValue(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.String => (JsonValueKind.String, string.Empty),
            JsonValueKind.Number => (JsonValueKind.Number, "0"),
            JsonValueKind.True or JsonValueKind.False => (JsonValueKind.False, null),
            JsonValueKind.Null => (JsonValueKind.Null, null),
            JsonValueKind.Object => (JsonValueKind.Object, null),
            JsonValueKind.Array => (JsonValueKind.Array, null),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown JSON value kind."),
        };
    }

    private static bool IsScalar(JsonValueKind? kind)
    {
        return kind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;
    }

    private static IEnumerable<DocNode> ElementChildren(DocNode node)
    {
        return node.Children.Where(child => child.Kind == NodeKind.XmlElement);
    }

    private static bool HasElementChildren(DocNode node)
    {
        return node.Children.Any(child => child.Kind == NodeKind.XmlElement);
    }

    private static string ElementName(DocNode element)
    {
        return ((ElementStep)element.Path.Steps[^1]).Name;
    }

    /// <summary>An element is simple when it is text-only or <c>&lt;a/&gt;</c>: it has a text part, or it has no end row.</summary>
    private static bool IsSimple(DocNode element)
    {
        return TextPart(element) is not null || element.EndParts.Count == 0;
    }

    private static DocPart? TextPart(DocNode node)
    {
        return node.Parts.FirstOrDefault(part => part.Kind == PartKind.Text);
    }

    private static DocPart ValuePart(DocNode node)
    {
        return node.Parts.First(part => part.Kind == PartKind.Value);
    }

    private static DocPart? AttributeValue(DocNode element, string attribute)
    {
        foreach (DocPart name in element.Parts)
        {
            if (name.Kind != PartKind.AttributeName || name.Value != attribute)
            {
                continue;
            }

            foreach (DocPart value in element.Parts)
            {
                if (value.Kind == PartKind.AttributeValue && value.AttributeIndex == name.AttributeIndex)
                {
                    return value;
                }
            }
        }

        return null;
    }
}
