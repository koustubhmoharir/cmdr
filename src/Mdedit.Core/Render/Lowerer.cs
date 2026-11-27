using System.Collections.Immutable;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Mdedit.Core.Render;

/// <summary>
/// Walks the Markdig AST and lowers it to leaf blocks in document order. Container blocks become entries of a stack, which
/// each leaf copies into its <see cref="RenderBlock.Containers"/>, with the first-leaf flags worked out as the walk goes.
/// </summary>
/// <remarks>
/// Code and HTML blocks take their lines from the source, and a table takes its cells from its inlines; a block mdedit has no
/// rendering for is a <see cref="BlockKind.Raw"/> block of its source lines, so that no source is lost. An alert whose kind is not one of the five is lowered as a plain
/// quote whose first line is its marker as written. An empty list item gets an empty paragraph that carries its marker.
/// </remarks>
internal sealed class Lowerer
{
    private readonly string _text;
    private readonly ICodeColorizer? _colorizer;
    private readonly List<RenderBlock> _blocks = [];
    private readonly List<Frame> _frames = [];
    private readonly SortedSet<string> _labels = new(StringComparer.Ordinal);
    private int _leafCount;

    /// <summary>Starts lowering the document with the given source text.</summary>
    /// <param name="text">The source text the AST was parsed from.</param>
    /// <param name="colorizer">The colorizer for the code blocks that have a language, or null for no colouring.</param>
    public Lowerer(string text, ICodeColorizer? colorizer = null)
    {
        _text = text;
        _colorizer = colorizer;
    }

    /// <summary>Lowers the document and returns its leaf blocks in document order.</summary>
    /// <param name="document">The parsed document.</param>
    public ImmutableArray<RenderBlock> Lower(MarkdownDocument document)
    {
        Children(document);
        return _blocks.ToImmutableArray();
    }

    /// <summary>
    /// The normalized labels of the document's link reference definitions, sorted and joined with line feeds, empty when there
    /// are none. Valid after <see cref="Lower"/>.
    /// </summary>
    public string ReferenceKey => string.Join('\n', _labels);

    /// <summary>One open container on the walk: its entry, how many leaves had been emitted when it opened, and for a list item whether its list is loose.</summary>
    private readonly record struct Frame(Container Entry, int LeavesAtEntry, bool ListIsLoose);

    private void Children(ContainerBlock container)
    {
        foreach (Block block in container)
        {
            Visit(block);
        }
    }

    private void Visit(Block block)
    {
        if (block is LinkReferenceDefinitionGroup group)
        {
            // Markdig normalizes the labels; a link anywhere in the document can resolve to one of them.
            foreach (LinkReferenceDefinition definition in group.Links.Values)
            {
                if (definition.Label is { } label)
                {
                    _labels.Add(label);
                }
            }
        }

        int start = block.Span.Start;
        int end = block.Span.End < block.Span.Start ? start : block.Span.End + 1;

        switch (block)
        {
            case AlertBlock alert when AlertKindOf(alert.Kind.ToString()) is { } kind:
                Open(Container.ForAlert(kind, isFirstLeaf: false), listIsLoose: false);
                Children(alert);
                Close();
                break;

            case AlertBlock alert:
                // A kind mdedit does not know: Markdig has removed the marker from the content, so the marker line is
                // shown as a leaf of its own, as written, before the content.
                Open(Container.Quote(isFirstLeaf: false), listIsLoose: false);
                MarkerLine(alert);
                Children(alert);
                Close();
                break;

            case QuoteBlock quote:
                Open(Container.Quote(isFirstLeaf: false), listIsLoose: false);
                Children(quote);
                Close();
                break;

            case ListBlock list:
                foreach (Block item in list)
                {
                    if (item is ListItemBlock listItem)
                    {
                        Item(list, listItem);
                    }
                }

                break;

            case ParagraphBlock paragraph:
                AddInlines(BlockKind.Paragraph, paragraph.Inline, start, end, StyleRole.Body, 0);
                break;

            case HeadingBlock heading:
                AddInlines(BlockKind.Heading, heading.Inline, start, end, StyleRole.Heading, heading.Level);
                break;

            case FencedCodeBlock fenced:
                string? language = CodeLanguages.FirstWord(fenced.Info);
                string? languageId = CodeLanguages.Normalize(language);
                Add(BlockKind.Code, start, end, ColoredLines(CodeLines(fenced, start), languageId), language: language, languageId: languageId);
                break;

            case CodeBlock code:
                Add(BlockKind.Code, start, end, CodeLines(code, start));
                break;

            case HtmlBlock html:
                Add(BlockKind.Html, start, end, LineRuns(html, start, new CellStyle(StyleRole.Html)));
                break;

            case ThematicBreakBlock:
                Add(BlockKind.ThematicBreak, start, end, ImmutableArray<Run>.Empty);
                break;

            case Table table:
                AddTable(table, start, end);
                break;

            default:
                // A block with no rendering: shown as its source, dim as written.
                Add(BlockKind.Raw, start, end, SourceLines(start, end, new CellStyle(StyleRole.Html)));
                break;
        }
    }

    private void Item(ListBlock list, ListItemBlock item)
    {
        Container entry;
        if (list.IsOrdered)
        {
            (int markerStart, string marker) = OrderedMarker(item.Span.Start);
            entry = Container.Ordered(markerStart, marker, isFirstLeaf: false);
        }
        else
        {
            entry = Container.Bullet(BulletDepth(), isFirstLeaf: false);
        }

        Open(entry, list.IsLoose);
        int leavesBefore = _leafCount;
        Children(item);
        if (_leafCount == leavesBefore)
        {
            // An empty item has no leaf to carry its marker, so it gets an empty paragraph just after the marker.
            int afterMarker = item.Span.Start + (list.IsOrdered ? entry.Marker!.Length : 1);
            Add(BlockKind.Paragraph, afterMarker, afterMarker, ImmutableArray.Create(new Run(ImmutableArray<Cell>.Empty, 0)));
        }

        Close();
    }

    /// <summary>Adds the marker line of an alert whose kind is unknown, as written, as a leaf paragraph of body cells.</summary>
    private void MarkerLine(AlertBlock alert)
    {
        int markerStart = _text.IndexOf("[!", alert.Span.Start, StringComparison.Ordinal);
        int close = markerStart < 0 ? -1 : _text.IndexOf(']', markerStart);
        if (close < 0 || close >= _text.Length || _text.AsSpan(markerStart, close - markerStart).Contains('\n'))
        {
            return;
        }

        int end = close + 1;
        Add(BlockKind.Paragraph, markerStart, end, ImmutableArray.Create(RunBuilder.Line(_text, markerStart, end, new CellStyle(StyleRole.Body), markerStart)));
    }

    private void Open(Container entry, bool listIsLoose)
    {
        _frames.Add(new Frame(entry, _leafCount, listIsLoose));
    }

    private void Close()
    {
        _frames.RemoveAt(_frames.Count - 1);
    }

    private int BulletDepth()
    {
        int depth = 0;
        foreach (Frame frame in _frames)
        {
            if (frame.Entry.Kind == ContainerKind.BulletItem)
            {
                depth++;
            }
        }

        return depth;
    }

    /// <summary>
    /// Adds a pipe table: its model, and its cells as the block's runs, in row order. Every cell of the table is lowered by one
    /// inline lowering, so that link ids are unique within the block.
    /// </summary>
    private void AddTable(Table table, int start, int end)
    {
        var inlines = new InlineLowering(_text, start);
        TableModel model = LowerTable(table, start, end, inlines);
        Add(BlockKind.Table, start, end, model.Rows.SelectMany(row => row.Cells).ToImmutableArray(), table: model, spans: inlines.Spans, links: inlines.Links);
    }

    /// <summary>Adds a paragraph or heading: its inline content lowered to runs, and the inline containers recorded while doing so.</summary>
    private void AddInlines(BlockKind kind, ContainerInline? content, int start, int end, StyleRole role, int headingLevel)
    {
        var inlines = new InlineLowering(_text, start);
        ImmutableArray<Run> runs = inlines.Lower(content, role, headingLevel);
        Add(kind, start, end, runs, headingLevel, spans: inlines.Spans, links: inlines.Links);
    }

    /// <summary>
    /// The model of a table, lowering its cells with <paramref name="inlines"/>, which records their inline containers. Markdig gives one row per source line after the delimiter row, so row <c>n</c> (n &gt; 0) is line
    /// <c>n + 1</c> of the block; the header is line 0. Each row has one run per column: the first cells of the row, then empty
    /// runs when it is short. Cells past the last column are not lowered.
    /// </summary>
    private TableModel LowerTable(Table table, int start, int end, InlineLowering inlines)
    {
        ImmutableArray<ColumnAlignment> alignments = table.ColumnDefinitions
            .Select(column => column.Alignment switch
            {
                TableColumnAlign.Center => ColumnAlignment.Center,
                TableColumnAlign.Right => ColumnAlignment.Right,
                _ => ColumnAlignment.Left,
            })
            .ToImmutableArray();
        ImmutableArray<int> lineStarts = LineStarts(start, end);
        var rows = ImmutableArray.CreateBuilder<TableRowModel>();

        int index = 0;
        foreach (Block block in table)
        {
            if (block is not TableRow row)
            {
                continue;
            }

            int line = index == 0 ? 0 : index + 1;
            int rowStart = lineStarts[Math.Min(line, lineStarts.Length - 1)];
            ImmutableArray<Run> cells = RowCells(row, alignments.Length, rowStart, inlines);
            rows.Add(new TableRowModel(row.IsHeader, rowStart, cells, CellPositions(row, alignments.Length, start + rowStart, start), ContentEnds(cells, start, end)));
            index++;
        }

        return new TableModel(alignments, rows.ToImmutable());
    }

    /// <summary>
    /// The source offset just after the last mapped character of each column's cell, relative to the block at <paramref name="start"/>, or
    /// <see cref="Cell.Synthetic"/> for a column with no mapped character. The last mapped cell's source length is taken from the text, so an
    /// entity or an escape ends where it ends in the source.
    /// </summary>
    /// <param name="cells">One run per column.</param>
    /// <param name="start">The absolute offset of the block.</param>
    /// <param name="end">The absolute offset of the block's end, exclusive.</param>
    private ImmutableArray<int> ContentEnds(ImmutableArray<Run> cells, int start, int end)
    {
        var ends = ImmutableArray.CreateBuilder<int>(cells.Length);
        foreach (Run run in cells)
        {
            int last = Array.FindLastIndex(run.Cells.ToArray(), cell => cell.IsMapped);
            ends.Add(last < 0 ? Cell.Synthetic : run.Cells[last].Offset + SourceLength.Of(_text, start, end, run, last));
        }

        return ends.ToImmutable();
    }

    /// <summary>
    /// The source position of each column's cell, relative to the block at <paramref name="start"/>: the cell's closing pipe, which is
    /// its end after its padding (for an empty cell, its place between its pipes); the line's end for the last column of a row with no
    /// trailing pipe. A column the row does not have is <see cref="Cell.Synthetic"/>. Markdig gives an empty cell no span, so the
    /// pipes are found in the row's line: a pipe is unescaped when no backslash precedes it, and the row has a leading pipe when its
    /// pipe count, less the trailing one, is one more than its cells' count less one (a cell is between two pipes, or the first and the
    /// line end).
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="columns">The table's columns.</param>
    /// <param name="lineStart">The absolute offset of the row's line.</param>
    /// <param name="start">The absolute offset of the block.</param>
    private ImmutableArray<int> CellPositions(TableRow row, int columns, int lineStart, int start)
    {
        int cells = row.OfType<TableCell>().Count();
        List<int> pipes = UnescapedPipes(lineStart, out int lineEnd);
        bool trailing = pipes.Count > 0 && pipes[^1] == LastNonSpace(lineStart, lineEnd);
        bool leading = pipes.Count - (trailing ? 1 : 0) - cells + 1 == 1;
        var positions = ImmutableArray.CreateBuilder<int>(columns);
        for (int column = 0; column < columns; column++)
        {
            if (column >= cells)
            {
                positions.Add(Cell.Synthetic);
                continue;
            }

            int index = leading ? column + 1 : column;
            positions.Add((index < pipes.Count ? pipes[index] : lineEnd) - start);
        }

        return positions.ToImmutable();
    }

    /// <summary>The offsets of the unescaped pipes of the line that starts at <paramref name="lineStart"/>, and the line's end.</summary>
    private List<int> UnescapedPipes(int lineStart, out int lineEnd)
    {
        var pipes = new List<int>();
        int index = lineStart;
        while (index < _text.Length && _text[index] is not ('\r' or '\n'))
        {
            if (_text[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (_text[index] == '|')
            {
                pipes.Add(index);
            }

            index++;
        }

        lineEnd = Math.Min(index, _text.Length);
        return pipes;
    }

    /// <summary>The offset of the last character of the line that is not a space or a tab, or -1 when there is none.</summary>
    private int LastNonSpace(int lineStart, int lineEnd)
    {
        for (int index = lineEnd - 1; index >= lineStart; index--)
        {
            if (_text[index] is not (' ' or '\t'))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The cells of a row, one run per column: the first <paramref name="columns"/> cells, padded with empty runs.</summary>
    private static ImmutableArray<Run> RowCells(TableRow row, int columns, int rowStart, InlineLowering inlines)
    {
        var runs = ImmutableArray.CreateBuilder<Run>(columns);
        foreach (Block block in row)
        {
            if (block is TableCell cell && runs.Count < columns)
            {
                runs.Add(CellRun(cell, row.IsHeader, rowStart, inlines));
            }
        }

        while (runs.Count < columns)
        {
            runs.Add(new Run(ImmutableArray<Cell>.Empty, rowStart));
        }

        return runs.ToImmutable();
    }

    /// <summary>
    /// The run of one cell: its paragraph's inlines, lowered with the usual mapping. Header cells are strong. Markdig's spans
    /// for a cell include the padding spaces around its text, which the inline mapping does not map.
    /// </summary>
    private static Run CellRun(TableCell cell, bool isHeader, int rowStart, InlineLowering inlines)
    {
        ContainerInline? content = cell.OfType<ParagraphBlock>().FirstOrDefault()?.Inline;
        ImmutableArray<Run> runs = inlines.Lower(content, StyleRole.Body, 0, isHeader ? InlineStyle.Strong : InlineStyle.None);
        return new Run(runs.SelectMany(run => run.Cells).ToImmutableArray(), rowStart);
    }

    /// <summary>
    /// The offsets, relative to <paramref name="start"/>, at which the lines of <c>text[start..end)</c> begin: 0 for the first line,
    /// then the one after each line break.
    /// </summary>
    private ImmutableArray<int> LineStarts(int start, int end)
    {
        var starts = ImmutableArray.CreateBuilder<int>();
        starts.Add(0);
        for (int index = start; index < end; index++)
        {
            if (_text[index] is '\r' or '\n')
            {
                if (_text[index] == '\r' && index + 1 < end && _text[index + 1] == '\n')
                {
                    index++;
                }

                starts.Add(index + 1 - start);
            }
        }

        return starts.ToImmutable();
    }

    /// <summary>Adds a leaf block with the containers it sits in, and counts it as the next leaf.</summary>
    private void Add(
        BlockKind kind,
        int start,
        int end,
        ImmutableArray<Run> runs,
        int headingLevel = 0,
        string? language = null,
        string? languageId = null,
        TableModel? table = null,
        ImmutableArray<InlineSpan> spans = default,
        ImmutableArray<LinkInfo> links = default)
    {
        ImmutableArray<Container> chain = Chain(start);

        bool tight = false;
        bool firstInItem = false;
        for (int index = _frames.Count - 1; index >= 0; index--)
        {
            Container entry = chain[index];
            if (entry.Kind is ContainerKind.BulletItem or ContainerKind.OrderedItem)
            {
                tight = !_frames[index].ListIsLoose;
                firstInItem = entry.IsFirstLeaf;
                break;
            }
        }

        _blocks.Add(new RenderBlock(kind, start, end, chain, runs, headingLevel, language, languageId, tight, firstInItem, table)
        {
            InlineSpans = spans.IsDefault ? ImmutableArray<InlineSpan>.Empty : spans,
            Links = links.IsDefault ? ImmutableArray<LinkInfo>.Empty : links,
        });
        _leafCount++;
    }

    private ImmutableArray<Container> Chain(int start)
    {
        var chain = ImmutableArray.CreateBuilder<Container>(_frames.Count);
        foreach (Frame frame in _frames)
        {
            Container entry = frame.Entry with { IsFirstLeaf = frame.LeavesAtEntry == _leafCount };
            if (entry.Kind == ContainerKind.OrderedItem)
            {
                entry = entry with { MarkerOffset = entry.MarkerOffset - start };
            }

            chain.Add(entry);
        }

        return chain.ToImmutable();
    }

    private ImmutableArray<Run> CodeLines(CodeBlock code, int start) =>
        LineRuns(code, start, new CellStyle(StyleRole.CodeBlock));

    /// <summary>
    /// The code runs with their tokens set. The lines are joined with line feeds and given to the colorizer once; each cell takes
    /// the token of the span that covers its first character. Without a colorizer, or a language, or when the colorizer returns
    /// null, the runs are returned as they are, every token <see cref="CodeToken.None"/>.
    /// </summary>
    /// <param name="runs">The runs of the code block, one per line, whose cells are the line's characters.</param>
    /// <param name="languageId">The normalized language, or null when the block has none.</param>
    private ImmutableArray<Run> ColoredLines(ImmutableArray<Run> runs, string? languageId)
    {
        if (_colorizer is null || languageId is null)
        {
            return runs;
        }

        string code = string.Join('\n', runs.Select(run => string.Concat(run.Cells.Select(cell => cell.Text))));
        IReadOnlyList<CodeSpan>? spans = _colorizer.Colorize(code, languageId);
        if (spans is null)
        {
            return runs;
        }

        var tokens = new CodeToken[code.Length];
        foreach (CodeSpan span in spans)
        {
            int to = Math.Min(code.Length, span.Start + span.Length);
            for (int index = Math.Max(0, span.Start); index < to; index++)
            {
                tokens[index] = span.Token;
            }
        }

        var colored = ImmutableArray.CreateBuilder<Run>(runs.Length);
        int position = 0;
        foreach (Run run in runs)
        {
            var cells = ImmutableArray.CreateBuilder<Cell>(run.Cells.Length);
            foreach (Cell cell in run.Cells)
            {
                cells.Add(cell with { Style = cell.Style with { Token = tokens[position] } });
                position += cell.Text.Length;
            }

            colored.Add(run with { Cells = cells.MoveToImmutable() });
            position++;
        }

        return colored.MoveToImmutable();
    }

    /// <summary>One run per line of a leaf block that has lines (code, HTML): each line is its content in the source.</summary>
    private ImmutableArray<Run> LineRuns(LeafBlock block, int start, CellStyle style)
    {
        StringLineGroup lines = block.Lines;
        var runs = ImmutableArray.CreateBuilder<Run>(lines.Count);
        for (int index = 0; index < lines.Count; index++)
        {
            StringSlice slice = lines.Lines[index].Slice;
            runs.Add(RunBuilder.Line(_text, slice.Start, slice.End + 1, style, start));
        }

        return runs.ToImmutable();
    }

    /// <summary>The source between <paramref name="start"/> and <paramref name="end"/>, one run per line (a table, or a block with no rendering yet).</summary>
    private ImmutableArray<Run> SourceLines(int start, int end, CellStyle style)
    {
        var runs = ImmutableArray.CreateBuilder<Run>();
        int lineStart = start;
        for (int index = start; index < end; index++)
        {
            if (_text[index] is '\r' or '\n')
            {
                runs.Add(RunBuilder.Line(_text, lineStart, index, style, start));
                if (_text[index] == '\r' && index + 1 < end && _text[index + 1] == '\n')
                {
                    index++;
                }

                lineStart = index + 1;
            }
        }

        runs.Add(RunBuilder.Line(_text, lineStart, end, style, start));
        return runs.ToImmutable();
    }

    /// <summary>
    /// The marker of an ordered item as written: its digits and delimiter, from the item's start in the source. Markdig's
    /// item number is not used because it is not the written number of every item.
    /// </summary>
    private (int Start, string Marker) OrderedMarker(int itemStart)
    {
        int index = itemStart;
        while (index < _text.Length && char.IsAsciiDigit(_text[index]))
        {
            index++;
        }

        if (index < _text.Length && _text[index] is '.' or ')')
        {
            index++;
        }

        return (itemStart, _text[itemStart..index]);
    }

    private static AlertKind? AlertKindOf(string kind) => kind.ToUpperInvariant() switch
    {
        "NOTE" => AlertKind.Note,
        "TIP" => AlertKind.Tip,
        "IMPORTANT" => AlertKind.Important,
        "WARNING" => AlertKind.Warning,
        "CAUTION" => AlertKind.Caution,
        _ => null,
    };
}
