using System.Reflection;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Cmdr.Tests;

/// <summary>
/// The command keys of the info and output panes act on the shown command alone; Esc stops nothing; the panes keep their own keys.
/// The tests press keys on the panes through <c>NewKeyDownEvent</c>, which runs the pane's own handler.
/// </summary>
[Collection("Terminal GUI")]
public sealed class ShownCommandKeysTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(10);

    [Fact]
    public void Esc_in_the_list_is_consumed_and_stops_nothing()
    {
        using TempSession temp = LongGroup("a");
        CommandEntry a = temp.Session.AllEntries.Single();
        Start(temp, a);
        try
        {
            using IApplication app = Application.Create();
            app.Init("ansi");
            using var controller = Build(app, temp);
            Focus(controller, a);

            Assert.True(controller.OnListKey(Key.Esc));
            Assert.Equal(ProcessState.Running, State(a));
        }
        finally
        {
            Kill(a);
        }
    }

    [Fact]
    public void Ctrl_C_in_the_output_pane_stops_the_shown_command()
    {
        using TempSession temp = LongGroup("a");
        CommandEntry a = temp.Session.AllEntries.Single();
        Start(temp, a);
        try
        {
            using IApplication app = Application.Create();
            app.Init("ansi");
            using var controller = Build(app, temp);
            Focus(controller, a);
            OutputView output = Field<OutputView>(controller, "_output");
            output.SetFocus();

            Assert.True(output.NewKeyDownEvent(Key.C.WithCtrl));
            Assert.True(a.Process.WaitForExitAsync().Wait(ExitWait), "The command did not stop.");
            Assert.Equal(ProcessState.Exited, State(a));
        }
        finally
        {
            Kill(a);
        }
    }

    [Fact]
    public void Marks_are_ignored_from_the_panes_so_only_the_shown_command_stops()
    {
        using TempSession temp = LongGroup("a", "b");
        CommandEntry a = temp.Session.AllEntries.First();
        CommandEntry b = temp.Session.AllEntries.Last();
        Start(temp, a, b);
        try
        {
            using IApplication app = Application.Create();
            app.Init("ansi");
            using var controller = Build(app, temp);

            // b is marked; a is focused and shown.
            Focus(controller, b);
            Assert.True(controller.OnListKey(Key.Space));
            Focus(controller, a);
            OutputView output = Field<OutputView>(controller, "_output");
            output.SetFocus();

            Assert.True(output.NewKeyDownEvent(Key.C.WithCtrl));
            Assert.True(a.Process.WaitForExitAsync().Wait(ExitWait), "The shown command did not stop.");
            Assert.Equal(ProcessState.Exited, State(a));
            Assert.Equal(ProcessState.Running, State(b));
        }
        finally
        {
            Kill(a);
            Kill(b);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enter_in_the_info_or_output_pane_starts_the_shown_command(bool fromOutput)
    {
        using TempSession temp = LongGroup("a");
        CommandEntry a = temp.Session.AllEntries.Single();
        try
        {
            using IApplication app = Application.Create();
            app.Init("ansi");
            using var controller = Build(app, temp);
            Focus(controller, a);
            View pane = fromOutput ? Field<OutputView>(controller, "_output") : Field<DetailsView>(controller, "_details");
            pane.SetFocus();

            Assert.True(pane.NewKeyDownEvent(Key.Enter));
            Assert.Equal(ProcessState.Running, State(a));
        }
        finally
        {
            Kill(a);
        }
    }

    [Fact]
    public void Command_keys_on_a_group_header_do_nothing_in_the_panes_but_are_consumed()
    {
        using TempSession temp = LongGroup("a", "b");
        CommandEntry a = temp.Session.AllEntries.First();
        CommandEntry b = temp.Session.AllEntries.Last();
        Start(temp, a);
        try
        {
            using IApplication app = Application.Create();
            app.Init("ansi");
            using var controller = Build(app, temp);
            Focus(controller, temp.Session.AllGroups.Single());
            OutputView output = Field<OutputView>(controller, "_output");
            DetailsView details = Field<DetailsView>(controller, "_details");

            foreach (View pane in new View[] { details, output })
            {
                pane.SetFocus();
                Assert.True(pane.NewKeyDownEvent(Key.Enter));
                Assert.True(pane.NewKeyDownEvent(Key.C.WithCtrl));
                Assert.True(pane.NewKeyDownEvent(Key.L));
            }

            // Enter on the header in the list would start b, and Ctrl+C would stop a; neither happens from the panes.
            Assert.Equal(ProcessState.Running, State(a));
            Assert.Equal(ProcessState.NotStarted, State(b));
        }
        finally
        {
            Kill(a);
            Kill(b);
        }
    }

    [Fact]
    public void Output_pane_keeps_c_for_copying_and_Esc_for_clearing_its_selection()
    {
        using TempSession temp = LongGroup("a");
        CommandEntry a = temp.Session.AllEntries.Single();
        a.Process.Output.WriteSystem("first line");
        a.Process.Output.WriteSystem("second line");
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, temp);
        Focus(controller, a);
        OutputView output = Field<OutputView>(controller, "_output");
        output.SetFocus();

        // The output pane copies its own text. The list's copy of the command line would say "Copied command line".
        Assert.True(output.NewKeyDownEvent(Key.C));
        string? message = (string?)typeof(MainController).GetField("_message", Members)!.GetValue(controller);
        Assert.NotNull(message);
        Assert.Matches("^Copied [0-9]+ characters$", message);

        // Shift+Down selects a line; Esc clears the selection and does nothing else.
        Assert.True(output.NewKeyDownEvent(Key.CursorDown.WithShift));
        Assert.NotNull(output.GetType().GetField("_selection", Members)!.GetValue(output));
        Assert.True(output.NewKeyDownEvent(Key.Esc));
        Assert.Null(output.GetType().GetField("_selection", Members)!.GetValue(output));
        Assert.Equal(ProcessState.NotStarted, State(a));
    }

    [Fact]
    public void Hint_describes_the_shown_command_in_the_output_pane_and_the_marks_in_the_list()
    {
        using TempSession temp = LongGroup("a", "b");
        CommandEntry a = temp.Session.AllEntries.First();
        CommandEntry b = temp.Session.AllEntries.Last();
        Start(temp, b);
        try
        {
            using IApplication app = Application.Create();
            app.Init("ansi");
            using var controller = Build(app, temp);

            // b is marked and running; a is shown in the output pane and not started.
            Focus(controller, b);
            Assert.True(controller.OnListKey(Key.Space));
            Focus(controller, a);
            OutputView output = Field<OutputView>(controller, "_output");
            output.SetFocus();
            Assert.True(output.HasFocus);

            string paneHint = HintLeft(controller);
            Assert.StartsWith("Enter Start  ", paneHint);
            Assert.DoesNotContain("Esc", paneHint);

            // The list's target is the mark: the running b, which Enter does not start.
            Field<CommandListView>(controller, "_list").SetFocus();
            Assert.DoesNotContain("Enter", HintLeft(controller));
        }
        finally
        {
            Kill(a);
            Kill(b);
        }
    }

    /// <summary>A group "g" of long-running commands with the given names, none started.</summary>
    private static TempSession LongGroup(params string[] names) =>
        TempSession.Open(new GroupSpec { Name = "g", Items = names.Select(name => (ItemSpec)LongCommand(name)).ToList() });

    /// <summary>A command that keeps running until it is stopped or killed.</summary>
    private static CommandSpec LongCommand(string name) => OperatingSystem.IsWindows()
        ? new CommandSpec { Name = name, Program = "ping", Args = { "-n", "30", "127.0.0.1" } }
        : new CommandSpec { Name = name, Program = "sleep", Args = { "30" } };

    private static void Start(TempSession temp, params CommandEntry[] entries)
    {
        Assert.Null(temp.Session.Activate(entries));
        foreach (CommandEntry entry in entries)
        {
            Assert.Equal(ProcessState.Running, State(entry));
        }
    }

    /// <summary>Force-kills a command that is still running or stopping, and waits for it to exit.</summary>
    private static void Kill(CommandEntry entry)
    {
        ProcessState state = State(entry);
        if (state is ProcessState.Running or ProcessState.Stopping)
        {
            entry.Process.ForceKill();
            Assert.True(entry.Process.WaitForExitAsync().Wait(ExitWait), "The command did not exit.");
        }
    }

    private static ProcessState State(CommandEntry entry) => entry.Process.Snapshot.State;

    /// <summary>A controller built on the initialized application, with its list focused on the first row.</summary>
    private static MainController Build(IApplication app, TempSession temp)
    {
        var controller = new MainController(app, temp.Session);
        controller.Build();
        return controller;
    }

    /// <summary>Points the panes at the row of the entry or group, as the list's focus does.</summary>
    private static void Focus(MainController controller, object key) =>
        typeof(MainController).GetMethod("FocusKey", Members)!.Invoke(controller, new[] { key });

    private static string HintLeft(MainController controller) =>
        (string)typeof(MainController).GetMethod("HintLeft", Members)!.Invoke(controller, null)!;

    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, Members)!.GetValue(controller)!;
}
