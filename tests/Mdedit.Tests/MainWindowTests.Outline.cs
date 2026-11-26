using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Editor.Document.Folding;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The outline of the window, in both modes: Alt+O, and <c>o</c> in view mode, open the outline over the headings; Enter or a double-click goes
/// to the chosen heading (the view's caret and top line, or the editor's caret with its folds opened); Esc changes nothing; a document with no
/// headings says so; and the external-change check waits while the outline is open. Dialogs are answered by <see cref="DialogWatch"/>.
/// </summary>
public sealed partial class MainWindowTests
{
    // A document with three headings, the second and third under the first, so that the first one's fold hides them.
    private const string Outlined = "# Install\n\nIntro text.\n\n## Linux\n\n- step one\n- step two\n\n## Windows\n\n- item a\n- item b\n- item c\n- item d\n- item e\n- item f\n";

    // The outline's rows, as the list shows them.
    private static readonly string[] OutlinedRows = ["Install", "  Linux", "  Windows"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void In_view_mode_o_and_Alt_O_open_the_outline_listing_every_heading(bool alt)
    {
        OpenedFile file = Opened("notes.md", Outlined);
        List<string> rows = [];
        string? title = null;

        Session session = Run(
            file,
            40,
            12,
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog =>
                {
                    rows.AddRange(dialog.Rows.Select(row => row.Label));
                    title = dialog.Title;
                });
                window.NewKeyDownEvent(alt ? MainWindow.OutlineKey : new Key('o'));
                outline.Finish();
            });

        Assert.Equal(OutlinedRows, rows);
        Assert.Equal("Outline", title);
        Assert.Equal(Index(Outlined, "Install"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void In_edit_mode_Alt_O_opens_the_outline_and_o_is_text_and_opens_nothing()
    {
        OpenedFile file = Opened("notes.md", Outlined);
        bool opened = false;

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (app, window) =>
            {
                using var outline = WatchOutline(app, _ => opened = true);
                int length = window.Editor.Buffer.TextLength;
                app.Keyboard.RaiseKeyDownEvent(new Key('o'));
                Assert.False(outline.Opened);
                Assert.Equal(length + 1, window.Editor.Buffer.TextLength);
            });

        Assert.False(opened);
        Assert.True(session.Window.InEditMode);
    }

    [Fact]
    public void In_edit_mode_Alt_O_opens_the_outline()
    {
        OpenedFile file = Opened("notes.md", Outlined);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (app, window) =>
            {
                using var outline = WatchOutline(app, _ => { });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.True(session.Window.InEditMode);
        Assert.Equal(Index(Outlined, "Install"), session.Window.Editor.CaretOffset);
    }

    [Fact]
    public void Enter_in_view_mode_goes_to_the_chosen_heading_with_the_caret_and_the_top_line()
    {
        OpenedFile file = Opened("notes.md", Outlined);

        Session session = Run(
            file,
            40,
            12,
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog =>
                {
                    dialog.Filter.Text = "windows";
                    app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.Equal(Index(Outlined, "Windows"), session.Window.View.CursorOffset);
        Assert.Equal(Framed("▾ Windows"), session.Screens[^1].Text(1));
    }

    [Fact]
    public void Enter_in_view_mode_unfolds_the_section_that_holds_the_chosen_heading()
    {
        OpenedFile file = Opened("notes.md", Outlined);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.ToggleFold(window.View.Units[0]),
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog =>
                {
                    app.Keyboard.RaiseKeyDownEvent(Key.CursorDown);
                    app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.False(session.Window.View.IsFolded(session.Window.View.Units[0]));
        Assert.Equal(Index(Outlined, "Linux"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void Enter_in_edit_mode_puts_the_editor_caret_at_the_heading_and_unfolds_what_hides_it()
    {
        OpenedFile file = Opened("notes.md", Outlined);
        FoldingSection? top = null;

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) =>
            {
                top = TopFolding(window);
                top.IsFolded = true;
            },
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog =>
                {
                    Assert.True(top!.IsFolded, "the section must be folded before the outline goes into it");
                    app.Keyboard.RaiseKeyDownEvent(Key.CursorDown);
                    app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.True(session.Window.InEditMode);
        Assert.Equal(Index(Outlined, "## Linux"), session.Window.Editor.CaretOffset);
        Assert.False(top!.IsFolded);
    }

    [Fact]
    public void Esc_in_the_outline_changes_neither_the_caret_nor_the_top_line()
    {
        OpenedFile file = Opened("notes.md", Outlined);
        int caret = Index(Outlined, "item a");

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(caret, null),
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog =>
                {
                    dialog.Filter.Text = "install";
                    app.Keyboard.RaiseKeyDownEvent(Key.Esc);
                });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.Equal(caret, session.Window.View.CursorOffset);
        Assert.Equal(session.Screens[1].Text(1), session.Screens[^1].Text(1));
    }

    [Fact]
    public void Typing_in_the_filter_narrows_the_list_and_keeps_the_indent()
    {
        OpenedFile file = Opened("notes.md", Outlined);
        List<string> rows = [];
        int? selected = null;

        Run(
            file,
            40,
            12,
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog =>
                {
                    dialog.Filter.Text = "LINUX";
                    rows.AddRange(dialog.Rows.Select(row => row.Label));
                    selected = dialog.List.SelectedItem;
                });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.Equal(["  Linux"], rows);
        Assert.Equal(0, selected);
    }

    [Fact]
    public void The_outline_starts_on_the_section_the_caret_is_in()
    {
        OpenedFile file = Opened("notes.md", Outlined);
        int? selected = null;

        Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Outlined, "item b"), null),
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog => selected = dialog.List.SelectedItem);
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.Equal(2, selected);
    }

    [Fact]
    public void Down_and_End_move_the_list_while_the_filter_field_has_the_focus()
    {
        OpenedFile file = Opened("notes.md", Outlined);
        int? afterDown = null;
        int? afterEnd = null;
        int? afterHome = null;

        Run(
            file,
            40,
            12,
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog =>
                {
                    app.Keyboard.RaiseKeyDownEvent(Key.CursorDown);
                    afterDown = dialog.List.SelectedItem;
                    app.Keyboard.RaiseKeyDownEvent(Key.End);
                    afterEnd = dialog.List.SelectedItem;
                    app.Keyboard.RaiseKeyDownEvent(Key.Home);
                    afterHome = dialog.List.SelectedItem;
                });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.Equal(1, afterDown);
        Assert.Equal(2, afterEnd);
        Assert.Equal(0, afterHome);
    }

    [Fact]
    public void A_double_click_on_an_item_goes_to_its_heading()
    {
        OpenedFile file = Opened("notes.md", Outlined);

        Session session = Run(
            file,
            40,
            12,
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog =>
                {
                    ClickRow(app, dialog, 2, MouseFlags.LeftButtonClicked);
                    ClickRow(app, dialog, 2, MouseFlags.LeftButtonDoubleClicked);
                });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.Equal(Index(Outlined, "Windows"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void A_click_on_an_item_selects_it_and_Enter_goes_to_it()
    {
        OpenedFile file = Opened("notes.md", Outlined);
        int? selected = null;

        Session session = Run(
            file,
            40,
            12,
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog =>
                {
                    ClickRow(app, dialog, 1, MouseFlags.LeftButtonClicked);
                    selected = dialog.List.SelectedItem;
                    app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.Equal(1, selected);
        Assert.Equal(Index(Outlined, "Linux"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void A_filter_that_shows_no_heading_has_no_selection_and_Enter_does_nothing()
    {
        OpenedFile file = Opened("notes.md", Outlined);
        bool stillOpen = false;

        Session session = Run(
            file,
            40,
            12,
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog =>
                {
                    dialog.Filter.Text = "zzz";
                    Assert.Empty(dialog.Rows);
                    Assert.Null(dialog.List.SelectedItem);
                    app.Keyboard.RaiseKeyDownEvent(Key.Enter);
                    stillOpen = app.TopRunnableView == dialog;
                });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.True(stillOpen);
        Assert.Equal(Index(Outlined, "Install"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void A_document_with_no_headings_says_so_and_opens_no_outline()
    {
        OpenedFile file = Opened("notes.md", "Just some text.\n");

        Session session = Run(
            file,
            40,
            12,
            (app, window) =>
            {
                using var outline = WatchOutline(app, _ => { });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                Assert.False(outline.Opened);
            });

        Assert.StartsWith(MainWindow.NoHeadings, session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
    }

    [Fact]
    public void The_outline_is_drawn_with_its_filter_over_the_document()
    {
        OpenedFile file = Opened("notes.md", Outlined);
        TerminalScreen? screen = null;

        Run(
            file,
            40,
            12,
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog => screen = TerminalHarness.Snapshot(app, dialog, 40, 12));
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            });

        Assert.NotNull(screen);
        Assert.Contains("Outline", screen.Text(screen.RowOf("Outline")), StringComparison.Ordinal);
        Assert.Contains("Windows", screen.Text(screen.RowOf("Windows")), StringComparison.Ordinal);
    }

    [Fact]
    public void While_the_outline_is_open_the_external_check_waits_and_sees_the_change_after_it_closes()
    {
        OpenedFile file = OpenedBytes("notes.md", Bytes(Small));

        Session session = RunWith(
            file,
            40,
            12,
            null,
            null,
            (app, window) =>
            {
                using var outline = WatchOutline(app, dialog =>
                {
                    File.WriteAllText(file.Path, Changed);
                    window.CheckExternalChange();
                    app.Keyboard.RaiseKeyDownEvent(Key.Esc);
                });
                window.NewKeyDownEvent(MainWindow.OutlineKey);
                outline.Finish();
            },
            (_, window) => window.CheckExternalChange());

        // While the outline was open the check did nothing: the window still shows the first text, and the check after it closed reloads.
        Assert.Equal(Framed("\u25BE Install"), session.Screens[1].Text(1));
        Assert.Equal(Framed("\u25BE Two"), session.Screens[^1].Text(1));
    }

    // The fold of the top-level section, the first in document order: it hides the headings below it.
    private static FoldingSection TopFolding(MainWindow window) => window.Editor.FoldingManager!.AllFoldings.OrderBy(folding => folding.StartOffset).First();

    // Clicks a row of the outline's list through the application's mouse, at the row's screen position.
    private static void ClickRow(IApplication app, OutlineDialog dialog, int row, MouseFlags flags)
    {
        var screen = dialog.List.FrameToScreen();
        var position = new Point(screen.X + 1, screen.Y + row - dialog.List.Viewport.Y);
        app.Mouse.RaiseMouseEvent(new Mouse { Flags = flags, ScreenPosition = position });
    }

    // Watches for the outline. While it is open, the action runs. Then Esc closes it, unless the action has already closed it.
    private static DialogWatch WatchOutline(IApplication app, Action<OutlineDialog> action) =>
        new(app, "the outline", view => view is OutlineDialog, view =>
        {
            var dialog = (OutlineDialog)view;
            try
            {
                action(dialog);
            }
            finally
            {
                if (app.TopRunnableView == dialog)
                {
                    app.Keyboard.RaiseKeyDownEvent(Key.Esc);
                }

                if (app.TopRunnableView == dialog)
                {
                    app.RequestStop(dialog);
                }
            }
        }, null);
}
