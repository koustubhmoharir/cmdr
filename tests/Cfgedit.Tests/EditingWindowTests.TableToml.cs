using Cfgedit.Tui;
using Terminal.Gui.Input;

namespace Cfgedit.Tests;

/// <summary>
/// The table editor on TOML: T on an array of tables opens it, a cell edit writes its text, a missing cell is inserted in the column order,
/// <c>a</c> adds a row shaped like the one above, and Esc returns to the tree at the cell's node. Assertions are on the drawn rows and on the
/// document text, as in the JSON and XML table tests.
/// </summary>
public sealed partial class EditingWindowTests
{
    // Two items: the first has no tags cell, the second has no port cell, so both kinds of missing cell are in the table.
    private const string TomlItems = "[[items]]\nname = \"web\"\nport = 8080\n[[items]]\nname = \"db\"\ntags = \"x\"\n";

    [Fact]
    public void T_on_a_TOML_array_of_tables_draws_the_table_in_place_of_the_tree()
    {
        OpenedFile file = Open("settings.toml", TomlItems);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, new Key('T')));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("#  name  port  tags", Inside(screen, 1));
        Assert.Equal("0  web   8080  \x00B7", Inside(screen, 2));
        Assert.Equal("1  db    \x00B7     x", Inside(screen, 3));
        Assert.Contains("[table items]", screen.Text(0), StringComparison.Ordinal);
        Assert.False(session.Window.Tree.Visible);
        Assert.True(session.Window.Grid.Visible);
    }

    [Fact]
    public void Enter_on_a_TOML_cell_edits_it_and_writes_the_text()
    {
        OpenedFile file = Open("settings.toml", TomlItems);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, new Key('T'), Key.Enter),
            (_, w) => Keys(w, Key.End, Key.Backspace, Key.Backspace, Key.Backspace),
            (_, w) => Type(w, "Web"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("0  Web   8080  \x00B7", Inside(session.Screens[^1], 2));
        Assert.Equal("[[items]]\nname = \"Web\"\nport = 8080\n[[items]]\nname = \"db\"\ntags = \"x\"\n", DocumentText(session));
    }

    [Fact]
    public void A_missing_TOML_cell_is_inserted_in_the_column_order_with_the_kind_of_its_column()
    {
        OpenedFile file = Open("settings.toml", TomlItems);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, new Key('T'), Key.CursorDown, Key.CursorRight, Key.Enter),
            (_, w) => Type(w, "5432"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("1  db    5432  x", Inside(session.Screens[^1], 3));
        Assert.Equal("[[items]]\nname = \"web\"\nport = 8080\n[[items]]\nname = \"db\"\nport = 5432\ntags = \"x\"\n", DocumentText(session));
    }

    [Fact]
    public void a_adds_a_TOML_row_shaped_like_the_cursor_row_and_opens_the_editor_on_its_first_cell()
    {
        OpenedFile file = Open("settings.toml", TomlItems);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, new Key('T'), new Key('a')),
            (_, w) => Assert.True(w.Grid.IsEditing),
            (_, w) => Type(w, "new"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal(1, session.Window.Grid.CursorRow);
        Assert.Equal(
            "[[items]]\nname = \"web\"\nport = 8080\n[[items]]\nname = \"new\"\nport = 0\n[[items]]\nname = \"db\"\ntags = \"x\"\n",
            DocumentText(session));
    }

    [Fact]
    public void Esc_on_a_TOML_table_returns_to_the_tree_with_the_cursor_on_the_node_of_the_last_cell()
    {
        OpenedFile file = Open("settings.toml", TomlItems);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, new Key('T'), Key.CursorDown, Key.Esc));

        Assert.True(session.Window.Tree.Visible);
        Assert.False(session.Window.Grid.Visible);
        Assert.Equal("items[1].name", PathText.Format(session.Window.Controller.Document, session.Window.Tree.CursorNode!, null));
        Assert.Equal(TomlItems, DocumentText(session));
    }
}
