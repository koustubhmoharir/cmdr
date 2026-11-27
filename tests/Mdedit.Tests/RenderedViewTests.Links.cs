using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The links of the view: Tab and Shift+Tab move the caret to the first cell of the next or previous link in document order, skip the links
/// that a fold hides, and wrap at the document's ends; the caret's link is reported; a Ctrl+click raises <see cref="RenderedView.LinkActivated"/>
/// while a plain click does not; and <see cref="RenderedView.GoTo"/> places the caret and the top line, unfolding what hides the caret.
/// </summary>
public sealed partial class RenderedViewTests
{
    // Four links: two external or anchor, one in the folded section, and one after it.
    private const string Linked = "# Guide\n\nSee [one](https://a.example) and [two](#guide).\n\n## Section\n\nMore [three](mailto:x@y.z) here.\n\n# Last\n\nEnd [four](#section).\n";

    // A document taller than the view, with a heading in its middle, so that a heading can be put at the top line.
    private static readonly string Tall = Repeat("Filler line.\n\n", 20) + "## Target\n\nTarget text.\n\n" + Repeat("More filler.\n\n", 20);

    [Fact]
    public void Tab_moves_the_caret_to_the_first_cell_of_each_link_in_document_order_and_wraps_to_the_first()
    {
        RenderedView view = View(Linked);
        var moves = new List<LinkMove>();

        Result result = Run(view, Enumerable.Repeat<Action<RenderedView>>(v => moves.Add(v.MoveToLink(forward: true)), 5).ToArray());

        Assert.Equal([LinkMove.Moved, LinkMove.Moved, LinkMove.Moved, LinkMove.Moved, LinkMove.Wrapped], moves);
        Assert.Equal(
            [Offset(Linked, "one"), Offset(Linked, "two"), Offset(Linked, "three"), Offset(Linked, "four"), Offset(Linked, "one")],
            result.Snaps.Select(snap => snap.Offset));
    }

    [Fact]
    public void Shift_Tab_moves_to_the_previous_link_and_wraps_from_the_first_to_the_last()
    {
        RenderedView view = View(Linked);
        var moves = new List<LinkMove>();

        Result result = Run(view, Enumerable.Repeat<Action<RenderedView>>(v => moves.Add(v.MoveToLink(forward: false)), 4).ToArray());

        Assert.Equal([LinkMove.Wrapped, LinkMove.Moved, LinkMove.Moved, LinkMove.Moved], moves);
        Assert.Equal(
            [Offset(Linked, "four"), Offset(Linked, "three"), Offset(Linked, "two"), Offset(Linked, "one")],
            result.Snaps.Select(snap => snap.Offset));
    }

    [Fact]
    public void From_inside_a_link_Tab_goes_to_the_next_link_and_Shift_Tab_to_the_previous_one()
    {
        RenderedView view = View(Linked);
        var moves = new List<LinkMove>();

        Result result = Run(
            view,
            v => v.PlaceCaret(Offset(Linked, "ne") , null),
            v => moves.Add(v.MoveToLink(forward: true)),
            v => v.PlaceCaret(Offset(Linked, "wo"), null),
            v => moves.Add(v.MoveToLink(forward: false)));

        Assert.Equal([LinkMove.Moved, LinkMove.Moved], moves);
        Assert.Equal(Offset(Linked, "two"), result.Snaps[1].Offset);
        Assert.Equal(Offset(Linked, "one"), result.Snaps[3].Offset);
    }

    [Fact]
    public void Tab_skips_the_links_that_a_folded_section_hides()
    {
        RenderedView view = View(Linked);
        var moves = new List<LinkMove>();

        Result result = Run(
            view,
            v => v.PlaceCaret(Offset(Linked, "Section"), null),
            v => v.HandleKey(RenderedView.ToggleFoldKey),
            v => v.PlaceCaret(Offset(Linked, "two"), null),
            v => moves.Add(v.MoveToLink(forward: true)));

        Assert.Equal([LinkMove.Moved], moves);
        Assert.Equal(Offset(Linked, "four"), result.Snaps[^1].Offset);
        Assert.Equal("#section", view.CaretLink?.Url);
    }

    [Fact]
    public void A_document_with_no_link_gives_NoLinks_and_the_caret_stays()
    {
        RenderedView view = View("# Plain\n\nText only.\n");
        LinkMove? move = null;
        int before = -1;

        Result result = Run(view, v => before = v.CursorOffset, v => move = v.MoveToLink(forward: true));

        Assert.Equal(LinkMove.NoLinks, move);
        Assert.Equal(before, result.Snaps[1].Offset);
    }

    [Fact]
    public void The_caret_link_is_the_link_under_the_caret_and_null_off_a_link()
    {
        RenderedView view = View(Linked);
        var urls = new List<string?>();

        Run(
            view,
            v => v.PlaceCaret(Offset(Linked, "one"), null),
            v => urls.Add(v.CaretLink?.Url),
            v => v.PlaceCaret(Offset(Linked, "Guide"), null),
            v => urls.Add(v.CaretLink?.Url));

        Assert.Equal(["https://a.example", null], urls);
    }

    [Fact]
    public void A_Ctrl_click_on_a_link_places_the_caret_and_raises_LinkActivated_and_a_plain_click_only_places_it()
    {
        RenderedView view = View(Linked);
        var activated = new List<string>();
        view.LinkActivated += link => activated.Add(link.Url);

        Run(
            view,
            v => v.PlaceCaret(Offset(Linked, "two"), null),
            v => v.HandleMouse(ClickAtCaret(v, MouseFlags.Ctrl)),
            v => v.PlaceCaret(Offset(Linked, "Guide"), null),
            v => v.HandleMouse(ClickAtCaret(v, MouseFlags.None)));

        Assert.Equal(["#guide"], activated);
    }

    [Fact]
    public void A_Ctrl_click_off_a_link_raises_nothing()
    {
        RenderedView view = View(Linked);
        var activated = new List<string>();
        view.LinkActivated += link => activated.Add(link.Url);

        Run(view, v => v.PlaceCaret(Offset(Linked, "Guide"), null), v => v.HandleMouse(ClickAtCaret(v, MouseFlags.Ctrl)));

        Assert.Empty(activated);
    }

    [Fact]
    public void GoTo_unfolds_the_section_that_hides_the_caret_and_places_the_caret_on_it()
    {
        RenderedView view = View(Linked);

        Result result = Run(
            view,
            v => v.PlaceCaret(Offset(Linked, "Guide"), null),
            v => v.HandleKey(RenderedView.ToggleFoldKey),
            v => v.GoTo(Offset(Linked, "Section"), Offset(Linked, "Section")));

        Assert.Equal(Offset(Linked, "Section"), result.Snaps[^1].Offset);
        Assert.Contains("\u25BE Guide", result.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void GoTo_puts_the_heading_on_the_top_line_when_the_document_is_long_enough()
    {
        RenderedView view = View(Tall);

        Result result = Run(view, v => v.GoTo(Offset(Tall, "Target"), Offset(Tall, "Target")));

        Assert.Equal(0, result.Snaps[^1].Line - result.Snaps[^1].Top);
        Assert.StartsWith("▾ Target", result.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    // A press at the caret's cell, as a click on it would be, with the modifiers given.
    private static Mouse ClickAtCaret(RenderedView view, MouseFlags modifiers) =>
        new() { Flags = MouseFlags.LeftButtonPressed | modifiers, Position = new Point(view.CursorColumn, view.CursorLine - view.TopLine) };
}
