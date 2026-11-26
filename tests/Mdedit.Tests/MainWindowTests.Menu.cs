using Mdedit.Core.Render;
using Mdedit.Tui;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The context menu of the view: a right-click opens it at the mouse with the copy entries, and choosing an entry copies as the copy keys
/// do. The entries are chosen with the keyboard, which the menu takes while it is open.
/// </summary>
public sealed partial class MainWindowTests
{
    [Fact]
    public void A_right_click_opens_the_menu_at_the_mouse_with_the_copy_entries()
    {
        OpenedFile file = Opened("notes.md", Copyable);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.RightButtonClicked, Position = new Point(3, 2), ScreenPosition = new Point(5, 3) }));

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("Copy text", screen.Text(3), StringComparison.Ordinal);
        Assert.Contains("Copy Markdown", screen.Text(4), StringComparison.Ordinal);
    }

    [Fact]
    public void Choosing_Copy_Markdown_in_the_menu_copies_the_selection_as_Markdown()
    {
        OpenedFile file = Opened("notes.md", Copyable);
        var written = new List<string>();
        int bold = Copyable.IndexOf("bold", StringComparison.Ordinal);

        Session session = Run(
            file,
            40,
            12,
            (_, window) =>
            {
                window.WriteRaw = written.Add;
                window.View.SelectRange(new SourceRange(bold, bold + 4));
                // The right-click is on "bold", inside the selection, so the selection stays.
                window.View.HandleMouse(new Mouse { Flags = MouseFlags.RightButtonClicked, Position = new Point(8, 2), ScreenPosition = new Point(5, 3) });
            },
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.CursorDown),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.Enter));

        Assert.Equal(new[] { Osc52.Build("**bold**") }, written);
        Assert.Equal(MainWindow.CopiedMarkdown, session.Screens[^1].Text(10));
    }

    [Fact]
    public void The_menu_key_opens_the_menu_at_the_cursor()
    {
        OpenedFile file = Opened("notes.md", Copyable);

        Session session = Run(file, 40, 12, (_, window) => window.NewKeyDownEvent(Key.F10.WithShift));

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("Copy text", screen.Text(1), StringComparison.Ordinal);
        Assert.Contains("Copy Markdown", screen.Text(2), StringComparison.Ordinal);
    }

    [Fact]
    public void A_right_click_outside_the_selection_clears_it_and_places_the_cursor()
    {
        OpenedFile file = Opened("notes.md", Copyable);
        int bold = Copyable.IndexOf("bold", StringComparison.Ordinal);

        Session session = Run(
            file,
            40,
            12,
            (_, window) =>
            {
                window.View.SelectRange(new SourceRange(bold, bold + 4));
                window.View.HandleMouse(new Mouse { Flags = MouseFlags.RightButtonClicked, Position = new Point(0, 0), ScreenPosition = new Point(5, 3) });
            });

        Assert.Null(session.Window.View.SelectionRange);
        Assert.Equal(Copyable.IndexOf("Title", StringComparison.Ordinal), session.Window.View.CursorOffset);
    }
}
