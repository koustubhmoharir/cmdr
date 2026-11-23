using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Cfgedit.Tests;

/// <summary>
/// The TOML format in the window: the rows and the cursor, the add dialog and the value editor that follows it, renames that reach every
/// piece of a table, the type menu and its refusals, deletes and moves, copying the shape of an array-of-tables item, the table editor's
/// message, and a new .toml file.
/// </summary>
public sealed partial class EditingWindowTests
{
    // ----- Rows and the cursor -----

    [Fact]
    public void A_toml_file_opens_with_the_cursor_on_the_root_and_the_root_row_reads_dollar_and_the_count()
    {
        OpenedFile file = Open("settings.toml", "a = 1\n[t]\nx = 2\n");

        Session session = Run(file, 80, 24, null, null);

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("▾ $ {…} (2)", Inside(screen, 1));
        Assert.StartsWith("$  table", screen.Text(22), StringComparison.Ordinal);
    }

    [Fact]
    public void Toml_rows_show_comments_tables_arrays_items_and_scalars_as_the_json_rows_do()
    {
        OpenedFile file = Open("settings.toml", "# top\na = \"x\"\n[t]\nlist = [1]\n[[items]]\nn = true\n");

        Session session = Run(file, 80, 24, null, null);

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("▾ $ {…} (3)", Inside(screen, 1));
        Assert.Equal("    # top", Inside(screen, 2));
        Assert.Equal("    a = x", Inside(screen, 3));
        Assert.Equal("  ▾ t: {…} (1)", Inside(screen, 4));
        Assert.Equal("    ▾ list: […] (1)", Inside(screen, 5));
        Assert.Equal("        0: 1", Inside(screen, 6));
        Assert.Equal("  ▾ items: […] (1)", Inside(screen, 7));
        Assert.Equal("    ▾ 0: {…} (1)", Inside(screen, 8));
        Assert.Equal("        n = true", Inside(screen, 9));
    }

    // ----- Add -----

    [Fact]
    public void The_add_kinds_of_a_toml_table_or_array_are_the_toml_kinds_in_order()
    {
        ConfigDocument doc = TreeFixtures.Parse("a = 1\n", "toml");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));

        Assert.Equal(
            new[]
            {
                AddKind.TomlString, AddKind.TomlInteger, AddKind.TomlFloat, AddKind.TomlBoolean, AddKind.TomlDateTime, AddKind.TomlTable,
                AddKind.TomlArray, AddKind.TomlComment,
            },
            AddKinds.For(doc, root));
        Assert.Equal("Value", AddKinds.TextLabel(AddKind.TomlDateTime));
        Assert.True(AddKinds.TakesText(AddKind.TomlDateTime));
        Assert.Equal("Key", AddKinds.NameLabel(root, AddKind.TomlString));
        Assert.Null(AddKinds.NameLabel(root, AddKind.TomlComment));
    }

    [Fact]
    public void An_array_of_tables_takes_only_a_table_and_an_inline_array_takes_every_kind()
    {
        ConfigDocument doc = TreeFixtures.Parse("list = [1]\n\n[[items]]\nname = \"a\"\n", "toml");
        DocNode items = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("items", 0));
        DocNode list = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("list", 0));

        Assert.True(TomlFormat.IsArrayOfTables(doc, items));
        Assert.False(TomlFormat.IsArrayOfTables(doc, list));
        Assert.Equal(new[] { AddKind.TomlTable }, AddKinds.For(doc, items));
        Assert.Equal(8, AddKinds.For(doc, list).Count);
    }

    [Fact]
    public void a_on_the_summary_of_an_empty_toml_file_asks_for_a_string_key_and_then_opens_its_value()
    {
        OpenedFile file = Open("settings.toml", string.Empty);
        string? title = null;
        AddKind? kind = null;

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
                    "the add dialog",
                    v => v is AddDialog,
                    v =>
                    {
                        title = ((AddDialog)v).Title;
                        kind = ((AddDialog)v).Kind;
                        ((AddDialog)v).NameField.Text = "name";
                    },
                    "OK");
                Keys(w, new Key('a'));
                dialog.Finish();
            },
            (_, w) => Type(w, "web"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("Add to $", title);
        Assert.Equal(AddKind.TomlString, kind);
        Assert.Equal("name = \"web\"\n", Text(session));
    }

    [Fact]
    public void a_on_an_item_of_an_array_of_tables_copies_its_shape_with_empty_values()
    {
        OpenedFile file = Open("settings.toml", "[[items]]\nname = \"x\"\nn = 1\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, new Key('a'), Key.Esc));

        Assert.Equal("[[items]]\nname = \"x\"\nn = 1\n[[items]]\nname = \"\"\nn = 0\n", Text(session));
    }

    // ----- Keys -----

    [Fact]
    public void Renaming_a_table_renames_its_header_and_the_headers_below_it()
    {
        OpenedFile file = Open("settings.toml", "[a]\nx = 1\n[a.b]\ny = 2\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.Enter),
            (_, w) => Keys(w, Key.End, Key.Backspace),
            (_, w) => Type(w, "z"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("[z]\nx = 1\n[z.b]\ny = 2\n", Text(session));
    }

    [Fact]
    public void t_on_an_inline_table_with_children_changed_to_an_array_asks_nothing_and_wraps_it()
    {
        OpenedFile file = Open("settings.toml", "o = {a = 1}\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(6), null);
                using var confirm = new DialogWatch(app, "the discard prompt", IsDialogTitled("Type"), null, "Change");
                Keys(w, Key.CursorDown, new Key('t'));
                menu.Finish();
                confirm.Finish(expectOpened: false);
            });

        Assert.Equal("o = [{a = 1}]\n", Text(session));
    }

    [Fact]
    public void t_on_a_one_item_inline_array_changed_to_its_items_type_unwraps_it_and_asks_nothing()
    {
        OpenedFile file = Open("settings.toml", "a = [1]\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(1), null);
                using var confirm = new DialogWatch(app, "the discard prompt", IsDialogTitled("Type"), null, "Change");
                Keys(w, Key.CursorDown, new Key('t'));
                menu.Finish();
                confirm.Finish(expectOpened: false);
            });

        Assert.Equal("a = 1\n", Text(session));
    }

    [Fact]
    public void t_on_a_two_item_inline_array_changed_to_an_integer_still_asks_before_discarding_them()
    {
        OpenedFile file = Open("settings.toml", "a = [1, 2]\n");
        bool asked = false;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(1), null);
                using var confirm = new DialogWatch(app, "the discard prompt", IsDialogTitled("Type"), _ => asked = true, "Change");
                Keys(w, Key.CursorDown, new Key('t'));
                menu.Finish();
                confirm.Finish();
            });

        Assert.True(asked);
        Assert.Equal("a = 0\n", Text(session));
    }

    [Fact]
    public void t_on_a_string_changes_it_to_an_integer_of_value_zero()
    {
        OpenedFile file = Open("settings.toml", "a = \"x\"\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(1), null);
                Keys(w, Key.CursorDown, new Key('t'));
                dialog.Finish();
            });

        Assert.Equal("a = 0\n", Text(session));
    }

    [Fact]
    public void t_on_a_header_table_shows_the_refusal_and_changes_nothing()
    {
        OpenedFile file = Open("settings.toml", "[a]\nx = 1\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(0), null);
                Keys(w, Key.CursorDown, new Key('t'));
                dialog.Finish();
            });

        Assert.Equal("[a]\nx = 1\n", Text(session));
        Assert.Contains("cannot change type", session.Screens[^1].Text(22), StringComparison.Ordinal);
    }

    [Fact]
    public void d_on_a_table_spread_over_dotted_keys_deletes_every_piece()
    {
        // a is defined by a dotted key at the root and by the header [a.q] below [b]: two pieces, one table.
        OpenedFile file = Open("settings.toml", "a.x = 1\n[b]\ny = 2\n[a.q]\nz = 3\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.Delete));

        Assert.Equal("[b]\ny = 2\n", Text(session));
    }

    [Fact]
    public void Alt_Down_moves_a_key_value_after_the_next_one_and_the_cursor_follows_it()
    {
        OpenedFile file = Open("settings.toml", "a = 1\nb = 2\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorDown.WithAlt));

        Assert.Equal("b = 2\na = 1\n", Text(session));
        Assert.Equal(2, session.Window.Tree.CursorRow);
    }

    [Fact]
    public void T_on_a_TOML_value_that_is_no_table_says_why_and_changes_nothing()
    {
        OpenedFile file = Open("settings.toml", "a = 1\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, new Key('T')));

        Assert.Equal("a = 1\n", Text(session));
        Assert.Equal("no table here: not a list of objects or values", session.Screens[^1].Text(MessageRow));
    }

    // ----- New file -----

    [Fact]
    public void Creating_dot_toml_asks_nothing_about_the_format_and_gives_an_empty_document_with_the_root_row_dollar_0()
    {
        string path = NewPath("settings.toml");
        NewFileChoice? choice = null;

        RunApp(
            80,
            24,
            app =>
            {
                using var create = new DialogWatch(app, "the create prompt", IsDialogTitled("New file"), null, "Create");
                choice = CfgeditHost.AskNewFile(app, path, null);
                create.Finish();
            });

        Assert.NotNull(choice);
        Assert.Equal("toml", choice!.Format.Name);
        Assert.Null(choice.RootName);
        Assert.False(File.Exists(path));

        var file = new OpenedFile(path, DocumentFile.CreateNew(path, choice.Format, choice.RootName));
        Session session = Run(file, 80, 24, null, null);

        Assert.Equal("  $ {…} (0)", Inside(session.Screens[^1], 1));
    }

    [Fact]
    public void Alt_Shift_Down_on_a_TOML_key_value_above_a_table_moves_it_into_that_table_at_its_start()
    {
        OpenedFile file = Open("settings.toml", "a = 1\n[t]\nx = 2\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown.WithAlt.WithShift));

        Assert.Equal("[t]\na = 1\nx = 2\n", Text(session));
    }

    [Fact]
    public void The_format_choice_offers_toml_as_dot_toml()
    {
        Assert.Contains("toml", CfgeditHost.FormatNames);
        Assert.Contains(".toml", CfgeditHost.FormatLabels);
    }
}
