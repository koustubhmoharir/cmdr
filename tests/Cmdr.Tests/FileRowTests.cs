using System.Reflection;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Tui;
using FileDialog = Cmdr.Tui.FileDialog;
using Shared.Core;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>
/// The file row of each file in the list, through the controller: the keys it takes and does nothing with, the collapse keys, the
/// focus at load and when a collapsed file hides the focused row, the hints, the marks, and the search that finds a file or reveals a
/// match in a collapsed one.
/// </summary>
[Collection("Terminal GUI")]
public sealed class FileRowTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string EditOnlyMessage = "Press Ctrl+E for edit mode to change groups and commands.";

    /// <summary>The keys that a file row takes and does nothing with, in either mode (Ctrl+N and Ctrl+G are not among them).</summary>
    private static readonly Key[] IgnoredKeys =
    {
        Key.Enter, Key.C.WithCtrl, Key.F5, Key.L, Key.Delete, Key.Space, Key.A.WithCtrl, Key.F2, Key.C, Key.CursorUp.WithAlt, Key.CursorDown.WithAlt,
    };

    /// <summary>The keys a file row takes and does nothing with in edit mode: Enter and F2 edit the opened file, so they are not among them.</summary>
    private static readonly Key[] EditModeIgnoredKeys =
    {
        Key.C.WithCtrl, Key.F5, Key.L, Key.Delete, Key.Space, Key.A.WithCtrl, Key.C, Key.CursorUp.WithAlt, Key.CursorDown.WithAlt,
    };

    [Fact]
    public void Initial_focus_stays_on_the_first_group_of_the_chain_and_not_on_a_file_row()
    {
        using TempChain chain = TempChain.Open();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, chain.Session);

        // The base file's file row comes first; the focus goes to its first group, as it did before file rows.
        Assert.Equal("shared", FocusedName(controller));
    }

    [Fact]
    public void Initial_focus_is_the_file_row_when_there_are_no_groups()
    {
        using TempSession temp = TempSession.Open();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);

        Assert.Equal(temp.Session.MainSource.DisplayPath, FocusedName(controller));
    }

    [Fact]
    public void Run_mode_keys_that_act_on_a_command_do_nothing_on_a_file_row_and_show_no_message()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        CommandEntry first = temp.Session.AllEntries.First();
        FocusKey(controller, temp.Session.MainSource);

        foreach (Key key in IgnoredKeys)
        {
            ClearMessage(controller);
            Assert.True(controller.OnListKey(key), $"{key} on a file row");
            Assert.Null(Message(controller));
        }

        Assert.Equal(temp.Session.MainSource.DisplayPath, FocusedName(controller));
        Assert.Empty(Marked(controller));
        Assert.Equal(ProcessState.NotStarted, first.Process.Snapshot.State);
        Assert.False(temp.Session.IsDirty);
    }

    [Fact]
    public void Edit_mode_keys_that_act_on_a_command_do_nothing_on_a_file_row_and_show_no_message()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        int entries = temp.Session.AllEntries.Count;
        int groups = temp.Session.AllGroups.Count;
        FocusKey(controller, temp.Session.MainSource);

        foreach (Key key in EditModeIgnoredKeys)
        {
            ClearMessage(controller);
            Assert.True(controller.OnListKey(key), $"{key} on a file row");
            Assert.Null(Message(controller));
        }

        Assert.Equal(temp.Session.MainSource.DisplayPath, FocusedName(controller));
        Assert.Empty(Marked(controller));
        Assert.False(temp.Session.IsDirty);
        Assert.Equal(entries, temp.Session.AllEntries.Count);
        Assert.Equal(groups, temp.Session.AllGroups.Count);
    }

    [Fact]
    public void Ctrl_N_and_Ctrl_G_on_a_file_row_in_run_mode_show_the_same_message_as_with_nothing_focused()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        FocusKey(controller, temp.Session.MainSource);

        foreach (Key key in new[] { Key.N.WithCtrl, Key.G.WithCtrl })
        {
            ClearMessage(controller);
            Assert.True(controller.OnListKey(key));
            Assert.Equal(EditOnlyMessage, Message(controller));
        }
    }

    [Fact]
    public void Ctrl_G_on_a_file_row_in_edit_mode_opens_the_add_group_dialog_with_top_level_as_the_parent()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        FocusKey(controller, temp.Session.MainSource);
        int groups = temp.Session.AllGroups.Count;
        GroupDialog? dialog = null;
        string? parent = null;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            // The dialog is modal: read its parent while it is open, then close it, which adds nothing.
            dialog = app.TopRunnableView as GroupDialog;
            parent = dialog is null ? null : ((DropDownList)Field(dialog, "_parent")).Value;
            app.RequestStop();
            return false;
        });

        Assert.True(controller.OnListKey(Key.G.WithCtrl));

        Assert.NotNull(dialog);
        Assert.Equal(GroupOptions.TopLevelLabel, parent);
        Assert.Equal(groups, temp.Session.AllGroups.Count);
    }

    [Fact]
    public void Ctrl_N_on_a_file_row_in_edit_mode_opens_the_add_command_dialog_for_the_first_group_as_with_nothing_focused()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        FocusKey(controller, temp.Session.MainSource);
        int entries = temp.Session.AllEntries.Count;
        CommandDialog? dialog = null;
        GroupEntry? group = null;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            dialog = app.TopRunnableView as CommandDialog;
            group = dialog is null ? null : (GroupEntry?)Field(dialog, "_initialGroup");
            app.RequestStop();
            return false;
        });

        Assert.True(controller.OnListKey(Key.N.WithCtrl));

        Assert.NotNull(dialog);
        Assert.Same(temp.Session.Groups[0], group);
        Assert.Equal(entries, temp.Session.AllEntries.Count);
    }

    [Fact]
    public void Left_collapses_a_file_row_and_hides_its_rows_and_right_expands_it_with_the_focus_kept_on_the_file()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        string file = temp.Session.MainSource.DisplayPath;
        FocusKey(controller, temp.Session.MainSource);

        Assert.True(controller.OnListKey(Key.CursorLeft));

        Assert.Empty(Names(controller));
        Assert.Equal(file, FocusedName(controller));

        // A collapsed file has nothing above it to collapse, so Left again changes nothing.
        Assert.True(controller.OnListKey(Key.CursorLeft));
        Assert.Empty(Names(controller));

        Assert.True(controller.OnListKey(Key.CursorRight));

        Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "d1", "last", "other", "oc" }, Names(controller));
        Assert.Equal(file, FocusedName(controller));
    }

    [Fact]
    public void A_focused_row_that_a_collapsed_file_hides_gives_the_focus_to_the_file_row()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        FocusKey(controller, temp.Session.AllEntries.First());

        Field<HashSet<object>>(controller, "_collapsed").Add(temp.Session.MainSource);
        Invoke(controller, "RebuildRows");

        Assert.Equal(temp.Session.MainSource.DisplayPath, FocusedName(controller));
        Assert.Empty(Names(controller));
    }

    [Fact]
    public void Left_on_a_collapsed_top_level_group_moves_the_focus_to_its_file_row()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        FocusKey(controller, temp.Session.AllGroups.Single(group => group.Name == "top"));

        Assert.True(controller.OnListKey(Key.CursorLeft));
        Assert.Equal(new[] { "top", "other", "oc" }, Names(controller));
        Assert.Equal("top", FocusedName(controller));

        // A top-level group has no parent group, so Left on it goes up to the file.
        Assert.True(controller.OnListKey(Key.CursorLeft));
        Assert.Equal(temp.Session.MainSource.DisplayPath, FocusedName(controller));
    }

    [Fact]
    public void Ctrl_click_on_a_file_row_marks_nothing()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);

        controller.OnCtrlClick(RowIndex(controller, temp.Session.MainSource));

        Assert.Empty(Marked(controller));
    }

    [Fact]
    public void Shift_arrows_do_not_mark_a_file_row_and_do_not_extend_the_marks_across_it()
    {
        using TempChain chain = TempChain.Open();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, chain.Session);
        FocusKey(controller, Command(chain.Session, "tool"));

        // tool, then the group extra, then the file row of the opened file, then the group main, then mine.
        Assert.True(controller.OnListKey(Key.CursorDown.WithShift));
        Assert.True(controller.OnListKey(Key.CursorDown.WithShift));
        Assert.True(controller.OnListKey(Key.CursorDown.WithShift));
        Assert.Empty(Marked(controller));

        Assert.True(controller.OnListKey(Key.CursorDown.WithShift));
        Assert.Equal(new[] { "mine" }, Marked(controller));
    }

    [Fact]
    public void Hints_with_a_file_row_focused_in_run_mode_show_only_Max_and_Edit()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        FocusKey(controller, temp.Session.MainSource);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.Equal("+ Max  ^E Edit", HintLeft(controller));
    }

    [Fact]
    public void Hints_with_the_opened_file_row_focused_in_edit_mode_show_edit_the_new_group_and_command_save_and_run()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        FocusKey(controller, temp.Session.MainSource);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.Equal("Enter Edit  ^N Command  ^G Group  ^S Save  ^E Run", HintLeft(controller));
    }

    [Fact]
    public void A_search_finds_a_file_row_by_its_display_path_and_focuses_it()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        FocusKey(controller, temp.Session.AllGroups.Single(group => group.Name == "other"));

        controller.ListSearch.SetSearch(Literal("cmdr"));
        controller.ListSearch.Find(backward: false, inclusive: true);

        Assert.Equal(temp.Session.MainSource.DisplayPath, FocusedName(controller));
    }

    [Fact]
    public void A_search_reveals_a_match_inside_a_collapsed_file_and_collapses_the_file_again_when_the_focus_leaves_it()
    {
        using TempChain chain = TempChain.Open();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, chain.Session);
        ConfigSource baseFile = chain.Session.Sources[0];
        FocusKey(controller, baseFile);
        Assert.True(controller.OnListKey(Key.CursorLeft));
        Assert.DoesNotContain("tool", Names(controller));

        controller.ListSearch.SetSearch(Literal("tool"));
        controller.ListSearch.Find(backward: false, inclusive: true);

        Assert.Contains("tool", Names(controller));
        Assert.Equal("tool", FocusedName(controller));

        // The focus is on the match, inside the file: the file stays open. Once the focus is elsewhere, the search collapses it again.
        FocusKey(controller, chain.Session.MainSource);
        controller.RestoreSearchCollapsed();

        Assert.DoesNotContain("tool", Names(controller));
    }

    [Fact]
    public void Enter_and_F2_on_the_opened_file_row_in_edit_mode_open_the_file_settings()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);

        foreach (Key key in new[] { Key.Enter, Key.F2 })
        {
            FocusKey(controller, temp.Session.MainSource);
            FileDialog? dialog = null;
            app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
            {
                // The dialog is modal: read it while it is open, then close it, which changes nothing.
                dialog = app.TopRunnableView as FileDialog;
                app.RequestStop();
                return false;
            });

            Assert.True(controller.OnListKey(key), $"{key} on the opened file's row");

            Assert.NotNull(dialog);
            Assert.False(temp.Session.IsDirty);
        }
    }

    [Fact]
    public void Enter_and_F2_on_a_base_file_row_in_edit_mode_show_the_refusal_and_open_nothing()
    {
        using TempChain chain = TempChain.Open(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, chain.Session);
        ConfigSource baseFile = chain.Session.Sources[0];
        FocusKey(controller, baseFile);
        string expected = $"'{baseFile.DisplayPath}' is a base file; edit that file.";

        foreach (Key key in new[] { Key.Enter, Key.F2 })
        {
            ClearMessage(controller);
            // Nothing modal opens here: the call returns at once.
            Assert.True(controller.OnListKey(key), $"{key} on a base file's row");
            Assert.Equal(expected, Message(controller));
        }

        Assert.False(chain.Session.IsDirty);
    }

    [Fact]
    public void Enter_and_F2_on_a_file_row_in_run_mode_open_nothing_and_show_no_message()
    {
        using TempChain chain = TempChain.Open(SessionMode.Run);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, chain.Session);
        FocusKey(controller, chain.Session.MainSource);

        foreach (Key key in new[] { Key.Enter, Key.F2 })
        {
            ClearMessage(controller);
            Assert.True(controller.OnListKey(key));
            Assert.Null(Message(controller));
        }
    }

    [Fact]
    public void Enter_Edit_hint_is_offered_on_the_opened_file_row_and_not_on_a_base_file_row()
    {
        using TempChain chain = TempChain.Open(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, chain.Session);
        Field<CommandListView>(controller, "_list").SetFocus();

        FocusKey(controller, chain.Session.MainSource);
        Assert.Contains("Enter Edit", HintLeft(controller));

        FocusKey(controller, chain.Session.Sources[0]);
        Assert.DoesNotContain("Enter Edit", HintLeft(controller));
    }

    /// <summary>Builds the controller over the session, without showing it: the list, its rows and its focus are set up.</summary>
    private static MainController Build(IApplication app, CmdrSession session)
    {
        var controller = new MainController(app, session);
        controller.Build();
        return controller;
    }

    /// <summary>Points the list at the row of the key (a file, group or command), as its focus does.</summary>
    private static void FocusKey(MainController controller, object key) =>
        typeof(MainController).GetMethod("FocusKey", Members)!.Invoke(controller, new[] { key });

    /// <summary>The command with the given name in the session.</summary>
    private static CommandEntry Command(CmdrSession session, string name) => session.AllEntries.Single(entry => entry.Name == name);

    /// <summary>The names of the group and command rows the list shows, in order. File rows are left out.</summary>
    private static IReadOnlyList<string> Names(MainController controller) =>
        Field<CommandListSource>(controller, "_source").Rows
            .Where(row => row is not FileRow)
            .Select(row => TreeRows.ItemOf(row).Name)
            .ToList();

    /// <summary>The text of the focused row (its name, or the display path of a file row), read from the list's selection.</summary>
    private static string? FocusedName(MainController controller)
    {
        CommandListView list = Field<CommandListView>(controller, "_list");
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        int row = list.SelectedItem is { } line ? source.Lines.LineToRow(line) : -1;
        return row >= 0 ? TreeRows.SearchTextOf(source.Rows[row]) : null;
    }

    /// <summary>The index of the shown row of the file.</summary>
    private static int RowIndex(MainController controller, ConfigSource file) =>
        Field<CommandListSource>(controller, "_source").Rows
            .Select((row, index) => (row, index))
            .Single(pair => pair.row is FileRow fileRow && ReferenceEquals(fileRow.Source, file))
            .index;

    /// <summary>The names of the commands that are marked, in file order.</summary>
    private static IReadOnlyList<string> Marked(MainController controller) =>
        Field<Cmdr.Core.Session.Selection>(controller, "_selection").Marked.Select(entry => entry.Name).ToList();

    /// <summary>The transient message in the status line, or null when none is shown.</summary>
    private static string? Message(MainController controller) =>
        (string?)typeof(MainController).GetField("_message", Members)!.GetValue(controller);

    private static void ClearMessage(MainController controller) =>
        typeof(MainController).GetField("_message", Members)!.SetValue(controller, null);

    /// <summary>The left part of the hint bar, as the controller shows it.</summary>
    private static string HintLeft(MainController controller) =>
        (string)typeof(MainController).GetMethod("HintLeft", Members)!.Invoke(controller, null)!;

    private static void Invoke(MainController controller, string method) =>
        typeof(MainController).GetMethod(method, Members)!.Invoke(controller, null);

    /// <summary>The pattern for a case-sensitive literal query.</summary>
    private static SearchPattern Literal(string text)
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(text, CaseSensitive: true, WholeWord: false, Regex: false), out string? error);
        Assert.Null(error);
        return Assert.IsType<SearchPattern>(pattern);
    }

    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, Members)!.GetValue(controller)!;

    /// <summary>The value of a private field of a dialog, read by reflection.</summary>
    private static object? Field(object dialog, string name) => dialog.GetType().GetField(name, Members)!.GetValue(dialog);
}
