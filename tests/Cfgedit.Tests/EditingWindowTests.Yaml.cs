using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Cfgedit.Tests;

/// <summary>
/// The YAML format in the window: the rows and the cursor, the add dialog and the value editor that follows it, the type menu and its
/// refusals, anchors and aliases that cannot be edited, deletes and moves, documents, the table editor's message, and a new .yaml file.
/// </summary>
public sealed partial class EditingWindowTests
{
    private const string ComposeFixture =
        "x-base: &base\n  restart: always\nservices:\n  web:\n    <<: *base\n    image: nginx\n    ports:\n      - \"80:80\"\n";

    // ----- Rows and the cursor -----

    [Fact]
    public void A_yaml_file_opens_with_the_cursor_on_the_root_and_its_rows_show_anchors_and_counts()
    {
        OpenedFile file = Open("compose.yaml", ComposeFixture);

        Session session = Run(file, 80, 24, null, null);

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("▾ $ {…} (2)", Inside(screen, 1));
        Assert.Equal("  ▾ x-base: &base {…} (1)", Inside(screen, 2));
    }

    [Fact]
    public void A_yaml_file_with_two_documents_shows_each_document_as_a_row_under_the_root()
    {
        OpenedFile file = Open("two.yaml", "---\na: 1\n---\nb: 2\n");

        Session session = Run(file, 80, 24, null, null);

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("▾ ---[0]", Inside(screen, 1));
        Assert.Equal("  ▾ $ {…} (1)", Inside(screen, 2));
    }

    // ----- Add -----

    [Fact]
    public void a_on_the_summary_of_an_empty_yaml_file_asks_for_a_string_key_and_then_opens_its_value()
    {
        OpenedFile file = Open("settings.yaml", string.Empty);
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
        Assert.Equal(AddKind.YamlString, kind);

        // A new empty string is written '' (quoted only because it is empty), so the typed value is written in the plainest style.
        Assert.Equal("name: web\n", Text(session));
    }

    [Fact]
    public void The_add_kinds_of_a_yaml_mapping_or_sequence_are_the_yaml_kinds_and_a_document_row_takes_none()
    {
        ConfigDocument empty = TreeFixtures.Parse(string.Empty, "yaml");
        DocNode root = TreeFixtures.Find(empty, new ItemStep(0));
        ConfigDocument list = TreeFixtures.Parse("l:\n  - 1\n", "yaml");
        DocNode sequence = TreeFixtures.Find(list, new ItemStep(0), new MemberStep("l", 0));
        ConfigDocument documents = TreeFixtures.Parse("---\na: 1\n---\nb: 2\n", "yaml");
        DocNode document = TreeFixtures.Find(documents, new ItemStep(1));

        AddKind[] yamlKinds =
        [
            AddKind.YamlString, AddKind.YamlNumber, AddKind.YamlBoolean, AddKind.YamlNull, AddKind.YamlMapping, AddKind.YamlSequence,
            AddKind.YamlComment,
        ];
        Assert.Equal(yamlKinds, AddKinds.For(empty, root));
        Assert.Equal("Key", AddKinds.NameLabel(root, AddKind.YamlString));
        Assert.Null(AddKinds.NameLabel(sequence, AddKind.YamlString));
        Assert.Null(AddKinds.NameLabel(root, AddKind.YamlComment));
        Assert.Empty(AddKinds.For(documents, document));
    }

    [Fact]
    public void a_on_an_item_of_a_sequence_of_mappings_copies_its_shape_with_empty_values()
    {
        OpenedFile file = Open("compose.yaml", "services:\n  - name: web\n    image: nginx\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, new Key('a'), Key.Esc));

        Assert.Equal("services:\n  - name: web\n    image: nginx\n  - name: ''\n    image: ''\n", Text(session));
    }

    [Fact]
    public void a_on_an_alias_item_asks_for_a_kind_instead_of_copying_its_shape()
    {
        OpenedFile file = Open("settings.yaml", "l:\n  - &x 1\n  - *x\n");
        string? title = null;

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
                    v => title = ((AddDialog)v).Title,
                    "Cancel");
                Keys(w, Key.CursorDown, Key.CursorDown, Key.CursorDown, new Key('a'));
                dialog.Finish();
            });

        Assert.StartsWith("Add after ", title, StringComparison.Ordinal);
        Assert.Equal("l:\n  - &x 1\n  - *x\n", Text(session));
    }

    // ----- Keys -----

    [Fact]
    public void Editing_a_string_to_true_writes_it_single_quoted_so_it_stays_a_string()
    {
        OpenedFile file = Open("settings.yaml", "name: web\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter),
            (_, w) => Keys(w, Key.End, Key.Backspace, Key.Backspace, Key.Backspace),
            (_, w) => Type(w, "true"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("name: 'true'\n", Text(session));
    }

    [Fact]
    public void t_on_a_one_item_sequence_changed_to_its_items_type_unwraps_it_and_asks_nothing()
    {
        OpenedFile file = Open("settings.yaml", "a:\n  - x\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(0), null);
                using var confirm = new DialogWatch(app, "the discard prompt", IsDialogTitled("Type"), null, "Change");
                Keys(w, Key.CursorDown, new Key('t'));
                menu.Finish();
                confirm.Finish(expectOpened: false);
            });

        Assert.Equal("a: x\n", Text(session));
    }

    [Fact]
    public void t_on_a_two_item_sequence_changed_to_a_number_still_asks_before_discarding_them()
    {
        OpenedFile file = Open("settings.yaml", "a:\n  - 1\n  - 2\n");
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
        Assert.Equal("a: 0\n", Text(session));
    }

    [Fact]
    public void t_on_a_string_changes_it_to_a_number_of_value_zero()
    {
        OpenedFile file = Open("settings.yaml", "a: x\n");

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

        Assert.Equal("a: 0\n", Text(session));
    }

    [Fact]
    public void t_on_an_alias_offers_the_menu_and_sequence_wraps_it()
    {
        OpenedFile file = Open("settings.yaml", "a: &x 1\nb: *x\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(5), null);
                Keys(w, Key.CursorDown, Key.CursorDown, new Key('t'));
                dialog.Finish();
            });

        Assert.Equal("a: &x 1\nb:\n  - *x\n", Text(session));
    }

    [Fact]
    public void t_on_an_alias_with_another_type_shows_the_refusal_and_changes_nothing()
    {
        const string text = "a: &x 1\nb: *x\n";
        OpenedFile file = Open("settings.yaml", text);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(0), null);
                Keys(w, Key.CursorDown, Key.CursorDown, new Key('t'));
                dialog.Finish();
            });

        Assert.Equal(text, Text(session));
        Assert.Equal("an alias can only be wrapped in a sequence", session.Screens[^1].Text(MessageRow));
    }

    [Fact]
    public void t_on_a_mapping_with_children_changed_to_a_sequence_asks_nothing_and_wraps_it()
    {
        OpenedFile file = Open("settings.yaml", "a:\n  x: 1\n  y: 2\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(5), null);
                using var confirm = new DialogWatch(app, "the discard prompt", IsDialogTitled("Type"), null, "Change");
                Keys(w, Key.CursorDown, new Key('t'));
                menu.Finish();
                confirm.Finish(expectOpened: false);
            });

        Assert.Equal("a:\n  - x: 1\n    y: 2\n", Text(session));
    }

    [Fact]
    public void Delete_on_an_anchor_that_an_alias_still_refers_to_is_refused_and_changes_nothing()
    {
        OpenedFile file = Open("settings.yaml", "base: &b 1\nuse: *b\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.Delete));

        Assert.Equal("base: &b 1\nuse: *b\n", Text(session));
        Assert.Contains("*b refers to it", session.Screens[^1].Text(MessageRow), StringComparison.Ordinal);
    }

    [Fact]
    public void Alt_Down_moves_a_yaml_entry_after_the_next_one_and_the_cursor_follows_it()
    {
        OpenedFile file = Open("settings.yaml", "a: 1\nb: 2\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorDown.WithAlt));

        Assert.Equal("b: 2\na: 1\n", Text(session));
        Assert.Equal(2, session.Window.Tree.CursorRow);
    }

    [Fact]
    public void Alt_Down_on_a_document_row_swaps_it_with_the_next_document()
    {
        OpenedFile file = Open("two.yaml", "---\na: 1\n---\nb: 2\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown.WithAlt));

        Assert.Equal("---\nb: 2\n---\na: 1\n", Text(session));
    }

    [Fact]
    public void Alt_Shift_Up_on_a_YAML_member_below_a_mapping_moves_it_into_that_mapping_as_its_last_member()
    {
        OpenedFile file = Open("settings.yaml", "box:\n  x: 1\nport: 2\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, Key.CursorDown, Key.CursorUp.WithAlt.WithShift));

        Assert.Equal("box:\n  x: 1\n  port: 2\n", Text(session));
    }

    [Fact]
    public void T_on_a_YAML_member_that_is_not_a_sequence_says_there_is_no_table_here()
    {
        OpenedFile file = Open("settings.yaml", "a: 1\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, new Key('T')));

        Assert.Equal("a: 1\n", Text(session));
        Assert.Equal("no table here: not a list of objects or values", session.Screens[^1].Text(MessageRow));
    }

    // ----- New file -----

    [Fact]
    public void Creating_dot_yaml_asks_nothing_about_the_format_and_gives_an_empty_document_with_the_root_row_dollar_0()
    {
        string path = NewPath("settings.yaml");
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
        Assert.Equal("yaml", choice!.Format.Name);
        Assert.Null(choice.RootName);
        Assert.False(File.Exists(path));

        var file = new OpenedFile(path, DocumentFile.CreateNew(path, choice.Format, choice.RootName));
        Session session = Run(file, 80, 24, null, null);

        Assert.Equal("  $ {…} (0)", Inside(session.Screens[^1], 1));
    }

    [Fact]
    public void The_format_choice_offers_yaml_as_dot_yaml()
    {
        Assert.Contains("yaml", CfgeditHost.FormatNames);
        Assert.Contains(".yaml", CfgeditHost.FormatLabels);
    }
}
