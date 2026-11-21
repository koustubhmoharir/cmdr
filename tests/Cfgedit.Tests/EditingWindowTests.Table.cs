using System.Text;
using Cfgedit.Tui;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace Cfgedit.Tests;

/// <summary>
/// The table editor, as drawn: entering with T and its refusals, the layout (header, frozen index column, widths and caps, missing and nested
/// cells, truncation, horizontal scrolling), the edits (inline, multi-line, missing cells in column order, errors), the row operations, clearing,
/// leaving with Esc or into the tree at a nested cell, copying, and a table that stops applying. Table rows are read between the frame's
/// borders (<see cref="Inside"/>); the detail line and the hints are read whole. Assertions are on the drawn rows and on the document text.
/// </summary>
public sealed partial class EditingWindowTests
{
    private const string TableHints = "Enter Edit  a Row  d Clear  Esc Tree  ^S Save";
    private const string TableEditingHints = "Enter Commit  Esc Cancel  F4 Multi-line";

    // An array of objects: the columns are name, port and tags; the second row has tags, the first has no tags cell.
    private const string TableArray = "[\n  {\"name\": \"web\", \"port\": 8080},\n  {\"name\": \"db\", \"port\": 5432, \"tags\": \"x\"}\n]";

    // The second row has no port: its missing cell is between name and tags.
    private const string MiddleArray = "[\n  {\"name\": \"db\", \"port\": 5432, \"tags\": \"x\"},\n  {\"name\": \"web\", \"tags\": \"t\"}\n]";

    // The column a is a boolean in the first row and missing in the second, so a missing a cell is in a column of booleans.
    private const string BooleanArray = "[{\"a\": true, \"b\": 1}, {\"b\": 2}]";

    // A nested object: its cell shows {2} and is not edited in the table.
    private const string NestedArray = "[{\"name\": \"a\", \"meta\": {\"x\": 1, \"y\": 2}}]";

    private const string TableConfig = "<configuration>\n  <appSettings>\n    <add key=\"a\" value=\"1\"/>\n    <add key=\"b\" value=\"2\"/>\n  </appSettings>\n</configuration>";

    // Attributes: the second row has no b, which sits between a and c.
    private const string MiddleAttributes = "<root>\n  <item a=\"1\" b=\"2\" c=\"3\"/>\n  <item a=\"4\" c=\"6\"/>\n</root>";

    // ----- Entering -----

    [Fact]
    public void T_on_a_JSON_array_of_objects_draws_the_table_in_place_of_the_tree()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T')));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("#  name  port  tags", Inside(screen, 1));
        Assert.Equal("0  web   8080  \x00B7", Inside(screen, 2));
        Assert.Equal("1  db    5432  x", Inside(screen, 3));
        Assert.Contains("list.json [jsonc] [table $]", screen.Text(0), StringComparison.Ordinal);
        Assert.False(session.Window.Tree.Visible);
        Assert.True(session.Window.Grid.Visible);
        Assert.Equal("$[0].name  string  web", screen.Text(MessageRow));
        AssertTableHints(screen, 23, TableHints);
    }

    [Fact]
    public void T_on_a_scalar_array_shows_one_value_column()
    {
        OpenedFile file = Open("list.json", "[10, \"x\", true]");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T')));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("#  value", Inside(screen, 1));
        Assert.Equal("0  10", Inside(screen, 2));
        Assert.Equal("1  x", Inside(screen, 3));
        Assert.Equal("2  true", Inside(screen, 4));
        Assert.Equal("$[0]  number  10", screen.Text(MessageRow));
    }

    [Fact]
    public void T_on_an_XML_element_with_repeated_children_shows_them_as_rows_with_their_attributes()
    {
        OpenedFile file = Open("app.config", TableConfig);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, new Key('T')));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("#  key  value", Inside(screen, 1));
        Assert.Equal("1  a    1", Inside(screen, 2));
        Assert.Equal("2  b    2", Inside(screen, 3));
        Assert.Contains("[table /configuration/appSettings]", screen.Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void T_on_a_row_node_opens_its_table_with_the_cursor_on_that_row()
    {
        OpenedFile file = Open("app.config", TableConfig);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, Key.CursorDown, new Key('T')));

        Assert.Equal(1, session.Window.Grid.CursorRow);
        Assert.Equal("2  b    2", Inside(session.Screens[^1], 3));
    }

    [Theory]
    [InlineData("list.json", "{\"name\": \"web\"}", "no table here: not a list of objects or values")]
    [InlineData("list.json", "[1, {\"a\": 1}]", "no table here: not a list of objects or values")]
    [InlineData("list.json", "[]", "no table here: the list is empty")]
    [InlineData("data.xml", "<a><b/><c/></a>", "no table here: no repeated elements")]
    public void T_on_a_node_that_is_no_table_says_why(string name, string text, string message)
    {
        OpenedFile file = Open(name, text);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T')));

        Assert.Equal(message, session.Screens[^1].Text(MessageRow));
        Assert.True(session.Window.Tree.Visible);
        Assert.False(session.Window.Grid.Visible);
    }

    // ----- Drawing -----

    [Fact]
    public void The_table_at_40_by_12_keeps_the_header_the_rows_the_detail_line_and_the_hints_in_place()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(file, 40, 12, null, null, (_, w) => Keys(w, new Key('T')));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("#  name  port  tags", Inside(screen, 1));
        Assert.Equal("0  web   8080  \x00B7", Inside(screen, 2));
        Assert.Equal("1  db    5432  x", Inside(screen, 3));

        // At 12 rows the detail line is row 10 and the hints row 11. The hints are cut in their middle to the width, and the quit key stays.
        Assert.Equal("$[0].name  string  web", screen.Text(10));
        Assert.Equal("Enter Edit  a Row  d Clear  Esc \x2026^Q Quit", screen.Text(11));
    }

    [Fact]
    public void The_index_column_is_as_wide_as_its_largest_number_and_its_header_is_right_aligned()
    {
        OpenedFile file = Open("list.json", "[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T')));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal(" #  value", Inside(screen, 1));
        Assert.Equal(" 0  0", Inside(screen, 2));
        Assert.Equal("10  10", Inside(screen, 12));
    }

    [Fact]
    public void A_column_is_as_wide_as_its_widest_value_up_to_thirty_and_a_longer_value_is_cut_with_an_ellipsis()
    {
        string longValue = new string('a', 40);
        string wideValue = new string('a', 28) + "\x65E5\x672C";
        string emojiValue = string.Concat(Enumerable.Repeat("\U0001F642", 20));
        OpenedFile file = Open(
            "list.json",
            "[{\"id\": 1, \"text\": \"" + longValue + "\"}, {\"id\": 2, \"text\": \"" + wideValue + "\"}, {\"id\": 3, \"text\": \"" + emojiValue + "\"}]");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T')));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("#  id  text", Inside(screen, 1));

        // The text column is 30 wide: 29 columns of text and the ellipsis. A wide character that does not fit is left out whole.
        Assert.Equal("0  1   " + new string('a', 29) + "\x2026", Inside(screen, 2));
        Assert.Equal("1  2   " + new string('a', 28) + "\x2026", Inside(screen, 3));

        // Each emoji is two columns: fourteen of them fill 28, and the ellipsis is the 29th. The second column of a wide glyph reads
        // back as a space, so each emoji is followed by one in the row's text.
        Assert.Equal("2  3   " + string.Concat(Enumerable.Repeat("\U0001F642 ", 14)) + "\x2026", Inside(screen, 4));
    }

    [Fact]
    public void Nested_cells_are_drawn_dim_and_missing_cells_are_drawn_dim()
    {
        OpenedFile nested = Open("list.json", NestedArray);
        TerminalScreen screen = Run(nested, 80, 24, null, null, (_, w) => Keys(w, new Key('T'))).Screens[^1];
        Assert.Equal("0  a     {2}", Inside(screen, 2));
        Assert.True(screen.At("{2}").Style.HasFlag(TextStyle.Faint));

        OpenedFile missing = Open("list.json", TableArray);
        TerminalScreen dots = Run(missing, 80, 24, null, null, (_, w) => Keys(w, new Key('T'))).Screens[^1];
        Assert.True(dots.At("\x00B7").Style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public void Control_characters_in_a_value_are_drawn_as_the_row_glyphs()
    {
        // The value is x, a line feed, y, a tab and z: the line feed is drawn as the return-symbol glyph and the tab as an arrow.
        OpenedFile file = Open("list.json", "[{\"a\": \"x\\ny\\tz\"}]");

        TerminalScreen screen = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T'))).Screens[^1];

        Assert.Equal("#  a", Inside(screen, 1));
        Assert.Equal("0  x\x23CEy\x2192z", Inside(screen, 2));
    }

    [Fact]
    public void Columns_scroll_as_a_group_while_the_index_column_stays_and_the_cursor_cell_is_in_reverse_video()
    {
        OpenedFile file = Open(
            "list.json",
            "[{\"k1\": \"one-111111\", \"k2\": \"two-222222\", \"k3\": \"tre-333333\", \"k4\": \"fou-444444\", \"k5\": \"fiv-555555\", \"k6\": \"six-666666\"}]");

        Session session = Run(
            file,
            40,
            12,
            null,
            null,
            (_, w) => Keys(w, new Key('T')),
            (_, w) => Keys(w, Key.CursorRight, Key.CursorRight),
            (_, w) => Keys(w, Key.CursorRight));

        Assert.Equal("#  k1          k2          k3", Inside(session.Screens[1], 1));
        Assert.Equal("0  one-111111  two-222222  tre-333333", Inside(session.Screens[1], 2));

        // The cursor is on k3 after two moves, and still in view. The third move goes to k4, which scrolls the columns by one.
        Assert.Equal("#  k2          k3          k4", Inside(session.Screens[3], 1));
        Assert.Equal("0  two-222222  tre-333333  fou-444444", Inside(session.Screens[3], 2));
        Assert.Equal("fou-444444", ReversedCells(session.Screens[3]));
    }

    // ----- Editing -----

    [Fact]
    public void Enter_edits_a_cell_inline_and_the_field_runs_to_the_right_edge_of_the_table()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('T'), Key.Enter),
            (_, w) => Keys(w, Key.End, Key.Backspace, Key.Backspace, Key.Backspace),
            (_, w) => Type(w, "api"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("0  web", Inside(session.Screens[1], 2));
        AssertTableHints(session.Screens[1], 23, TableEditingHints);
        Assert.Equal("[\n  {\"name\": \"api\", \"port\": 8080},\n  {\"name\": \"db\", \"port\": 5432, \"tags\": \"x\"}\n]", DocumentText(session));
        Assert.True(session.Window.Grid.Visible);
    }

    [Fact]
    public void Ctrl_Enter_edits_a_value_with_a_line_break_in_the_multi_line_editor()
    {
        OpenedFile file = Open("list.json", TableArray);
        string? shown = null;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(
                    app,
                    "the multi-line editor",
                    v => v is MultilineEditor,
                    v =>
                    {
                        shown = ((MultilineEditor)v).Title;
                        ((MultilineEditor)v).Area.Text = "line1\nline2";
                    },
                    "OK");
                Keys(w, new Key('T'), Key.CursorDown, Key.CursorRight, Key.CursorRight, Key.Enter.WithCtrl);
                dialog.Finish();
            });

        Assert.Equal("Edit string $[1].tags", shown);
        Assert.Equal("[\n  {\"name\": \"web\", \"port\": 8080},\n  {\"name\": \"db\", \"port\": 5432, \"tags\": \"line1\\nline2\"}\n]", DocumentText(session));
    }

    [Fact]
    public void A_missing_cell_is_inserted_in_the_column_order()
    {
        OpenedFile file = Open("list.json", MiddleArray);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('T'), Key.CursorDown, Key.CursorRight, Key.Enter),
            (_, w) => Type(w, "9000"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("1  web   9000  t", Inside(session.Screens[^1], 3));
        Assert.Equal("[\n  {\"name\": \"db\", \"port\": 5432, \"tags\": \"x\"},\n  {\"name\": \"web\", \"port\": 9000, \"tags\": \"t\"}\n]", DocumentText(session));
    }

    [Fact]
    public void A_missing_attribute_is_inserted_in_the_column_order()
    {
        OpenedFile file = Open("app.config", MiddleAttributes);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('T'), Key.CursorDown, Key.CursorRight, Key.Enter),
            (_, w) => Type(w, "5"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("<root>\n  <item a=\"1\" b=\"2\" c=\"3\"/>\n  <item a=\"4\" b=\"5\" c=\"6\"/>\n</root>", DocumentText(session));
    }

    [Fact]
    public void An_invalid_value_keeps_the_field_open_with_its_error_on_the_detail_line()
    {
        OpenedFile file = Open("list.json", MiddleArray);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('T'), Key.CursorDown, Key.CursorRight, Key.Enter),
            (_, w) => Type(w, "abc"),
            (_, w) => Keys(w, Key.Enter));

        Assert.True(session.Window.Grid.IsEditing);
        Assert.Equal("abc", session.Window.Grid.EditorText);
        Assert.Equal("[\n  {\"name\": \"db\", \"port\": 5432, \"tags\": \"x\"},\n  {\"name\": \"web\", \"tags\": \"t\"}\n]", DocumentText(session));
        Assert.False(string.IsNullOrEmpty(session.Screens[^1].Text(MessageRow)));
    }

    [Fact]
    public void Enter_on_an_existing_true_cell_opens_the_type_menu_and_Esc_changes_nothing()
    {
        OpenedFile file = Open("list.json", BooleanArray);
        int? chosen = null;
        string? title = null;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(
                    app,
                    "the type menu",
                    v => v is ChoiceDialog,
                    v =>
                    {
                        chosen = ((ChoiceDialog)v).List.SelectedItem;
                        title = ((ChoiceDialog)v).Title;
                        v.NewKeyDownEvent(Key.Esc);
                    },
                    null);
                Keys(w, new Key('T'), Key.Enter);
                menu.Finish();
            });

        Assert.Equal("Type of $[0].a", title);
        Assert.Equal(2, chosen);
        Assert.Equal(BooleanArray, DocumentText(session));
        Assert.True(session.Window.Grid.Visible);
    }

    [Fact]
    public void Enter_on_an_existing_true_cell_and_picking_null_changes_its_type()
    {
        OpenedFile file = Open("list.json", BooleanArray);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(4), null);
                Keys(w, new Key('T'), Key.Enter);
                menu.Finish();
            });

        Assert.Equal("[{\"a\": null, \"b\": 1}, {\"b\": 2}]", DocumentText(session));
    }

    [Fact]
    public void A_missing_cell_in_a_boolean_column_opens_the_type_menu_with_the_column_kind_chosen_and_inserts_the_kind_picked()
    {
        OpenedFile file = Open("list.json", BooleanArray);
        int? chosen = null;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(
                    app,
                    "the type menu",
                    v => v is ChoiceDialog,
                    v =>
                    {
                        chosen = ((ChoiceDialog)v).List.SelectedItem;
                        ((ChoiceDialog)v).List.SelectedItem = 3;
                        v.NewKeyDownEvent(Key.Enter);
                    },
                    null);
                Keys(w, new Key('T'), Key.CursorDown, Key.Enter);
                menu.Finish();
            });

        Assert.Equal(2, chosen);
        Assert.Equal("[{\"a\": true, \"b\": 1}, {\"a\": false, \"b\": 2}]", DocumentText(session));
    }

    [Fact]
    public void Esc_on_the_type_menu_of_a_missing_boolean_cell_inserts_nothing()
    {
        OpenedFile file = Open("list.json", BooleanArray);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, v => v.NewKeyDownEvent(Key.Esc), null);
                Keys(w, new Key('T'), Key.CursorDown, Key.Enter);
                menu.Finish();
            });

        Assert.Equal(BooleanArray, DocumentText(session));
        Assert.False(session.Window.Grid.IsEditing);
    }

    [Fact]
    public void A_missing_boolean_cell_picked_as_a_string_opens_the_inline_editor_for_its_text()
    {
        OpenedFile file = Open("list.json", BooleanArray);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(0), null);
                Keys(w, new Key('T'), Key.CursorDown, Key.Enter);
                menu.Finish();
            },
            (_, w) => Assert.True(w.Grid.IsEditing),
            (_, w) => Type(w, "x"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("[{\"a\": true, \"b\": 1}, {\"a\": \"x\", \"b\": 2}]", DocumentText(session));
    }

    [Fact]
    public void A_missing_boolean_cell_picked_as_a_number_opens_the_inline_editor_on_zero()
    {
        OpenedFile file = Open("list.json", BooleanArray);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(1), null);
                Keys(w, new Key('T'), Key.CursorDown, Key.Enter);
                menu.Finish();
            },
            (_, w) => Keys(w, Key.End, Key.Backspace),
            (_, w) => Type(w, "7"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("[{\"a\": true, \"b\": 1}, {\"a\": 7, \"b\": 2}]", DocumentText(session));
    }

    [Fact]
    public void Enter_on_the_index_column_says_to_select_a_cell()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T'), Key.CursorLeft, Key.Enter));

        Assert.Equal("select a cell", session.Screens[^1].Text(MessageRow));
        Assert.False(session.Window.Grid.IsEditing);
    }

    // ----- Rows -----

    [Fact]
    public void a_adds_a_row_after_the_cursor_and_opens_the_editor_on_its_first_cell()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('T'), new Key('a')),
            (_, w) => Assert.True(w.Grid.IsEditing),
            (_, w) => Type(w, "new"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal(1, session.Window.Grid.CursorRow);

        // A new object is written across lines, as Core lays out an object added to an array. Its number is a copy's empty value, 0.
        Assert.Equal(
            "[\n  {\"name\": \"web\", \"port\": 8080},\n  {\n    \"name\": \"new\",\n    \"port\": 0\n  },\n  {\"name\": \"db\", \"port\": 5432, \"tags\": \"x\"}\n]",
            DocumentText(session));
    }

    [Fact]
    public void A_adds_a_row_before_the_cursor()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('T'), new Key('A')),
            (_, w) => Type(w, "new"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal(0, session.Window.Grid.CursorRow);
        Assert.Equal(
            "[\n  {\n    \"name\": \"new\",\n    \"port\": 0\n  },\n  {\"name\": \"web\", \"port\": 8080},\n  {\"name\": \"db\", \"port\": 5432, \"tags\": \"x\"}\n]",
            DocumentText(session));
    }

    [Fact]
    public void d_on_the_index_column_deletes_the_row_and_the_columns_it_alone_had()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T'), Key.CursorDown, Key.CursorLeft, new Key('d')));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("#  name  port", Inside(screen, 1));
        Assert.Equal("0  web   8080", Inside(screen, 2));
        Assert.Equal("[\n  {\"name\": \"web\", \"port\": 8080}\n]", DocumentText(session));
    }

    [Fact]
    public void Alt_Up_moves_the_row_and_the_cursor_goes_with_it()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T'), Key.CursorDown, Key.CursorUp.WithAlt));

        Assert.Equal(0, session.Window.Grid.CursorRow);
        Assert.Equal("0  db    5432  x", Inside(session.Screens[^1], 2));
        Assert.Equal("[\n  {\"name\": \"db\", \"port\": 5432, \"tags\": \"x\"},\n  {\"name\": \"web\", \"port\": 8080}\n]", DocumentText(session));
    }

    [Fact]
    public void Undo_takes_back_an_added_row_in_one_step()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('T'), new Key('a'), Key.Esc),
            (_, w) => Keys(w, Key.Z.WithCtrl),
            (_, w) => Keys(w, Key.Z.WithCtrl));

        Assert.Equal(TableArray, DocumentText(session));
        Assert.Equal("Nothing to undo", session.Screens[^1].Text(MessageRow));
        Assert.True(session.Window.Grid.Visible);
    }

    // ----- Clearing and leaving -----

    [Fact]
    public void d_on_a_cell_removes_its_member_and_d_on_a_missing_cell_says_it_is_empty()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('T'), Key.CursorRight, new Key('d')),
            (_, w) => Keys(w, Key.CursorRight, new Key('d')));

        Assert.Equal("[\n  {\"name\": \"web\"},\n  {\"name\": \"db\", \"port\": 5432, \"tags\": \"x\"}\n]", DocumentText(session));
        Assert.Equal("the cell is already empty", session.Screens[^1].Text(MessageRow));
    }

    [Fact]
    public void Enter_on_a_nested_cell_leaves_the_table_for_the_tree_at_that_node()
    {
        OpenedFile file = Open("list.json", NestedArray);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T'), Key.CursorRight, Key.Enter));

        Assert.True(session.Window.Tree.Visible);
        Assert.False(session.Window.Grid.Visible);
        Assert.Equal("$[0].meta", PathText.Format(session.Window.Controller.Document, session.Window.Tree.CursorNode!, null));
        Assert.DoesNotContain("[table", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Esc_returns_to_the_tree_with_the_cursor_on_the_node_of_the_last_focused_cell()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T'), Key.CursorDown, Key.Esc));

        Assert.True(session.Window.Tree.Visible);
        Assert.Equal("$[1].name", PathText.Format(session.Window.Controller.Document, session.Window.Tree.CursorNode!, null));
        Assert.DoesNotContain("[table", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void Leaving_the_table_keeps_the_tree_collapsed_set_and_the_cursor_goes_to_the_folded_row()
    {
        OpenedFile file = Open("list.json", TableArray);
        int folded = -1;
        int afterLeaving = -1;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.Space, Key.CursorUp),
            (_, w) =>
            {
                folded = w.Tree.Collapsed.Count;
                Keys(w, new Key('T'));
            },
            (_, w) =>
            {
                Keys(w, Key.Esc);
                afterLeaving = w.Tree.Collapsed.Count;
            });

        Assert.Equal(1, folded);
        Assert.Equal(folded, afterLeaving);
        Assert.Equal("$[0]", PathText.Format(session.Window.Controller.Document, session.Window.Tree.CursorNode!, null));
    }

    [Fact]
    public void A_table_that_stops_applying_closes_with_its_message()
    {
        OpenedFile file = Open("app.config", "<root>\n  <item a=\"1\"/>\n  <item a=\"2\"/>\n  <item a=\"3\"/>\n</root>");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('T'), Key.CursorLeft, new Key('d')),
            (_, w) => Keys(w, new Key('d')));

        Assert.True(session.Window.Tree.Visible);
        Assert.False(session.Window.Grid.Visible);
        Assert.Equal("the table no longer applies here", session.Screens[^1].Text(MessageRow));
        Assert.DoesNotContain("[table", session.Screens[^1].Text(0), StringComparison.Ordinal);
    }

    // ----- Keys without a table -----

    [Fact]
    public void y_copies_the_cell_value_and_Y_its_path()
    {
        OpenedFile file = Open("list.json", TableArray);
        var written = new List<string>();

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('T')),
            (_, w) =>
            {
                w.WriteRaw = written.Add;
                Keys(w, new Key('y'));
            },
            (_, w) => Keys(w, new Key('Y')));

        Assert.Equal(new[] { Osc52.Build("web"), Osc52.Build("$[0].name") }, written);
        Assert.Equal("Copied path", session.Screens[^1].Text(MessageRow));
    }

    [Fact]
    public void The_search_keys_say_they_are_not_available_in_a_table()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T'), new Key('/')));

        Assert.Equal("not available in a table", session.Screens[^1].Text(MessageRow));
        Assert.False(session.Window.Tree.Visible);
    }

    [Fact]
    public void The_cursor_row_is_tinted_and_the_other_rows_are_not()
    {
        OpenedFile file = Open("list.json", TableArray);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('T')));

        // Column 0 is the frame's border, so the index column is column 1: row 2 is the cursor's row and row 3 is not.
        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("0", screen.Cells[2][1].Grapheme);
        Assert.Equal("1", screen.Cells[3][1].Grapheme);
        Assert.NotEqual(screen.Cells[3][1].Attribute.Background, screen.Cells[2][1].Attribute.Background);
    }

    // ----- Helpers -----

    private static void AssertTableHints(TerminalScreen screen, int row, string left)
    {
        string text = screen.Text(row);
        Assert.StartsWith(left, text, StringComparison.Ordinal);
        Assert.EndsWith(QuitHint, text, StringComparison.Ordinal);
    }

    private static string DocumentText(Session session) => Encoding.UTF8.GetString(session.Window.Controller.Document.Source.Encode());

    /// <summary>The text of the cells drawn in reverse video.</summary>
    private static string ReversedCells(TerminalScreen screen) => string.Concat(
        screen.Cells.SelectMany(row => row.Where(cell => cell.Attribute.Style.HasFlag(TextStyle.Reverse)).Select(cell => cell.Grapheme)));
}
