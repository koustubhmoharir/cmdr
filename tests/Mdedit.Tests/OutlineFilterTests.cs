using Mdedit.Core.Render;
using Mdedit.Tui;

namespace Mdedit.Tests;

/// <summary>
/// The outline dialog's rules without a user interface: the labels with their indent, the rows that a filter keeps (case-insensitive, in
/// document order, each with its heading's start), and the section the list starts on.
/// </summary>
public sealed class OutlineFilterTests
{
    private const string Guide = "# Guide\n\nIntro.\n\n## Install\n\nSteps.\n\n### Linux\n\nMore.\n\n## Usage\n\n# Appendix\n\n## Linux notes\n";

    [Fact]
    public void Each_level_below_1_is_indented_by_two_spaces_per_level()
    {
        Assert.Equal("Guide", OutlineFilter.Label(new OutlineEntry(1, "Guide", 0, false)));
        Assert.Equal("  Install", OutlineFilter.Label(new OutlineEntry(2, "Install", 0, false)));
        Assert.Equal("    Linux", OutlineFilter.Label(new OutlineEntry(3, "Linux", 0, false)));
    }

    [Fact]
    public void Without_a_filter_every_heading_is_a_row_in_document_order_with_its_start_and_indent()
    {
        IReadOnlyList<OutlineEntry> entries = Entries(Guide);

        IReadOnlyList<OutlineRow> rows = OutlineFilter.Rows(entries, string.Empty);

        Assert.Equal(
            ["Guide", "  Install", "    Linux", "  Usage", "Appendix", "  Linux notes"],
            rows.Select(row => row.Label));
        Assert.Equal(entries.Select(entry => entry.Start), rows.Select(row => row.Start));
    }

    [Fact]
    public void The_filter_is_a_substring_of_the_heading_text_ignoring_case()
    {
        IReadOnlyList<OutlineRow> rows = OutlineFilter.Rows(Entries(Guide), "LINUX");

        Assert.Equal(["    Linux", "  Linux notes"], rows.Select(row => row.Label));
    }

    [Fact]
    public void A_filtered_row_keeps_its_indent_and_its_heading_start()
    {
        IReadOnlyList<OutlineEntry> entries = Entries(Guide);

        OutlineRow row = Assert.Single(OutlineFilter.Rows(entries, "usa"));

        Assert.Equal("  Usage", row.Label);
        Assert.Equal(entries[3].Start, row.Start);
    }

    [Fact]
    public void The_filter_matches_the_heading_text_and_not_its_indent()
    {
        // Every heading below level 1 is indented; a filter of spaces matches only the headings whose text has a space.
        IReadOnlyList<OutlineRow> rows = OutlineFilter.Rows(Entries(Guide), " ");

        Assert.Equal(["  Linux notes"], rows.Select(row => row.Label));
    }

    [Fact]
    public void A_filter_that_matches_no_heading_gives_no_rows()
    {
        Assert.Empty(OutlineFilter.Rows(Entries(Guide), "zzz"));
    }

    [Fact]
    public void A_filter_gives_the_matching_headings_in_document_order()
    {
        // "Install" and "Linux" have no "e"; the other four headings do, in document order.
        IReadOnlyList<OutlineRow> rows = OutlineFilter.Rows(Entries(Guide), "e");

        Assert.Equal(["Guide", "  Usage", "Appendix", "  Linux notes"], rows.Select(row => row.Label));
    }

    [Fact]
    public void The_list_starts_on_the_section_the_caret_is_in()
    {
        IReadOnlyList<OutlineEntry> entries = Entries(Guide);
        int usage = entries[3].Start;

        // The caret inside "Usage" (after its line, before "Appendix"), at the heading's start, and after the last heading.
        Assert.Equal(3, OutlineFilter.InitialIndex(entries, usage + 4));
        Assert.Equal(3, OutlineFilter.InitialIndex(entries, usage));
        Assert.Equal(5, OutlineFilter.InitialIndex(entries, entries[^1].Start + 100));
    }

    [Fact]
    public void A_caret_before_the_first_heading_starts_on_the_first_heading()
    {
        IReadOnlyList<OutlineEntry> entries = Entries("Text first.\n\n# Title\n\nBody.\n");

        Assert.Equal(0, OutlineFilter.InitialIndex(entries, 0));
    }

    [Fact]
    public void A_caret_at_a_heading_start_selects_that_heading()
    {
        IReadOnlyList<OutlineEntry> entries = Entries(Guide);

        Assert.Equal(1, OutlineFilter.InitialIndex(entries, entries[1].Start));
        Assert.Equal(0, OutlineFilter.InitialIndex(entries, entries[1].Start - 1));
    }

    [Fact]
    public void With_no_headings_the_initial_index_is_minus_one_and_there_are_no_rows()
    {
        IReadOnlyList<OutlineEntry> entries = Entries("Only text.\n");

        Assert.Equal(-1, OutlineFilter.InitialIndex(entries, 3));
        Assert.Empty(OutlineFilter.Rows(entries, string.Empty));
    }

    private static IReadOnlyList<OutlineEntry> Entries(string markdown) => Outline.Of(RenderDocument.Parse(markdown));
}
