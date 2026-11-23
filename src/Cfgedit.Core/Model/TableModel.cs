using static Cfgedit.Core.Model.SiblingShape;

namespace Cfgedit.Core.Model;

/// <summary>The kinds of table column.</summary>
public enum TableColumnKind
{
    /// <summary>A member key of the row objects: a JSON member or a TOML key.</summary>
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
/// A column of a table. <see cref="Name"/> is the JSON or TOML key, the attribute or child element name, "value" for an array of
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
/// A table view of an array of objects, an array of scalars (JSON, TOML or YAML), or an XML element's same-named child elements. The model is built
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
        TomlObjects,
        TomlScalars,
        YamlObjects,
        YamlScalars,
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
    /// A JSON array is a table when its items (ignoring comments) are all objects, or all scalars, with at least one item. A TOML array
    /// (an array of tables, or an inline array) is a table by the same rule, its items being tables or scalars. A YAML sequence is a
    /// table when its items are all mappings, or all scalars (an alias item is neither). An XML
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

        if (target.Kind is NodeKind.JsonArray or NodeKind.TomlArray or NodeKind.YamlSequence)
        {
            return ArrayTable(document, target);
        }

        if ((target.Kind is NodeKind.JsonObject or NodeKind.JsonScalar or NodeKind.TomlTable or NodeKind.TomlScalar
                or NodeKind.YamlMapping or NodeKind.YamlScalar or NodeKind.YamlAlias)
            && target.Parent?.Kind is NodeKind.JsonArray or NodeKind.TomlArray or NodeKind.YamlSequence)
        {
            return ArrayTable(document, target.Parent);
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
            case TableShape.TomlScalars:
            case TableShape.YamlScalars:
            {
                DocPart value = ValuePart(rowNode);
                return new TableCell(rowNode, value, false, false, value.Value);
            }
            case TableShape.JsonObjects:
            case TableShape.TomlObjects:
            case TableShape.YamlObjects:
                return ObjectCell(rowNode, col);
            default:
                return XmlCell(rowNode, col);
        }
    }

    /// <summary>
    /// The edit that sets a cell. An existing part is set. A missing JSON member is inserted into the row object, a missing
    /// attribute is inserted into the row element, and a missing child element is inserted among the row's children in column order.
    /// A missing JSON cell takes the column's most common scalar kind (String on a tie or when there is none); the value text is
    /// ignored for True, False and Null. A missing TOML cell takes the column's most common TOML kind, with the typed text as its value:
    /// the four date-time forms count as one kind, and a tie or no scalar cell gives a string. The text column of a row that is
    /// <c>&lt;a/&gt;</c> gets its text.
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

        if (cell.Node?.Kind == NodeKind.YamlAlias)
        {
            throw new CfgeditException("an alias cannot be edited");
        }

        if (cell.Part is { } part)
        {
            return new SetPart(cell.Node!.Path, part.Kind, part.AttributeIndex, value);
        }

        switch (col.Kind)
        {
            case TableColumnKind.Key when _shape == TableShape.TomlObjects:
                return InsertMember(row, column, new NewTomlValue(col.Name, MostCommonTomlKind(column), value));
            case TableColumnKind.Key when _shape == TableShape.YamlObjects:
            {
                // A column of aliases (a merge key's <<, typically) takes an alias, which the table cannot write; a string there would
                // change what the key means.
                if (col.Name == "<<" || HasOnlyAliases(column))
                {
                    throw new CfgeditException("this column holds aliases; add one in the tree");
                }

                YamlNewKind kind = MostCommonYamlKind(column);
                return InsertMember(row, column, new NewYamlValue(col.Name, kind, kind == YamlNewKind.Null ? null : value));
            }
            case TableColumnKind.Key:
            {
                JsonValueKind kind = MostCommonScalarKind(column);
                return InsertMember(row, column, new NewJsonValue(col.Name, kind, kind is JsonValueKind.String or JsonValueKind.Number ? value : null));
            }
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
    /// The JSON kind that <see cref="EditCell(int, int, string)"/> inserts a missing cell of a column with: the column's most common scalar kind.
    /// Null for a column of a table that is not a JSON object table.
    /// </summary>
    /// <param name="column">The column index.</param>
    /// <exception cref="CfgeditException">The column is outside the table.</exception>
    public JsonValueKind? MissingKind(int column)
    {
        CheckColumn(column);
        return _shape == TableShape.JsonObjects && Columns[column].Kind == TableColumnKind.Key ? MostCommonScalarKind(column) : null;
    }

    /// <summary>
    /// The edit that sets a cell to a JSON value of a given scalar kind, for a JSON table. A missing member is inserted with that kind. An existing
    /// value of another kind changes its type (<see cref="ChangeJsonType"/>), and of the same kind is set (for a string or a number) as
    /// <see cref="EditCell(int, int, string)"/> does. The value text is used for a string and a number, and ignored for true, false and null.
    /// </summary>
    /// <param name="row">The row index.</param>
    /// <param name="column">The column index.</param>
    /// <param name="kind">The scalar kind: string, number, true, false or null.</param>
    /// <param name="value">The value text for a string or a number, or null.</param>
    /// <exception cref="CfgeditException">
    /// The request is invalid: the table is not JSON, the cell is nested, or the value already has the kind (true, false or null).
    /// </exception>
    public EditOp EditCell(int row, int column, JsonValueKind kind, string? value)
    {
        CheckRow(row);
        CheckColumn(column);
        if (!IsScalar(kind))
        {
            throw new ArgumentException("A cell takes a scalar kind: string, number, true, false or null.", nameof(kind));
        }

        if (_shape == TableShape.XmlElements)
        {
            throw new CfgeditException("an XML cell has no type; set its text instead");
        }

        if (_shape is TableShape.TomlObjects or TableShape.TomlScalars)
        {
            throw new CfgeditException("a TOML value's type is changed in the tree, not in the table");
        }

        if (_shape is TableShape.YamlObjects or TableShape.YamlScalars)
        {
            throw new CfgeditException("a YAML value's type is changed in the tree, not in the table");
        }

        TableCell cell = Cell(row, column);
        if (cell.IsNested)
        {
            throw new CfgeditException("this cell holds a nested value and cannot be edited in the table; edit it in the tree");
        }

        bool takesText = kind is JsonValueKind.String or JsonValueKind.Number;
        string? text = takesText ? value ?? string.Empty : null;
        if (cell.IsMissing)
        {
            return InsertMember(row, column, new NewJsonValue(Columns[column].Name, kind, text));
        }

        DocNode typed = _shape == TableShape.JsonScalars ? _rowNodes[row] : cell.Node!;
        if (typed.JsonKind != kind)
        {
            return new ChangeJsonType(typed.Path, kind, text);
        }

        if (takesText)
        {
            return new SetPart(typed.Path, PartKind.Value, -1, text!);
        }

        throw new CfgeditException("this value is already of that type");
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
        if (_shape is TableShape.JsonScalars or TableShape.TomlScalars or TableShape.YamlScalars)
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

        if (cell.Node?.Kind == NodeKind.YamlAlias)
        {
            throw new CfgeditException("an alias cannot be cleared in the table; delete it in the tree");
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

        // The row is shaped like the row it is added after (or the first row, for the top). Only the table's child-element columns
        // are copied for an XML row; the shape itself is SiblingShape's, shared with the editor's add keys.
        DocNode source = _rowNodes[Math.Max(afterRow, 0)];
        int index = afterRow < 0
            ? Document.InsertionIndexBefore(_rowPaths[0])
            : Document.InsertionIndexAfter(_rowPaths[afterRow]);
        return SiblingShape.Build(Document, source.Path, Container, index, keepElement: child => ColumnIndex(TableColumnKind.ChildElement, ElementName(child)) >= 0).Edit;
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

    private static TableModel? ArrayTable(ConfigDocument document, DocNode array)
    {
        List<DocNode> items = array.Children.Where(child => child.Kind is not (NodeKind.JsonComment or NodeKind.TomlComment or NodeKind.YamlComment)).ToList();
        if (items.Count == 0)
        {
            return null;
        }

        bool json = items.All(item => item.JsonKind == JsonValueKind.Object);
        bool toml = !json && items.All(item => item.Kind == NodeKind.TomlTable);
        bool yaml = !json && !toml && items.All(item => item.Kind == NodeKind.YamlMapping);
        if (json || toml || yaml)
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

            TableShape shape = json ? TableShape.JsonObjects : toml ? TableShape.TomlObjects : TableShape.YamlObjects;
            return new TableModel(
                document,
                array,
                shape,
                items,
                keys.Select(key => new TableColumn(TableColumnKind.Key, key)).ToList());
        }

        if (items.All(item => IsScalar(item.JsonKind)))
        {
            return new TableModel(
                document, array, TableShape.JsonScalars, items, [new TableColumn(TableColumnKind.Value, "value")]);
        }

        if (items.All(item => item.Kind == NodeKind.TomlScalar))
        {
            return new TableModel(
                document, array, TableShape.TomlScalars, items, [new TableColumn(TableColumnKind.Value, "value")]);
        }

        if (items.All(item => item.Kind == NodeKind.YamlScalar))
        {
            return new TableModel(
                document, array, TableShape.YamlScalars, items, [new TableColumn(TableColumnKind.Value, "value")]);
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

    private static DocNode? MemberNamed(DocNode obj, string key)
    {
        return obj.Children.FirstOrDefault(child => child.Path.Steps[^1] is MemberStep step && step.Key == key);
    }

    /// <summary>
    /// A cell of a row object: a JSON member, a TOML key or a YAML mapping member. A nested object or array (or TOML table or array, or YAML
    /// mapping or sequence) shows its count. A YAML alias shows its name, as a read-only value (its part is the alias, which cannot be edited).
    /// </summary>
    private TableCell ObjectCell(DocNode rowNode, TableColumn col)
    {
        DocNode? member = MemberNamed(rowNode, col.Name);
        if (member is null)
        {
            return new TableCell(null, null, true, false, string.Empty);
        }

        if (member.Kind == NodeKind.YamlAlias)
        {
            DocPart alias = member.Parts.First(part => part.Kind == PartKind.Alias);
            return new TableCell(member, alias, false, false, alias.Value);
        }

        if (IsNestedMember(member))
        {
            string count = member.Parts.First(part => part.Kind == PartKind.Summary).Value;
            string display = IsArrayNode(member) ? "[" + count + "]" : "{" + count + "}";
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

    /// <summary>Inserts a member into a row, before the first member of a later column, else at the end.</summary>
    private EditOp InsertMember(int row, int column, NewNode member)
    {
        DocNode rowNode = _rowNodes[row];
        int index = rowNode.Children.Count;
        foreach (DocNode child in rowNode.Children)
        {
            if (child.Path.Steps[^1] is MemberStep step && ColumnIndex(TableColumnKind.Key, step.Key) > column)
            {
                index = Document.InsertionIndexBefore(child.Path);
                break;
            }
        }

        return new InsertNode(rowNode.Path, index, member);
    }

    /// <summary>
    /// The TOML kind that a missing cell of a column is inserted with: the most common kind among the present scalar cells, with the
    /// four date-time forms counted as one kind (DateTime). A tie, or no scalar cell, gives a string.
    /// </summary>
    private TomlNewKind MostCommonTomlKind(int column)
    {
        var counts = new Dictionary<TomlNewKind, int>();
        for (int r = 0; r < _rowNodes.Count; r++)
        {
            if (Cell(r, column).Node?.TomlKind is { } kind)
            {
                TomlNewKind mapped = NewKindOf(kind);
                counts[mapped] = counts.GetValueOrDefault(mapped) + 1;
            }
        }

        if (counts.Count == 0)
        {
            return TomlNewKind.String;
        }

        int max = counts.Values.Max();
        var top = counts.Where(pair => pair.Value == max).ToList();
        return top.Count == 1 ? top[0].Key : TomlNewKind.String;
    }

    private static TomlNewKind NewKindOf(TomlValueKind kind)
    {
        return kind switch
        {
            TomlValueKind.String => TomlNewKind.String,
            TomlValueKind.Integer => TomlNewKind.Integer,
            TomlValueKind.Float => TomlNewKind.Float,
            TomlValueKind.Boolean => TomlNewKind.Boolean,
            _ => TomlNewKind.DateTime,
        };
    }

    /// <summary>
    /// The YAML kind that a missing cell of a column is inserted with: the most common kind among the present scalar cells, with integers and
    /// floats counted as Number. A tie, or no scalar cell, gives a string.
    /// </summary>
    /// <summary>True when every present cell of a YAML column is an alias (and at least one is present).</summary>
    private bool HasOnlyAliases(int column)
    {
        bool any = false;
        for (int r = 0; r < _rowNodes.Count; r++)
        {
            if (Cell(r, column).Node is { } node)
            {
                if (node.Kind != NodeKind.YamlAlias)
                {
                    return false;
                }

                any = true;
            }
        }

        return any;
    }

    private YamlNewKind MostCommonYamlKind(int column)
    {
        var counts = new Dictionary<YamlNewKind, int>();
        for (int r = 0; r < _rowNodes.Count; r++)
        {
            if (Cell(r, column).Node is { Kind: NodeKind.YamlScalar, YamlKind: { } kind })
            {
                YamlNewKind mapped = NewYamlKindOf(kind);
                counts[mapped] = counts.GetValueOrDefault(mapped) + 1;
            }
        }

        if (counts.Count == 0)
        {
            return YamlNewKind.String;
        }

        int max = counts.Values.Max();
        var top = counts.Where(pair => pair.Value == max).ToList();
        return top.Count == 1 ? top[0].Key : YamlNewKind.String;
    }

    private static YamlNewKind NewYamlKindOf(YamlValueKind kind)
    {
        return kind switch
        {
            YamlValueKind.Integer or YamlValueKind.Float => YamlNewKind.Number,
            YamlValueKind.Boolean => YamlNewKind.Boolean,
            YamlValueKind.Null => YamlNewKind.Null,
            _ => YamlNewKind.String,
        };
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

    /// <summary>
    /// A member that is a container: a JSON object or array, a TOML table or array (inline, or a header or array of tables), or a YAML
    /// mapping or sequence.
    /// </summary>
    private static bool IsNestedMember(DocNode member)
    {
        return member.JsonKind is JsonValueKind.Object or JsonValueKind.Array
            || member.Kind is NodeKind.TomlTable or NodeKind.TomlArray or NodeKind.YamlMapping or NodeKind.YamlSequence;
    }

    /// <summary>An array container: a JSON array, a TOML array or a YAML sequence.</summary>
    private static bool IsArrayNode(DocNode node)
    {
        return node.JsonKind == JsonValueKind.Array || node.Kind is NodeKind.TomlArray or NodeKind.YamlSequence;
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

    /// <summary>An element is simple when it is text-only or <c>&lt;a/&gt;</c>: it has a text part, or it has no end row.</summary>
    private static bool IsSimple(DocNode element)
    {
        return TextPart(element) is not null || element.EndParts.Count == 0;
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
