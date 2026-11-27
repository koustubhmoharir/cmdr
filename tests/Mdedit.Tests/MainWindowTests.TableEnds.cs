using System.Drawing;
using Mdedit.Tui;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// The end stop of a table cell and a selection over styled text, through the window: Right reaches the stop after a cell's text, and typing or
/// Backspace there edits the cell, in an aligned and in a minimal table; Tab still lands on the next cell's first stop; Ctrl+T maps the stop both
/// ways; and a double-click on a bold word, then Backspace or typing, removes or replaces the word with its markers.
/// </summary>
public sealed partial class MainWindowTests
{
    // An aligned table: its columns are as wide as their text, so an edit rewrites every line at the width the cell needs.
    private const string AlignedOne = "| one | two |\n| --- | --- |\n| 1   | 2   |\n";

    [Fact]
    public void Right_three_times_from_the_first_letter_of_a_minimal_cell_reaches_its_end_and_typing_there_appends_to_the_cell()
    {
        OpenedFile file = Opened("notes.md", Wide);
        int caret = -1;

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Wide, "one"), null),
            (_, window) =>
            {
                window.NewKeyDownEvent(Key.CursorRight);
                window.NewKeyDownEvent(Key.CursorRight);
                window.NewKeyDownEvent(Key.CursorRight);
                caret = window.View.CursorOffset;
            },
            (_, window) => window.NewKeyDownEvent(new Key('x')));

        // The third Right stops after the "e", at the end of the cell's text, before the padding; the table is not aligned, so only the cell changes.
        Assert.Equal(Index(Wide, "one") + 3, caret);
        Assert.Equal(Wide.Replace("| one |", "| onex |", StringComparison.Ordinal), session.Window.Editor.Buffer.Text);
        Assert.Equal(Index(Wide, "one") + 4, session.Window.View.CursorOffset);
    }

    [Fact]
    public void Typing_at_the_end_of_a_cell_in_an_aligned_table_rewrites_the_table_aligned()
    {
        OpenedFile file = Opened("notes.md", AlignedOne);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(AlignedOne, "one"), null),
            (_, window) =>
            {
                for (int step = 0; step < 3; step++)
                {
                    window.NewKeyDownEvent(Key.CursorRight);
                }
            },
            (_, window) => window.NewKeyDownEvent(new Key('x')));

        // "onex" is four wide, so the first column is four wide on every line; the caret is after the "x" at 6.
        Assert.Equal("| onex | two |\n| ---- | --- |\n| 1    | 2   |\n", session.Window.Editor.Buffer.Text);
        Assert.Equal(6, session.Window.View.CursorOffset);
    }

    [Fact]
    public void Backspace_at_the_end_of_a_cell_in_an_aligned_table_removes_its_last_letter()
    {
        OpenedFile file = Opened("notes.md", AlignedOne);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(AlignedOne, "one"), null),
            (_, window) =>
            {
                for (int step = 0; step < 3; step++)
                {
                    window.NewKeyDownEvent(Key.CursorRight);
                }
            },
            (_, window) => window.NewKeyDownEvent(Key.Backspace));

        // The first column stays three wide for its delimiter, so the table is written at that width: "on" is padded to it.
        Assert.Equal("| on  | two |\n| --- | --- |\n| 1   | 2   |\n", session.Window.Editor.Buffer.Text);
        Assert.Equal(4, session.Window.View.CursorOffset);
    }

    [Fact]
    public void Tab_from_a_cell_still_lands_on_the_first_stop_of_the_next_cell()
    {
        OpenedFile file = Opened("notes.md", Wide);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(Index(Wide, "one"), null),
            (_, window) => window.NewKeyDownEvent(Key.Tab));

        Assert.Equal(Index(Wide, "two"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void Ctrl_T_maps_the_end_stop_of_a_cell_to_the_source_both_ways()
    {
        OpenedFile file = Opened("notes.md", Wide);
        int stop = Index(Wide, "one") + 3;
        int source = -1;
        int view = -1;

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.PlaceCaret(stop, null),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) =>
            {
                source = window.Editor.CaretOffset;
                window.Editor.CaretOffset = stop;
            },
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => view = window.View.CursorOffset);

        Assert.Equal(stop, source);
        Assert.Equal(stop, view);
        Assert.Equal(stop, session.Window.Editor.CaretOffset);
    }

    [Fact]
    public void A_double_click_on_a_bold_word_and_Backspace_removes_the_word_with_its_markers()
    {
        OpenedFile file = Opened("notes.md", Strong);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonDoubleClicked, Position = new Point(8, 0) }),
            (_, window) => window.NewKeyDownEvent(Key.Backspace));

        // "bold" is 7..10 and its closing stars 11 and 12. Both spaces around the span stay, so the words are two spaces apart.
        Assert.Equal("Some  word here.\n", session.Window.Editor.Buffer.Text);
        Assert.Equal(5, session.Window.View.CursorOffset);
    }

    [Fact]
    public void Typing_over_a_double_clicked_bold_word_replaces_the_word_and_its_markers()
    {
        OpenedFile file = Opened("notes.md", Strong);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonDoubleClicked, Position = new Point(8, 0) }),
            (_, window) => window.NewKeyDownEvent(new Key('X')));

        Assert.Equal("Some X word here.\n", session.Window.Editor.Buffer.Text);
    }
}
