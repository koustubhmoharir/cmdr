using System.Reflection;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Cmdr.Tests;

/// <summary>
/// The command keys of the bar's left hint depend on the focused pane: the list shows all of them, and the info and output panes
/// show Ctrl+C, F5 and L only for a shown command, never Space or Ctrl+A. Save shows only with unsaved changes; Max and Edit always show. Covers the hint text itself.
/// </summary>
[Collection("Terminal GUI")]
public sealed class HintsByFocusTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Info_pane_with_a_command_shows_Stop_Restart_and_Log_but_not_Mark_or_All()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        FocusKey(controller, temp.Session.AllEntries.First());
        Field<DetailsView>(controller, "_details").SetFocus();

        Assert.Equal("Enter Start  ^C Stop  F5 Restart  L Log  c Copy command  + Max  ^E Edit", HintLeft(controller));
    }

    [Fact]
    public void Output_pane_with_a_command_shows_Stop_Restart_and_Log_but_not_Mark_or_All()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        FocusKey(controller, temp.Session.AllEntries.First());
        Field<OutputView>(controller, "_output").SetFocus();

        // No output lines yet, so c copies nothing and has no hint.
        Assert.Equal("Enter Start  ^C Stop  F5 Restart  L Log  + Max  ^E Edit", HintLeft(controller));
    }

    [Fact]
    public void Info_pane_with_a_group_row_shows_only_Max_and_Edit()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        FocusKey(controller, Group(temp));
        Field<DetailsView>(controller, "_details").SetFocus();

        Assert.Equal("+ Max  ^E Edit", HintLeft(controller));
    }

    [Fact]
    public void Output_pane_with_a_group_row_shows_only_Max_and_Edit()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        FocusKey(controller, Group(temp));
        Field<OutputView>(controller, "_output").SetFocus();

        Assert.Equal("+ Max  ^E Edit", HintLeft(controller));
    }

    [Fact]
    public void List_with_a_command_row_still_shows_all_the_command_keys()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        FocusKey(controller, temp.Session.AllEntries.First());
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.Contains("^C Stop  F5 Restart  Space Mark  ^A All  L Log  c Copy command  + Max  ^E Edit", HintLeft(controller), StringComparison.Ordinal);
    }

    [Fact]
    public void No_pane_focused_shows_no_command_keys_and_ends_with_Max_and_Edit()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        FocusKey(controller, temp.Session.AllEntries.First());
        Assert.False(Field<CommandListView>(controller, "_list").HasFocus);
        Assert.False(Field<DetailsView>(controller, "_details").HasFocus);
        Assert.False(Field<OutputView>(controller, "_output").HasFocus);

        // Enter does not reach the list with no pane focused, so neither Enter nor the command keys show, even for a focused command.
        Assert.Equal("+ Max  ^E Edit", HintLeft(controller));
    }

    [Fact]
    public void List_with_a_group_row_shows_Stop_Restart_and_All_but_not_Mark_or_Log()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        FocusKey(controller, Group(temp));
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.Equal("Enter Start  ^C Stop  F5 Restart  ^A All  + Max  ^E Edit", HintLeft(controller));
    }

    [Fact]
    public void List_with_no_row_shows_only_Max_and_Edit()
    {
        using TempSession temp = TempSession.Open();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.Equal("+ Max  ^E Edit", HintLeft(controller));
    }

    [Fact]
    public void Search_field_focus_shows_the_search_keys_and_no_command_keys()
    {
        using TempSession temp = TempSession.Sample();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = new MainController(app, temp.Session);
        controller.Build();
        FocusKey(controller, temp.Session.AllEntries.First());
        Field<CommandListView>(controller, "_list").SetFocus();
        controller.OnListKey((Key)'/');
        SearchBar bar = Field<SearchBar>(controller, "_searchBar");
        Assert.True(bar.HasFieldFocus);

        string text = HintLeft(controller);
        Assert.Contains("F3 Next", text, StringComparison.Ordinal);
        Assert.DoesNotContain("^C Stop", text, StringComparison.Ordinal);
        Assert.DoesNotContain("L Log", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Space Mark", text, StringComparison.Ordinal);
    }

    private static GroupEntry Group(TempSession temp) => temp.Session.BuildRows().OfType<GroupRow>().First().Group;

    private static void FocusKey(MainController controller, object key) =>
        typeof(MainController).GetMethod("FocusKey", Members)!.Invoke(controller, new[] { key });

    private static string HintLeft(MainController controller) =>
        (string)typeof(MainController).GetMethod("HintLeft", Members)!.Invoke(controller, null)!;

    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, Members)!.GetValue(controller)!;
}
