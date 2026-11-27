using Mdedit.Core.Render;
using Shared.Core;

namespace Mdedit.Core.Tests.Render;

/// <summary>
/// A non-empty cell has an end stop: the layout maps the first padding cell after its text, on the cell's last line of text, at the source
/// offset just after the cell's last mapped character. The stop is an ordinary mapped cell, so it round-trips through the position API, and
/// the runs (which copy and search read) are unchanged.
/// </summary>
public sealed class TableEndStopLayoutTests
{
    // Offsets: the header "| abc | x |" is line 0 of the source (its "abc" is 2..4, its end stop 5; "x" is 8, its stop 9), the delimiter
    // row starts at 12, the body row "| 1 | 2 |" at 22, with "1" at 24 (stop 25) and "2" at 28 (stop 29).
    private const string Plain = "| abc | x |\n|---|---|\n| 1 | 2 |\n";

    [Fact]
    public void The_end_stop_is_the_first_padding_cell_after_the_text()
    {
        string[] lines = LayoutDump.Lines(Plain);
        DocumentLayout layout = LayoutDump.Build(Plain);

        Assert.Equal(" @-| @-|│@-/TableBorder| @-|a@2|b@3|c@4| @5|│@-/TableBorder| @-|x@8| @9|│@-/TableBorder", LayoutDump.Cells(layout, 1));
        Assert.Equal(" @-| @-|│@-/TableBorder| @-|1@24| @25| @-| @-|│@-/TableBorder| @-|2@28| @29|│@-/TableBorder", LayoutDump.Cells(layout, 3));
        Assert.Equal("  │ abc │ x │", lines[1]);
        Assert.Equal("  │ 1   │ 2 │", lines[3]);
    }

    [Fact]
    public void The_model_records_the_source_end_of_each_column_and_the_runs_are_unchanged()
    {
        RenderDocument document = RenderDocument.Parse(Plain);
        TableModel table = document.Blocks[0].Table!;

        Assert.Equal(new[] { 5, 9 }, table.Rows[0].ContentEnds.ToArray());
        Assert.Equal(new[] { 25, 29 }, table.Rows[1].ContentEnds.ToArray());
        Assert.Equal(new[] { "abc", "x" }, table.Rows[0].Cells.Select(run => string.Concat(run.Cells.Select(cell => cell.Text))).ToArray());
    }

    [Fact]
    public void The_end_stop_round_trips_through_the_position_API()
    {
        DocumentLayout layout = LayoutDump.Build(Plain);

        (int line, int column) = layout.PositionOf(5);

        Assert.Equal(1, line);
        Assert.Equal(7, column);
        Assert.Equal(5, layout.OffsetAt(line, column));
        Assert.Equal((1, 10), layout.PositionOf(8));
    }

    [Fact]
    public void A_right_aligned_cell_has_its_stop_in_its_one_padding_cell_after_the_blanks_that_align_it()
    {
        // The delimiter row "|---:|---|" is 10 characters, so the body row starts at 23 and its "1" at 25 (stop 26).
        DocumentLayout layout = LayoutDump.Build("| abc | x |\n|---:|---|\n| 1 | 2 |\n");

        Assert.Equal(" @-| @-|│@-/TableBorder| @-| @-| @-|1@25| @26|│@-/TableBorder| @-|2@29| @30|│@-/TableBorder", LayoutDump.Cells(layout, 3));
    }

    [Fact]
    public void A_wrapped_cell_has_its_stop_on_its_last_line_of_text()
    {
        // At width 16 the content is 14 wide (the gutter takes two), so the first column is 6 wide and "aaaa bbbb cccc" wraps to three lines.
        // The first two lines keep the space after their word (it fits), which is text, so the padding follows it; the stop is after "cccc"
        // (offset 16), on the third line. The second column is empty, so its blank is on the header's first line only.
        DocumentLayout layout = LayoutDump.Build("| aaaa bbbb cccc | |\n|---|---|\n| 1 | 2 |\n", 16);

        Assert.Equal(" @-| @-|│@-/TableBorder| @-|a@2|a@3|a@4|a@5| @6| @-| @-|│@-/TableBorder| @-| @19| @-|│@-/TableBorder", LayoutDump.Cells(layout, 1));
        Assert.Equal(" @-| @-|│@-/TableBorder| @-|b@7|b@8|b@9|b@10| @11| @-| @-|│@-/TableBorder| @-| @-| @-|│@-/TableBorder", LayoutDump.Cells(layout, 2));
        Assert.Equal(" @-| @-|│@-/TableBorder| @-|c@12|c@13|c@14|c@15| @16| @-| @-|│@-/TableBorder| @-| @-| @-|│@-/TableBorder", LayoutDump.Cells(layout, 3));
    }

    [Fact]
    public void The_end_of_an_entity_is_after_its_last_character_in_the_source()
    {
        // "&amp;" is 2..6 and its cell maps to the "&" at 2, so the stop is at 7, after the semicolon.
        DocumentLayout layout = LayoutDump.Build("| &amp; | x |\n|---|---|\n| 1 | 2 |\n");

        Assert.Equal(" @-| @-|│@-/TableBorder| @-|&@2| @7|│@-/TableBorder| @-|x@10| @11|│@-/TableBorder", LayoutDump.Cells(layout, 1));
    }

    [Fact]
    public void A_linked_text_ends_its_stop_at_the_end_of_its_text_not_after_the_destination()
    {
        // "[go](u)" maps "go" at 3 and 4, so the stop is at 5, before the "]"; the insertion point after the link is an editing rule.
        DocumentLayout layout = LayoutDump.Build("| [go](u) | x |\n|---|---|\n| 1 | 2 |\n");

        Assert.Equal(" @-| @-|│@-/TableBorder| @-|g@3|o@4| @5|│@-/TableBorder| @-|x@12| @13|│@-/TableBorder", LayoutDump.Cells(layout, 1));
    }

    [Fact]
    public void Every_mapped_offset_of_the_table_is_unique_in_the_source_map()
    {
        DocumentLayout layout = LayoutDump.Build("| aaaa bbbb cccc | |\n|---|---|\n| 1 | 2 |\n", 16);
        int[] offsets = Enumerable.Range(0, layout.LineCount)
            .SelectMany(line => layout.Line(line).Cells.Where(cell => cell.IsMapped).Select(cell => cell.Offset))
            .ToArray();

        Assert.Equal(offsets.Length, offsets.Distinct().Count());
    }

    [Fact]
    public void Copy_and_search_do_not_see_the_stop()
    {
        RenderDocument document = RenderDocument.Parse(Plain);
        SearchPattern pattern = SearchPattern.Create(new SearchQuery("abc", false, false, false), out _)!;

        Assert.Equal("abc\tx\n1\t2", TextExtraction.PlainText(document, 0, Plain.Length));
        Assert.Equal(new[] { new SourceRange(2, 5) }, RenderSearch.Find(document, pattern).Matches.ToArray());
    }
}
