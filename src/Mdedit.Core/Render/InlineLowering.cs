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
    private readonly RunBuilder _builder;
    private int _nextLinkId;

    /// <summary>Starts lowering a block.</summary>
    /// <param name="text">The whole source text.</param>
    /// <param name="blockStart">The absolute source offset of the block, which the offsets are made relative to.</param>
    public InlineLowering(string text, int blockStart)
    {
        _text = text;
        _builder = new RunBuilder(blockStart);
    }

    /// <summary>Lowers <paramref name="inlines"/> to runs of the given role. An empty or missing container gives one empty run.</summary>
    /// <param name="inlines">The inline content of a paragraph or heading; null when there is none.</param>
    /// <param name="role">The role of the text cells: <see cref="StyleRole.Body"/> or <see cref="StyleRole.Heading"/>.</param>
    /// <param name="headingLevel">The heading level for heading cells; 0 otherwise.</param>
    public ImmutableArray<Run> Lower(ContainerInline? inlines, StyleRole role, int headingLevel)
    {
        if (inlines is not null)
        {
            Children(inlines, new State(role, InlineStyle.None, headingLevel, -1));
        }

        return _builder.Finish();
    }

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
                Children(emphasis, state.With(EmphasisFlags(emphasis)));
                break;

            case LinkInline link when link.IsImage:
                _builder.AddSynthetic("[image] ", new CellStyle(StyleRole.ImageMarker, state.Flags, state.HeadingLevel));
                Children(link, state.With(InlineStyle.Image));
                break;

            case LinkInline link:
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

    private static InlineStyle EmphasisFlags(EmphasisInline emphasis)
    {
        if (emphasis.DelimiterChar == '~')
        {
            return InlineStyle.Strikethrough;
        }

        return emphasis.DelimiterCount >= 2 ? InlineStyle.Strong : InlineStyle.Emphasis;
    }
}
