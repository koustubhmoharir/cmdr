using Cfgedit.Core.Json;
using Cfgedit.Core.Toml;

namespace Cfgedit.Core.Model;

/// <summary>How a TOML table was defined. It decides what later text may do to it.</summary>
internal enum TomlTableState
{
    /// <summary>The document's root table.</summary>
    Root,

    /// <summary>A table made as the parent of a deeper header (<c>[a.b]</c> makes <c>a</c>). Its own header may still define it.</summary>
    Implicit,

    /// <summary>A table defined by its own <c>[a]</c> header, or an item of an array of tables. Dotted keys may not define it.</summary>
    Header,

    /// <summary>A table made by dotted keys (<c>a.b = 1</c> makes <c>a</c>). A header may add sub-tables to it, not define it.</summary>
    Dotted,

    /// <summary>An inline table (<c>a = { … }</c>). It is closed: nothing may be added to it.</summary>
    Inline,
}

/// <summary>
/// A value of the merged data model, as the TOML text defines it: a table, an array or a scalar. Its <see cref="Pieces"/> are the
/// text that defines it (see <see cref="TomlModel.PiecesOf"/>), and <see cref="Start"/> and <see cref="End"/> are its span, set by the
/// measure pass.
/// </summary>
internal abstract class MergedValue
{
    /// <summary>The text that defines the value, in file order: headers, key/value lines, inline tables or array items.</summary>
    internal List<TomlNode> Pieces { get; } = [];

    /// <summary>The offset of the first character of the value's span.</summary>
    internal int Start { get; set; }

    /// <summary>The offset just after the value's span.</summary>
    internal int End { get; set; }
}

/// <summary>A table of the merged data model. Its members are in order of first appearance.</summary>
internal sealed class MergedTable : MergedValue
{
    private readonly Dictionary<string, MergedMember> _byName = new(StringComparer.Ordinal);

    internal MergedTable(TomlTableState state, int depth, string fullName)
    {
        State = state;
        Depth = depth;
        FullName = fullName;
    }

    /// <summary>How the table was defined.</summary>
    internal TomlTableState State { get; set; }

    /// <summary>The number of tables from the root to this one.</summary>
    internal int Depth { get; }

    /// <summary>The table this one is a member of, or null for the root. For an array of tables' item, it is the table that holds the array.</summary>
    internal MergedTable? Parent { get; init; }

    /// <summary>The dotted name of the table from the root, for messages: <c>a.b</c>. Empty for the root.</summary>
    internal string FullName { get; }

    /// <summary>The members, in order of first appearance.</summary>
    internal List<MergedMember> Members { get; } = [];

    /// <summary>The comments that are children of this table before its first member: comments above its header and its header's trailing comment.</summary>
    internal List<TomlComment> Leading { get; } = [];

    /// <summary>The comments of this table's sections that stand alone, in file order.</summary>
    internal List<TomlComment> Loose { get; } = [];

    internal bool TryGetMember(string name, out MergedMember? member)
    {
        return _byName.TryGetValue(name, out member);
    }

    internal void AddMember(MergedMember member)
    {
        Members.Add(member);
        _byName.Add(member.Name, member);
    }
}

/// <summary>A member of a table: a key, its value, and the comments attached to its line.</summary>
/// <param name="name">The decoded key.</param>
/// <param name="keyPart">The key segment in the first piece that names it.</param>
/// <param name="position">The offset where the member first appears, which orders it among the members.</param>
/// <param name="value">The value.</param>
internal sealed class MergedMember(string name, TomlKeyPart keyPart, int position, MergedValue value)
{
    /// <summary>The decoded key.</summary>
    internal string Name { get; } = name;

    /// <summary>The key segment in the member's first piece.</summary>
    internal TomlKeyPart KeyPart { get; } = keyPart;

    /// <summary>The offset where the member first appears.</summary>
    internal int Position { get; } = position;

    /// <summary>The value.</summary>
    internal MergedValue Value { get; } = value;

    /// <summary>The comment lines directly above the member's line, in file order.</summary>
    internal List<TomlComment> Before { get; } = [];

    /// <summary>The trailing comment of the member's line, or null.</summary>
    internal TomlComment? After { get; set; }
}

/// <summary>
/// An array: a static array (<c>[...]</c>, with its syntax) or an array of tables (<c>[[x]]</c>, whose items are tables and whose
/// pieces are the headers that made them).
/// </summary>
internal sealed class MergedArray(bool isTableArray, TomlArray? syntax) : MergedValue
{
    /// <summary>True for an array of tables.</summary>
    internal bool IsTableArray { get; } = isTableArray;

    /// <summary>The syntax of a static array, or null for an array of tables.</summary>
    internal TomlArray? Syntax { get; } = syntax;

    /// <summary>The items, in order. Their comments are in <see cref="Syntax"/>, not here.</summary>
    internal List<MergedValue> Items { get; } = [];
}

/// <summary>A scalar: a string, number, boolean or date-time.</summary>
internal sealed class MergedScalar(TomlValue value) : MergedValue
{
    /// <summary>The syntax of the scalar.</summary>
    internal TomlValue Value { get; } = value;
}

/// <summary>
/// The first pass of the TOML model: it merges the tables and arrays defined by the text, in file order, and checks the TOML 1.0
/// rules that the syntax layer does not check. Then it places the comments: a comment line directly above a key/value line or a
/// header attaches to it, a trailing comment attaches to its line, and any other comment stands in the table of its section.
/// Comments inside arrays stay in their arrays.
/// </summary>
/// <remarks>
/// The rules: a key is defined once per table; a header defines a table once, and only where it is not already defined by a header,
/// dotted keys or an inline table; dotted keys may pass through tables made by headers and dotted keys, but not through a table
/// defined by a header, an inline table, an array or a scalar; an inline table is closed; a static array may not be extended; an
/// array of tables takes only <c>[[x]]</c> headers. Nesting is capped at <see cref="JsonDocument.MaxDepth"/>, as in the other formats.
/// </remarks>
internal sealed class TomlMerger
{
    private const int MaxDepth = JsonDocument.MaxDepth;

    private readonly TomlDocument _document;
    private readonly MergedTable _root = new(TomlTableState.Root, 0, string.Empty);
    private readonly Dictionary<TomlHeaderItem, MergedTable> _sections = [];
    private readonly Dictionary<TomlKeyValueItem, MergedMember> _members = [];

    private TomlMerger(TomlDocument document)
    {
        _document = document;
    }

    /// <summary>Merges a parsed document into its table model.</summary>
    /// <exception cref="Cfgedit.Core.CfgeditException">A TOML rule about tables, keys or arrays is broken.</exception>
    internal static MergedTable Merge(TomlDocument document)
    {
        var merger = new TomlMerger(document);
        merger.DefineTables();
        merger.PlaceComments();
        return merger._root;
    }

    private void DefineTables()
    {
        MergedTable section = _root;
        foreach (TomlItem item in _document.Items)
        {
            switch (item)
            {
                case TomlKeyValueItem keyValue:
                    DefineKeyValue(section, keyValue);
                    break;
                case TomlHeaderItem header:
                    section = DefineHeader(header, section);
                    _sections[header] = section;
                    break;
            }
        }
    }

    private void DefineKeyValue(MergedTable section, TomlKeyValueItem keyValue)
    {
        IReadOnlyList<TomlKeyPart> path = keyValue.Key.Parts;
        MergedTable table = section;
        for (int i = 0; i < path.Count - 1; i++)
        {
            table = DottedTable(table, path, i, keyValue, keyValue.Start);
        }

        TomlKeyPart leaf = path[^1];
        if (table.TryGetMember(leaf.Name, out _))
        {
            throw Duplicate(leaf);
        }

        var member = new MergedMember(leaf.Name, leaf, keyValue.Start, Value(keyValue.Value, keyValue, table, leaf.Name));
        table.AddMember(member);
        _members[keyValue] = member;
    }

    /// <summary>
    /// The table that a dotted key walks through or defines, at one segment. A table made by headers or dotted keys is walked
    /// through; a table defined by a header, an inline table or a non-table is refused.
    /// </summary>
    private MergedTable DottedTable(MergedTable table, IReadOnlyList<TomlKeyPart> path, int index, TomlNode piece, int position)
    {
        TomlKeyPart part = path[index];
        if (table.TryGetMember(part.Name, out MergedMember? member))
        {
            switch (member!.Value)
            {
                case MergedTable existing when existing.State is TomlTableState.Implicit or TomlTableState.Dotted:
                    existing.Pieces.Add(piece);
                    return existing;
                case MergedTable { State: TomlTableState.Inline }:
                    throw Fail(part.Start, $"inline table '{Join(table.FullName, part.Name)}' cannot be extended");
                case MergedTable:
                    throw Fail(part.Start, $"table '{Join(table.FullName, part.Name)}' is already defined");
                default:
                    throw Fail(part.Start, $"'{Join(table.FullName, part.Name)}' is not a table");
            }
        }

        MergedTable created = NewTable(TomlTableState.Dotted, table, part.Start, part.Name);
        created.Pieces.Add(piece);
        table.AddMember(new MergedMember(part.Name, part, position, created));
        return created;
    }

    /// <summary>
    /// The table that a header walks through, at one segment of its name. A table made by headers or dotted keys, or an array of
    /// tables (through its last item), is walked through; an inline table, a static array or a non-table is refused. A missing
    /// table is made implicitly, by this header.
    /// </summary>
    /// <remarks>
    /// A header that walks through an array of tables points to the array's last item. The walk is refused when another table header
    /// came between that item and this header, since the header would not be part of the item in the text.
    /// <paramref name="previous"/> is the table of the header before this one.
    /// </remarks>
    private MergedTable HeaderTable(MergedTable table, IReadOnlyList<TomlKeyPart> path, int index, TomlHeaderItem header, MergedTable previous)
    {
        TomlKeyPart part = path[index];
        if (table.TryGetMember(part.Name, out MergedMember? member))
        {
            switch (member!.Value)
            {
                case MergedTable { State: TomlTableState.Inline }:
                    throw Fail(part.Start, $"inline table '{Join(table.FullName, part.Name)}' cannot be extended");
                case MergedTable existing:
                    return existing;
                case MergedArray { IsTableArray: true } array:
                {
                    var item = (MergedTable)array.Items[^1];

                    // The previous header must be the item or inside it. Tomlyn reads a header after another table as a table of
                    // its own, so such a header cannot be verified; it is refused rather than left to the cross-check.
                    if (!IsWithin(previous, item))
                    {
                        string headerName = string.Join('.', path.Select(p => p.Name));
                        throw Fail(
                            header.Start,
                            $"cfgedit cannot verify a [{headerName}] header that follows another table after [[{Join(table.FullName, part.Name)}]]; Tomlyn reads it differently");
                    }

                    return item;
                }
                default:
                    throw Fail(part.Start, $"'{Join(table.FullName, part.Name)}' is not a table");
            }
        }

        MergedTable created = NewTable(TomlTableState.Implicit, table, part.Start, part.Name);
        created.Pieces.Add(header);
        table.AddMember(new MergedMember(part.Name, part, header.Start, created));
        return created;
    }

    /// <summary>
    /// Defines the table of a header, or appends an item to the array of tables it names. Returns the table that the header's section is
    /// in. <paramref name="previous"/> is the section of the header before this one.
    /// </summary>
    private MergedTable DefineHeader(TomlHeaderItem header, MergedTable previous)
    {
        IReadOnlyList<TomlKeyPart> path = header.Key.Parts;
        MergedTable table = _root;
        for (int i = 0; i < path.Count - 1; i++)
        {
            table = HeaderTable(table, path, i, header, previous);
        }

        TomlKeyPart leaf = path[^1];
        bool arrayTable = header is TomlArrayTableItem;
        if (!table.TryGetMember(leaf.Name, out MergedMember? member))
        {
            if (arrayTable)
            {
                var array = new MergedArray(true, null);
                array.Pieces.Add(header);
                MergedTable item = NewTable(TomlTableState.Header, table, leaf.Start, leaf.Name);
                item.Pieces.Add(header);
                array.Items.Add(item);
                table.AddMember(new MergedMember(leaf.Name, leaf, header.Start, array));
                return item;
            }

            MergedTable defined = NewTable(TomlTableState.Header, table, leaf.Start, leaf.Name);
            defined.Pieces.Add(header);
            table.AddMember(new MergedMember(leaf.Name, leaf, header.Start, defined));
            return defined;
        }

        switch (member!.Value)
        {
            case MergedTable { State: TomlTableState.Implicit } implicitTable when !arrayTable:
                implicitTable.State = TomlTableState.Header;
                implicitTable.Pieces.Add(header);
                return implicitTable;
            case MergedArray { IsTableArray: true } array when arrayTable:
            {
                // Tomlyn drops the earlier items of an array whose items another table header separates, so such an item is refused.
                if (!IsWithin(previous, (MergedTable)array.Items[^1]))
                {
                    throw Fail(
                        header.Start,
                        $"cfgedit cannot verify [[{Join(table.FullName, leaf.Name)}]] items separated by another table; Tomlyn reads them differently");
                }

                MergedTable item = NewTable(TomlTableState.Header, table, leaf.Start, leaf.Name);
                item.Pieces.Add(header);
                array.Items.Add(item);
                array.Pieces.Add(header);
                return item;
            }
            case MergedArray { IsTableArray: false } when arrayTable:
                throw Fail(leaf.Start, $"cannot append to static array '{Join(table.FullName, leaf.Name)}'");
            case MergedTable { State: TomlTableState.Inline }:
                throw Fail(leaf.Start, $"inline table '{Join(table.FullName, leaf.Name)}' cannot be extended");
            case MergedTable:
            case MergedArray { IsTableArray: true }:
                throw Fail(leaf.Start, $"table '{Join(table.FullName, leaf.Name)}' is already defined");
            default:
                throw Duplicate(leaf);
        }
    }

    /// <summary>Adds the members of an inline table. Its dotted keys walk through tables that it made itself.</summary>
    private void DefineInlineMembers(MergedTable table, TomlInlineTable inline)
    {
        foreach (TomlNode entry in inline.Entries)
        {
            if (entry is not TomlInlineMember member)
            {
                continue;
            }

            IReadOnlyList<TomlKeyPart> path = member.Key.Parts;
            MergedTable owner = table;
            for (int i = 0; i < path.Count - 1; i++)
            {
                owner = DottedTable(owner, path, i, member, member.Start);
            }

            TomlKeyPart leaf = path[^1];
            if (owner.TryGetMember(leaf.Name, out _))
            {
                throw Duplicate(leaf);
            }

            owner.AddMember(new MergedMember(leaf.Name, leaf, member.Start, Value(member.Value, member, owner, leaf.Name)));
        }
    }

    /// <summary>
    /// The merged value of a syntax value. A scalar or a static array takes the piece <paramref name="carrier"/> (the key/value line
    /// or the array item that holds it); an inline table takes itself as its piece. <paramref name="name"/> is the key the value is
    /// under, or the key of the array that holds it, which names an inline table for a message.
    /// </summary>
    private MergedValue Value(TomlValue value, TomlNode carrier, MergedTable owner, string name)
    {
        switch (value)
        {
            case TomlInlineTable inline:
            {
                MergedTable table = NewTable(TomlTableState.Inline, owner, inline.Start, name);
                table.Pieces.Add(inline);
                DefineInlineMembers(table, inline);
                return table;
            }

            case TomlArray array:
            {
                var result = new MergedArray(false, array);
                result.Pieces.Add(carrier);
                foreach (TomlNode entry in array.Entries)
                {
                    if (entry is TomlArrayItem item)
                    {
                        result.Items.Add(Value(item.Value, item, owner, name));
                    }
                }

                return result;
            }

            default:
            {
                var scalar = new MergedScalar(value);
                scalar.Pieces.Add(carrier);
                return scalar;
            }
        }
    }

    private MergedTable NewTable(TomlTableState state, MergedTable parent, int offset, string name)
    {
        int depth = parent.Depth + 1;
        if (depth > MaxDepth)
        {
            throw Fail(offset, $"nesting is deeper than {MaxDepth} levels");
        }

        return new MergedTable(state, depth, Join(parent.FullName, name)) { Parent = parent };
    }

    /// <summary>True when <paramref name="table"/> is <paramref name="ancestor"/> or is inside it.</summary>
    private static bool IsWithin(MergedTable table, MergedTable ancestor)
    {
        for (MergedTable? current = table; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Places the comments. A run of comment lines that is directly followed by a key/value line or a header (no blank line between)
    /// attaches to it; a run followed by a blank line or the end of the text stands alone in the table of its section. A trailing
    /// comment attaches to its line: to the member of a key/value line, and to the header's table, after its above-comments.
    /// </summary>
    private void PlaceComments()
    {
        IReadOnlyList<TomlItem> items = _document.Items;
        MergedTable section = _root;
        int i = 0;
        while (i < items.Count)
        {
            if (items[i] is TomlCommentItem)
            {
                int end = i;
                while (end < items.Count && items[end] is TomlCommentItem)
                {
                    end++;
                }

                bool attach = end < items.Count && items[end] is TomlKeyValueItem or TomlHeaderItem;
                for (int k = i; k < end; k++)
                {
                    TomlComment comment = items[k].Comment!;
                    if (!attach)
                    {
                        section.Loose.Add(comment);
                    }
                    else if (items[end] is TomlKeyValueItem keyValue)
                    {
                        _members[keyValue].Before.Add(comment);
                    }
                    else
                    {
                        _sections[(TomlHeaderItem)items[end]].Leading.Add(comment);
                    }
                }

                i = end;
                continue;
            }

            switch (items[i])
            {
                case TomlKeyValueItem keyValue when keyValue.Comment is { } trailing:
                    _members[keyValue].After = trailing;
                    break;
                case TomlHeaderItem header:
                    section = _sections[header];
                    if (header.Comment is { } headerTrailing)
                    {
                        section.Leading.Add(headerTrailing);
                    }

                    break;
            }

            i++;
        }
    }

    /// <summary>The dotted name of a table, or of a key in it, for a message.</summary>
    private static string Join(string prefix, string name)
    {
        return prefix.Length == 0 ? name : prefix + "." + name;
    }

    private CfgeditException Duplicate(TomlKeyPart leaf)
    {
        return Fail(leaf.Start, $"duplicate key '{leaf.Name}'");
    }

    private CfgeditException Fail(int offset, string message)
    {
        return TomlStrings.Fail(_document.Source, offset, message);
    }
}
