using Mdedit.Core.Render;
using Mdedit.Core.Tables;
using Mdedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The table keys and the inline field of the window in view mode: F2 edits a cell in its field, Enter moves between rows (and adds one on the last row), Enter, Esc, Tab and Shift+Tab in the field, the
/// edit as one undo step with the caret on its cell, the refusals, the row and column moves, the table menu (Alt+T and the right-click), the
/// keys that edit mode leaves alone, the external-change check that waits for the field, and an aligned table that stays aligned.
/// </summary>
public sealed partial class MainWindowTests
{
    // A table whose lines are not all the same width, so that an edit writes only the cells that change.
    private const string Plain = "| a | b |\n|---|---|\n| 1 | 22 |\n| 3 | 4 |\n\nAfter.\n";

    // A table with a link in its first cell, under a heading.
    private const string LinkedTable = "# Top\n\n| [go](#top) | b |\n|---|---|\n| 1 | 22 |\n\nAfter.\n";

    // A table with a cell whose source differs from its text, for the field's source text.
    private const string Markup = "| **bold** | b |\n|---|---|\n| 1 | 22 |\n\nAfter.\n";

    // An aligned table: every line's pipes at the same columns.
    private const string Aligned = "| Name  | Qty |\n| :---- | --: |\n| apple |   3 |\n| pear  |  10 |\n\nAfter.\n";

    private const int TableWidth = 60;
    private const int TableHeight = 14;

    [Fact]
    public void Typing_at_the_start_of_a_cell_of_an_aligned_table_realigns_the_table_on_screen_and_in_the_text()
    {
        Session session = RunTable(
            Opened("notes.md", Aligned),
            (_, window) => window.View.PlaceCaret(Index(Aligned, "pear"), null),
            (_, window) => Type(window, "ZZZZZZ"));

        // The first column grows to fit the new text, and the table stays aligned.
        Assert.Equal(
            "| Name       | Qty |\n| :--------- | --: |\n| apple      |   3 |\n| ZZZZZZpear |  10 |\n\nAfter.\n",
            session.Window.Editor.Buffer.Text);
        Assert.True(TableSource.Parse(session.Window.Editor.Buffer.Text, session.Window.View.Document.Blocks.Single(block => block.Table is not null)).IsAligned);

        // On the screen the dividers of the rows fall in the same columns.
        TerminalScreen screen = session.Screens[^1];
        int apple = screen.RowOf("apple");
        int pear = screen.RowOf("ZZZZZZpear");
        Assert.Equal(Dividers(screen.Text(apple)), Dividers(screen.Text(pear)));
        Assert.Equal(Dividers(screen.Text(screen.RowOf("Name"))), Dividers(screen.Text(apple)));
    }

    // The columns of the vertical dividers in a row of the screen.
    private static int[] Dividers(string row) => row.Select((c, column) => (c, column)).Where(cell => cell.c == '\u2502').Select(cell => cell.column).ToArray();

    [Fact]
    public void Enter_on_the_last_row_adds_a_row_below_and_moves_the_caret_into_it_as_one_undo_step()
    {
        string added = string.Empty;
        RenderedView.TableCell? caret = null;
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "3"), null),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) =>
            {
                added = window.Editor.Buffer.Text;
                caret = window.View.TableCellAtCaret();
            },
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        string[] lines = added.Split('\n');
        Assert.Equal("| 3 | 4 |", lines[3]);
        Assert.Equal("After.", lines[^2]);
        Assert.Equal(new RenderedView.TableCell(0, 3, 0), caret);
        Assert.Equal(Plain, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void F2_on_a_table_cell_opens_the_field_with_the_cells_source_text()
    {
        Session session = RunTable(
            Opened("notes.md", Markup),
            (_, window) => window.View.PlaceCaret(Index(Markup, "bold"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2));

        // The view shows the cell's text, "bold"; the field shows its source, "**bold**", on the same row.
        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("**bold**", screen.Text(screen.RowOf("**bold**")), StringComparison.Ordinal);
    }

    [Fact]
    public void F2_shows_an_escaped_pipe_as_a_pipe_so_that_a_commit_without_a_change_keeps_the_source()
    {
        const string escaped = "| a\\|b | b |\n|---|---|\n| 1 | 22 |\n\nAfter.\n";
        Session session = RunTable(
            Opened("notes.md", escaped),
            (_, window) => window.View.PlaceCaret(Index(escaped, "a"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => window.NewKeyDownEvent(Key.Enter));

        // The field holds "a|b"; committing it unchanged writes nothing, so the source keeps its escaped pipe.
        Assert.Equal(escaped, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void F2_on_an_empty_cell_opens_an_empty_field_and_a_commit_fills_the_cell()
    {
        const string withEmpty = "| a |  |\n|---|---|\n| 1 | 2 |\n\nAfter.\n";
        Session session = RunTable(
            Opened("notes.md", withEmpty),
            (_, window) => Assert.True(window.View.PlaceTableCell(0, 0, 1)),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => Type(window, "Z"),
            (_, window) => window.NewKeyDownEvent(Key.Enter));

        Assert.StartsWith("| a | Z", session.Window.Editor.Buffer.Text.Split('\n')[0], StringComparison.Ordinal);
        Assert.Equal(new RenderedView.TableCell(0, 0, 1), session.Window.View.TableCellAtCaret());
    }

    [Fact]
    public void Enter_on_a_table_cell_moves_the_caret_down_a_row_and_opens_no_field()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "1"), null),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => Type(window, "Z"));

        // The caret moves to the cell below; the text is not changed by Enter, and a typed character goes into that cell, not into a field.
        Assert.Equal(new RenderedView.TableCell(0, 2, 0), session.Window.View.TableCellAtCaret());
        Assert.Equal("| a | b |\n|---|---|\n| 1 | 22 |\n| Z3 | 4 |\n\nAfter.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Ctrl_Enter_on_a_link_in_a_table_follows_the_link_and_opens_no_field()
    {
        Session session = RunTable(
            Opened("notes.md", LinkedTable),
            (_, window) => window.View.PlaceCaret(Index(LinkedTable, "go"), null),
            (_, window) => window.NewKeyDownEvent(Key.Enter.WithCtrl));

        // The anchor goes to the heading's text, and the link's cell does not open the field.
        Assert.Equal(Index(LinkedTable, "Top"), session.Window.View.CursorOffset);
        Assert.Equal(LinkedTable, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Committing_the_field_writes_the_cell_as_one_undo_step_and_the_caret_lands_on_the_cell()
    {
        string edited = string.Empty;
        int caret = -1;
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "22"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => ReplaceFieldText(window, "9"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) =>
            {
                edited = window.Editor.Buffer.Text;
                caret = window.View.CursorOffset;
            },
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        Assert.Contains("| 1 | 9", edited, StringComparison.Ordinal);
        Assert.DoesNotContain("22", edited, StringComparison.Ordinal);
        Assert.Equal(edited.IndexOf('9'), caret);
        Assert.Equal(Plain, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_commit_without_a_change_adds_no_undo_step()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "22"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => ReplaceFieldText(window, "9"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.View.PlaceCaret(window.Editor.Buffer.Text.IndexOf('9'), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        // The one undo step is the first edit: the unchanged commit added none, so undo returns to the original text.
        Assert.Equal(Plain, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Esc_closes_the_field_and_writes_nothing()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "22"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => Type(window, "x"),
            (_, window) => window.NewKeyDownEvent(Key.Esc));

        Assert.Equal(Plain, session.Window.Editor.Buffer.Text);
        Assert.DoesNotContain("22x", AllText(session.Screens[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public void Tab_commits_and_edits_the_next_cell_across_the_row_end_and_Shift_Tab_the_previous()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "a"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => ReplaceFieldText(window, "A"),
            (_, window) => window.NewKeyDownEvent(Key.Tab),
            (_, window) => ReplaceFieldText(window, "B"),
            (_, window) => window.NewKeyDownEvent(Key.Tab),
            (_, window) => ReplaceFieldText(window, "X"),
            (_, window) => window.NewKeyDownEvent(Key.Enter));

        Assert.Equal("| A | B |\n|---|---|\n| X | 22 |\n| 3 | 4 |\n\nAfter.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Shift_Tab_commits_and_edits_the_previous_cell()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "22"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => window.NewKeyDownEvent(Key.Tab.WithShift),
            (_, window) => ReplaceFieldText(window, "Q"),
            (_, window) => window.NewKeyDownEvent(Key.Enter));

        Assert.Equal("| a | b |\n|---|---|\n| Q | 22 |\n| 3 | 4 |\n\nAfter.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Tab_after_the_last_cell_commits_and_closes_the_field_so_the_keys_reach_the_view_again()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "4"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => ReplaceFieldText(window, "7"),
            (_, window) => window.NewKeyDownEvent(Key.Tab),
            (_, window) => window.NewKeyDownEvent(Key.CursorUp));

        // The last cell is the row 2, column 1; with the field closed, Up moves the caret to the row above.
        Assert.Equal("| a | b |\n|---|---|\n| 1 | 22 |\n| 3 | 7 |\n\nAfter.\n", session.Window.Editor.Buffer.Text);
        Assert.Equal(new RenderedView.TableCell(0, 1, 1), session.Window.View.TableCellAtCaret());
    }

    [Fact]
    public void A_refused_commit_keeps_the_field_open_with_its_text_and_shows_the_message()
    {
        // A backslash before a typed pipe splits the cell, so the core refuses it (here in the first column, as its own tests do).
        const string refused = "a\\|b";
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "1"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => ReplaceFieldText(window, refused),
            (_, window) => window.NewKeyDownEvent(Key.Enter));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal(TableEdit.CouldNotEdit, screen.Text(screen.RowOf(TableEdit.CouldNotEdit)));
        Assert.Contains(refused, screen.Text(screen.RowOf(refused)), StringComparison.Ordinal);
        Assert.Equal(Plain, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Alt_Down_moves_the_row_and_the_caret_follows_it()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "1"), null),
            (_, window) => window.NewKeyDownEvent(Key.CursorDown.WithAlt));

        string moved = "| a | b |\n|---|---|\n| 3 | 4 |\n| 1 | 22 |\n\nAfter.\n";
        Assert.Equal(moved, session.Window.Editor.Buffer.Text);
        Assert.Equal(new RenderedView.TableCell(0, 2, 0), session.Window.View.TableCellAtCaret());
        Assert.Equal(Index(moved, "1"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void Alt_Up_onto_the_header_is_refused_with_its_message()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "1"), null),
            (_, window) => window.NewKeyDownEvent(Key.CursorUp.WithAlt));

        Assert.Equal(Plain, session.Window.Editor.Buffer.Text);
        Assert.Equal(TableEdit.HeaderStaysFirst, session.Screens[^1].Text(session.Screens[^1].RowOf(TableEdit.HeaderStaysFirst)));
    }

    [Fact]
    public void Alt_Right_moves_the_column_and_the_caret_follows_it()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "1"), null),
            (_, window) => window.NewKeyDownEvent(Key.CursorRight.WithAlt));

        Assert.StartsWith("| b | a |", session.Window.Editor.Buffer.Text, StringComparison.Ordinal);
        Assert.Equal(new RenderedView.TableCell(0, 1, 1), session.Window.View.TableCellAtCaret());
    }

    [Fact]
    public void Alt_Left_outside_a_table_is_still_Back_and_says_No_previous_file_with_no_file_to_go_back_to()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "After"), null),
            (_, window) => window.NewKeyDownEvent(Key.CursorLeft.WithAlt));

        Assert.Equal(MainWindow.NoPreviousFile, session.Screens[^1].Text(session.Screens[^1].RowOf(MainWindow.NoPreviousFile)));
    }

    [Fact]
    public void Alt_Left_in_a_table_moves_the_column_and_is_not_Back()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "1"), null),
            (_, window) => window.NewKeyDownEvent(Key.CursorRight.WithAlt),
            (_, window) => window.NewKeyDownEvent(Key.CursorLeft.WithAlt));

        Assert.Equal(new RenderedView.TableCell(0, 1, 0), session.Window.View.TableCellAtCaret());
        Assert.DoesNotContain(MainWindow.NoPreviousFile, session.Screens[^1].Text(session.Screens[^1].RowOf("notes.md")), StringComparison.Ordinal);
        Assert.Equal(Plain, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Alt_T_shows_the_row_items_and_the_Column_and_Align_column_submenus_and_no_column_operation_at_the_top()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "22"), null),
            (_, window) => window.NewKeyDownEvent(MainWindow.TableMenuKey));

        TerminalScreen screen = session.Screens[^1];
        string text = AllText(screen);
        Assert.Contains("Edit cell", text, StringComparison.Ordinal);
        Assert.Contains("Duplicate row", text, StringComparison.Ordinal);
        Assert.Contains("Column", text, StringComparison.Ordinal);
        Assert.Contains("Align column", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Insert column left", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Align column right", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Alt_T_menu_Align_column_right_in_its_submenu_runs_the_alignment()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "22"), null),
            (_, window) => window.NewKeyDownEvent(MainWindow.TableMenuKey),
            (app, _) => PressDown(app, 8),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.CursorRight),
            (app, _) => PressDown(app, 2),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.Enter));

        // The delimiter cell of the column becomes its alignment: the minimal rule writes only that cell.
        Assert.Equal(Plain.Replace("|---|---|", "|---|---:|", StringComparison.Ordinal), session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Alt_T_menu_Insert_column_left_in_the_Column_submenu_inserts_a_column_at_the_caret()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "1"), null),
            (_, window) => window.NewKeyDownEvent(MainWindow.TableMenuKey),
            (app, _) => PressDown(app, 7),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.CursorRight),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.Enter));

        // The header had two cells and three pipes; a column inserted on the left adds one pipe to each line.
        string[] lines = session.Window.Editor.Buffer.Text.Split('\n');
        Assert.Equal(4, lines[0].Count(c => c == '|'));
        Assert.Equal(4, lines[2].Count(c => c == '|'));
    }

    [Fact]
    public void The_right_click_menu_over_a_table_lists_the_copy_fold_row_and_submenu_items_within_the_screen()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.RightButtonClicked, Position = new Point(2, 3), ScreenPosition = new Point(3, 5) }));

        string text = AllText(session.Screens[^1]);
        Assert.Contains("Copy Markdown", text, StringComparison.Ordinal);
        Assert.Contains("Column", text, StringComparison.Ordinal);
        Assert.Contains("Align column", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Alt_T_menu_Delete_row_on_the_header_shows_its_refusal()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "a"), null),
            (_, window) => window.NewKeyDownEvent(MainWindow.TableMenuKey),
            (app, _) => PressDown(app, 4),
            (app, _) => app.Keyboard.RaiseKeyDownEvent(Key.Enter));

        Assert.Equal(Plain, session.Window.Editor.Buffer.Text);
        TerminalScreen screen = session.Screens[^1];
        Assert.Equal(TableEdit.HeaderCannotBeDeleted, screen.Text(screen.RowOf(TableEdit.HeaderCannotBeDeleted)));
    }

    [Fact]
    public void Alt_T_outside_a_table_says_Not_in_a_table()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "After"), null),
            (_, window) => window.NewKeyDownEvent(MainWindow.TableMenuKey));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal(MainWindow.NotInTable, screen.Text(screen.RowOf(MainWindow.NotInTable)));
    }

    [Fact]
    public void While_the_field_is_open_the_window_keys_wait_so_Ctrl_T_does_not_switch_the_mode()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "22"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl));

        Assert.False(session.Window.InEditMode);
        TerminalScreen screen = session.Screens[^1];
        Assert.DoesNotContain("[edit]", screen.Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void The_right_click_menu_over_a_table_has_the_table_items_after_the_copy_items()
    {
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.RightButtonClicked, Position = new Point(2, 3), ScreenPosition = new Point(3, 5) }));

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("Copy text", screen.Text(screen.RowOf("Copy text")), StringComparison.Ordinal);
        Assert.Contains("Insert row below", screen.Text(screen.RowOf("Insert row below")), StringComparison.Ordinal);
    }

    [Fact]
    public void Edit_mode_has_no_table_keys()
    {
        string before = string.Empty;
        Session session = RunTable(
            Opened("notes.md", Plain),
            (_, window) => window.View.PlaceCaret(Index(Plain, "22"), null),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => before = window.Editor.Buffer.Text,
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => window.NewKeyDownEvent(Key.CursorUp.WithAlt),
            (_, window) => window.NewKeyDownEvent(MainWindow.TableMenuKey));

        Assert.True(session.Window.InEditMode);
        Assert.Equal(before, session.Window.Editor.Buffer.Text);
        Assert.Equal(Plain, session.Window.Editor.Buffer.Text);
        Assert.DoesNotContain(MainWindow.NotInTable, session.Screens[^1].Text(session.Screens[^1].RowOf("notes.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_external_change_check_waits_while_the_field_is_open()
    {
        OpenedFile file = Opened("notes.md", Plain);
        const string changed = "Changed on disk.\n";
        string whileOpen = string.Empty;
        Session session = RunTable(
            file,
            (_, window) => window.View.PlaceCaret(Index(Plain, "22"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) =>
            {
                File.WriteAllText(file.Path, changed);
                window.CheckExternalChange();
                whileOpen = window.Editor.Buffer.Text;
            },
            (_, window) => window.NewKeyDownEvent(Key.Esc),
            (_, window) => window.CheckExternalChange());

        Assert.Equal(Plain, whileOpen);
        Assert.Equal(changed, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void An_aligned_table_stays_aligned_after_an_edit_through_the_window()
    {
        Session session = RunTable(
            Opened("notes.md", Aligned),
            (_, window) => window.View.PlaceCaret(Index(Aligned, "3"), null),
            (_, window) => window.NewKeyDownEvent(Key.F2),
            (_, window) => window.NewKeyDownEvent(Key.End),
            (_, window) => window.NewKeyDownEvent(Key.Backspace),
            (_, window) => Type(window, "12"),
            (_, window) => window.NewKeyDownEvent(Key.Enter));

        string[] lines = session.Window.Editor.Buffer.Text.Split('\n');
        Assert.Contains("| apple |  12 |", lines);
        int width = lines[0].Length;
        Assert.All(lines.Take(4), line => Assert.Equal(width, line.Length));
        Assert.True(TableSource.Parse(session.Window.Editor.Buffer.Text, session.Window.View.Document.Blocks.Single(block => block.Table is not null)).IsAligned);
    }

    // The window of the table tests: a size that shows a table and the message line.
    private static Session RunTable(OpenedFile file, params Action<IApplication, MainWindow>[] steps) =>
        Run(file, TableWidth, TableHeight, steps);

    // Replaces the field's text: the caret goes to the end, the backspaces clear the text, and the new text is typed.
    private static void ReplaceFieldText(MainWindow window, string text)
    {
        window.NewKeyDownEvent(Key.End);
        for (int i = 0; i < 8; i++)
        {
            window.NewKeyDownEvent(Key.Backspace);
        }

        Type(window, text);
    }

    // The text of every row of a screen, one after another.
    private static string AllText(TerminalScreen screen) => string.Concat(Enumerable.Range(0, screen.Cells.Count).Select(screen.Text));

    // Moves the menu's selection down by a number of entries, as the keyboard does.
    private static void PressDown(IApplication app, int count)
    {
        for (int i = 0; i < count; i++)
        {
            app.Keyboard.RaiseKeyDownEvent(Key.CursorDown);
        }
    }
}
