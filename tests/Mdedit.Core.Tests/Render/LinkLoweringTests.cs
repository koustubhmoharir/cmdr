using System.Collections.Immutable;
using System.Linq;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The links of a block: their destinations, absolute spans, autolink flags and the ids their cells carry.</summary>
public sealed class LinkLoweringTests
{
    private static ImmutableArray<LinkInfo> LinksOf(string source, int block = 0) =>
        RenderDocument.Parse(source).Blocks[block].Links;

    /// <summary>The text of the cells that carry a link id, concatenated, in cell order.</summary>
    private static string TextWithLink(RenderBlock block, int id) =>
        string.Concat(block.Runs.SelectMany(run => run.Cells).Where(cell => cell.LinkId == id).Select(cell => cell.Text));

    [Fact]
    public void Inline_link_is_recorded_with_its_destination_and_span()
    {
        const string source = "[a](http://x)";

        LinkInfo link = Assert.Single(LinksOf(source));

        Assert.Equal(new LinkInfo(0, "http://x", 0, source.Length, false), link);
    }

    [Fact]
    public void Full_reference_link_gives_the_definitions_url()
    {
        const string source = "[a][r]\n\n[r]: http://y\n";

        LinkInfo link = Assert.Single(LinksOf(source));

        Assert.Equal("http://y", link.Url);
        Assert.Equal((0, 6), (link.Start, link.End));
    }

    [Fact]
    public void Collapsed_reference_link_gives_the_definitions_url()
    {
        const string source = "[a][]\n\n[a]: http://y\n";

        LinkInfo link = Assert.Single(LinksOf(source));

        Assert.Equal("http://y", link.Url);
        Assert.Equal((0, 5), (link.Start, link.End));
    }

    [Fact]
    public void Shortcut_reference_link_gives_the_definitions_url()
    {
        const string source = "[a] z\n\n[a]: http://y\n";

        LinkInfo link = Assert.Single(LinksOf(source));

        Assert.Equal("http://y", link.Url);
        Assert.Equal((0, 3), (link.Start, link.End));
    }

    [Fact]
    public void Bare_autolink_is_recorded_as_an_autolink_over_its_text()
    {
        const string source = "see https://x.y/z now";

        LinkInfo link = Assert.Single(LinksOf(source));

        Assert.Equal(new LinkInfo(0, "https://x.y/z", 4, 4 + "https://x.y/z".Length, true), link);
    }

    [Fact]
    public void Angle_autolink_gives_the_address_as_written_over_the_brackets()
    {
        const string source = "<https://x.y>";

        LinkInfo link = Assert.Single(LinksOf(source));

        Assert.Equal(new LinkInfo(0, "https://x.y", 0, source.Length, true), link);
    }

    [Fact]
    public void Angle_email_autolink_gives_the_address_with_mailto_so_it_is_external()
    {
        const string source = "<a@b.co>";

        LinkInfo link = Assert.Single(LinksOf(source));

        Assert.Equal(new LinkInfo(0, "mailto:a@b.co", 0, source.Length, true), link);
        Assert.IsType<LinkTarget.External>(LinkTargets.Classify(link.Url));
    }

    [Fact]
    public void Image_is_not_a_link()
    {
        ImmutableArray<LinkInfo> links = LinksOf("![alt](img.png) and [a](b)");

        Assert.Equal(new LinkInfo(0, "b", 20, 26, false), Assert.Single(links));
    }

    [Fact]
    public void Two_links_in_one_block_have_ids_zero_and_one_and_their_cells_carry_them()
    {
        RenderBlock block = RenderDocument.Parse("[a](x) and [b](y)").Blocks[0];

        Assert.Equal(["x", "y"], block.Links.Select(link => link.Url));
        Assert.Equal([0, 1], block.Links.Select(link => link.Id));
        Assert.Equal("a", TextWithLink(block, 0));
        Assert.Equal("b", TextWithLink(block, 1));
    }

    [Fact]
    public void Link_ids_restart_in_each_block()
    {
        RenderDocument doc = RenderDocument.Parse("[a](x)\n\n[b](y)\n");

        Assert.Equal(0, doc.Blocks[1].Links[0].Id);
        Assert.Equal("y", doc.Blocks[1].Links[0].Url);
    }

    [Fact]
    public void Emphasis_inside_a_link_keeps_the_link_id_on_every_cell()
    {
        RenderBlock block = RenderDocument.Parse("[*a* b](x)").Blocks[0];

        Assert.Equal("a b", TextWithLink(block, 0));
        Assert.All(block.Runs.SelectMany(run => run.Cells), cell => Assert.Equal(0, cell.LinkId));
    }

    [Fact]
    public void Link_in_a_table_cell_is_recorded_and_its_cells_index_the_block_links()
    {
        const string source = "| [a](http://x) | b |\n| - | - |\n| c | [d](e.md) |\n";

        RenderBlock block = RenderDocument.Parse(source).Blocks[0];

        Assert.Equal(["http://x", "e.md"], block.Links.Select(link => link.Url));
        Assert.Equal("a", TextWithLink(block, 0));
        Assert.Equal("d", TextWithLink(block, 1));
        Assert.Equal(source.IndexOf("[d]", StringComparison.Ordinal), block.Links[1].Start);
    }

    [Fact]
    public void Span_is_absolute_in_the_document_and_ends_after_the_closer()
    {
        const string source = "x [a *b*](u) y";

        LinkInfo link = Assert.Single(LinksOf(source));

        Assert.Equal((2, 12), (link.Start, link.End));
    }

    [Fact]
    public void Span_inside_a_quote_is_absolute()
    {
        const string source = "> [a](x)\n";

        LinkInfo link = Assert.Single(LinksOf(source));

        Assert.Equal(2, link.Start);
        Assert.Equal(source.Length - 1, link.End);
    }

    [Fact]
    public void Block_without_links_has_none()
    {
        Assert.Empty(LinksOf("plain text"));
    }
}
