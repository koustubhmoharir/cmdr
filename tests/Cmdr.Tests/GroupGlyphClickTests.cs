using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Cmdr.Tests;

/// <summary>A plain click on the ▾ or ▸ of a file row or a group header toggles it; a click anywhere else, and Ctrl+click, do not.</summary>
[Collection("Terminal GUI")]
public sealed class GroupGlyphClickTests
{
    private const int Width = 120;
    private const int Height = 40;

    // The glyph columns of the sample tree (see RowLayout.GroupGlyphColumn): the file row's is column 0, and each list depth adds two.
    // top and other are at list depth 1 (glyph in column 2), mid at depth 2 (column 4), deep at depth 3 (column 6).
    private const int FileGlyphColumn = 0;
    private const int TopGlyphColumn = 2;
    private const int MidGlyphColumn = 4;
    private const int DeepGlyphColumn = 6;

    [Fact]
    public void A_plain_press_on_the_glyph_of_an_expanded_header_collapses_its_group_and_focuses_the_header()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            Click(app, controller, Row(controller, "top"), TopGlyphColumn);

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
            Click(app, controller, Row(controller, "top"), TopGlyphColumn);
            Click(app, controller, Row(controller, "top"), TopGlyphColumn);

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
            // The name of other starts two columns after its glyph.
            Click(app, controller, Row(controller, "other"), TopGlyphColumn + 2);

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
            Click(app, controller, Row(controller, "mid"), 0);

            Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "d1", "last", "other", "oc" }, Names(controller));
        });
    }

    [Fact]
    public void The_glyph_of_a_nested_header_sits_at_its_depth_and_toggles_only_that_group()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            Click(app, controller, Row(controller, "mid"), MidGlyphColumn);

            Assert.Equal(new[] { "top", "first", "mid", "last", "other", "oc" }, Names(controller));
            Assert.Equal("mid", FocusedName(controller));

            Click(app, controller, Row(controller, "mid"), MidGlyphColumn);
            Click(app, controller, Row(controller, "deep"), DeepGlyphColumn);

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
            // The command "first" is in top (list depth 1), so its glyph is at column 4: the glyph column of its sibling mid.
            Click(app, controller, Row(controller, "first"), MidGlyphColumn);

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
            Click(app, controller, Row(controller, "top"), TopGlyphColumn, MouseFlags.Ctrl);

            Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "d1", "last", "other", "oc" }, Names(controller));
        });
    }

    [Fact]
    public void A_plain_press_on_the_glyph_of_a_file_row_collapses_the_file_and_hides_all_its_rows()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            Click(app, controller, Row(controller, temp.Session.MainSource.DisplayPath), FileGlyphColumn);

            Assert.Empty(Names(controller));
            Assert.Equal(temp.Session.MainSource.DisplayPath, FocusedName(controller));
        });
    }

    [Fact]
    public void A_plain_press_on_the_glyph_of_a_collapsed_file_row_expands_it()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            Click(app, controller, Row(controller, temp.Session.MainSource.DisplayPath), FileGlyphColumn);
            Click(app, controller, Row(controller, temp.Session.MainSource.DisplayPath), FileGlyphColumn);

            Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "d1", "last", "other", "oc" }, Names(controller));
            Assert.Equal(temp.Session.MainSource.DisplayPath, FocusedName(controller));
        });
    }

    [Fact]
    public void A_press_on_the_name_of_a_file_row_does_not_toggle_it()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            Click(app, controller, Row(controller, temp.Session.MainSource.DisplayPath), 2);

            Assert.Equal(new[] { "top", "first", "mid", "m1", "deep", "d1", "last", "other", "oc" }, Names(controller));
            Assert.Equal(temp.Session.MainSource.DisplayPath, FocusedName(controller));
        });
    }

    [Fact]
    public void Ctrl_click_on_the_glyph_of_a_file_row_does_not_toggle_it()
    {
        using TempSession temp = TempSession.Sample();

        RunLaidOut(temp, (app, controller) =>
        {
            Click(app, controller, Row(controller, temp.Session.MainSource.DisplayPath), FileGlyphColumn, MouseFlags.Ctrl);

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

    /// <summary>The index of the shown row whose text is <paramref name="text"/> (see <see cref="TreeRows.SearchTextOf"/>). The row must be shown.</summary>
    private static int Row(MainController controller, string text)
    {
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        int index = -1;
        for (int i = 0; i < source.Rows.Count && index < 0; i++)
        {
            if (TreeRows.SearchTextOf(source.Rows[i]) == text)
            {
                index = i;
            }
        }

        Assert.True(index >= 0, $"No shown row '{text}'.");
        return index;
    }

    /// <summary>The names of the group and command rows the list shows, in order. File rows are left out.</summary>
    private static IEnumerable<string> Names(MainController controller)
    {
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        return source.Rows.Where(row => row is not FileRow).Select(row => TreeRows.ItemOf(row).Name).ToArray();
    }

    /// <summary>The text of the focused row (its name, or the display path of a file row), read from the list's selection.</summary>
    private static string? FocusedName(MainController controller)
    {
        CommandListView list = Field<CommandListView>(controller, "_list");
        CommandListSource source = Field<CommandListSource>(controller, "_source");
        int row = list.SelectedItem is { } line ? source.Lines.LineToRow(line) : -1;
        return row >= 0 ? TreeRows.SearchTextOf(source.Rows[row]) : null;
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
