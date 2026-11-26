using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// The view's document replaced after an edit (<see cref="RenderedView.SetDocument"/>): the fold state and the table scroll offsets follow
/// the text through tracked positions, a fold whose position is lost is dropped, and the caret is placed with its selection (and unfolds
/// what hides it). The edits are made through <see cref="MovingOffsetTracker"/>, which moves the positions as the buffer does.
/// </summary>
public sealed partial class RenderedViewTests
{
    // The section with a fold: its heading is at offset 0, and the section holds the second heading.
    private const string Titled = "# Title\n\nFirst paragraph.\n\n## Part\n\nPart text.\n";

    [Fact]
    public void A_folded_section_stays_folded_at_its_moved_place_when_text_is_inserted_above_it()
    {
        var tracker = new MovingOffsetTracker();
        RenderedView view = new(RenderDocument.Parse(Titled), tracker);
        const string lead = "Lead.\n\n";

        Result result = Run(
            view,
            v => v.ToggleFold(v.Units[0]),
            v =>
            {
                tracker.Insert(0, lead.Length);
                v.SetDocument(RenderDocument.Parse(lead + Titled));
            });

        TerminalScreen screen = result.Screens[^1];
        Assert.Equal("  Lead.", screen.Text(0));
        Assert.Equal("▸ Title … 6 lines", screen.Text(2));
        Assert.True(view.IsFolded(view.Units[0]));
        Assert.Equal(lead.Length, view.Units[0].Start);
    }

    [Fact]
    public void A_fold_whose_heading_was_deleted_is_dropped_and_the_rest_of_the_document_is_shown_unfolded()
    {
        var tracker = new MovingOffsetTracker();
        RenderedView view = new(RenderDocument.Parse(Titled), tracker);
        const string heading = "# Title\n";

        Result result = Run(
            view,
            v => v.ToggleFold(v.Units[0]),
            v =>
            {
                tracker.Remove(0, heading.Length);
                v.SetDocument(RenderDocument.Parse(Titled[heading.Length..]));
            });

        TerminalScreen screen = result.Screens[^1];
        Assert.Equal("  First paragraph.", screen.Text(0));
        Assert.Equal("▾ Part", screen.Text(2));
        Assert.DoesNotContain("▸", string.Concat(Enumerable.Range(0, 10).Select(screen.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_tracker_a_fold_keeps_its_offset_when_the_same_text_is_set_again()
    {
        RenderedView view = View(Titled);

        Result result = Run(
            view,
            v => v.ToggleFold(v.Units[0]),
            v => v.SetDocument(RenderDocument.Parse(Titled)));

        Assert.Equal("▸ Title … 6 lines", result.Screens[^1].Text(0));
    }

    [Fact]
    public void A_table_scroll_offset_stays_on_its_table_when_text_is_inserted_above_it()
    {
        var tracker = new MovingOffsetTracker();
        RenderedView view = new(RenderDocument.Parse(WideTable), tracker);
        const string lead = "Lead.\n\n";

        Result result = Run(
            view,
            v => v.PlaceCaret(Offset(WideTable, "Bcdefghij"), null),
            v => v.HandleKey(Greater),
            v =>
            {
                tracker.Insert(0, lead.Length);
                v.SetDocument(RenderDocument.Parse(lead + WideTable));
            });

        // The table row with the left edge marker (the header) is drawn the same after the edit, two rows lower with the new lead.
        string before = ScrolledRow(result.Screens[3]);
        string after = ScrolledRow(result.Screens[^1]);
        Assert.StartsWith("  ◂", before, StringComparison.Ordinal);
        Assert.Equal(before, after);
    }

    [Fact]
    public void PlaceCaret_selects_from_the_anchor_to_the_offset_with_the_caret_on_the_offset()
    {
        RenderedView view = View(Mixed);
        int start = Offset(Mixed, "item");

        Result result = Run(view, v => v.PlaceCaret(start + 4, start));

        Assert.Equal(start + 4, result.Snaps[0].Offset);
        Assert.Equal(new SourceRange(start, start + 4), view.SelectionRange);
        Assert.Equal(start, view.SelectionAnchor);
    }

    [Fact]
    public void PlaceCaret_unfolds_the_section_that_hides_its_offset()
    {
        RenderedView view = View(Titled);
        int part = Offset(Titled, "Part text");

        Result result = Run(
            view,
            v => v.ToggleFold(v.Units[0]),
            v => v.PlaceCaret(part, null));

        Assert.False(view.IsFolded(view.Units[0]));
        Assert.Equal(part, result.Snaps[^1].Offset);
    }

    // The row of a screen that has the left edge marker of a scrolled table.
    private static string ScrolledRow(TerminalScreen screen) => screen.Text(screen.RowOf("◂"));
}
