using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The Fold and Unfold entries of the context menu: the entry is there for the unit at the mouse, labelled by the unit's state, and choosing
/// it folds or unfolds that unit. A position with no unit has no fold entry.
/// </summary>
public sealed partial class MainWindowTests
{
    // Source lines: 0 "# Title", 2 "First paragraph.", 4 "## Part", 6 "Part text.". Title's section holds Part, so it hides lines 1 to 6.
    private const string Sectioned = "# Title\n\nFirst paragraph.\n\n## Part\n\nPart text.\n";

    [Fact]
    public void A_right_click_on_an_unfolded_heading_offers_Fold()
    {
        OpenedFile file = Opened("notes.md", Sectioned);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.RightButtonClicked, Position = new Point(3, 0), ScreenPosition = new Point(5, 1) }));

        // The entries are Copy text, Copy Markdown and Fold, from the menu's first row (row 1 of the screen).
        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("Copy Markdown", screen.Text(2), StringComparison.Ordinal);
        Assert.Contains("Fold", screen.Text(3), StringComparison.Ordinal);
        Assert.DoesNotContain("Unfold", screen.Text(3), StringComparison.Ordinal);
    }

    [Fact]
    public void Choosing_Fold_in_the_menu_folds_the_unit_at_the_mouse()
    {
        OpenedFile file = Opened("notes.md", Sectioned);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.RightButtonClicked, Position = new Point(3, 0), ScreenPosition = new Point(5, 1) }),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.CursorDown),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.CursorDown),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.Enter));

        // Copy text, Copy Markdown, then Fold: the third entry.
        Assert.Equal(Framed("▸ Title … 6 lines"), session.Screens[^1].Text(1));
    }

    [Fact]
    public void Choosing_Unfold_in_the_menu_on_a_folded_heading_unfolds_it()
    {
        OpenedFile file = Opened("notes.md", Sectioned);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.RightButtonClicked, Position = new Point(3, 0), ScreenPosition = new Point(5, 1) }),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.CursorDown),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.CursorDown),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.Enter),
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.RightButtonClicked, Position = new Point(3, 0), ScreenPosition = new Point(5, 1) }),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.CursorDown),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.CursorDown),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.Enter));

        Assert.Equal(Framed("▾ Title"), session.Screens[^1].Text(1));
    }

    [Fact]
    public void A_right_click_where_no_unit_is_has_no_fold_entry()
    {
        OpenedFile file = Opened("notes.md", "Just text, with no heading.\n");

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.RightButtonClicked, Position = new Point(3, 0), ScreenPosition = new Point(5, 1) }));

        // Rows 0 to 9 are the view and the menu; the hints line (row 11) names Space Fold.
        Assert.DoesNotContain(Enumerable.Range(0, 10), row => session.Screens[^1].Text(row).Contains("Fold", StringComparison.Ordinal));
    }
}
