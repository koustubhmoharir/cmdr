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
/// Code, HTML and table blocks take their lines from the source; a block mdedit has no rendering for is a <see cref="BlockKind.Raw"/>
/// block of its source lines, so that no source is lost. An alert whose kind is not one of the five is lowered as a plain
/// quote whose first line is its marker as written. An empty list item gets an empty paragraph that carries its marker.
/// </remarks>
internal sealed class Lowerer
{
    private readonly string _text;
    private readonly List<RenderBlock> _blocks = [];
    private readonly List<Frame> _frames = [];
    private int _leafCount;

    /// <summary>Starts lowering the document with the given source text.</summary>
    /// <param name="text">The source text the AST was parsed from.</param>
    public Lowerer(string text)
    {
        _text = text;
    }

    /// <summary>Lowers the document and returns its leaf blocks in document order.</summary>
    /// <param name="document">The parsed document.</param>
    public ImmutableArray<RenderBlock> Lower(MarkdownDocument document)
    {
        Children(document);
        return _blocks.ToImmutableArray();
    }

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
                Add(BlockKind.Paragraph, start, end, Inlines(paragraph.Inline, start, StyleRole.Body, 0));
                break;

            case HeadingBlock heading:
                Add(BlockKind.Heading, start, end, Inlines(heading.Inline, start, StyleRole.Heading, heading.Level), heading.Level);
                break;

            case FencedCodeBlock fenced:
                Add(BlockKind.Code, start, end, CodeLines(fenced, start), language: LanguageOf(fenced.Info));
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

            case Table:
                Add(BlockKind.Table, start, end, SourceLines(start, end, new CellStyle(StyleRole.Body)));
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

    /// <summary>Adds a leaf block with the containers it sits in, and counts it as the next leaf.</summary>
    private void Add(BlockKind kind, int start, int end, ImmutableArray<Run> runs, int headingLevel = 0, string? language = null)
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

        _blocks.Add(new RenderBlock(kind, start, end, chain, runs, headingLevel, language, tight, firstInItem));
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

    private ImmutableArray<Run> Inlines(ContainerInline? inlines, int start, StyleRole role, int headingLevel) =>
        new InlineLowering(_text, start).Lower(inlines, role, headingLevel);

    private ImmutableArray<Run> CodeLines(CodeBlock code, int start) =>
        LineRuns(code, start, new CellStyle(StyleRole.CodeBlock));

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

    private static string? LanguageOf(string? info) => string.IsNullOrEmpty(info) ? null : info;

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
