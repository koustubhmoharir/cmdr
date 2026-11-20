using System.Reflection;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Tui;
using StatusLine = Cmdr.Tui.StatusLine;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cmdr.Tests;

/// <summary>
/// The main window in each mode: which keys act, which of them say how to reach the other mode, and what Ctrl+E does to the focus,
/// the marks, the search and the title. The geometry of the switch is in <see cref="MainLayoutTests"/>.
/// </summary>
[Collection("Terminal GUI")]
public sealed class MainModeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string EditOnlyMessage = "Press Ctrl+E for edit mode to change groups and commands.";
    private const string RunOnlyMessage = "Press Ctrl+E for run mode to start or stop commands.";

    [Fact]
    public void Ctrl_E_in_run_mode_switches_to_edit_mode_and_marks_the_title()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        string file = Path.GetFileName(temp.Session.ConfigPath);
        Assert.Equal(file, ListTitle(controller));

        Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));

        Assert.Equal(SessionMode.Edit, temp.Session.Mode);
        Assert.Equal(file + " [edit]", ListTitle(controller));
    }

    [Fact]
    public void Ctrl_E_in_edit_mode_switches_to_run_mode_and_keeps_the_focus_and_the_title_follows()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        CommandListView list = Field<CommandListView>(controller, "_list");
        list.SetFocus();

        Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));

        Assert.Equal(SessionMode.Run, temp.Session.Mode);
        Assert.True(list.HasFocus);
        Assert.Equal(Path.GetFileName(temp.Session.ConfigPath), ListTitle(controller));
    }

    [Fact]
    public void The_title_shows_the_dirty_mark_after_the_edit_and_loses_it_on_save()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        string file = Path.GetFileName(temp.Session.ConfigPath);

        temp.Session.AddCommand(temp.Session.AllGroups.First(), new CommandSpec { Name = "added", Program = "sh" });
        Assert.Equal(file + " [edit] *", ListTitle(controller));

        temp.Session.Save();
        Assert.Equal(file + " [edit]", ListTitle(controller));
    }

    [Fact]
    public void Ctrl_E_clears_the_marks_when_entering_edit_mode()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        Focus(controller, temp.Session.AllEntries.First());
        Field<CommandListView>(controller, "_list").SetFocus();
        Assert.True(controller.OnListKey(Key.Space));
        Assert.Single(Field<Selection>(controller, "_selection").Marked);

        Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));

        Assert.Empty(Field<Selection>(controller, "_selection").Marked);
    }

    [Fact]
    public void Ctrl_E_from_the_output_pane_moves_the_focus_and_the_open_search_to_the_list()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        Focus(controller, temp.Session.AllEntries.First());
        OutputView output = Field<OutputView>(controller, "_output");
        CommandListView list = Field<CommandListView>(controller, "_list");
        output.SetFocus();
        Assert.True(controller.OnGlobalKey((Key)'/'));
        Assert.Same(output, Field<View>(controller, "_searchPane"));
        // Focus back on the output pane with the bar open: the search target is the output.
        output.SetFocus();

        Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));

        Assert.True(list.HasFocus);
        Assert.False(output.HasFocus);
        Assert.Same(list, Field<View>(controller, "_searchPane"));
    }

    [Fact]
    public void Ctrl_E_from_the_find_field_leaves_the_search_on_the_list_and_the_field_focused()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        Focus(controller, temp.Session.AllEntries.First());
        OutputView output = Field<OutputView>(controller, "_output");
        output.SetFocus();
        Assert.True(controller.OnGlobalKey((Key)'/'));
        SearchBar bar = Field<SearchBar>(controller, "_searchBar");
        Assert.True(bar.HasFieldFocus);

        Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));

        Assert.Equal(SessionMode.Edit, temp.Session.Mode);
        Assert.True(bar.HasFieldFocus);
        Assert.Same(Field<CommandListView>(controller, "_list"), Field<View>(controller, "_searchPane"));
    }

    [Fact]
    public void Ctrl_S_saves_in_edit_mode()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        temp.Session.AddCommand(temp.Session.AllGroups.First(), new CommandSpec { Name = "added", Program = "sh" });
        Assert.True(temp.Session.IsDirty);

        Assert.True(controller.OnGlobalKey(Key.S.WithCtrl));

        Assert.False(temp.Session.IsDirty);
        Assert.Equal("Saved " + Path.GetFileName(temp.Session.ConfigPath), Message(controller));
    }

    [Fact]
    public void Save_is_in_the_run_bar_only_while_there_are_unsaved_changes()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        Focus(controller, temp.Session.AllEntries.First());
        Field<CommandListView>(controller, "_list").SetFocus();
        temp.Session.AddCommand(temp.Session.AllGroups.First(), new CommandSpec { Name = "added", Program = "sh" });

        Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));
        Tick(controller);
        Assert.EndsWith("^S Save  + Max  ^E Edit  ^Q Quit", Bar(controller), StringComparison.Ordinal);

        temp.Session.Save();
        Tick(controller);
        Assert.EndsWith("+ Max  ^E Edit  ^Q Quit", Bar(controller), StringComparison.Ordinal);
        Assert.DoesNotContain("^S Save", Bar(controller), StringComparison.Ordinal);
    }

    [Fact]
    public void Run_mode_edit_keys_show_the_edit_message_and_change_nothing()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        CommandEntry first = temp.Session.AllEntries.First();
        Focus(controller, first);
        Field<CommandListView>(controller, "_list").SetFocus();
        int before = temp.Session.AllEntries.Count;

        foreach (Key key in new[] { Key.F2, Key.Delete, Key.N.WithCtrl, Key.G.WithCtrl, Key.CursorUp.WithAlt, Key.CursorDown.WithAlt })
        {
            ClearMessage(controller);
            Assert.True(controller.OnListKey(key), $"{key} in the list");
            Assert.Equal(EditOnlyMessage, Message(controller));
        }

        // The info pane takes F2 and Del too, and shows the same message.
        DetailsView details = Field<DetailsView>(controller, "_details");
        details.SetFocus();
        foreach (Key key in new[] { Key.F2, Key.Delete })
        {
            ClearMessage(controller);
            Assert.True(details.NewKeyDownEvent(key), $"{key} in the info pane");
            Assert.Equal(EditOnlyMessage, Message(controller));
        }

        Assert.Equal(SessionMode.Run, temp.Session.Mode);
        Assert.False(temp.Session.IsDirty);
        Assert.Equal(before, temp.Session.AllEntries.Count);
        Assert.Equal(ProcessState.NotStarted, first.Process.Snapshot.State);
    }

    [Fact]
    public void Edit_mode_run_keys_show_the_run_message_and_start_nothing()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        CommandEntry first = temp.Session.AllEntries.First();
        Focus(controller, first);
        Field<CommandListView>(controller, "_list").SetFocus();

        foreach (Key key in new[] { Key.C.WithCtrl, Key.F5, Key.L })
        {
            ClearMessage(controller);
            Assert.True(controller.OnListKey(key), $"{key} in the list");
            Assert.Equal(RunOnlyMessage, Message(controller));
        }

        DetailsView details = Field<DetailsView>(controller, "_details");
        details.SetFocus();
        foreach (Key key in new[] { Key.C.WithCtrl, Key.F5, Key.L })
        {
            ClearMessage(controller);
            Assert.True(details.NewKeyDownEvent(key), $"{key} in the info pane");
            Assert.Equal(RunOnlyMessage, Message(controller));
        }

        Assert.Equal(SessionMode.Edit, temp.Session.Mode);
        Assert.Equal(ProcessState.NotStarted, first.Process.Snapshot.State);
    }

    [Fact]
    public void Edit_mode_consumes_the_mark_keys_and_marks_nothing()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        Focus(controller, temp.Session.AllEntries.First());
        Field<CommandListView>(controller, "_list").SetFocus();
        ClearMessage(controller);

        Assert.True(controller.OnListKey(Key.Space));
        Assert.True(controller.OnListKey(Key.CursorDown.WithShift));
        Assert.True(controller.OnListKey(Key.CursorUp.WithShift));
        Assert.True(controller.OnListKey(Key.A.WithCtrl));
        controller.OnCtrlClick(-1);

        Assert.Empty(Field<Selection>(controller, "_selection").Marked);
        Assert.Null(Message(controller));
    }

    [Fact]
    public void Edit_mode_maximize_key_is_consumed_and_the_output_stays_hidden()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.True(controller.OnGlobalKey((Key)'='));

        Assert.False(Field<OutputView>(controller, "_output").Visible);
        Assert.True(Field<FrameView>(controller, "_listFrame").Visible);
        Assert.False((bool)typeof(MainController).GetField("_maximized", Members)!.GetValue(controller)!);
    }

    [Fact]
    public void Edit_mode_tab_cycles_only_the_list_and_the_info_pane()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        CommandListView list = Field<CommandListView>(controller, "_list");
        DetailsView details = Field<DetailsView>(controller, "_details");
        OutputView output = Field<OutputView>(controller, "_output");
        list.SetFocus();

        controller.CycleFocusPane(backward: false);
        Assert.True(details.HasFocus);
        controller.CycleFocusPane(backward: false);
        Assert.True(list.HasFocus);
        controller.CycleFocusPane(backward: true);
        Assert.True(details.HasFocus);
        Assert.False(output.HasFocus);
    }

    [Fact]
    public void Alt_Up_at_the_first_sibling_does_nothing_and_shows_nothing()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        GroupEntry top = temp.Session.AllGroups.Single(g => g.Name == "top");
        Focus(controller, top.Items[0]);
        Field<CommandListView>(controller, "_list").SetFocus();
        ClearMessage(controller);

        Assert.True(controller.OnListKey(Key.CursorUp.WithAlt));

        Assert.Equal("first", top.Items[0].Name);
        Assert.Null(Message(controller));
    }

    [Fact]
    public void Alt_Down_moves_the_command_among_its_siblings_in_edit_mode()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        GroupEntry top = temp.Session.AllGroups.Single(g => g.Name == "top");
        Focus(controller, top.Items[0]);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.True(controller.OnListKey(Key.CursorDown.WithAlt));

        Assert.Equal("mid", top.Items[0].Name);
        Assert.Equal("first", top.Items[1].Name);
    }

    /// <summary>The list's title, as the frame shows it.</summary>
    private static string ListTitle(MainController controller) => Field<FrameView>(controller, "_listFrame").Title;

    /// <summary>The transient message in the status line, or null when none is shown.</summary>
    private static string? Message(MainController controller) =>
        (string?)typeof(MainController).GetField("_message", Members)!.GetValue(controller);

    private static void ClearMessage(MainController controller) =>
        typeof(MainController).GetField("_message", Members)!.SetValue(controller, null);

    [Fact]
    public void A_switch_drops_a_message_about_the_mode_it_left_and_shows_the_new_modes_keys()
    {
        using TempSession temp = TempSession.Sample(SessionMode.Edit);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.True(controller.OnListKey(Key.C.WithCtrl));
        Assert.Contains(RunOnlyMessage, Bar(controller));

        Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));

        Assert.DoesNotContain(RunOnlyMessage, Bar(controller));
        Assert.Contains("^E Edit", Bar(controller));
    }

    private static MainController Build(IApplication app, TempSession temp)
    {
        var controller = new MainController(app, temp.Session);
        controller.Build();
        return controller;
    }

    /// <summary>Points the panes at the row of the entry or group, as the list's focus does.</summary>
    private static void Focus(MainController controller, object key) =>
        typeof(MainController).GetMethod("FocusKey", Members)!.Invoke(controller, new[] { key });

    /// <summary>One fast tick of the controller, which refreshes the bar's hints as the running timer does.</summary>
    private static void Tick(MainController controller) =>
        typeof(MainController).GetMethod("OnFastTick", Members)!.Invoke(controller, null);

    /// <summary>The left and right parts of the bar, as the status line shows them.</summary>
    private static string Bar(MainController controller)
    {
        StatusLine status = Field<StatusLine>(controller, "_status");
        return status.Shown.Text + status.ShownRight.Text;
    }

    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, Members)!.GetValue(controller)!;
}
