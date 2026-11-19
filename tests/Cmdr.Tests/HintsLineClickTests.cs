using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tests;

/// <summary>
/// The hint bar of the main window, clicked and drawn: a left press on a label performs its key through the application's keyboard,
/// with the focus where it was, and the bar's targets are drawn as buttons. The clicks are injected through the input pipeline.
/// </summary>
[Collection("Terminal GUI")]
public sealed class HintsLineClickTests
{
    private const int Width = 120;
    private const int Height = 40;
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Clicking_Space_Mark_marks_the_focused_command_and_leaves_the_focus_and_the_list_selection_alone()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, (app, controller) =>
        {
            CommandListView list = FocusCommand(app, controller);
            int? selected = list.SelectedItem;
            CommandEntry build = temp.Session.AllEntries.First();

            Click(app, controller, "Space Mark");

            Assert.True(Field<Selection>(controller, "_selection").IsMarked(build));
            Assert.True(list.HasFocus);
            Assert.Equal(selected, list.SelectedItem);
        });
    }

    [Fact]
    public void Clicking_c_Copy_command_copies_the_command_line_and_names_it_in_the_bar()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, (app, controller) =>
        {
            CommandListView list = FocusCommand(app, controller);

            Click(app, controller, "c Copy command");

            StatusLine status = Field<StatusLine>(controller, "_status");
            Assert.StartsWith("Copied command line", status.Shown.Text, StringComparison.Ordinal);
            Assert.True(list.HasFocus);
        });
    }

    [Fact]
    public void Clicking_Alt_C_Case_toggles_case_in_the_find_field_and_the_field_keeps_the_focus()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, (app, controller) =>
        {
            FocusCommand(app, controller);
            app.Keyboard.RaiseKeyDownEvent((Key)'/');
            app.LayoutAndDraw();
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            Assert.True(bar.HasFieldFocus);
            Assert.False(bar.Query.CaseSensitive);

            Click(app, controller, "Alt+C Case");
            Assert.True(bar.Query.CaseSensitive);
            Assert.True(bar.HasFieldFocus);

            Click(app, controller, "Alt+C Case");
            Assert.False(bar.Query.CaseSensitive);
            Assert.True(bar.HasFieldFocus);
        });
    }

    [Fact]
    public void Clicking_Enter_Pane_gives_the_focus_back_to_the_pane_the_search_came_from()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, (app, controller) =>
        {
            CommandListView list = FocusCommand(app, controller);
            app.Keyboard.RaiseKeyDownEvent((Key)'/');
            app.LayoutAndDraw();
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");

            Click(app, controller, "Enter Pane");

            Assert.False(bar.HasFieldFocus);
            Assert.True(list.HasFocus);
        });
    }

    [Fact]
    public void Clicking_Esc_Close_closes_the_empty_search_bar()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, (app, controller) =>
        {
            FocusCommand(app, controller);
            app.Keyboard.RaiseKeyDownEvent((Key)'/');
            app.LayoutAndDraw();
            SearchBar bar = Field<SearchBar>(controller, "_searchBar");
            Assert.True(bar.Visible);

            Click(app, controller, "Esc Close");

            Assert.False(bar.Visible);
        });
    }

    [Fact]
    public void Clicking_Ctrl_Q_Quit_requests_quit()
    {
        using TempSession temp = Session();
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(Width, Height);
        using var controller = new MainController(app, temp.Session);
        controller.Build();

        // A session with nothing running and nothing unsaved quits at once, so the loop returns; the timeout only guards a hang.
        bool timedOut = false;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            Click(app, controller, "^Q Quit");
            return false;
        });
        app.AddTimeout(TimeSpan.FromSeconds(10), () =>
        {
            timedOut = true;
            app.RequestStop(controller.Window);
            return false;
        });
        app.Run(controller.Window);

        Assert.False(timedOut);
    }

    [Fact]
    public void A_press_between_two_labels_does_nothing()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, (app, controller) =>
        {
            CommandListView list = FocusCommand(app, controller);
            CommandEntry build = temp.Session.AllEntries.First();
            int gap = ColumnOf(controller, "Space Mark") + "Space Mark".Length;

            ClickColumn(app, gap);

            Assert.False(Field<Selection>(controller, "_selection").IsMarked(build));
            Assert.True(list.HasFocus);
        });
    }

    [Fact]
    public void A_click_during_a_message_does_nothing_even_where_a_label_was()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, (app, controller) =>
        {
            CommandListView list = FocusCommand(app, controller);
            CommandEntry build = temp.Session.AllEntries.First();
            int column = ColumnOf(controller, "Space Mark");
            ShowMessage(controller, "Something happened");
            app.LayoutAndDraw();

            ClickColumn(app, column);

            Assert.False(Field<Selection>(controller, "_selection").IsMarked(build));
            Assert.Equal("Something happened", Field<StatusLine>(controller, "_status").Shown.Text);
            Assert.True(list.HasFocus);
        });
    }

    [Fact]
    public void The_labels_are_drawn_as_buttons_and_the_rest_of_the_bar_keeps_the_bar_colours()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, (app, controller) =>
        {
            FocusCommand(app, controller);
            Hint shown = Field<StatusLine>(controller, "_status").Shown;
            int row = Height - 1;

            Assert.Equal(shown.Text, ScreenRow(app, row, shown.Text.Length));
            var labelColumns = new HashSet<int>();
            foreach (HintTarget target in shown.Targets)
            {
                for (int i = target.Start; i < target.Start + target.Length; i++)
                {
                    labelColumns.Add(i);
                    AssertButton(app, row, i);
                }
            }

            Assert.Contains("Space Mark", shown.Text, StringComparison.Ordinal);
            Attribute? bar = null;
            for (int i = 0; i < shown.Text.Length; i++)
            {
                if (labelColumns.Contains(i))
                {
                    continue;
                }

                Attribute cell = CellAt(app, row, i);
                bar ??= cell;
                Assert.Equal(bar.Value.Background, cell.Background);
                Assert.Equal(bar.Value.Foreground, cell.Foreground);
                Assert.False(cell.Background.Equals(HintColors.TargetBackground), $"column {i} is drawn as a button");
            }
        });
    }

    [Fact]
    public void The_quit_hint_is_a_button_at_the_right_edge()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, (app, controller) =>
        {
            FocusCommand(app, controller);
            int row = Height - 1;

            Assert.Equal("^Q Quit", ScreenRow(app, row, Width).Substring(Width - 7, 7));
            for (int column = Width - 7; column < Width; column++)
            {
                AssertButton(app, row, column);
            }
        });
    }

    [Fact]
    public void A_narrow_bar_keeps_the_quit_hint_at_the_right_edge_and_cuts_the_left_text()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, 40, Height, (app, controller) =>
        {
            FocusCommand(app, controller);
            string row = ScreenRow(app, Height - 1, 40);

            Assert.StartsWith("Enter Start", row, StringComparison.Ordinal);
            Assert.EndsWith("^Q Quit", row, StringComparison.Ordinal);
            AssertButton(app, Height - 1, 40 - 1);
        });
    }

    [Fact]
    public void A_message_is_bold_on_the_bar_colours_and_has_no_buttons()
    {
        using TempSession temp = Session();

        RunLaidOut(temp, (app, controller) =>
        {
            FocusCommand(app, controller);
            ShowMessage(controller, "Saved");
            app.LayoutAndDraw();
            int row = Height - 1;

            Assert.StartsWith("Saved", ScreenRow(app, row, Width), StringComparison.Ordinal);
            for (int column = 0; column < 5; column++)
            {
                Attribute cell = CellAt(app, row, column);
                Assert.Equal(TextStyle.Bold, cell.Style);
                Assert.False(cell.Background.Equals(HintColors.TargetBackground));
            }
        });
    }

    /// <summary>A session with one group "work" holding the commands "build" and "other", both run by the shell and not started.</summary>
    private static TempSession Session() => TempSession.Open(
        new GroupSpec
        {
            Name = "work",
            Items = { new CommandSpec { Name = "build", Program = "sh" }, new CommandSpec { Name = "other", Program = "sh" } },
        });

    /// <summary>Focuses the list on the first command, so that the hints are the command's and c copies it.</summary>
    private static CommandListView FocusCommand(IApplication app, MainController controller)
    {
        CommandListView list = Field<CommandListView>(controller, "_list");
        list.SetFocus();
        app.Keyboard.RaiseKeyDownEvent(Key.CursorDown);
        Tick(controller);
        app.LayoutAndDraw();
        return list;
    }

    /// <summary>
    /// Clicks the first character of the label in the bar, through the input pipeline: a press and a release at the bottom row.
    /// The bar is refreshed first, as its timer does.
    /// </summary>
    private static void Click(IApplication app, MainController controller, string label)
    {
        Tick(controller);
        app.LayoutAndDraw();
        ClickColumn(app, ColumnOf(controller, label));
    }

    /// <summary>Clicks a column of the bottom row, through the input pipeline.</summary>
    private static void ClickColumn(IApplication app, int column)
    {
        app.InjectSequence(InputInjectionExtensions.LeftButtonClick(new Point(column, Height - 1)));
        app.LayoutAndDraw();
    }

    /// <summary>The display column of the label's first character in the bar, on the left text or the right part.</summary>
    private static int ColumnOf(MainController controller, string label)
    {
        StatusLine status = Field<StatusLine>(controller, "_status");
        int index = status.Shown.Text.IndexOf(label, StringComparison.Ordinal);
        if (index >= 0)
        {
            return TextWidth.Columns(status.Shown.Text[..index]);
        }

        string right = status.ShownRight.Text;
        int at = right.IndexOf(label, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{label}' is not in the bar '{status.Shown.Text}{right}'");
        return Width - TextWidth.Columns(right) + TextWidth.Columns(right[..at]);
    }

    /// <summary>Shows the transient message in the bar, as a failed action does.</summary>
    private static void ShowMessage(MainController controller, string text) =>
        typeof(MainController).GetMethod("ShowMessage", Members)!.Invoke(controller, new object[] { text });

    /// <summary>One fast tick of the controller, which refreshes the bar's hints as the running timer does.</summary>
    private static void Tick(MainController controller) =>
        typeof(MainController).GetMethod("OnFastTick", Members)!.Invoke(controller, null);

    /// <summary>Asserts that the cell of the bottom row is drawn on the target colours.</summary>
    private static void AssertButton(IApplication app, int row, int column)
    {
        Attribute cell = CellAt(app, row, column);
        Assert.True(cell.Background.Equals(HintColors.TargetBackground), $"column {column} background");
        Assert.True(cell.Foreground.Equals(HintColors.TargetText), $"column {column} text");
    }

    /// <summary>The attribute of a cell of the screen.</summary>
    private static Attribute CellAt(IApplication app, int row, int column) => app.Driver!.Contents![row, column].Attribute!.Value;

    /// <summary>The characters drawn on a row of the screen, left to right.</summary>
    private static string ScreenRow(IApplication app, int row, int width)
    {
        var text = new System.Text.StringBuilder();
        for (int column = 0; column < width; column++)
        {
            text.Append(app.Driver!.Contents![row, column].Grapheme);
        }

        return text.ToString();
    }

    /// <summary>The value of a private field of the controller, read by reflection.</summary>
    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, Members)!.GetValue(controller)!;

    /// <summary>Builds the controller over the session in a laid-out window, and runs the check on the UI loop.</summary>
    private static void RunLaidOut(TempSession temp, Action<IApplication, MainController> check) => RunLaidOut(temp, Width, check);

    private static void RunLaidOut(TempSession temp, int width, Action<IApplication, MainController> check) => RunLaidOut(temp, width, Height, check);

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
}
