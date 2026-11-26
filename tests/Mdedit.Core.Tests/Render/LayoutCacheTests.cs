using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>The layout cache: reuse of unchanged blocks, separation by width, and eviction past capacity.</summary>
public sealed class LayoutCacheTests
{
    private const string Document = "# Title\n\nFirst paragraph.\n\n- item\n- other\n\n> quoted\n";

    [Fact]
    public void A_second_build_at_the_same_width_returns_the_same_block_layouts()
    {
        var cache = new LayoutCache();
        DocumentLayout first = LayoutDump.Build(Document, 40, cache);
        DocumentLayout second = LayoutDump.Build(Document, 40, cache);

        Assert.Equal(first.Blocks.Length, second.Blocks.Length);
        for (int index = 0; index < first.Blocks.Length; index++)
        {
            Assert.Same(first.Blocks[index], second.Blocks[index]);
        }
    }

    [Fact]
    public void After_editing_one_paragraph_only_its_layout_is_new()
    {
        var cache = new LayoutCache();
        DocumentLayout before = LayoutDump.Build(Document, 40, cache);
        int countBefore = cache.Count;
        DocumentLayout after = LayoutDump.Build(Document.Replace("First paragraph.", "First paragraph, edited."), 40, cache);

        Assert.Equal(countBefore + 1, cache.Count);
        Assert.Same(before.Blocks[0], after.Blocks[0]);
        Assert.NotSame(before.Blocks[1], after.Blocks[1]);
        Assert.Same(before.Blocks[2], after.Blocks[2]);
        Assert.Same(before.Blocks[3], after.Blocks[3]);
    }

    [Fact]
    public void Moving_a_block_does_not_change_its_layout_because_offsets_are_relative()
    {
        var cache = new LayoutCache();
        DocumentLayout before = LayoutDump.Build("Para.\n", 40, cache);
        DocumentLayout after = LayoutDump.Build("New line.\n\nPara.\n", 40, cache);

        Assert.Same(before.Blocks[0], after.Blocks[1]);
    }

    [Fact]
    public void Different_widths_do_not_share_entries()
    {
        var cache = new LayoutCache();
        DocumentLayout narrow = LayoutDump.Build("aaa bbb ccc\n", 9, cache);
        DocumentLayout wide = LayoutDump.Build("aaa bbb ccc\n", 40, cache);

        Assert.NotSame(narrow.Blocks[0], wide.Blocks[0]);
        Assert.Equal(2, cache.Count);
        Assert.Equal(2, narrow.LineCount);
        Assert.Equal(1, wide.LineCount);
    }

    [Fact]
    public void Blocks_that_differ_only_in_their_container_chain_do_not_share_entries()
    {
        var cache = new LayoutCache();
        DocumentLayout plain = LayoutDump.Build("text\n", 40, cache);
        DocumentLayout quoted = LayoutDump.Build("> text\n", 40, cache);

        Assert.NotSame(plain.Blocks[0], quoted.Blocks[0]);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Eviction_removes_the_least_recently_used_layout_past_capacity()
    {
        var cache = new LayoutCache(2);
        RenderDocument a = RenderDocument.Parse("a\n");
        RenderDocument b = RenderDocument.Parse("b\n");
        RenderDocument c = RenderDocument.Parse("c\n");
        BlockLayout aLayout = cache.Get(a, a.Blocks[0], 40);
        cache.Get(b, b.Blocks[0], 40);
        Assert.Same(aLayout, cache.Get(a, a.Blocks[0], 40));
        cache.Get(c, c.Blocks[0], 40);

        Assert.Equal(2, cache.Count);
        Assert.Same(aLayout, cache.Get(a, a.Blocks[0], 40));
        cache.Get(b, b.Blocks[0], 40);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Evicted_blocks_are_laid_out_again()
    {
        var cache = new LayoutCache(1);
        RenderDocument a = RenderDocument.Parse("a\n");
        RenderDocument b = RenderDocument.Parse("b\n");
        BlockLayout first = cache.Get(a, a.Blocks[0], 40);
        cache.Get(b, b.Blocks[0], 40);

        Assert.Equal(1, cache.Count);
        Assert.NotSame(first, cache.Get(a, a.Blocks[0], 40));
    }

    [Fact]
    public void A_default_cache_keeps_4096_layouts()
    {
        Assert.Equal(4096, LayoutCache.DefaultCapacity);
        Assert.Equal(0, new LayoutCache().Count);
    }

    [Fact]
    public void Capacity_must_be_at_least_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LayoutCache(0));
    }

    [Fact]
    public void Adding_a_reference_definition_elsewhere_lays_out_its_link_again()
    {
        var cache = new LayoutCache();
        DocumentLayout without = LayoutDump.Build("see [x]\n", 40, cache);
        DocumentLayout with = LayoutDump.Build("see [x]\n\n[x]: https://example.com\n", 40, cache);

        Assert.NotSame(without.Blocks[0], with.Blocks[0]);
        Cell x = with.Line(0).Cells.Single(cell => cell.Text == "x");
        Assert.True(x.Style.Inline.HasFlag(InlineStyle.Link));
        Assert.True(x.LinkId >= 0);
    }

    [Fact]
    public void Removing_the_reference_definition_again_lays_out_the_text_without_a_link()
    {
        var cache = new LayoutCache();
        LayoutDump.Build("see [x]\n\n[x]: https://example.com\n", 40, cache);
        DocumentLayout again = LayoutDump.Build("see [x]\n", 40, cache);

        Assert.DoesNotContain(again.Line(0).Cells, cell => cell.Style.Inline.HasFlag(InlineStyle.Link));
        Assert.Equal("  see [x]", again.TextOf(0));
    }

    [Fact]
    public void An_unrelated_edit_keeps_the_layout_of_a_paragraph_that_uses_a_reference()
    {
        var cache = new LayoutCache();
        DocumentLayout before = LayoutDump.Build("see [x]\n\n[x]: https://example.com\n\nother\n", 40, cache);
        DocumentLayout after = LayoutDump.Build("see [x]\n\n[x]: https://example.com\n\nother edited\n", 40, cache);

        Assert.Same(before.Blocks[0], after.Blocks[0]);
        Assert.NotSame(before.Blocks[2], after.Blocks[2]);
    }

    [Fact]
    public void Lowest_mapped_offset_is_computed_with_the_layout()
    {
        RenderDocument doc = RenderDocument.Parse("1. x\n\nplain\n");

        Assert.Equal(-3, BlockLayout.Layout(doc.Blocks[0], 40).MinOffset);
        Assert.Equal(0, BlockLayout.Layout(doc.Blocks[1], 40).MinOffset);
    }
}
