using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The inline containers recorded on a block: kinds, outer and inner ranges (block-relative) and the delimiters as written.</summary>
public sealed class InlineSpanTests
{
    private static ImmutableArray<InlineSpan> Spans(string source) => RenderDocument.Parse(source).Blocks[0].InlineSpans;

    [Fact]
    public void Emphasis_records_its_delimiters_and_content()
    {
        ImmutableArray<InlineSpan> spans = Spans("*em*");

        InlineSpan span = Assert.Single(spans);
        Assert.Equal(new InlineSpan(InlineSpanKind.Emphasis, 0, 4, 1, 3, "*", "*"), span);
    }

    [Fact]
    public void Strong_and_strikethrough_are_their_own_kinds()
    {
        Assert.Equal(new InlineSpan(InlineSpanKind.Strong, 0, 8, 2, 6, "**", "**"), Assert.Single(Spans("**bold**")));
        Assert.Equal(new InlineSpan(InlineSpanKind.Strikethrough, 0, 6, 2, 4, "~~", "~~"), Assert.Single(Spans("~~st~~")));
    }

    [Fact]
    public void Code_span_records_the_backticks_as_written()
    {
        Assert.Equal(new InlineSpan(InlineSpanKind.Code, 0, 5, 2, 3, "``", "``"), Assert.Single(Spans("``c``")));
        Assert.Equal(new InlineSpan(InlineSpanKind.Code, 2, 7, 3, 6, "`", "`"), Assert.Single(Spans("x `abc` y")));
    }

    [Fact]
    public void Link_records_the_brackets_and_the_target_as_written_for_the_closer()
    {
        // "[click here](http://x)": the text is 1..11, the closer "](http://x)" runs from 11 to the end (22).
        InlineSpan span = Assert.Single(Spans("[click here](http://x)"));

        Assert.Equal(new InlineSpan(InlineSpanKind.Link, 0, 22, 1, 11, "[", "](http://x)"), span);
    }

    [Fact]
    public void Image_opener_is_the_bang_and_bracket()
    {
        InlineSpan span = Assert.Single(Spans("![alt](x.png)"));

        Assert.Equal(new InlineSpan(InlineSpanKind.Image, 0, 13, 2, 5, "![", "](x.png)"), span);
    }

    [Fact]
    public void Link_text_with_a_code_span_containing_a_bracket_still_closes_at_the_right_bracket()
    {
        InlineSpan link = Spans("[a `]` b](x)").Single(span => span.Kind == InlineSpanKind.Link);

        Assert.Equal("](x)", link.Closer);
        Assert.Equal("a `]` b", "[a `]` b](x)"[(link.InnerStart)..link.InnerEnd]);
    }

    [Fact]
    public void Nested_spans_are_all_recorded_outermost_first()
    {
        // *emphasis with **strong** inside*
        ImmutableArray<InlineSpan> spans = Spans("*a **b** c*");

        Assert.Equal(2, spans.Length);
        Assert.Equal(InlineSpanKind.Emphasis, spans[0].Kind);
        Assert.Equal(InlineSpanKind.Strong, spans[1].Kind);
        Assert.Equal("**", spans[1].Opener);
        Assert.Equal(3, spans[1].OuterStart);
    }

    [Fact]
    public void Triple_star_gives_nested_spans_covering_the_same_text()
    {
        ImmutableArray<InlineSpan> spans = Spans("***both***");

        Assert.Equal(2, spans.Length);
        Assert.Equal("***", string.Concat(spans.Select(span => span.Opener)));
        Assert.Equal("***", string.Concat(spans.Reverse().Select(span => span.Closer)));
    }

    [Fact]
    public void Autolinks_and_task_boxes_are_not_recorded()
    {
        Assert.Empty(Spans("<http://x.y>"));
        Assert.Empty(Spans("see http://x.y now"));
        Assert.Empty(Spans("- [x] done"));
    }

    [Fact]
    public void Offsets_are_relative_to_the_block_start()
    {
        // "> - a *b*": the paragraph starts at 4, after the quote bar and the bullet; the emphasis is at 6..9 in the source.
        RenderBlock block = RenderDocument.Parse("> - a *b*\n").Blocks[0];

        Assert.Equal(4, block.Start);
        Assert.Equal(new InlineSpan(InlineSpanKind.Emphasis, 2, 5, 3, 4, "*", "*"), Assert.Single(block.InlineSpans));
    }

    [Fact]
    public void A_table_records_the_spans_of_all_its_cells_in_row_order()
    {
        RenderBlock table = RenderDocument.Parse("| *a* | b |\n|---|---|\n| c | **d** |\n").Blocks[0];

        Assert.Equal(new[] { InlineSpanKind.Emphasis, InlineSpanKind.Strong }, table.InlineSpans.Select(span => span.Kind));
    }
}
