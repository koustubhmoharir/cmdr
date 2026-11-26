using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The heading outline: levels, plain texts, starts (the start of the heading block, its marker included) and containers.</summary>
public sealed class OutlineTests
{
    [Fact]
    public void Headings_are_listed_in_order_with_their_levels_and_starts()
    {
        const string source = "# One\n\ntext\n\n## Two\n\n### Three\n";

        ImmutableArray<OutlineEntry> outline = Outline.Of(RenderDocument.Parse(source));

        Assert.Equal(
            [
                new OutlineEntry(1, "One", source.IndexOf("# One", StringComparison.Ordinal), false),
                new OutlineEntry(2, "Two", source.IndexOf("## Two", StringComparison.Ordinal), false),
                new OutlineEntry(3, "Three", source.IndexOf("### Three", StringComparison.Ordinal), false),
            ],
            outline.ToArray());
    }

    [Fact]
    public void Emphasis_and_code_spans_are_plain_text()
    {
        ImmutableArray<OutlineEntry> outline = Outline.Of(RenderDocument.Parse("## A *b* and `c` **d**\n"));

        Assert.Equal("A b and c d", Assert.Single(outline).Text);
    }

    [Fact]
    public void A_heading_in_a_quote_is_in_a_container()
    {
        const string source = "> # Quoted\n";

        OutlineEntry entry = Assert.Single(Outline.Of(RenderDocument.Parse(source)));

        Assert.Equal(new OutlineEntry(1, "Quoted", source.IndexOf('#'), true), entry);
    }

    [Fact]
    public void Setext_headings_are_headings()
    {
        const string source = "Title\n=====\n\nSub\n---\n";

        ImmutableArray<OutlineEntry> outline = Outline.Of(RenderDocument.Parse(source));

        Assert.Equal([new OutlineEntry(1, "Title", 0, false), new OutlineEntry(2, "Sub", source.IndexOf("Sub", StringComparison.Ordinal), false)], outline.ToArray());
    }

    [Fact]
    public void A_document_without_headings_has_an_empty_outline()
    {
        Assert.Empty(Outline.Of(RenderDocument.Parse("just text\n\n- item\n")));
    }
}
