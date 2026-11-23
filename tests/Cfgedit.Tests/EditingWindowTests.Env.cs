using Cfgedit.Core.Files;
using Cfgedit.Core.Model;
using Cfgedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Cfgedit.Tests;

/// <summary>
/// The .env format in the window: the rows and the cursor, the add dialog and the value editor that follows it, the loader note after a
/// value is written, renames and deletes and moves, the refused type change and table, trailing comments, and a new .env file.
/// </summary>
public sealed partial class EditingWindowTests
{
    private const string LoaderNote = "note: loaders may expand $ or read \\ differently in double quotes";
    private const string StringsOnly = "`.env` values are all strings";
    private const string NoTable = "the table editor is not available for .env files";

    // ----- Rows and the cursor -----

    [Fact]
    public void A_dotenv_file_opens_with_the_cursor_on_the_root_summary_and_the_root_row_reads_dollar_and_the_count()
    {
        OpenedFile file = Open(".env", "A=1\nB=2\n");

        Session session = Run(file, 80, 24, null, null);

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("▾ $ {…} (2)", Inside(screen, 1));
        Assert.StartsWith("$  root", screen.Text(22), StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_shows_its_segment_and_count_and_its_entries_show_the_last_segment_and_value()
    {
        OpenedFile file = Open(".env", "Logging__Level=info\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("▾ $ {…} (1)", Inside(screen, 1));
        Assert.Equal("  ▾ Logging: {…} (1)", Inside(screen, 2));
        Assert.Equal("      Level = info", Inside(screen, 3));
    }

    [Fact]
    public void A_trailing_comment_shows_as_a_comment_row_after_its_entry()
    {
        OpenedFile file = Open(".env", "A=1 # note\n");

        Session session = Run(file, 80, 24, null, null);

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("    A = 1", Inside(screen, 2));
        Assert.Equal("    # note", Inside(screen, 3));
    }

    // ----- Add -----

    [Fact]
    public void a_on_the_summary_of_an_empty_dotenv_file_asks_for_an_entry_and_then_opens_its_value()
    {
        OpenedFile file = Open(".env", string.Empty);
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
                        ((AddDialog)v).NameField.Text = "Logging__Level";
                    },
                    "OK");
                Keys(w, new Key('a'));
                dialog.Finish();
            },
            (_, w) => Type(w, "info"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("Add to $", title);
        Assert.Equal(AddKind.EnvEntry, kind);
        Assert.Equal("Logging__Level=info\n", Text(session));
        Assert.Equal("▾ $ {…} (1)", Inside(session.Screens[^1], 1));
        Assert.Equal("  ▾ Logging: {…} (1)", Inside(session.Screens[^1], 2));
    }

    [Fact]
    public void A_group_offers_an_entry_and_a_comment_and_the_root_offers_the_same()
    {
        ConfigDocument doc = TreeFixtures.Parse("Logging__Level=info\n", "env");
        DocNode root = TreeFixtures.Find(doc, new ItemStep(0));
        DocNode group = TreeFixtures.Find(doc, new ItemStep(0), new MemberStep("Logging", 0));

        Assert.Equal(new[] { AddKind.EnvEntry, AddKind.EnvComment }, AddKinds.For(doc, root));
        Assert.Equal(new[] { AddKind.EnvEntry, AddKind.EnvComment }, AddKinds.For(doc, group));
    }

    // ----- Values and the loader note -----

    [Fact]
    public void Editing_a_value_with_a_dollar_and_a_quote_writes_double_quotes_and_shows_the_loader_note()
    {
        OpenedFile file = Open(".env", "A=1\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace),
            (_, w) => Type(w, "pa$s'w"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("A=\"pa$s'w\"\n", Text(session));
        Assert.Equal(LoaderNote, session.Screens[^1].Text(22));
    }

    [Fact]
    public void Editing_a_value_without_a_dollar_or_backslash_shows_no_loader_note()
    {
        OpenedFile file = Open(".env", "A=1\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter, Key.End, Key.Backspace),
            (_, w) => Type(w, "two words"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("A=two words\n", Text(session));
        Assert.DoesNotContain("loaders", session.Screens[^1].Text(22), StringComparison.Ordinal);
    }

    // ----- Keys -----

    [Fact]
    public void Renaming_a_group_renames_every_key_under_it_in_one_edit()
    {
        OpenedFile file = Open(".env", "Logging__A=1\nOther=2\nLogging__B=3\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.Enter),
            (_, w) => Keys(w, Key.End, Key.Backspace, Key.Backspace, Key.Backspace, Key.Backspace, Key.Backspace, Key.Backspace, Key.Backspace),
            (_, w) => Type(w, "Svc"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("Svc__A=1\nOther=2\nSvc__B=3\n", Text(session));
    }

    [Fact]
    public void Delete_on_an_entry_deletes_it_with_the_comment_shown_with_it()
    {
        OpenedFile file = Open(".env", "# note\nA=1\nB=2\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, Key.Delete));

        Assert.Equal("B=2\n", Text(session));
    }

    [Fact]
    public void Alt_Down_moves_an_entry_after_the_next_one_and_the_cursor_follows_it()
    {
        OpenedFile file = Open(".env", "A=1\nB=2\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, Key.CursorDown.WithAlt));

        Assert.Equal("B=2\nA=1\n", Text(session));
        Assert.Equal(2, session.Window.Tree.CursorRow);
    }

    [Fact]
    public void Alt_Shift_Up_on_an_entry_below_a_group_moves_it_into_that_group_as_its_last_member()
    {
        OpenedFile file = Open(".env", "G__X=1\nA=2\n");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, Key.CursorDown, Key.CursorUp.WithAlt.WithShift));

        Assert.Equal("G__X=1\nG__A=2\n", Text(session));
    }

    [Fact]
    public void t_on_an_entry_shows_that_env_values_are_all_strings_and_changes_nothing()
    {
        OpenedFile file = Open(".env", "A=1\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, new Key('t')));

        Assert.Equal("A=1\n", Text(session));
        Assert.Equal(StringsOnly, session.Screens[^1].Text(22));
    }

    [Fact]
    public void T_says_the_table_editor_is_not_available_for_dotenv_files_and_changes_nothing()
    {
        OpenedFile file = Open(".env", "A=1\n");

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, Key.CursorDown, new Key('T')));

        Assert.Equal("A=1\n", Text(session));
        Assert.Equal(NoTable, session.Screens[^1].Text(22));
    }

    // ----- New file -----

    [Fact]
    public void Creating_dot_env_asks_nothing_about_the_format_and_gives_an_empty_document_with_the_root_row_dollar_0()
    {
        string path = NewPath(".env");
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
        Assert.Equal("env", choice!.Format.Name);
        Assert.Null(choice.RootName);
        Assert.False(File.Exists(path));

        var file = new OpenedFile(path, DocumentFile.CreateNew(path, choice.Format, choice.RootName));
        Session session = Run(file, 80, 24, null, null);

        // An empty root has nothing to fold, so no fold glyph, as for an empty JSON root.
        Assert.Equal("  $ {…} (0)", Inside(session.Screens[^1], 1));
    }
}
