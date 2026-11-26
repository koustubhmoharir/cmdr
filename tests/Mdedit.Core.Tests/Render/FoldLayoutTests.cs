using System.Collections.Immutable;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Render;

/// <summary>Folding in the layout: the folded lines and summaries, the separators, the cache, the fold lookup and the position of hidden content.</summary>
public sealed class FoldLayoutTests
{
    private const string Sections = "# A\n\nparagraph\n\n## B\n\nparagraph\n\n# C\n\nparagraph\n";

    [Fact]
    public void Unfolded_sections_show_every_line()
    {
        Assert.Equal(
            new[] { "▾ A", string.Empty, "  paragraph", string.Empty, "▾ B", string.Empty, "  paragraph", string.Empty, "▾ C", string.Empty, "  paragraph" },
            Lines(Sections, 40, folded: null));
    }

    [Fact]
    public void Folding_a_section_hides_its_blocks_and_nested_sections_and_keeps_the_next_section()
    {
        int a = Units(Sections)[0].Start;

        Assert.Equal(
            new[] { "▸ A … 6 lines", string.Empty, "▾ C", string.Empty, "  paragraph" },
            Lines(Sections, 40, Set(a)));
    }

    [Fact]
    public void Folding_a_nested_section_hides_only_its_own_blocks()
    {
        int b = Units(Sections)[1].Start;

        Assert.Equal(
            new[] { "▾ A", string.Empty, "  paragraph", string.Empty, "▸ B … 2 lines", string.Empty, "▾ C", string.Empty, "  paragraph" },
            Lines(Sections, 40, Set(b)));
    }

    [Fact]
    public void Unfolding_an_outer_section_keeps_the_nested_section_folded()
    {
        ImmutableArray<FoldUnit> units = Units(Sections);
        HashSet<int> state = Set(units[1].Start);

        state.Add(units[0].Start);
        Assert.Equal(new[] { "▸ A … 6 lines", string.Empty, "▾ C", string.Empty, "  paragraph" }, Lines(Sections, 40, state));

        state.Remove(units[0].Start);
        Assert.Equal(
            new[] { "▾ A", string.Empty, "  paragraph", string.Empty, "▸ B … 2 lines", string.Empty, "▾ C", string.Empty, "  paragraph" },
            Lines(Sections, 40, state));
    }

    [Fact]
    public void A_summary_of_one_line_is_singular()
    {
        Assert.Equal(new[] { "▸ A … 1 line" }, Lines("# A\npara\n", 40, Set(0)));
    }

    [Fact]
    public void Summary_counts_source_lines_with_crlf_endings()
    {
        Assert.Equal(
            new[] { "▸ A … 2 lines", string.Empty, "▾ C" },
            Lines("# A\r\n\r\npara\r\n\r\n# C\r\n", 40, Set(0)));
    }

    [Fact]
    public void A_folded_head_that_is_the_last_block_changes_only_its_glyph()
    {
        Assert.Equal(new[] { "▸ A" }, Lines("# A\n", 40, Set(0)));
    }

    [Fact]
    public void A_folded_code_block_is_its_header_with_the_summary_and_no_code_lines()
    {
        Assert.Equal(new[] { "▸ cs … 3 lines" }, Lines("```cs\nint x;\nint y;\nint z;\n```\n", 40, Set(0)));
        Assert.Equal(new[] { "▸ cs … 1 line" }, Lines("```cs\nint x;\n```\n", 40, Set(0)));
        Assert.Equal(new[] { "▸ cs" }, Lines("```cs\n```\n", 40, Set(0)));
    }

    [Fact]
    public void A_folded_fenced_code_header_has_one_mapped_cell_which_is_its_label_at_the_block_start()
    {
        DocumentLayout layout = DocumentLayout.Build(RenderDocument.Parse("```cs\nint x;\n```\n"), 40, new LayoutCache(), Set(0));

        Assert.Equal(new[] { 0 }, layout.Line(0).Cells.Where(cell => cell.IsMapped).Select(cell => cell.Offset));
        Assert.Equal("▸", layout.Line(0).Cells[0].Text);
        Assert.Equal("c", layout.Line(0).Cells[2].Text);
    }

    [Fact]
    public void A_folded_code_block_header_is_not_wrapped_even_when_the_summary_does_not_fit()
    {
        // Width 12 leaves 10 columns after the gutter; "csharp … 1 line" is 15 wide and stays on its one line.
        Assert.Equal(new[] { "▸ csharp … 1 line" }, Lines("```csharp\nxxxxxxxxxx\n```\n", 12, Set(0)));
    }

    [Fact]
    public void A_summary_that_does_not_fit_wraps_with_the_heading_words()
    {
        // Content width 12 after the two-column prefix: the heading takes 11, the summary's space fits, its ellipsis does not.
        Assert.Equal(new[] { "▸ Title words ", "  … 1 line" }, Lines("# Title words\npara\n", 14, Set(0)));
    }

    [Fact]
    public void A_folded_heading_with_nothing_hidden_changes_only_its_glyph()
    {
        Assert.Equal(new[] { "▸ Title" }, Lines("# Title\n", 40, Set(0)));
    }

    [Fact]
    public void A_heading_in_a_quote_is_not_a_unit_so_folding_its_key_changes_nothing()
    {
        string text = "> # Q\n>\n> text\n";

        Assert.Equal(Lines(text, 40, null), Lines(text, 40, Set(2)));
        Assert.Empty(Units(text).Where(unit => unit.Kind == FoldUnitKind.Section));
    }

    [Fact]
    public void Keys_that_are_not_unit_starts_are_ignored()
    {
        Assert.Equal(Lines(Sections, 40, null), Lines(Sections, 40, Set(999)));
    }

    [Fact]
    public void Folding_a_code_block_inside_a_folded_section_hides_it_whatever_its_own_state()
    {
        const string text = "# A\n\n```cs\nx\n```\n\n# C\n";
        ImmutableArray<FoldUnit> units = Units(text);

        Assert.Equal(new[] { "▸ A … 4 lines", string.Empty, "▾ C" }, Lines(text, 40, Set(units[0].Start)));
        Assert.Equal(new[] { "▸ A … 4 lines", string.Empty, "▾ C" }, Lines(text, 40, Set(units[0].Start, units[1].Start)));
        Assert.Equal(new[] { "▾ A", string.Empty, "▸ cs … 1 line", string.Empty, "▾ C" }, Lines(text, 40, Set(units[1].Start)));
    }

    [Fact]
    public void Folding_a_unit_changes_the_head_layout_and_reuses_the_others()
    {
        var cache = new LayoutCache();
        DocumentLayout unfolded = LayoutDump.Build(Sections, 40, cache);
        int before = cache.Count;

        DocumentLayout folded = DocumentLayout.Build(RenderDocument.Parse(Sections), 40, cache, Set(Units(Sections)[0].Start));

        Assert.Equal(before + 1, cache.Count);
        Assert.NotSame(unfolded.Blocks[0], folded.Blocks[0]);
        Assert.Same(unfolded.Blocks[4], folded.Blocks[4]);
        Assert.Same(unfolded.Blocks[5], folded.Blocks[5]);
    }

    [Fact]
    public void Fold_unit_at_a_heading_line_is_its_section_and_a_separator_or_paragraph_is_none()
    {
        DocumentLayout layout = LayoutDump.Build("# aaa bbb ccc\n\npara\n", 9);

        Assert.Equal(new[] { "▾ aaa bbb", "  ccc", string.Empty, "  para" }, Enumerable.Range(0, layout.LineCount).Select(layout.TextOf));
        Assert.Equal(0, layout.FoldUnitAt(0)!.Start);
        Assert.Equal(0, layout.FoldUnitAt(1)!.Start);
        Assert.Null(layout.FoldUnitAt(2));
        Assert.Null(layout.FoldUnitAt(3));
    }

    [Fact]
    public void Fold_unit_at_a_code_header_is_the_unit_and_its_code_lines_are_none()
    {
        DocumentLayout layout = LayoutDump.Build("```cs\nint x;\n```\n\ntext\n", 40);

        Assert.Equal(0, layout.FoldUnitAt(0)!.Start);
        Assert.Equal(FoldUnitKind.Code, layout.FoldUnitAt(0)!.Kind);
        Assert.Null(layout.FoldUnitAt(1));
        Assert.Null(layout.FoldUnitAt(3));
    }

    [Fact]
    public void Fold_unit_at_a_folded_code_header_is_the_unit()
    {
        DocumentLayout layout = DocumentLayout.Build(RenderDocument.Parse("```cs\nint x;\n```\n"), 40, new LayoutCache(), Set(0));

        Assert.Equal(FoldUnitKind.Code, layout.FoldUnitAt(0)!.Kind);
        Assert.Equal(1, layout.LineCount);
    }

    [Fact]
    public void Position_of_an_offset_in_a_hidden_block_is_the_folded_head_and_the_column_of_its_first_text()
    {
        DocumentLayout layout = DocumentLayout.Build(RenderDocument.Parse(Sections), 40, new LayoutCache(), Set(Units(Sections)[0].Start));

        Assert.Equal((0, 2), layout.PositionOf(5));
        Assert.Equal((0, 2), layout.PositionOf(22));
        Assert.Equal((2, 2), layout.PositionOf(35));
        Assert.Equal((4, 2), layout.PositionOf(38));
    }

    [Fact]
    public void Position_of_an_offset_in_a_folded_code_block_is_its_header()
    {
        const string text = "```cs\nint x;\nint y;\n```\n";
        DocumentLayout layout = DocumentLayout.Build(RenderDocument.Parse(text), 40, new LayoutCache(), Set(0));

        Assert.Equal((0, 2), layout.PositionOf(0));
        Assert.Equal((0, 2), layout.PositionOf(15));
    }

    [Fact]
    public void Position_of_an_offset_in_a_folded_code_block_in_a_list_item_is_the_label_after_the_bullet()
    {
        const string text = "- ```cs\n  int x;\n  ```\n";
        DocumentLayout layout = DocumentLayout.Build(RenderDocument.Parse(text), 40, new LayoutCache(), Set(Units(text)[0].Start));

        Assert.Equal((0, 4), layout.PositionOf(text.IndexOf("int", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_folded_section_at_the_end_of_the_document_has_no_separator_after_it()
    {
        Assert.Equal(new[] { "▸ A … 1 line" }, Lines("# A\npara\n\n", 40, Set(0)));
    }

    private static ImmutableArray<FoldUnit> Units(string text) => RenderDocument.Parse(text).FoldUnits;

    private static HashSet<int> Set(params int[] keys) => [.. keys];

    /// <summary>The text of every line of a document laid out at a width with the given folded keys.</summary>
    private static string[] Lines(string text, int width, IReadOnlySet<int>? folded) =>
        Enumerate(DocumentLayout.Build(RenderDocument.Parse(text), width, new LayoutCache(), folded));

    private static string[] Enumerate(DocumentLayout layout) => Enumerable.Range(0, layout.LineCount).Select(layout.TextOf).ToArray();
}
