using System.Reflection;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Cmdr.Tests;

/// <summary>
/// The "c" part of the status line: it names what c copies in the focused pane, and is left out where c copies nothing. Covers
/// the pure rule (<see cref="CopyHint"/>), the selection state the info and output panes report, and the hint text itself.
/// </summary>
[Collection("Terminal GUI")]
public sealed class CopyHintTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void List_names_the_command_on_a_command_row_and_nothing_on_a_group_or_no_row()
    {
        Assert.Equal("c Copy command", CopyHint.Describe(CopyPane.List, hasSelection: false, hasCommand: true, hasLine: false));
        Assert.Null(CopyHint.Describe(CopyPane.List, hasSelection: false, hasCommand: false, hasLine: false));
    }

    [Fact]
    public void Info_says_selection_when_there_is_one_and_command_otherwise()
    {
        Assert.Equal("c Copy selection", CopyHint.Describe(CopyPane.Details, hasSelection: true, hasCommand: true, hasLine: false));
        Assert.Equal("c Copy selection", CopyHint.Describe(CopyPane.Details, hasSelection: true, hasCommand: false, hasLine: false));
        Assert.Equal("c Copy command", CopyHint.Describe(CopyPane.Details, hasSelection: false, hasCommand: true, hasLine: false));
        Assert.Null(CopyHint.Describe(CopyPane.Details, hasSelection: false, hasCommand: false, hasLine: false));
    }

    [Fact]
    public void Output_says_selection_when_there_is_one_and_line_otherwise()
    {
        Assert.Equal("c Copy selection", CopyHint.Describe(CopyPane.Output, hasSelection: true, hasCommand: false, hasLine: true));
        Assert.Equal("c Copy selection", CopyHint.Describe(CopyPane.Output, hasSelection: true, hasCommand: false, hasLine: false));
        Assert.Equal("c Copy line", CopyHint.Describe(CopyPane.Output, hasSelection: false, hasCommand: true, hasLine: true));
        Assert.Null(CopyHint.Describe(CopyPane.Output, hasSelection: false, hasCommand: true, hasLine: false));
    }

    [Fact]
    public void No_pane_has_no_copy_hint()
    {
        Assert.Null(CopyHint.Describe(CopyPane.None, hasSelection: true, hasCommand: true, hasLine: true));
    }

    [Fact]
    public void Details_selection_is_reported_only_when_it_is_non_empty()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        var view = new DetailsView(controller) { X = 0, Y = 0, Width = 60, Height = 12 };
        view.Show(FirstCommandRow(temp), Path.GetFullPath(temp.Session.ConfigDir));
        view.Layout();
        Assert.False(view.HasSelection);

        // A press and a drag select the characters between the two points.
        SendMouse(view, MouseFlags.LeftButtonPressed, 0, 0);
        SendMouse(view, MouseFlags.LeftButtonPressed, 3, 0);
        SendMouse(view, MouseFlags.LeftButtonReleased, 3, 0);
        Assert.True(view.HasSelection);

        // Esc clears it.
        Assert.True(KeyDown(view, Key.Esc));
        Assert.False(view.HasSelection);

        // A press without a drag leaves no selection.
        SendMouse(view, MouseFlags.LeftButtonPressed, 2, 0);
        SendMouse(view, MouseFlags.LeftButtonReleased, 2, 0);
        Assert.False(view.HasSelection);
    }

    [Fact]
    public void Output_selection_is_reported_while_it_exists_and_cleared_by_Esc()
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = OutputViewHarness.Create(app, "first line", "second line");
        Assert.False(h.View.HasSelection);

        h.Press(0, 0);
        h.Drag(2, 0);
        h.Release(2, 0);
        Assert.True(h.View.HasSelection);

        Assert.True(KeyDown(h.View, Key.Esc));
        Assert.False(h.View.HasSelection);
    }

    [Fact]
    public void Output_has_lines_only_when_the_buffer_has_one()
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness empty = OutputViewHarness.Create(app);
        Assert.False(empty.View.HasLines);

        using OutputViewHarness written = OutputViewHarness.Create(app, "a line");
        Assert.True(written.View.HasLines);
    }

    [Fact]
    public void Status_line_names_the_copy_of_the_focused_pane()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        CommandEntry entry = temp.Session.AllEntries.First();
        entry.Process.Output.WriteSystem("a line");
        FocusKey(controller, entry);

        // The list is focused on a command row.
        Field<CommandListView>(controller, "_list").SetFocus();
        Assert.Contains("L Log  c Copy command  ^S Save  + Max", HintLeft(controller));

        // The output pane has a line to copy.
        Field<OutputView>(controller, "_output").SetFocus();
        Assert.Contains("L Log  c Copy line  ^S Save  + Max", HintLeft(controller));

        // The info pane shows the command.
        Field<DetailsView>(controller, "_details").SetFocus();
        Assert.Contains("L Log  c Copy command  ^S Save  + Max", HintLeft(controller));
    }

    [Fact]
    public void Status_line_has_no_copy_hint_and_no_doubled_gap_where_c_copies_nothing()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();

        // Output without any lines: nothing to copy.
        FocusKey(controller, temp.Session.AllEntries.First());
        Field<OutputView>(controller, "_output").SetFocus();
        string text = HintLeft(controller);
        Assert.DoesNotContain("c Copy", text);
        Assert.Contains("L Log  ^S Save  + Max", text);

        // No gap is doubled where the hint is left out: the separators are two spaces, never more.
        Assert.DoesNotContain("   ", text);
    }

    [Fact]
    public void Status_line_has_no_copy_hint_for_a_group_row_in_the_list()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        GroupEntry group = temp.Session.BuildRows().OfType<GroupRow>().First().Group;
        FocusKey(controller, group);

        Field<CommandListView>(controller, "_list").SetFocus();
        string text = HintLeft(controller);
        Assert.DoesNotContain("c Copy", text);
        Assert.Contains("F5 Restart  ^A All  ^S Save  + Max", text);
    }

    private static CommandRow FirstCommandRow(TempSession temp) => temp.Session.BuildRows().OfType<CommandRow>().First();

    private static void SendMouse(DetailsView view, MouseFlags flags, int x, int y) =>
        typeof(DetailsView).GetMethod("OnMouseEvent", Members)!.Invoke(view, new object[] { new Mouse { Flags = flags, Position = new(x, y) } });

    private static bool KeyDown(View view, Key key) =>
        (bool)view.GetType().GetMethod("OnKeyDown", Members)!.Invoke(view, new object[] { key })!;

    private static void FocusKey(MainController controller, object key) =>
        typeof(MainController).GetMethod("FocusKey", Members)!.Invoke(controller, new[] { key });

    private static string HintLeft(MainController controller) =>
        (string)typeof(MainController).GetMethod("HintLeft", Members)!.Invoke(controller, null)!;

    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, Members)!.GetValue(controller)!;
}
