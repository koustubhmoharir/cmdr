using System.Collections.Immutable;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax.Inlines;

namespace Mdedit.Core.Render;

/// <summary>
/// Lowers the inline content of one block to runs, following the mapping table of the render core: each inline type gives
/// its cells and their source offsets. The inline style flags accumulate down the tree; delimiters, brackets and link
/// targets are not cells.
/// </summary>
/// <remarks>
/// Offsets are read from the Markdig spans, which are inclusive at their end, and aligned with the produced text by
/// <see cref="Alignment"/>. The text of the source is only read, never rewritten.
/// </remarks>
internal sealed class InlineLowering
{
    private readonly string _text;
    private readonly int _blockStart;
    private readonly List<InlineSpan> _spans = [];
    private RunBuilder _builder;
    private int _nextLinkId;

    /// <summary>Starts lowering a block.</summary>
    /// <param name="text">The whole source text.</param>
    /// <param name="blockStart">The absolute source offset of the block, which the offsets are made relative to.</param>
    public InlineLowering(string text, int blockStart)
    {
        _text = text;
        _blockStart = blockStart;
        _builder = new RunBuilder(blockStart);
    }

    /// <summary>
    /// Lowers <paramref name="inlines"/> to runs of the given role. An empty or missing container gives one empty run. Link ids
    /// keep counting across calls, so a block lowered in several calls (the cells of a table) has unique ids.
    /// </summary>
    /// <param name="inlines">The inline content of a paragraph, heading or table cell; null when there is none.</param>
    /// <param name="role">The role of the text cells: <see cref="StyleRole.Body"/> or <see cref="StyleRole.Heading"/>.</param>
    /// <param name="headingLevel">The heading level for heading cells; 0 otherwise.</param>
    /// <param name="flags">The inline styles every cell starts with, such as <see cref="InlineStyle.Strong"/> for a table's header cells.</param>
    public ImmutableArray<Run> Lower(ContainerInline? inlines, StyleRole role, int headingLevel, InlineStyle flags = InlineStyle.None)
    {
        _builder = new RunBuilder(_blockStart);
        if (inlines is not null)
        {
            Children(inlines, new State(role, flags, headingLevel, -1));
        }

        return _builder.Finish();
    }

    /// <summary>
    /// The inline containers recorded so far, outermost first in document order, with offsets relative to the block. Lowering a
    /// block in several calls (the cells of a table) adds to the same list.
    /// </summary>
    public ImmutableArray<InlineSpan> Spans => _spans.ToImmutableArray();

    /// <summary>The state carried down the inline tree.</summary>
    private readonly record struct State(StyleRole Role, InlineStyle Flags, int HeadingLevel, int LinkId)
    {
        public CellStyle Style(StyleRole role) => new(role, Flags, HeadingLevel);

        public State With(InlineStyle flags) => this with { Flags = Flags | flags };
    }

    private void Children(ContainerInline container, State state)
    {
        bool afterTaskBox = false;
        foreach (Inline child in container)
        {
            if (child is LiteralInline literal && afterTaskBox)
            {
                Literal(literal, state, skipLeadingSpace: true);
            }
            else
            {
                Inline(child, state);
            }

            afterTaskBox = child is TaskList;
        }
    }

    private void Inline(Inline inline, State state)
    {
        switch (inline)
        {
            case LiteralInline literal:
                Literal(literal, state, skipLeadingSpace: false);
                break;

            case LineBreakInline lineBreak when lineBreak.IsHard:
                _builder.HardBreak(lineBreak.Span.Start);
                break;

            case LineBreakInline lineBreak:
                // A soft break is one space, drawn where the line ends in the source.
                _builder.AddText(" ", [lineBreak.Span.Start], state.Style(state.Role), state.LinkId);
                break;

            case HtmlEntityInline entity:
                Entity(entity, state);
                break;

            case CodeInline code:
                Code(code, state);
                break;

            case EmphasisInline emphasis:
                Delimited(emphasis.Span.Start, emphasis.Span.End, emphasis.DelimiterCount, EmphasisKind(emphasis));
                Children(emphasis, state.With(EmphasisFlags(emphasis)));
                break;

            case LinkInline link when link.IsImage:
                Bracketed(link.Span.Start, link.Span.End, InlineSpanKind.Image, link.Span.Start + 1);
                _builder.AddSynthetic("[image] ", new CellStyle(StyleRole.ImageMarker, state.Flags, state.HeadingLevel));
                Children(link, state.With(InlineStyle.Image));
                break;

            case LinkInline link when link.IsAutoLink:
                // A bare URL is an autolink: its text is the source, and it is not recorded.
                Children(link, state with { Flags = state.Flags | InlineStyle.Link, LinkId = _nextLinkId++ });
                break;

            case LinkInline link:
                Bracketed(link.Span.Start, link.Span.End, InlineSpanKind.Link, link.Span.Start);
                Children(link, state with { Flags = state.Flags | InlineStyle.Link, LinkId = _nextLinkId++ });
                break;

            case AutolinkInline autolink:
                // The address as written between the angle brackets; the source is the URL, which avoids Markdig's "mailto:" prefix.
                Source(autolink.Span.Start + 1, autolink.Span.End, state with { Flags = state.Flags | InlineStyle.Link, LinkId = _nextLinkId++ }, StyleRole.Body);
                break;

            case HtmlInline html:
                Source(html.Span.Start, html.Span.End + 1, state, StyleRole.Html);
                break;

            case TaskList task:
                TaskBox(task, state);
                break;

            case ContainerInline container:
                Children(container, state);
                break;

            default:
                // Anything the mapping does not list renders its source span as plain text, one for one.
                Source(inline.Span.Start, inline.Span.End + 1, state, state.Role);
                break;
        }
    }

    private void Literal(LiteralInline literal, State state, bool skipLeadingSpace)
    {
        string content = literal.Content.ToString();
        if (skipLeadingSpace && content.StartsWith(' '))
        {
            // The space after a task box is the one the layout draws, so the source space is not a cell.
            content = content[1..];
        }

        if (content.Length == 0)
        {
            return;
        }

        AlignedText aligned = Alignment.Align(_text, literal.Span.Start, literal.Span.End + 1, content, escapes: true);
        _builder.AddText(aligned.Text, aligned.Offsets, state.Style(state.Role), state.LinkId);
    }

    private void Entity(HtmlEntityInline entity, State state)
    {
        string text = entity.Transcoded.ToString();
        if (text.Length == 0)
        {
            return;
        }

        // Every cell of an entity stands for the whole entity, so each takes the offset of its first character, the ampersand.
        int[] offsets = new int[text.Length];
        Array.Fill(offsets, entity.Span.Start);
        _builder.AddText(text, offsets, state.Style(state.Role), state.LinkId);
    }

    private void Code(CodeInline code, State state)
    {
        // The content sits between the delimiters; Markdig's content has the stripped padding removed, and the aligner
        // skips the padding that is left in the source.
        int start = code.Span.Start + code.DelimiterCount;
        int end = code.Span.End + 1 - code.DelimiterCount;
        Delimited(code.Span.Start, code.Span.End, code.DelimiterCount, InlineSpanKind.Code);
        string content = code.Content;
        if (content.Length == 0)
        {
            return;
        }

        AlignedText aligned = Alignment.Align(_text, start, end, content, escapes: false);
        _builder.AddText(aligned.Text, aligned.Offsets, state.Style(state.Role) with { Inline = state.Flags | InlineStyle.Code }, state.LinkId);
    }

    private void TaskBox(TaskList task, State state)
    {
        // The box is the text as written, "[ ]" or "[x]"; the space after it is synthetic.
        Source(task.Span.Start, task.Span.End + 1, state, StyleRole.TaskBox);
        _builder.AddSynthetic(" ", new CellStyle(StyleRole.Body, state.Flags, state.HeadingLevel));
    }

    /// <summary>Adds the source between two absolute offsets as cells of one role, one for one.</summary>
    private void Source(int start, int end, State state, StyleRole role)
    {
        if (end <= start)
        {
            return;
        }

        int[] offsets = new int[end - start];
        for (int k = 0; k < offsets.Length; k++)
        {
            offsets[k] = start + k;
        }

        _builder.AddText(_text[start..end], offsets, state.Style(role), state.LinkId);
    }

    /// <summary>
    /// Records a container whose opener and closer are delimiters of <paramref name="count"/> characters each, as the span of the
    /// whole construct gives them.
    /// </summary>
    /// <param name="spanStart">The offset of the construct's first character.</param>
    /// <param name="spanEnd">The offset of its last character (inclusive).</param>
    /// <param name="count">The number of delimiter characters on each side.</param>
    /// <param name="kind">The kind of the container.</param>
    private void Delimited(int spanStart, int spanEnd, int count, InlineSpanKind kind)
    {
        int outerStart = spanStart;
        int outerEnd = spanEnd + 1;
        RecordSpan(kind, outerStart, outerEnd, outerStart + count, outerEnd - count);
    }

    /// <summary>
    /// Records a link or an image: the opener runs from the span's start to just after the <c>[</c> at
    /// <paramref name="bracket"/>, and the closer from the <c>]</c> that matches it to the span's end, as written.
    /// </summary>
    /// <param name="spanStart">The offset of the construct's first character.</param>
    /// <param name="spanEnd">The offset of its last character (inclusive).</param>
    /// <param name="kind">Link or image.</param>
    /// <param name="bracket">The index of the <c>[</c> that opens the text.</param>
    private void Bracketed(int spanStart, int spanEnd, InlineSpanKind kind, int bracket)
    {
        int outerStart = spanStart;
        int outerEnd = spanEnd + 1;
        int innerStart = bracket + 1;
        int close = MatchingClose(innerStart, outerEnd);
        RecordSpan(kind, outerStart, outerEnd, innerStart, close < 0 ? outerEnd : close);
    }

    /// <summary>Adds a span to the list, with its opener and closer read from the source; the bounds are absolute.</summary>
    private void RecordSpan(InlineSpanKind kind, int outerStart, int outerEnd, int innerStart, int innerEnd)
    {
        innerStart = Math.Clamp(innerStart, outerStart, outerEnd);
        innerEnd = Math.Clamp(innerEnd, innerStart, outerEnd);
        _spans.Add(new InlineSpan(
            kind,
            outerStart - _blockStart,
            outerEnd - _blockStart,
            innerStart - _blockStart,
            innerEnd - _blockStart,
            _text[outerStart..innerStart],
            _text[innerEnd..outerEnd]));
    }

    /// <summary>
    /// The index of the <c>]</c> that closes a link's text, from <paramref name="from"/> (just after its <c>[</c>) to
    /// <paramref name="end"/>. Brackets inside nested brackets balance, and backslash escapes and code spans are skipped. Returns
    /// -1 when there is none.
    /// </summary>
    private int MatchingClose(int from, int end)
    {
        int depth = 1;
        int index = from;
        while (index < end)
        {
            char c = _text[index];
            if (c == '\\' && index + 1 < end)
            {
                index += 2;
                continue;
            }

            if (c == '`')
            {
                int run = BacktickRun(index, end);
                int close = FindBackticks(index + run, end, run);
                index = close < 0 ? index + run : close + run;
                continue;
            }

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']' && --depth == 0)
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    /// <summary>The index of the first backtick of a run of exactly <paramref name="count"/> backticks at or after <paramref name="from"/>, or -1.</summary>
    private int FindBackticks(int from, int end, int count)
    {
        int index = from;
        while (index < end)
        {
            if (_text[index] != '`')
            {
                index++;
                continue;
            }

            int run = BacktickRun(index, end);
            if (run == count)
            {
                return index;
            }

            index += run;
        }

        return -1;
    }

    /// <summary>The number of consecutive backticks starting at <paramref name="index"/>, not past <paramref name="end"/>.</summary>
    private int BacktickRun(int index, int end)
    {
        int run = 0;
        while (index + run < end && _text[index + run] == '`')
        {
            run++;
        }

        return run;
    }

    /// <summary>The span kind of an emphasis: strikethrough for <c>~</c>, strong for two delimiters, emphasis for one.</summary>
    private static InlineSpanKind EmphasisKind(EmphasisInline emphasis)
    {
        if (emphasis.DelimiterChar == '~')
        {
            return InlineSpanKind.Strikethrough;
        }

        return emphasis.DelimiterCount >= 2 ? InlineSpanKind.Strong : InlineSpanKind.Emphasis;
    }

    private static InlineStyle EmphasisFlags(EmphasisInline emphasis)
    {
        if (emphasis.DelimiterChar == '~')
        {
            return InlineStyle.Strikethrough;
        }

        return emphasis.DelimiterCount >= 2 ? InlineStyle.Strong : InlineStyle.Emphasis;
    }
}
