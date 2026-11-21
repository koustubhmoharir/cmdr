using System.Text;
using Cfgedit.Core.Model;
using Shared.Tui;

namespace Cfgedit.Tui;

/// <summary>A segment of a display line: its text, its style, and the part it belongs to (-1 for decoration).</summary>
public sealed record Segment(string Text, StyleKind Style, int PartIndex);

/// <summary>One display line: its indent in columns, then its segments. The fold glyph is a segment of the first line.</summary>
public sealed record DisplayLine(int Indent, IReadOnlyList<Segment> Segments);

/// <summary>
/// Where a part is on the display: its line, and its start and end columns from the left edge of the view, indent and fold
/// included. A part cut off by the width has a zero-width range at the cut.
/// </summary>
public readonly record struct PartRange(int Line, int Start, int End);

/// <summary>The display lines of one row, and the range of each of its parts (in the row's parts, or its end parts for an end row).</summary>
public sealed record RowLines(IReadOnlyList<DisplayLine> Lines, IReadOnlyList<PartRange> Parts);

/// <summary>
/// Turns a row into display lines for a width, with the range of each part. The layout is pure: it uses no Terminal.Gui types.
/// Widths come from <see cref="TextWidth"/>, which measures per grapheme cluster. Text is never cut inside a grapheme.
/// </summary>
public static class RowLayout
{
    // The glyphs are written as \x escapes so that the source stays ASCII.
    private const string FoldExpanded = "\x25BE";
    private const string FoldCollapsed = "\x25B8";
    private const string Ellipsis = "\x2026";
    private const string GlyphLineFeed = "\x23CE";
    private const string GlyphTab = "\x2192";
    private const string GlyphCarriageReturn = "\x240D";
    private const string GlyphOther = "\x00B7";

    /// <summary>The fewest columns an aligned attribute line may leave; with less, its continuation lines indent by 4 instead.</summary>
    private const int MinAlignedColumns = 24;

    /// <summary>The extra indent of continuation lines when the alignment leaves too few columns.</summary>
    private const int FallbackContinuationExtra = 4;

    /// <summary>The columns of the fold glyph and the space after it.</summary>
    private const int FoldColumns = 2;

    /// <summary>
    /// Lays out one row for a width. <paramref name="collapsed"/> applies to a collapsible node; an end row ignores it.
    /// The width is clamped to at least 0. The result always has at least one line.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="collapsed">True when the row's node is collapsed.</param>
    /// <param name="width">The width of the view in columns.</param>
    public static RowLines Layout(Row row, bool collapsed, int width)
    {
        ArgumentNullException.ThrowIfNull(row);

        int columns = Math.Max(0, width);
        int indent = 2 * Math.Max(0, row.Depth);
        DocNode node = row.Node;
        if (row.IsEnd)
        {
            var items = FoldItems(row, collapsed);
            items.Add(Fixed("</", StyleKind.Punctuation, -1));
            items.Add(Begin(0));
            AddText(items, node.EndParts[0].Value, StyleKind.ElementName, 0);
            items.Add(End(0));
            items.Add(Fixed(">", StyleKind.Punctuation, -1));
            return SingleLine(indent, items, node.EndParts.Count, columns);
        }

        return node.Kind switch
        {
            NodeKind.XmlElement => LayoutElement(row, collapsed, indent, columns),
            NodeKind.JsonObject or NodeKind.JsonArray or NodeKind.JsonScalar or NodeKind.JsonComment =>
                SingleLine(indent, JsonItems(row, collapsed), node.Parts.Count, columns),
            NodeKind.XmlText or NodeKind.XmlCData or NodeKind.XmlComment or NodeKind.XmlProcessingInstruction
                or NodeKind.XmlDeclaration or NodeKind.XmlDoctype =>
                SingleLine(indent, XmlItems(row, collapsed), node.Parts.Count, columns),
            _ => throw new ArgumentException("The document node has no row.", nameof(row)),
        };
    }

    // ----- Items -----

    /// <summary>
    /// One thing on a line: a cell (a grapheme, a glyph or a piece of decoration, with its width) or the start or end of a part.
    /// Part markers have no width. A cut removes cells, and the markers that follow it are kept at the cut.
    /// </summary>
    private enum ItemKind
    {
        Cell,
        Begin,
        End,
    }

    private readonly record struct Item(ItemKind Kind, string Text, StyleKind Style, int Part, int Width);

    /// <summary>A line before its segments are built: its indent, and its items.</summary>
    private sealed record OutLine(int Indent, List<Item> Items);

    /// <summary>A piece of an element's row that wraps as one: an attribute, or a tail such as <c>&gt;</c> or the text.</summary>
    private sealed record Unit(List<Item> Items, bool Separated);

    private static Item Fixed(string text, StyleKind style, int part)
    {
        return new Item(ItemKind.Cell, text, style, part, text.Length);
    }

    private static Item Begin(int part)
    {
        return new Item(ItemKind.Begin, string.Empty, StyleKind.Text, part, 0);
    }

    private static Item End(int part)
    {
        return new Item(ItemKind.End, string.Empty, StyleKind.Text, part, 0);
    }

    /// <summary>
    /// Adds the text as cells: one per grapheme cluster, with the control characters as glyphs. A glyph is one column, and the
    /// text keeps the given style.
    /// </summary>
    private static void AddText(List<Item> items, string text, StyleKind style, int part)
    {
        foreach ((int offset, int length, _) in TextWidth.Graphemes(text))
        {
            string grapheme = text.Substring(offset, length);
            if (grapheme == "\r\n")
            {
                items.Add(Fixed(GlyphCarriageReturn, StyleKind.Glyph, part));
                items.Add(Fixed(GlyphLineFeed, StyleKind.Glyph, part));
            }
            else if (length == 1 && char.IsControl(grapheme[0]))
            {
                items.Add(Fixed(ControlGlyph(grapheme[0]), StyleKind.Glyph, part));
            }
            else
            {
                items.Add(new Item(ItemKind.Cell, grapheme, style, part, TextWidth.WidthOf(grapheme.AsSpan())));
            }
        }
    }

    private static string ControlGlyph(char c)
    {
        return c switch
        {
            '\n' => GlyphLineFeed,
            '\t' => GlyphTab,
            '\r' => GlyphCarriageReturn,
            _ => GlyphOther,
        };
    }

    /// <summary>The raw marker after a part that is edited as source text.</summary>
    private static void AddRaw(List<Item> items, DocPart part)
    {
        if (part.IsRaw)
        {
            items.Add(Fixed(" raw", StyleKind.RawMarker, -1));
        }
    }

    /// <summary>The fold column: the glyph and a space for a collapsible node, else two spaces. End rows have two spaces.</summary>
    private static List<Item> FoldItems(Row row, bool collapsed)
    {
        bool collapsible = !row.IsEnd && DocRows.IsCollapsible(row.Node);
        string glyph = collapsible ? (collapsed ? FoldCollapsed : FoldExpanded) : " ";
        return [Fixed(glyph, StyleKind.Fold, -1), Fixed(" ", StyleKind.Fold, -1)];
    }

    // ----- JSON -----

    private static List<Item> JsonItems(Row row, bool collapsed)
    {
        DocNode node = row.Node;
        List<Item> items = FoldItems(row, collapsed);
        if (node.Kind == NodeKind.JsonComment)
        {
            // A block comment's content ends two characters before the node's end; a line comment's content ends at the node's end.
            DocPart comment = node.Parts[0];
            bool block = node.End - comment.End == 2;
            items.Add(Fixed(block ? "/*" : "//", StyleKind.Punctuation, -1));
            items.Add(Begin(0));
            AddText(items, comment.Value, StyleKind.Comment, 0);
            items.Add(End(0));
            if (block)
            {
                items.Add(Fixed("*/", StyleKind.Punctuation, -1));
            }

            return items;
        }

        bool labelled = node.Parts.Any(part => part.Kind is PartKind.Key or PartKind.Index);
        for (int i = 0; i < node.Parts.Count; i++)
        {
            DocPart part = node.Parts[i];
            switch (part.Kind)
            {
                case PartKind.Key:
                    items.Add(Begin(i));
                    if (part.Value.Length == 0)
                    {
                        items.Add(Fixed("\"\"", StyleKind.Punctuation, i));
                    }
                    else
                    {
                        AddText(items, part.Value, StyleKind.Key, i);
                    }

                    items.Add(End(i));
                    break;
                case PartKind.Index:
                    items.Add(Begin(i));
                    AddText(items, part.Value, StyleKind.Index, i);
                    items.Add(End(i));
                    break;
                case PartKind.Value:
                    if (labelled)
                    {
                        items.Add(Fixed(": ", StyleKind.Punctuation, -1));
                    }

                    items.Add(Begin(i));
                    JsonScalarItems(items, node.JsonKind, part.Value, i);
                    items.Add(End(i));
                    break;
                case PartKind.Summary:
                    if (labelled)
                    {
                        items.Add(Fixed(": ", StyleKind.Punctuation, -1));
                    }

                    items.Add(Begin(i));
                    items.Add(Fixed(node.JsonKind == JsonValueKind.Array ? "[" : "{", StyleKind.Punctuation, i));
                    AddText(items, part.Value, StyleKind.Summary, i);
                    items.Add(Fixed(node.JsonKind == JsonValueKind.Array ? "]" : "}", StyleKind.Punctuation, i));
                    items.Add(End(i));
                    break;
                default:
                    throw new ArgumentException("A JSON node has an unexpected part: " + part.Kind + ".", nameof(row));
            }
        }

        return items;
    }

    private static void JsonScalarItems(List<Item> items, JsonValueKind? kind, string value, int part)
    {
        switch (kind)
        {
            case JsonValueKind.String when value.Length == 0:
                items.Add(Fixed("\"\"", StyleKind.Punctuation, part));
                break;
            case JsonValueKind.String:
                AddText(items, value, StyleKind.String, part);
                break;
            case JsonValueKind.Number:
                AddText(items, value, StyleKind.Number, part);
                break;
            case JsonValueKind.True or JsonValueKind.False:
                AddText(items, value, StyleKind.Bool, part);
                break;
            case JsonValueKind.Null:
                AddText(items, value, StyleKind.Null, part);
                break;
            default:
                AddText(items, value, StyleKind.ReadOnly, part);
                break;
        }
    }

    // ----- XML (single line) -----

    private static List<Item> XmlItems(Row row, bool collapsed)
    {
        DocNode node = row.Node;
        List<Item> items = FoldItems(row, collapsed);
        DocPart first = node.Parts[0];
        switch (node.Kind)
        {
            case NodeKind.XmlText:
                items.Add(Begin(0));
                AddText(items, first.Value, StyleKind.Text, 0);
                items.Add(End(0));
                AddRaw(items, first);
                break;
            case NodeKind.XmlCData:
                items.Add(Fixed("<![CDATA[", StyleKind.Punctuation, -1));
                items.Add(Begin(0));
                AddText(items, first.Value, StyleKind.CData, 0);
                items.Add(End(0));
                items.Add(Fixed("]]>", StyleKind.Punctuation, -1));
                break;
            case NodeKind.XmlComment:
                items.Add(Fixed("<!--", StyleKind.Punctuation, -1));
                items.Add(Begin(0));
                AddText(items, first.Value, StyleKind.Comment, 0);
                items.Add(End(0));
                items.Add(Fixed("-->", StyleKind.Punctuation, -1));
                break;
            case NodeKind.XmlProcessingInstruction:
                items.Add(Fixed("<?", StyleKind.Punctuation, -1));
                items.Add(Begin(0));
                AddText(items, first.Value, StyleKind.ElementName, 0);
                items.Add(End(0));
                if (node.Parts[1].Value.Length > 0)
                {
                    items.Add(Fixed(" ", StyleKind.Punctuation, -1));
                }

                items.Add(Begin(1));
                AddText(items, node.Parts[1].Value, StyleKind.Text, 1);
                items.Add(End(1));
                items.Add(Fixed("?>", StyleKind.Punctuation, -1));
                break;
            case NodeKind.XmlDeclaration:
            case NodeKind.XmlDoctype:
                items.Add(Begin(0));
                AddText(items, first.Value, StyleKind.ReadOnly, 0);
                items.Add(End(0));
                break;
            default:
                throw new ArgumentException("The node is not an XML row.", nameof(row));
        }

        return items;
    }

    // ----- XML elements -----

    /// <summary>
    /// An element's start row: <c>&lt;name attrs&gt;</c>, with the text and the closing tag of a text-only element, or an
    /// ellipsis (<c>&#x2026;</c>) and the closing tag of a collapsed one. The attributes wrap: an attribute that does not fit goes to a new line, aligned
    /// under the first attribute's name, and the tail follows the same rule.
    /// </summary>
    private static RowLines LayoutElement(Row row, bool collapsed, int indent, int width)
    {
        DocNode node = row.Node;
        IReadOnlyList<DocPart> parts = node.Parts;
        bool collapsedNow = collapsed && DocRows.IsCollapsible(node);
        int textIndex = IndexOf(parts, PartKind.Text);
        bool selfClosing = textIndex < 0 && node.EndParts.Count == 0;
        string name = parts[0].Value;

        List<Item> head = FoldItems(row, collapsed);
        head.Add(Fixed("<", StyleKind.Punctuation, -1));
        head.Add(Begin(0));
        AddText(head, name, StyleKind.ElementName, 0);
        head.Add(End(0));

        var units = new List<Unit>();
        for (int p = 1; p < parts.Count; p++)
        {
            if (parts[p].Kind != PartKind.AttributeName)
            {
                continue;
            }

            int value = ValueIndexOf(parts, parts[p].AttributeIndex);
            var items = new List<Item>();
            items.Add(Begin(p));
            AddText(items, parts[p].Value, StyleKind.AttributeName, p);
            items.Add(End(p));
            items.Add(Fixed("=\"", StyleKind.Punctuation, -1));
            items.Add(Begin(value));
            AddText(items, parts[value].Value, StyleKind.AttributeValue, value);
            items.Add(End(value));
            items.Add(Fixed("\"", StyleKind.Punctuation, -1));
            AddRaw(items, parts[value]);
            units.Add(new Unit(items, true));
        }

        if (textIndex >= 0)
        {
            units.Add(new Unit([Fixed(">", StyleKind.Punctuation, -1)], false));
            var text = new List<Item>();
            text.Add(Begin(textIndex));
            AddText(text, parts[textIndex].Value, StyleKind.Text, textIndex);
            text.Add(End(textIndex));
            AddRaw(text, parts[textIndex]);
            units.Add(new Unit(text, false));
            units.Add(new Unit(ClosingTag(name), false));
        }
        else if (selfClosing)
        {
            units.Add(new Unit([Fixed("/>", StyleKind.Punctuation, -1)], false));
        }
        else if (collapsedNow)
        {
            units.Add(new Unit([Fixed(">", StyleKind.Punctuation, -1)], false));
            units.Add(new Unit([Fixed(Ellipsis, StyleKind.Glyph, -1)], false));
            units.Add(new Unit(ClosingTag(name), false));
        }
        else
        {
            units.Add(new Unit([Fixed(">", StyleKind.Punctuation, -1)], false));
        }

        int headColumn = indent + Sum(head);
        int alignColumn = headColumn + 1;
        int continuation = width - alignColumn < MinAlignedColumns ? indent + FallbackContinuationExtra : alignColumn;
        if (headColumn > width || continuation >= width)
        {
            // Too narrow to wrap: the whole row is one line, cut at the width.
            var all = new List<Item>(head);
            foreach (Unit unit in units)
            {
                if (unit.Separated)
                {
                    all.Add(Fixed(" ", StyleKind.Punctuation, -1));
                }

                all.AddRange(unit.Items);
            }

            return SingleLine(indent, all, parts.Count, width);
        }

        var lines = new List<OutLine> { new(indent, head) };
        OutLine current = lines[0];
        int column = headColumn;
        bool firstLine = true;
        bool hasUnit = false;
        for (int u = 0; u < units.Count; u++)
        {
            Unit unit = units[u];
            bool separate = unit.Separated && (firstLine || hasUnit);
            int needed = (separate ? 1 : 0) + Sum(unit.Items);
            if (column + needed <= width)
            {
                if (separate)
                {
                    current.Items.Add(Fixed(" ", StyleKind.Punctuation, -1));
                }

                current.Items.AddRange(unit.Items);
                column += needed;
                hasUnit = true;
                continue;
            }

            if (hasUnit || firstLine)
            {
                // The unit goes to a new line, aligned under the first attribute.
                current = new OutLine(continuation, []);
                lines.Add(current);
                column = continuation;
                firstLine = false;
                hasUnit = false;
                if (column + Sum(unit.Items) <= width)
                {
                    current.Items.AddRange(unit.Items);
                    column += Sum(unit.Items);
                    hasUnit = true;
                    continue;
                }
            }

            // Wider than a whole line: the unit is cut with an ellipsis, and the rest of the row is dropped. The parts that
            // follow are kept as zero-width ranges at the cut.
            int cut = FindCut(unit.Items, column, width - 1);
            current.Items.AddRange(unit.Items.Take(cut));
            current.Items.AddRange(Markers(unit.Items.Skip(cut)));
            for (int later = u + 1; later < units.Count; later++)
            {
                current.Items.AddRange(Markers(units[later].Items));
            }

            current.Items.Add(Fixed(Ellipsis, StyleKind.Glyph, -1));
            return Finish(lines, parts.Count);
        }

        return Finish(lines, parts.Count);
    }

    private static List<Item> ClosingTag(string name)
    {
        var items = new List<Item> { Fixed("</", StyleKind.Punctuation, -1) };
        AddText(items, name, StyleKind.ElementName, -1);
        items.Add(Fixed(">", StyleKind.Punctuation, -1));
        return items;
    }

    private static int IndexOf(IReadOnlyList<DocPart> parts, PartKind kind)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Kind == kind)
            {
                return i;
            }
        }

        return -1;
    }

    private static int ValueIndexOf(IReadOnlyList<DocPart> parts, int attributeIndex)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Kind == PartKind.AttributeValue && parts[i].AttributeIndex == attributeIndex)
            {
                return i;
            }
        }

        throw new ArgumentException("An attribute has no value part.", nameof(parts));
    }

    // ----- Lines and cuts -----

    /// <summary>
    /// One line, cut at the width. The line keeps the indent and the cells that fit. When the width leaves room for the
    /// indent, the fold and one more column, the cut ends with an ellipsis. Otherwise the line is cut at the width, so a narrow
    /// view still shows the fold as far as it goes.
    /// </summary>
    private static RowLines SingleLine(int indent, List<Item> items, int partCount, int width)
    {
        int total = indent + Sum(items);
        if (total <= width)
        {
            return Finish([new OutLine(indent, items)], partCount);
        }

        bool ellipsis = width >= indent + FoldColumns + 1;
        int limit = ellipsis ? width - 1 : width;
        if (indent > limit)
        {
            return Finish([new OutLine(limit, Markers(items).ToList())], partCount);
        }

        int cut = FindCut(items, indent, limit);
        var kept = new List<Item>(items.Take(cut));
        kept.AddRange(Markers(items.Skip(cut)));
        if (ellipsis)
        {
            kept.Add(Fixed(Ellipsis, StyleKind.Glyph, -1));
        }

        return Finish([new OutLine(indent, kept)], partCount);
    }

    /// <summary>
    /// The index of the first cell that does not fit in <paramref name="limit"/> columns when the items start at
    /// <paramref name="startColumn"/>, or the item count when all of them fit. Zero-width cells never stop the scan.
    /// </summary>
    private static int FindCut(IReadOnlyList<Item> items, int startColumn, int limit)
    {
        int column = startColumn;
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            if (item.Kind != ItemKind.Cell || item.Width == 0)
            {
                continue;
            }

            if (column + item.Width > limit)
            {
                return i;
            }

            column += item.Width;
        }

        return items.Count;
    }

    private static IEnumerable<Item> Markers(IEnumerable<Item> items)
    {
        return items.Where(item => item.Kind != ItemKind.Cell);
    }

    private static int Sum(IEnumerable<Item> items)
    {
        int total = 0;
        foreach (Item item in items)
        {
            total += item.Width;
        }

        return total;
    }

    /// <summary>Builds the segments and the part ranges from the lines. Each part has exactly one start and one end marker.</summary>
    private static RowLines Finish(IReadOnlyList<OutLine> lines, int partCount)
    {
        var begins = new (int Line, int Column)?[partCount];
        var ends = new (int Line, int Column)?[partCount];
        var displayLines = new List<DisplayLine>(lines.Count);
        for (int line = 0; line < lines.Count; line++)
        {
            int column = lines[line].Indent;
            var segments = new SegmentBuilder();
            foreach (Item item in lines[line].Items)
            {
                switch (item.Kind)
                {
                    case ItemKind.Cell:
                        segments.Add(item.Text, item.Style, item.Part);
                        column += item.Width;
                        break;
                    case ItemKind.Begin:
                        begins[item.Part] = (line, column);
                        break;
                    default:
                        ends[item.Part] = (line, column);
                        break;
                }
            }

            displayLines.Add(new DisplayLine(lines[line].Indent, segments.Finish()));
        }

        var ranges = new PartRange[partCount];
        for (int p = 0; p < partCount; p++)
        {
            (int Line, int Column) begin = begins[p] ?? (0, 0);
            (int Line, int Column) end = ends[p] ?? begin;
            ranges[p] = new PartRange(begin.Line, begin.Column, end.Column);
        }

        return new RowLines(displayLines, ranges);
    }

    /// <summary>Joins the consecutive cells that have the same style and part into one segment.</summary>
    private sealed class SegmentBuilder
    {
        private readonly List<Segment> _segments = [];
        private readonly StringBuilder _text = new();
        private StyleKind _style;
        private int _part;
        private bool _open;

        public void Add(string text, StyleKind style, int part)
        {
            if (_open && (style != _style || part != _part))
            {
                Flush();
            }

            _style = style;
            _part = part;
            _open = true;
            _text.Append(text);
        }

        public IReadOnlyList<Segment> Finish()
        {
            if (_open)
            {
                Flush();
            }

            return _segments;
        }

        private void Flush()
        {
            _segments.Add(new Segment(_text.ToString(), _style, _part));
            _text.Clear();
            _open = false;
        }
    }
}
