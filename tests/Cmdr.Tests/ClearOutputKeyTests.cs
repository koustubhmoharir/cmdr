using System.Reflection;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>
/// Del in run mode clears the in-memory output of what the key acts on: the marks, else the focused command, or the commands
/// of a focused group. It does not ask, and the info and output panes clear the shown command alone. In edit mode Del still
/// deletes, which a base command refuses before any confirmation.
/// </summary>
[Collection("Terminal GUI")]
public sealed class ClearOutputKeyTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Del_on_a_focused_command_clears_only_its_output()
    {
        using TempSession temp = TempSession.Sample();
        CommandEntry first = Entry(temp.Session, "first");
        CommandEntry last = Entry(temp.Session, "last");
        Write(first, 2);
        Write(last, 2);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        Focus(controller, first);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.True(controller.OnListKey(Key.Delete));

        Assert.Equal(0, first.Process.Output.Buffer.Count);
        Assert.Equal(2, last.Process.Output.Buffer.Count);
    }

    [Fact]
    public void Del_with_marks_clears_the_marked_commands_only()
    {
        using TempSession temp = TempSession.Sample();
        CommandEntry first = Entry(temp.Session, "first");
        CommandEntry last = Entry(temp.Session, "last");
        CommandEntry m1 = Entry(temp.Session, "m1");
        Write(first, 1);
        Write(last, 1);
        Write(m1, 1);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);

        // last is marked; first is focused, but the mark is the target.
        Focus(controller, last);
        Assert.True(controller.OnListKey(Key.Space));
        Focus(controller, first);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.True(controller.OnListKey(Key.Delete));

        Assert.Equal(0, last.Process.Output.Buffer.Count);
        Assert.Equal(1, first.Process.Output.Buffer.Count);
        Assert.Equal(1, m1.Process.Output.Buffer.Count);
    }

    [Fact]
    public void Del_on_a_group_header_clears_every_command_below_it_including_subgroups()
    {
        using TempSession temp = TempSession.Sample();
        GroupEntry top = temp.Session.AllGroups.Single(g => g.Name == "top");
        CommandEntry[] under = { Entry(temp.Session, "first"), Entry(temp.Session, "m1"), Entry(temp.Session, "d1"), Entry(temp.Session, "last") };
        CommandEntry other = Entry(temp.Session, "oc");
        foreach (CommandEntry entry in under.Append(other))
        {
            Write(entry, 1);
        }

        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        Focus(controller, top);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.True(controller.OnListKey(Key.Delete));

        foreach (CommandEntry entry in under)
        {
            Assert.Equal(0, entry.Process.Output.Buffer.Count);
        }

        Assert.Equal(1, other.Process.Output.Buffer.Count);
    }

    [Fact]
    public void Del_in_the_output_pane_clears_the_shown_command_and_ignores_marks()
    {
        using TempSession temp = TempSession.Sample();
        CommandEntry first = Entry(temp.Session, "first");
        CommandEntry last = Entry(temp.Session, "last");
        Write(first, 2);
        Write(last, 2);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);

        // last is marked and first is shown: only the shown command clears.
        Focus(controller, last);
        Assert.True(controller.OnListKey(Key.Space));
        Focus(controller, first);
        OutputView output = Field<OutputView>(controller, "_output");
        output.SetFocus();

        Assert.True(output.NewKeyDownEvent(Key.Delete));

        Assert.Equal(0, first.Process.Output.Buffer.Count);
        Assert.Equal(2, last.Process.Output.Buffer.Count);
    }

    [Fact]
    public void Del_in_the_output_pane_on_a_group_header_clears_nothing()
    {
        using TempSession temp = TempSession.Sample();
        CommandEntry first = Entry(temp.Session, "first");
        Write(first, 1);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp.Session);
        Focus(controller, temp.Session.AllGroups.Single(g => g.Name == "top"));
        OutputView output = Field<OutputView>(controller, "_output");
        output.SetFocus();

        Assert.True(output.NewKeyDownEvent(Key.Delete));

        Assert.Equal(1, first.Process.Output.Buffer.Count);
    }

    [Fact]
    public void Del_in_edit_mode_deletes_and_does_not_clear_the_output()
    {
        using TempChain chain = TempChain.Open(SessionMode.Edit);
        CommandEntry tool = chain.Session.AllEntries.Single(e => e.Name == "tool");
        Write(tool, 2);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, chain.Session);
        Focus(controller, tool);
        Field<CommandListView>(controller, "_list").SetFocus();

        // A base command is refused before any confirmation, so the delete goes no further and the output stays.
        Assert.True(controller.OnListKey(Key.Delete));
        Assert.Equal(tool.ReadOnlyMessage, Message(controller));
        Assert.Equal(2, tool.Process.Output.Buffer.Count);
        Assert.Contains(tool, chain.Session.AllEntries);

        DetailsView details = Field<DetailsView>(controller, "_details");
        details.SetFocus();
        Assert.True(details.NewKeyDownEvent(Key.Delete));
        Assert.Equal(2, tool.Process.Output.Buffer.Count);
    }

    /// <summary>Writes <paramref name="count"/> system lines to the output of the entry, as a run would show them.</summary>
    private static void Write(CommandEntry entry, int count)
    {
        for (int i = 0; i < count; i++)
        {
            entry.Process.Output.WriteSystem($"{entry.Name} line {i}");
        }
    }

    private static CommandEntry Entry(CmdrSession session, string name) => session.AllEntries.Single(e => e.Name == name);

    /// <summary>A controller built on the initialized application.</summary>
    private static MainController Build(IApplication app, CmdrSession session)
    {
        var controller = new MainController(app, session);
        controller.Build();
        return controller;
    }

    /// <summary>Points the panes at the row of the entry or group, as the list's focus does.</summary>
    private static void Focus(MainController controller, object key) =>
        typeof(MainController).GetMethod("FocusKey", Members)!.Invoke(controller, new[] { key });

    private static string? Message(MainController controller) =>
        (string?)typeof(MainController).GetField("_message", Members)!.GetValue(controller);

    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, Members)!.GetValue(controller)!;
}
