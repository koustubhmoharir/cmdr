using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Cmdr.Tests;

/// <summary>A plain click on the ▾ or ▸ of a group header toggles the group; a click anywhere else, and Ctrl+click, do not.</summary>
[Collection("Terminal GUI")]
public sealed class GroupGlyphClickTests
{
    private const int Width = 120;
    private const int Height = 40;

    // The sample tree's rows, in order: top (depth 0), first, mid (depth 1), m1, deep (depth 2), d1, last, other (depth 0), oc.
    private const int TopRow = 0;
    private const int MidRow = 2;
    private const int DeepRow = 4;
    private const int FirstRow = 1;
    private const int OtherRow = 7;

    [Fact]
    public void A_plain_press_on_the_glyph_of_an_expanded_header_collapses_its_group_and_focuses_the_header()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            Click(app, controller, TopRow, 0);

            Assert.Equal(new[] { "top", "other", "oc" }, Names(controller));
            Assert.Equal("top", FocusedName(controller));
        });
    }

    [Fact]
    public void A_plain_press_on_the_glyph_of_a_collapsed_header_expands_it()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            Click(app, controller, TopRow, 0);
            Click(app, controller, TopRow, 0);

            Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "d1", "last", "other", "oc" }, Names(controller));
            Assert.Equal("top", FocusedName(controller));
        });
    }

    [Fact]
    public void A_plain_press_on_the_name_of_a_header_selects_it_without_toggling()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            Click(app, controller, OtherRow, 2);

            Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "d1", "last", "other", "oc" }, Names(controller));
            Assert.Equal("other", FocusedName(controller));
        });
    }

    [Fact]
    public void A_press_in_the_indent_of_a_nested_header_does_not_toggle_it()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            Click(app, controller, MidRow, 0);

            Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "d1", "last", "other", "oc" }, Names(controller));
        });
    }

    [Fact]
    public void The_glyph_of_a_nested_header_sits_at_its_depth_and_toggles_only_that_group()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            // mid is at depth 1, so its glyph is at column 2; deep is at depth 2, so its glyph is at column 4.
            Click(app, controller, MidRow, 2);

            Assert.Equal(new[] { "top", "first", "mid", "last", "other", "oc" }, Names(controller));
            Assert.Equal("mid", FocusedName(controller));

            Click(app, controller, MidRow, 2);
            Click(app, controller, DeepRow, 4);

            Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "last", "other", "oc" }, Names(controller));
            Assert.Equal("deep", FocusedName(controller));
        });
    }

    [Fact]
    public void A_press_on_a_command_row_does_not_toggle_anything()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            // The command "first" is in top (depth 0), so its glyph is at column 2, where a header one deeper would have its glyph.
            Click(app, controller, FirstRow, 2);

            Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "d1", "last", "other", "oc" }, Names(controller));
            Assert.Equal("first", FocusedName(controller));
        });
    }

    [Fact]
    public void Ctrl_click_on_the_glyph_of_a_header_does_not_toggle_it()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            Click(app, controller, TopRow, 0, MouseFlags.Ctrl);

            Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "d1", "last", "other", "oc" }, Names(controller));
        });
    }

    /// <summary>
    /// A left click at the cell of the shown row (by its index) and column, through the application's mouse routing. A terminal
    /// sends the press, then the click on release; each carries only its own button flag and the given modifiers.
    /// </summary>
    private static void Click(IApplication app, MainController controller, int row, int column, MouseFlags modifiers = MouseFlags.None)
    {
        CommandListView list = Field<CommandListView>(controller, "_list");
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        Rectangle screen = list.FrameToScreen();
        int line = source.Lines.RowToLine(row);
        var position = new Point(screen.X + column, screen.Y + line - list.Viewport.Y);
        app.Mouse.RaiseMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonPressed | modifiers, ScreenPosition = position });
        app.Mouse.RaiseMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonClicked | modifiers, ScreenPosition = position });
        app.LayoutAndDraw();
    }

    /// <summary>The names of the rows the list shows, in order.</summary>
    private static IEnumerable<string> Names(MainController controller)
    {
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        return source.Rows.Select(row => TreeRows.ItemOf(row).Name).ToArray();
    }

    /// <summary>The name of the focused row, read from the list's selection.</summary>
    private static string? FocusedName(MainController controller)
    {
        CommandListView list = Field<CommandListView>(controller, "_list");
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        int row = list.SelectedItem is { } line ? source.Lines.LineToRow(line) : -1;
        return row >= 0 ? TreeRows.ItemOf(source.Rows[row]).Name : null;
    }

    /// <summary>The value of a private field of the controller, read by reflection.</summary>
    private static T Field<T>(MainController controller, string name) =>
        (T)typeof(MainController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;

    /// <summary>Builds the controller over the session in a laid-out window, and runs the check on the UI loop.</summary>
    private static void RunLaidOut(TempSession temp, Action<IApplication, MainController> check)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(Width, Height);
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
