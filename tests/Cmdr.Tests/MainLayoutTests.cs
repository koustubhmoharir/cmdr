using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cmdr.Tests;

/// <summary>The main window's geometry, and the output pane's maximize toggle and the focus cycle that goes with it.</summary>
[Collection("Terminal GUI")]
public sealed class MainLayoutTests
{
    private const int Width = 120;
    private const int Height = 40;

    [Fact]
    public void Top_row_holds_list_and_info_pane_and_the_output_sits_below_them_above_the_status_line()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            FrameView list = Field<FrameView>(controller, "_listFrame");
            FrameView details = Field<FrameView>(controller, "_detailsFrame");
            OutputHeader header = Field<OutputHeader>(controller, "_outputHeader");
            OutputView output = Field<OutputView>(controller, "_output");
            HintsLine status = Field<HintsLine>(controller, "_status");
            CommandListSource source = Field<CommandListSource>(controller, "_source");

            int expectedHeight = Math.Min(Math.Max(5, source.Count + 2), Height * 60 / 100);
            Assert.Equal(expectedHeight, list.Frame.Height);
            Assert.Equal(expectedHeight, details.Frame.Height);
            Assert.Equal(0, list.Frame.Y);
            Assert.Equal(0, details.Frame.Y);
            Assert.Equal(list.Frame.Right, details.Frame.X);
            Assert.Equal(Width, details.Frame.Right);

            // The header line sits under the top row, and the output directly under the header, with no box around it.
            Assert.Equal(0, header.Frame.X);
            Assert.Equal(Width, header.Frame.Width);
            Assert.Equal(1, header.Frame.Height);
            Assert.Equal(details.Frame.Bottom, header.Frame.Y);

            Assert.Equal(0, output.Frame.X);
            Assert.Equal(Width, output.Frame.Width);
            Assert.Equal(header.Frame.Bottom, output.Frame.Y);
            Assert.Equal(Height - 1, status.Frame.Y);
            Assert.Equal(1, status.Frame.Height);
            Assert.Equal(output.Frame.Bottom, status.Frame.Y);
        });
    }

    [Fact]
    public void Top_row_is_capped_at_sixty_percent_of_the_window_when_the_list_is_longer()
    {
        var commands = Enumerable.Range(0, 100)
            .Select(i => (ItemSpec)new CommandSpec { Name = "c" + i, Program = "sh" })
            .ToList();
        using TempSession temp = TempSession.Open(new GroupSpec { Name = "many", Items = commands });

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            FrameView list = Field<FrameView>(controller, "_listFrame");
            FrameView details = Field<FrameView>(controller, "_detailsFrame");
            CommandListSource source = Field<CommandListSource>(controller, "_source");

            Assert.True(source.Count + 2 > Height * 60 / 100, "The list must not fit, for this test to mean anything.");
            Assert.Equal(Height * 60 / 100, list.Frame.Height);
            Assert.Equal(Height * 60 / 100, details.Frame.Height);
        });
    }

    [Fact]
    public void Top_row_is_at_least_five_rows_when_there_are_no_groups_or_commands()
    {
        using TempSession temp = TempSession.Open();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            FrameView details = Field<FrameView>(controller, "_detailsFrame");
            OutputHeader header = Field<OutputHeader>(controller, "_outputHeader");
            OutputView output = Field<OutputView>(controller, "_output");

            // The file row of the opened file is the only line: the list has no group or command.
            Assert.Equal(1, Field<CommandListSource>(controller, "_source").Count);
            Assert.Equal(5, details.Frame.Height);
            Assert.Equal(5, header.Frame.Y);
            Assert.Equal(6, output.Frame.Y);
        });
    }

    [Fact]
    public void Top_row_height_follows_the_list_when_a_group_is_collapsed_and_expanded()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            FrameView list = Field<FrameView>(controller, "_listFrame");
            FrameView details = Field<FrameView>(controller, "_detailsFrame");
            CommandListSource source = Field<CommandListSource>(controller, "_source");

            // The focus starts on the first row, the "top" group header. Left collapses it.
            int expanded = details.Frame.Height;
            Assert.True(expanded > 5, $"Expected the sample list to be taller than the minimum, was {expanded}.");

            controller.OnListKey(Key.CursorLeft);
            app.LayoutAndDraw();
            int collapsedLines = source.Count;
            Assert.Equal(Math.Max(5, collapsedLines + 2), details.Frame.Height);
            Assert.True(details.Frame.Height < expanded, $"Collapsed {details.Frame.Height}, expanded {expanded}.");
            Assert.Equal(list.Frame.Height, details.Frame.Height);

            // Right expands it again: the top row goes back to its first height.
            controller.OnListKey(Key.CursorRight);
            app.LayoutAndDraw();
            Assert.Equal(expanded, details.Frame.Height);
        });
    }

    [Theory]
    [InlineData('=', false)]
    [InlineData('+', false)]
    [InlineData('=', true)]
    public void Maximize_key_toggles_the_output_over_the_whole_area_and_restores_it(char ch, bool shift)
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            FrameView list = Field<FrameView>(controller, "_listFrame");
            FrameView details = Field<FrameView>(controller, "_detailsFrame");
            OutputHeader header = Field<OutputHeader>(controller, "_outputHeader");
            OutputView output = Field<OutputView>(controller, "_output");
            CommandListView listView = Field<CommandListView>(controller, "_list");
            OutputView outputView = Field<OutputView>(controller, "_output");
            listView.SetFocus();
            app.LayoutAndDraw();
            Assert.True(listView.HasFocus);
            Rectangle normalHeader = header.Frame;
            Rectangle normalOutput = output.Frame;

            Key key = shift ? ((Key)ch).WithShift : (Key)ch;
            Assert.True(controller.OnGlobalKey(key));
            app.LayoutAndDraw();

            Assert.Equal(new Rectangle(0, 0, Width, 1), header.Frame);
            Assert.Equal(new Rectangle(0, 1, Width, Height - 2), output.Frame);
            Assert.False(list.Visible);
            Assert.False(details.Visible);
            Assert.True(outputView.HasFocus, "Focus must leave the hidden list.");
            Assert.False(listView.HasFocus);

            Assert.True(controller.OnGlobalKey(key));
            app.LayoutAndDraw();

            Assert.True(list.Visible);
            Assert.True(details.Visible);
            Assert.Equal(normalHeader, header.Frame);
            Assert.Equal(normalOutput, output.Frame);
            Assert.True(outputView.HasFocus, "Restoring keeps the focus where it is.");
        });
    }

    [Fact]
    public void Maximize_from_the_info_pane_moves_focus_to_the_output()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            OutputHeader header = Field<OutputHeader>(controller, "_outputHeader");
            OutputView outputView = Field<OutputView>(controller, "_output");
            DetailsView detailsView = Field<DetailsView>(controller, "_details");
            detailsView.SetFocus();
            app.LayoutAndDraw();
            Assert.True(detailsView.HasFocus);

            controller.OnGlobalKey((Key)'=');
            app.LayoutAndDraw();

            Assert.Equal(new Rectangle(0, 0, Width, 1), header.Frame);
            Assert.Equal(new Rectangle(0, 1, Width, Height - 2), outputView.Frame);
            Assert.True(outputView.HasFocus);
            Assert.False(detailsView.HasFocus);
        });
    }

    [Fact]
    public void The_header_shows_the_output_focus_state()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            OutputHeader header = Field<OutputHeader>(controller, "_outputHeader");
            OutputView outputView = Field<OutputView>(controller, "_output");
            CommandListView listView = Field<CommandListView>(controller, "_list");
            listView.SetFocus();
            app.LayoutAndDraw();
            Assert.False(header.PaneFocused);

            outputView.SetFocus();
            app.LayoutAndDraw();
            Assert.True(header.PaneFocused);

            listView.SetFocus();
            app.LayoutAndDraw();
            Assert.False(header.PaneFocused);
        });
    }

    [Fact]
    public void While_maximized_tab_and_shift_tab_keep_the_focus_on_the_output()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            FrameView list = Field<FrameView>(controller, "_listFrame");
            FrameView details = Field<FrameView>(controller, "_detailsFrame");
            OutputView outputView = Field<OutputView>(controller, "_output");
            CommandListView listView = Field<CommandListView>(controller, "_list");
            outputView.SetFocus();
            app.LayoutAndDraw();

            // Without maximize, Tab moves on from the output to the list, so the checks below are not vacuous.
            controller.CycleFocusPane(backward: false);
            Assert.True(listView.HasFocus);
            outputView.SetFocus();

            Assert.True(controller.OnGlobalKey((Key)'='));
            app.LayoutAndDraw();

            controller.CycleFocusPane(backward: false);
            Assert.True(outputView.HasFocus);
            controller.CycleFocusPane(backward: true);
            Assert.True(outputView.HasFocus);
            Assert.False(list.Visible);
            Assert.False(details.Visible);
        });
    }

    [Fact]
    public void Edit_mode_hides_the_output_and_the_top_row_fills_the_height_above_the_status_line()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            FrameView list = Field<FrameView>(controller, "_listFrame");
            FrameView details = Field<FrameView>(controller, "_detailsFrame");
            OutputHeader header = Field<OutputHeader>(controller, "_outputHeader");
            OutputView output = Field<OutputView>(controller, "_output");
            HintsLine status = Field<HintsLine>(controller, "_status");
            CommandListView listView = Field<CommandListView>(controller, "_list");
            listView.SetFocus();
            app.LayoutAndDraw();

            Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));
            app.LayoutAndDraw();

            Assert.Equal(SessionMode.Edit, temp.Session.Mode);
            Assert.Equal(new Rectangle(0, 0, list.Frame.Width, Height - 1), list.Frame);
            Assert.Equal(new Rectangle(list.Frame.Right, 0, Width - list.Frame.Right, Height - 1), details.Frame);
            Assert.False(header.Visible);
            Assert.False(output.Visible);
            Assert.True(status.Visible);
            Assert.Equal(Height - 1, status.Frame.Y);
        });
    }

    [Fact]
    public void Edit_mode_top_row_stops_above_the_search_bar_while_it_is_open()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            FrameView list = Field<FrameView>(controller, "_listFrame");
            FrameView details = Field<FrameView>(controller, "_detailsFrame");
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            CommandListView listView = Field<CommandListView>(controller, "_list");
            listView.SetFocus();
            app.LayoutAndDraw();

            Assert.True(controller.OnGlobalKey((Key)'/'));
            app.LayoutAndDraw();
            // Tab-like move of the focus off the find field, so the bar stays open on the list.
            listView.SetFocus();
            Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));
            app.LayoutAndDraw();

            Assert.True(bar.Visible);
            Assert.Equal(Height - 2, list.Frame.Height);
            Assert.Equal(Height - 2, details.Frame.Height);
            Assert.Equal(Height - 2, bar.Frame.Y);
        });
    }

    [Fact]
    public void Edit_mode_ignores_the_maximize_key_and_keeps_the_layout()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            FrameView list = Field<FrameView>(controller, "_listFrame");
            OutputView output = Field<OutputView>(controller, "_output");
            CommandListView listView = Field<CommandListView>(controller, "_list");
            listView.SetFocus();
            Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));
            app.LayoutAndDraw();
            Rectangle listFrame = list.Frame;

            Assert.True(controller.OnGlobalKey((Key)'='));
            app.LayoutAndDraw();

            Assert.Equal(listFrame, list.Frame);
            Assert.True(list.Visible);
            Assert.False(output.Visible);
            Assert.True(listView.HasFocus);
        });
    }

    [Fact]
    public void Edit_then_run_restores_the_top_row_header_and_output()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            FrameView list = Field<FrameView>(controller, "_listFrame");
            FrameView details = Field<FrameView>(controller, "_detailsFrame");
            OutputHeader header = Field<OutputHeader>(controller, "_outputHeader");
            OutputView output = Field<OutputView>(controller, "_output");
            CommandListView listView = Field<CommandListView>(controller, "_list");
            listView.SetFocus();
            app.LayoutAndDraw();
            Rectangle normalList = list.Frame;
            Rectangle normalDetails = details.Frame;
            Rectangle normalHeader = header.Frame;
            Rectangle normalOutput = output.Frame;

            Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));
            app.LayoutAndDraw();
            Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));
            app.LayoutAndDraw();

            Assert.Equal(SessionMode.Run, temp.Session.Mode);
            Assert.Equal(normalList, list.Frame);
            Assert.Equal(normalDetails, details.Frame);
            Assert.True(header.Visible);
            Assert.True(output.Visible);
            Assert.Equal(normalHeader, header.Frame);
            Assert.Equal(normalOutput, output.Frame);
            Assert.True(listView.HasFocus, "Switching back to run mode keeps the focus where it was.");
        });
    }

    [Fact]
    public void Entering_edit_from_maximized_output_restores_the_top_row_and_moves_focus_to_the_list()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, Width, Height, (app, controller) =>
        {
            FrameView list = Field<FrameView>(controller, "_listFrame");
            OutputHeader header = Field<OutputHeader>(controller, "_outputHeader");
            OutputView output = Field<OutputView>(controller, "_output");
            CommandListView listView = Field<CommandListView>(controller, "_list");
            listView.SetFocus();
            Assert.True(controller.OnGlobalKey((Key)'='));
            app.LayoutAndDraw();
            Assert.True(output.HasFocus);

            Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));
            app.LayoutAndDraw();

            Assert.True(list.Visible);
            Assert.True(listView.HasFocus);
            Assert.False(output.HasFocus);
            Assert.False(output.Visible);
            Assert.False(header.Visible);
            Assert.Equal(Height - 1, list.Frame.Height);
        });
    }

    [Fact]
    public void Edit_switch_refused_while_a_command_runs_keeps_the_run_layout()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using TempSession temp = TempSession.Open(new GroupSpec { Name = "g", Items = { new CommandSpec { Name = "a", Program = "sleep", Args = { "30" } } } });
        CommandEntry entry = temp.Session.AllEntries.Single();
        Assert.Null(temp.Session.Activate(new[] { entry }));
        try
        {
            RunLaidOut(temp, Width, Height, (app, controller) =>
            {
                FrameView list = Field<FrameView>(controller, "_listFrame");
                FrameView details = Field<FrameView>(controller, "_detailsFrame");
                OutputHeader header = Field<OutputHeader>(controller, "_outputHeader");
                OutputView output = Field<OutputView>(controller, "_output");
                CommandListView listView = Field<CommandListView>(controller, "_list");
                listView.SetFocus();
                app.LayoutAndDraw();
                Rectangle normalHeader = header.Frame;

                Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));
                app.LayoutAndDraw();

                Assert.Equal(SessionMode.Run, temp.Session.Mode);
                Assert.True(output.Visible);
                Assert.True(header.Visible);
                Assert.Equal(normalHeader, header.Frame);
                Assert.Equal(list.Frame.Height, details.Frame.Height);
                Assert.Contains("Cannot switch to edit mode", Field<HintsLine>(controller, "_status").Shown.Text);
            });
        }
        finally
        {
            entry.Process.ForceKill();
            Assert.True(entry.Process.WaitForExitAsync().Wait(TimeSpan.FromSeconds(10)), "The command did not exit.");
        }
    }

    /// <summary>Builds the controller at the given size, lays it out and runs <paramref name="check"/> on the UI loop.</summary>
    /// <remarks>
    /// Assertions inside <paramref name="check"/> run on the Terminal.Gui loop. Their failures are rethrown here after the
    /// application stops, so they fail the test as usual.
    /// </remarks>
    private static void RunLaidOut(TempSession temp, int width, int height, Action<IApplication, MainController> check)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);
        using var controller = new MainController(app, temp.Session);
        controller.Build();

        ExceptionDispatchInfo? failure = null;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            try
            {
                app.LayoutAndDraw();
                check(app, controller);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                app.RequestStop(controller.Window);
            }

            return false;
        });
        app.Run(controller.Window);

        failure?.Throw();
    }

    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
}
