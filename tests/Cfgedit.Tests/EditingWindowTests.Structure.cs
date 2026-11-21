using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cfgedit.Tests;

/// <summary>The structure keys of the tree (a, A, i, +, d, moves, t, Ctrl+D), with the resulting document text asserted exactly.</summary>
public sealed partial class EditingWindowTests
{
    private const string Config = "<configuration>\n  <add key=\"a\" value=\"1\"/>\n</configuration>";

    // ----- a and A -----

    [Fact]
    public void a_on_an_XML_element_copies_its_shape_and_opens_the_value_of_its_first_attribute()
    {
        OpenedFile file = Open("app.config", Config);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, new Key('a')),
            (_, w) => AssertEditing(w),
            (_, w) => Type(w, "x"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("<configuration>\n  <add key=\"a\" value=\"1\"/>\n  <add key=\"x\" value=\"\"/>\n</configuration>", Text(session));
        AssertHints(session.Screens[1], EditingHints);
    }

    [Fact]
    public void A_on_an_XML_element_inserts_a_copy_before_it_and_its_attached_comment_stays_with_it()
    {
        OpenedFile file = Open("app.config", "<configuration>\n  <!-- about add -->\n  <add key=\"a\"/>\n</configuration>");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, new Key('A')),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("<configuration>\n  <add key=\"\"/>\n  <!-- about add -->\n  <add key=\"a\"/>\n</configuration>", Text(session));
    }

    [Fact]
    public void a_after_an_element_with_a_comment_on_its_line_inserts_after_the_comment()
    {
        OpenedFile file = Open("app.config", "<configuration>\n  <add key=\"a\"/><!-- note -->\n  <b/>\n</configuration>");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, new Key('a')),
            (_, w) => Keys(w, Key.Esc));

        Assert.Equal("<configuration>\n  <add key=\"a\"/><!-- note -->\n  <add key=\"\"/>\n  <b/>\n</configuration>", Text(session));
    }

    [Fact]
    public void a_on_a_JSON_object_item_in_an_array_copies_its_members_and_opens_the_first_number()
    {
        OpenedFile file = Open("list.json", "[\n  {\"a\": 1, \"b\": \"x\"}\n]");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, new Key('a')),
            (_, w) => Keys(w, Key.End, Key.Backspace),
            (_, w) => Type(w, "7"),
            (_, w) => Keys(w, Key.Enter));

        // The copy is written by the formatter as a multi-line object, the way Core lays out a new object in an array.
        Assert.Equal("[\n  {\"a\": 1, \"b\": \"x\"},\n  {\n    \"a\": 7,\n    \"b\": \"\"\n  }\n]", Text(session));
    }

    [Fact]
    public void a_on_a_JSON_scalar_item_inserts_an_empty_value_of_its_kind_after_it()
    {
        OpenedFile file = Open("list.json", "[1, 2]");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, new Key('a')),
            (_, w) => Keys(w, Key.Esc));

        Assert.Equal("[1, 0, 2]", Text(session));
    }

    [Fact]
    public void a_on_a_JSON_member_asks_for_the_key_and_type_and_then_opens_the_string_value()
    {
        OpenedFile file = Open("settings.json", JsonText);
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
                    v =>
                    {
                        title = ((AddDialog)v).Title;
                        ((AddDialog)v).NameField.Text = "title";
                    },
                    "OK");
                Keys(w, Key.CursorDown, new Key('a'));
                dialog.Finish();
            },
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("Add after $.name", title);
        Assert.Equal("{\n  \"name\": \"web\",\n  \"title\": \"\",\n  \"port\": 8080\n}", Text(session));
    }

    [Fact]
    public void A_on_a_JSON_member_with_a_key_that_exists_keeps_the_dialog_open_with_the_error()
    {
        OpenedFile file = Open("settings.json", JsonText);
        string? error = null;
        bool stillOpen = false;

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
                        var add = (AddDialog)v;
                        add.NameField.Text = "port";
                        PressButton(v, "OK");
                        stillOpen = app.TopRunnableView is AddDialog;
                        error = add.ErrorText;
                        add.NameField.Text = "speed";
                    },
                    "OK");
                Keys(w, Key.CursorDown, new Key('A'));
                dialog.Finish();
            });

        Assert.True(stillOpen);
        Assert.Contains("port", error, StringComparison.Ordinal);
        // The second attempt was made with speed, which goes before name because the cursor was on name.
        Assert.Equal("{\n  \"speed\": \"\",\n  \"name\": \"web\",\n  \"port\": 8080\n}", Text(session));
    }

    [Fact]
    public void a_on_an_attribute_part_asks_for_the_name_and_then_opens_the_value_editor()
    {
        OpenedFile file = Open("app.config", Config);

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
                    "the attribute name dialog",
                    v => v is TextPromptDialog,
                    v => ((TextPromptDialog)v).Field.Text = "extra",
                    "OK");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.CursorRight, new Key('a'));
                dialog.Finish();
            },
            (_, w) => Type(w, "2"),
            (_, w) => Keys(w, Key.Enter));

        // The new attribute goes after the one the cursor is on, which is key: it sits between key and value.
        Assert.Equal("<configuration>\n  <add key=\"a\" extra=\"2\" value=\"1\"/>\n</configuration>", Text(session));
    }

    [Fact]
    public void A_on_an_attribute_part_inserts_the_attribute_before_it()
    {
        OpenedFile file = Open("app.config", Config);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the attribute name dialog", v => v is TextPromptDialog, v => ((TextPromptDialog)v).Field.Text = "first", "OK");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.CursorRight, new Key('A'));
                dialog.Finish();
            },
            (_, w) => Type(w, "0"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("<configuration>\n  <add first=\"0\" key=\"a\" value=\"1\"/>\n</configuration>", Text(session));
    }

    // ----- i and + -----

    [Fact]
    public void i_on_the_text_of_a_text_only_element_asks_for_a_child_and_appends_it_last()
    {
        OpenedFile file = Open("app.config", "<configuration>\n  <note>hello</note>\n</configuration>");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the add dialog", v => v is AddDialog, v => ((AddDialog)v).NameField.Text = "b", "OK");

                // i adds a child from any part of the element; the cursor is on its text part here.
                Keys(w, Key.CursorDown, Key.CursorRight, new Key('i'));
                dialog.Finish();
            });

        Assert.Equal("<configuration>\n  <note>hello<b /></note>\n</configuration>", Text(session));
    }

    [Fact]
    public void i_on_the_name_of_a_self_closing_element_adds_a_child_element()
    {
        OpenedFile file = Open("app.config", "<configuration>\n  <a/>\n</configuration>");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the add dialog", v => v is AddDialog, v => ((AddDialog)v).NameField.Text = "b", "OK");
                Keys(w, Key.CursorDown, new Key('i'));
                dialog.Finish();
            });

        Assert.Equal("<configuration>\n  <a>\n    <b />\n  </a>\n</configuration>", Text(session));
    }

    [Fact]
    public void Plus_on_an_element_name_adds_an_attribute_at_the_end()
    {
        OpenedFile file = Open("app.config", Config);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the attribute name dialog", v => v is TextPromptDialog, v => ((TextPromptDialog)v).Field.Text = "flag", "OK");
                Keys(w, Key.CursorDown, new Key('+'));
                dialog.Finish();
            },
            (_, w) => Type(w, "on"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("<configuration>\n  <add key=\"a\" value=\"1\" flag=\"on\"/>\n</configuration>", Text(session));
    }

    [Fact]
    public void Plus_on_a_JSON_node_says_that_attributes_apply_to_XML()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, new Key('+')));

        Assert.Equal(JsonText, Text(session));
        Assert.Equal("attributes apply to XML elements only", session.Screens[^1].Text(22));
    }

    [Fact]
    public void i_on_a_JSON_object_adds_a_member_at_the_end_and_opens_its_string_value()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the add dialog", v => v is AddDialog, v => ((AddDialog)v).NameField.Text = "host", "OK");
                Keys(w, new Key('i'));
                dialog.Finish();
            },
            (_, w) => Type(w, "example"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("{\n  \"name\": \"web\",\n  \"port\": 8080,\n  \"host\": \"example\"\n}", Text(session));
    }

    [Fact]
    public void i_on_a_JSON_array_adds_an_item_at_the_end()
    {
        OpenedFile file = Open("list.json", "[1]");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the add dialog", v => v is AddDialog, null, "OK");
                Keys(w, new Key('i'));
                dialog.Finish();
            },
            (_, w) => Type(w, "x"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("[1, \"x\"]", Text(session));
    }

    // ----- d and Delete -----

    [Fact]
    public void d_deletes_the_node_with_no_confirmation_and_focuses_the_next_sibling()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, new Key('d')));

        Assert.Equal("{\n  \"port\": 8080\n}", Text(session));
        // The cursor keeps its part (the key), so the detail line describes the key of port.
        Assert.Equal("$.port  key  port", session.Screens[^1].Text(22));
    }

    [Fact]
    public void Delete_on_an_attribute_deletes_only_that_attribute_and_focuses_the_element()
    {
        OpenedFile file = Open("app.config", Config);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.CursorRight, Key.Delete));

        Assert.Equal("<configuration>\n  <add value=\"1\"/>\n</configuration>", Text(session));
        Assert.Equal(1, session.Window.Tree.CursorRow);
    }

    // ----- Moves -----

    [Fact]
    public void Alt_Down_moves_a_node_after_its_next_sibling_and_K_moves_it_back()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown.WithAlt),
            (_, w) => Keys(w, new Key('K')));

        Assert.Equal("{\n  \"name\": \"web\",\n  \"port\": 8080\n}", Text(session));
        Assert.Equal(1, session.Window.Tree.CursorRow);
    }

    [Fact]
    public void J_moves_a_node_down_and_says_already_last_at_the_end()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, new Key('J')),
            (_, w) => Keys(w, new Key('J')));

        Assert.Equal("{\n  \"port\": 8080,\n  \"name\": \"web\"\n}", Text(session));
        Assert.Equal("already last", session.Screens[^1].Text(22));
    }

    [Fact]
    public void Alt_Up_on_the_first_node_says_already_first_and_changes_nothing()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorUp.WithAlt));

        Assert.Equal(JsonText, Text(session));
        Assert.Equal("already first", session.Screens[^1].Text(22));
    }

    [Fact]
    public void Alt_Left_moves_an_attribute_before_its_neighbour()
    {
        OpenedFile file = Open("app.config", Config);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.CursorRight, Key.CursorRight, Key.CursorLeft.WithAlt));

        Assert.Equal("<configuration>\n  <add value=\"1\" key=\"a\"/>\n</configuration>", Text(session));
    }

    [Fact]
    public void Alt_Left_on_the_first_attribute_says_already_first_and_changes_nothing()
    {
        OpenedFile file = Open("app.config", Config);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.CursorLeft.WithAlt));

        Assert.Equal(Config, Text(session));
        Assert.Equal("already first", session.Screens[^1].Text(22));
    }

    [Fact]
    public void Alt_Right_on_a_node_that_is_not_an_attribute_says_only_an_attribute_moves()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorRight.WithAlt));

        Assert.Equal(JsonText, Text(session));
        Assert.Equal("only an attribute moves with Alt+Left and Alt+Right", session.Screens[^1].Text(22));
    }

    // ----- t -----

    [Fact]
    public void t_changes_a_string_to_a_number_as_0_when_the_text_is_not_a_number()
    {
        OpenedFile file = Open("settings.json", JsonText);

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

        Assert.Equal("{\n  \"name\": 0,\n  \"port\": 8080\n}", Text(session));
    }

    [Fact]
    public void t_changes_a_number_to_a_string_with_its_text()
    {
        OpenedFile file = Open("settings.json", JsonText);

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

        Assert.Equal("{\n  \"name\": \"web\",\n  \"port\": \"8080\"\n}", Text(session));
    }

    [Theory]
    [InlineData(2, "{\n  \"name\": true,\n  \"port\": 8080\n}")]
    [InlineData(3, "{\n  \"name\": false,\n  \"port\": 8080\n}")]
    [InlineData(4, "{\n  \"name\": null,\n  \"port\": 8080\n}")]
    [InlineData(5, "{\n  \"name\": {},\n  \"port\": 8080\n}")]
    [InlineData(6, "{\n  \"name\": [],\n  \"port\": 8080\n}")]
    public void t_reaches_each_other_type_from_the_menu(int choice, string expected)
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(choice), null);
                Keys(w, Key.CursorDown, new Key('t'));
                dialog.Finish();
            });

        Assert.Equal(expected, Text(session));
    }

    [Fact]
    public void t_on_a_container_with_children_asks_before_discarding_them_and_Change_discards_them()
    {
        OpenedFile file = Open("nested.json", "{\n  \"o\": {\"a\": 1}\n}");
        bool asked = false;

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(0), null);
                using var confirm = new DialogWatch(app, "the discard prompt", IsDialogTitled("Type"), _ => asked = true, "Change");
                Keys(w, Key.CursorDown, new Key('t'));
                menu.Finish();
                confirm.Finish();
            });

        Assert.True(asked);
        Assert.Equal("{\n  \"o\": \"\"\n}", Text(session));
    }

    [Fact]
    public void t_on_a_container_with_children_keeps_them_when_Cancel_is_chosen()
    {
        const string text = "{\n  \"o\": {\"a\": 1}\n}";
        OpenedFile file = Open("nested.json", text);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (app, w) =>
            {
                using var menu = new DialogWatch(app, "the type menu", v => v is ChoiceDialog, PickItem(0), null);
                using var confirm = new DialogWatch(app, "the discard prompt", IsDialogTitled("Type"), null, "Cancel");
                Keys(w, Key.CursorDown, new Key('t'));
                menu.Finish();
                confirm.Finish();
            });

        Assert.Equal(text, Text(session));
    }

    [Fact]
    public void t_on_an_XML_element_says_types_apply_to_JSON_only()
    {
        OpenedFile file = Open("app.config", Config);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, new Key('t')));

        Assert.Equal(Config, Text(session));
        Assert.Equal("types apply to JSON only", session.Screens[^1].Text(22));
    }

    // ----- Ctrl+D -----

    [Fact]
    public void Ctrl_D_duplicates_the_node_after_itself_with_a_new_key()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.D.WithCtrl));

        Assert.Equal("{\n  \"name\": \"web\",\n  \"name-copy\": \"web\",\n  \"port\": 8080\n}", Text(session));
    }

    // ----- Hints and layout -----

    [Fact]
    public void Dialogs_at_40_by_12_show_every_row_above_the_buttons()
    {
        OpenedFile file = Open("settings.json", JsonText);
        TerminalScreen? shown = null;

        Run(
            file,
            40,
            12,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the add dialog", v => v is AddDialog, v => shown = Snap(app, v, 40, 12), "Cancel");
                Keys(w, Key.CursorDown, new Key('a'));
                dialog.Finish();
            });

        Assert.NotNull(shown);
        string drawn = Drawn(shown!);
        Assert.Contains("Kind: < string >", drawn, StringComparison.Ordinal);
        Assert.True(shown!.RowOf("Key:") < shown.RowOf("Cancel"), drawn);
        Assert.True(shown.RowOf("Cancel") == shown.RowOf("OK"), drawn);
    }

    [Fact]
    public void The_type_menu_at_40_by_12_lists_all_seven_types()
    {
        OpenedFile file = Open("settings.json", JsonText);
        TerminalScreen? shown = null;

        Run(
            file,
            40,
            12,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(
                    app,
                    "the type menu",
                    v => v is ChoiceDialog,
                    v =>
                    {
                        shown = Snap(app, v, 40, 12);
                        v.NewKeyDownEvent(Key.Esc);
                    },
                    null);
                Keys(w, Key.CursorDown, new Key('t'));
                dialog.Finish();
            });

        Assert.NotNull(shown);
        string drawn = Drawn(shown!);
        int previous = -1;
        foreach (string name in new[] { "string", "number", "true", "false", "null", "object", "array" })
        {
            int row = shown!.RowOf(name);
            Assert.True(row > previous, $"{name} is not below the one above it:\n{drawn}");
            previous = row;
        }

        Assert.True(previous < 11, $"the last type is drawn on the last row:\n{drawn}");
    }

    [Fact]
    public void The_tree_hints_show_the_structure_keys_at_80_columns()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(file, 80, 24, null, null);

        AssertHints(session.Screens[^1], TreeHints);
    }

    [Fact]
    public void An_invalid_XML_name_in_the_add_dialog_keeps_it_open_with_the_error()
    {
        OpenedFile file = Open("app.config", "<configuration>\n  <note>hello</note>\n</configuration>");
        bool stillOpen = false;
        string? error = null;

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
                        var add = (AddDialog)v;
                        add.NameField.Text = "1bad";
                        PressButton(v, "OK");
                        stillOpen = app.TopRunnableView is AddDialog;
                        error = add.ErrorText;
                        PressButton(v, "Cancel");
                    },
                    null);
                Keys(w, Key.CursorDown, Key.CursorRight, new Key('i'));
                dialog.Finish();
            });

        Assert.True(stillOpen);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Equal("<configuration>\n  <note>hello</note>\n</configuration>", Text(session));
    }

    [Theory]
    [InlineData(80, 24)]
    [InlineData(40, 12)]
    public void The_add_dialog_and_the_attribute_name_dialog_show_every_row_at_both_sizes(int width, int height)
    {
        OpenedFile file = Open("settings.json", JsonText);
        OpenedFile config = Open("app.config", Config);
        TerminalScreen? addShown = null;
        TerminalScreen? nameShown = null;

        Run(
            file,
            width,
            height,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the add dialog", v => v is AddDialog, v => addShown = Snap(app, v, width, height), "Cancel");
                Keys(w, Key.CursorDown, new Key('a'));
                dialog.Finish();
            });

        Run(
            config,
            width,
            height,
            null,
            null,
            (app, w) =>
            {
                using var dialog = new DialogWatch(app, "the attribute name dialog", v => v is TextPromptDialog, v => nameShown = Snap(app, v, width, height), "Cancel");
                Keys(w, Key.CursorDown, Key.CursorRight, Key.CursorRight, new Key('a'));
                dialog.Finish();
            });

        Assert.NotNull(addShown);
        Assert.Contains("Kind: < string >", Drawn(addShown!), StringComparison.Ordinal);
        Assert.True(addShown!.RowOf("Key:") < addShown.RowOf("Cancel"), Drawn(addShown));
        Assert.NotNull(nameShown);
        Assert.True(nameShown!.RowOf("Name:") < nameShown.RowOf("Cancel"), Drawn(nameShown));
        Assert.Equal(nameShown.RowOf("Cancel"), nameShown.RowOf("OK"));
    }

    private static void AssertEditing(MainWindow window)
    {
        Assert.True(window.Tree.IsEditing);
    }

    // Picks an item of a choice dialog as the user does: the item is highlighted, and Enter picks it.
    private static Action<View> PickItem(int index) => v =>
    {
        ((ChoiceDialog)v).List.SelectedItem = index;
        v.NewKeyDownEvent(Key.Enter);
    };

    private static void PressButton(View view, string label)
    {
        ButtonsOf(view).Single(b => b.Text.TrimStart('_') == label).InvokeCommand(Command.Accept);
    }
}
