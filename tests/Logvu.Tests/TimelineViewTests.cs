using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Logvu.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tests;

/// <summary>The Terminal.Gui tests run one at a time, as the Cmdr tests do.</summary>
[CollectionDefinition("Terminal GUI", DisableParallelization = true)]
public sealed class TerminalGuiCollection
{
}

/// <summary>The timeline view: its drawn cells, and the mapping from keys and the wheel to the model's commands.</summary>
[Collection("Terminal GUI")]
public sealed class TimelineViewTests
{
    private const int Width = 40;

    [Fact]
    public void The_first_row_is_a_bold_header_naming_the_columns()
    {
        TimelineModel model = Model(Store(3));
        model.Start(2);

        Screen screen = Draw(model, Width, 3);

        // At 40 columns the message has 21 columns, so nothing is dropped: the header has Time, Level and Message.
        Assert.Equal("Time" + new string(' ', 9) + "Level Message", screen.Text(0));
        Assert.True(screen.Cells[0][0].Attribute.Style.HasFlag(TextStyle.Bold));
    }

    [Fact]
    public void The_selected_row_is_drawn_in_the_focus_colors_and_the_others_in_the_normal_ones()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);

        Screen screen = Draw(model, Width, 4);

        Attribute focus = screen.Focus;
        Attribute selected = screen.Cells[3][^1].Attribute;
        Attribute other = screen.Cells[1][^1].Attribute;
        Assert.Equal(focus.Foreground, selected.Foreground);
        Assert.Equal(focus.Background, selected.Background);
        Assert.NotEqual(screen.Normal.Background, selected.Background);
        Assert.Equal(screen.Normal.Foreground, other.Foreground);
        Assert.Equal(screen.Normal.Background, other.Background);
        Assert.Contains("r2", screen.Text(3));
        Assert.Contains("r0", screen.Text(1));
    }

    [Fact]
    public void Rows_show_the_row_text_in_the_message_column()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);

        Screen screen = Draw(model, Width, 4);

        Assert.Contains("r0", screen.Text(1));
        Assert.Contains("r1", screen.Text(2));
        Assert.Contains("r2", screen.Text(3));
    }

    [Fact]
    public void Before_the_timeline_starts_a_placeholder_shows_under_the_header()
    {
        TimelineModel model = Model(Store(3));

        Screen screen = Draw(model, Width, 3, placeholder: "Loading…");

        Assert.StartsWith("Loading…", screen.Text(1));
    }

    [Fact]
    public void Level_labels_are_always_text_and_take_their_colors()
    {
        // The newest row is the selected one, so the Fatal row is last: the others keep their row colors.
        var source = new FakeTimelineSource(
        [
            Row(0, LogLevel.Trace),
            Row(1, LogLevel.Warn),
            Row(2, LogLevel.Error),
            Row(3, LogLevel.Fatal),
        ]);
        TimelineModel model = Model(source);
        model.Start(4);

        Screen screen = Draw(model, 60, 5);

        Assert.Contains("TRACE", screen.Text(1));
        Assert.Contains("WARN", screen.Text(2));
        Assert.Contains("ERROR", screen.Text(3));
        Assert.Contains("FATAL", screen.Text(4));
        Assert.Equal(LevelColors.Warn, screen.Cells[2][TextOf(screen, 2, "WARN")].Attribute.Foreground);
        Assert.Equal(LevelColors.Error, screen.Cells[3][TextOf(screen, 3, "ERROR")].Attribute.Foreground);
        Assert.Equal(LevelColors.FatalBackground, screen.Cells[4][TextOf(screen, 4, "FATAL")].Attribute.Background);
        Assert.True(screen.Cells[1][TextOf(screen, 1, "TRACE")].Attribute.Style.HasFlag(TextStyle.Faint));
    }

    [Fact]
    public void Up_selects_the_previous_row_and_pauses()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        Assert.True(view.HandleKey(Key.CursorUp));

        Assert.Equal("r98", model.SelectedRow!.Preview);
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void Down_selects_the_next_row()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        view.HandleKey(Key.CursorUp);
        view.HandleKey(Key.CursorUp);

        Assert.True(view.HandleKey(Key.CursorDown));

        Assert.Equal("r98", model.SelectedRow!.Preview);
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void Shift_up_and_shift_down_move_the_viewport_by_one_row()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        Assert.True(view.HandleKey(Key.CursorUp.WithShift));
        Assert.Equal("r96", model.VisibleRows[0].Preview);
        Assert.Equal(TimelineMode.Paused, model.Mode);

        Assert.True(view.HandleKey(Key.CursorDown.WithShift));
        Assert.Equal("r97", model.VisibleRows[0].Preview);
    }

    [Fact]
    public void Page_up_and_page_down_move_the_viewport_by_a_page()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        Assert.True(view.HandleKey(Key.PageUp));
        string afterUp = model.VisibleRows[0].Preview;
        Assert.NotEqual("r97", afterUp);
        Assert.Equal(TimelineMode.Paused, model.Mode);

        Assert.True(view.HandleKey(Key.PageDown));
        Assert.Equal("r97", model.VisibleRows[0].Preview);
    }

    [Fact]
    public void End_resumes_follow_and_selects_the_newest_row()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        view.HandleKey(Key.CursorUp);

        Assert.True(view.HandleKey(Key.End));

        Assert.Equal(TimelineMode.Follow, model.Mode);
        Assert.Equal("r99", model.SelectedRow!.Preview);
    }

    [Fact]
    public void Space_pauses_follow_and_resumes_it()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        Assert.True(view.HandleKey(Key.Space));
        Assert.Equal(TimelineMode.Paused, model.Mode);

        Assert.True(view.HandleKey(Key.Space));
        Assert.Equal(TimelineMode.Follow, model.Mode);
    }

    [Fact]
    public void Enter_opens_the_selected_row_and_leaves_the_model_to_the_owner()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var opened = new List<RowKey>();
        view.OpenRequested += opened.Add;

        Assert.True(view.HandleKey(Key.Enter));

        Assert.Equal(new[] { model.SelectedRow!.Key }, opened);
        Assert.Equal(TimelineMode.Follow, model.Mode);
        Assert.Equal("r99", model.SelectedRow!.Preview);
    }

    [Fact]
    public void Enter_with_no_rows_is_consumed_and_opens_nothing()
    {
        TimelineModel model = Model(Store(0));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var opened = new List<RowKey>();
        view.OpenRequested += opened.Add;

        Assert.True(view.HandleKey(Key.Enter));

        Assert.Empty(opened);
    }

    [Fact]
    public void Right_and_l_open_the_selected_row_like_enter()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var opened = new List<RowKey>();
        view.OpenRequested += opened.Add;

        Assert.True(view.HandleKey(Key.CursorRight));
        Assert.True(view.HandleKey(Key.L));

        Assert.Equal(new[] { model.SelectedRow!.Key, model.SelectedRow!.Key }, opened);
        Assert.Equal(TimelineMode.Follow, model.Mode);
        Assert.Equal("r99", model.SelectedRow!.Preview);
    }

    [Fact]
    public void Right_and_l_with_no_rows_are_consumed_and_open_nothing()
    {
        TimelineModel model = Model(Store(0));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var opened = new List<RowKey>();
        view.OpenRequested += opened.Add;

        Assert.True(view.HandleKey(Key.CursorRight));
        Assert.True(view.HandleKey(Key.L));

        Assert.Empty(opened);
    }

    [Fact]
    public void J_and_k_move_the_selection_like_down_and_up()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        Assert.True(view.HandleKey(Key.K));
        Assert.Equal("r98", model.SelectedRow!.Preview);
        Assert.Equal(TimelineMode.Paused, model.Mode);

        Assert.True(view.HandleKey(Key.J));
        Assert.Equal("r99", model.SelectedRow!.Preview);
    }

    [Fact]
    public void Left_and_h_are_not_bound_in_the_timeline()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var opened = new List<RowKey>();
        view.OpenRequested += opened.Add;

        Assert.False(view.HandleKey(Key.CursorLeft));
        Assert.False(view.HandleKey(Key.H));

        Assert.Empty(opened);
        Assert.Equal(TimelineMode.Follow, model.Mode);
        Assert.Equal("r99", model.SelectedRow!.Preview);
    }

    [Theory]
    [InlineData('f')]
    [InlineData('n')]
    [InlineData('N')]
    [InlineData('/')]
    public void Printable_keys_reserved_for_later_are_not_handled(char ch)
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        Assert.False(view.HandleKey((Key)ch));
        Assert.Equal(TimelineMode.Follow, model.Mode);
    }

    [Fact]
    public void Ctrl_q_and_escape_are_left_to_the_window()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        Assert.False(view.HandleKey(Key.Q.WithCtrl));
        Assert.False(view.HandleKey(Key.Esc));
        Assert.Equal(TimelineMode.Follow, model.Mode);
    }

    [Fact]
    public void The_wheel_up_shifts_the_viewport_three_rows_toward_older_rows()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        Assert.True(view.HandleMouse(new Mouse { Flags = MouseFlags.WheeledUp }));

        Assert.Equal("r94", model.VisibleRows[0].Preview);
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void The_wheel_down_shifts_the_viewport_three_rows_toward_newer_rows()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        view.HandleMouse(new Mouse { Flags = MouseFlags.WheeledUp });

        Assert.True(view.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown }));

        Assert.Equal("r97", model.VisibleRows[0].Preview);
    }

    [Fact]
    public void A_left_press_on_a_row_selects_it_and_pauses_without_moving_the_viewport()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        // Row 2 of the screen is the second data row (the header is row 0).
        Assert.True(view.HandleMouse(Press(x: 5, y: 2)));

        Assert.Equal("r98", model.SelectedRow!.Preview);
        Assert.Equal(TimelineMode.Paused, model.Mode);
        Assert.Equal("r97", model.VisibleRows[0].Preview);
    }

    [Fact]
    public void A_left_press_on_the_header_is_ignored()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        Assert.True(view.HandleMouse(Press(x: 5, y: 0)));

        Assert.Equal("r99", model.SelectedRow!.Preview);
        Assert.Equal(TimelineMode.Follow, model.Mode);
    }

    [Fact]
    public void A_left_press_below_the_last_row_is_ignored()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        // Only three rows are loaded in the viewport of the model: rows 1 to 3 of the screen. Row 4 is past them.
        Assert.True(view.HandleMouse(Press(x: 5, y: 4)));

        Assert.Equal("r99", model.SelectedRow!.Preview);
        Assert.Equal(TimelineMode.Follow, model.Mode);
    }

    private static Mouse Press(int x, int y) => new()
    {
        Flags = MouseFlags.LeftButtonPressed,
        Position = new(x, y),
    };

    [Fact]
    public void A_mouse_event_that_is_not_a_wheel_event_is_not_handled()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        Assert.False(view.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased }));
        Assert.Equal(TimelineMode.Follow, model.Mode);
    }

    [Fact]
    public void Extra_columns_are_drawn_under_their_titles_between_level_and_message()
    {
        var specs = new[] { new ColumnSpec("method", "Method", 10) };
        TimelineModel model = Model(new FakeTimelineSource([WithValues(Row(0, LogLevel.Unknown), "GET"), WithValues(Row(1, LogLevel.Unknown), "POST")]));
        model.Start(2);

        Screen screen = Draw(model, 80, 3, columns: specs);

        string header = screen.Text(0);
        int method = header.IndexOf("Method", StringComparison.Ordinal);
        Assert.True(header.IndexOf("Level", StringComparison.Ordinal) < method);
        Assert.True(method < header.IndexOf("Message", StringComparison.Ordinal));
        Assert.Equal(method, screen.Text(1).IndexOf("GET", StringComparison.Ordinal));
        Assert.Equal(method, screen.Text(2).IndexOf("POST", StringComparison.Ordinal));
    }

    [Fact]
    public void An_extra_column_keeps_its_width_when_scrolling_to_shorter_values_and_resets_on_a_resize()
    {
        // The newest row, which Follow shows at the bottom, has the long value; the rows above have short ones.
        var rows = Enumerable.Range(0, 10).Select(index => WithValues(Row(index, LogLevel.Unknown), index == 9 ? "abcdefghi" : "x")).ToList();
        TimelineModel model = Model(new FakeTimelineSource(rows));
        model.Start(3);
        var specs = new[] { new ColumnSpec("v", "Ext", 30) };
        TimelineView? view = null;

        List<TerminalScreen> screens = TerminalHarness.Run(
            view = new TimelineView(model, TimeZoneInfo.Utc, specs),
            60,
            4,
            () =>
            {
                // Three rows up: the newest row, with the long value, leaves the viewport.
                for (int i = 0; i < 3; i++)
                {
                    model.Execute(TimelineCommand.ShiftUp);
                }

                // Without the controller there is no owner to redraw the view when the model changes.
                view!.SetNeedsDraw();
            },
            () => view!.Width = 50);

        int wide = screens[0].Text(0).IndexOf("Message", StringComparison.Ordinal);
        Assert.Contains("abcdefghi", screens[0].Text(1) + screens[0].Text(2) + screens[0].Text(3));
        Assert.DoesNotContain("abcdefghi", screens[1].Text(1) + screens[1].Text(2) + screens[1].Text(3));
        Assert.Equal(wide, screens[1].Text(0).IndexOf("Message", StringComparison.Ordinal));

        // A narrower view is a new width: the column is only as wide as its title and its values now.
        Assert.True(screens[2].Text(0).IndexOf("Message", StringComparison.Ordinal) < wide);
    }

    [Fact]
    public void The_model_height_is_the_view_height_less_the_header_and_at_least_one()
    {
        Assert.Equal(4, TimelineView.RowsFor(5));
        Assert.Equal(1, TimelineView.RowsFor(1));
        Assert.Equal(1, TimelineView.RowsFor(0));
    }

    private static int TextOf(Screen screen, int row, string text)
    {
        string line = screen.Text(row);
        return line.IndexOf(text, StringComparison.Ordinal);
    }

    private static TimelineModel Model(FakeTimelineSource source) =>
        new(source, action => action(), FakeTimelineSource.CompareKeys);

    private static EventRow WithValues(EventRow row, params string?[] values) => row with { Columns = values };

    private static FakeTimelineSource Store(int count) =>
        new(Enumerable.Range(0, count).Select(index => Row(index, LogLevel.Unknown)));

    private static EventRow Row(int index, LogLevel level) => new(
        new RowKey(1, index * 100L),
        new EventExtent(0, 0, 0, false, false),
        new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero).AddSeconds(index),
        null,
        level,
        null,
        $"r{index}",
        false,
        false,
        false);

    /// <summary>
    /// Draws the view at a size, in a window of the same size, and reads the driver's cells back. The model must be started before
    /// the call; the view is sized to the model's rows plus the header.
    /// </summary>
    private static Screen Draw(TimelineModel model, int width, int height, string? placeholder = null, IReadOnlyList<ColumnSpec>? columns = null)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);

        Screen? screen = null;
        var view = new TimelineView(model, TimeZoneInfo.Utc, columns) { X = 0, Y = 0, Width = width, Height = height, Placeholder = placeholder };
        var window = new Window { X = 0, Y = 0, Width = width, Height = height, BorderStyle = LineStyle.None };
        window.Add(view);
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            var driver = app.Driver!;
            var cells = new List<(string Grapheme, Attribute Attribute)[]>(height);
            for (int r = 0; r < height; r++)
            {
                var row = new (string, Attribute)[width];
                for (int c = 0; c < width; c++)
                {
                    row[c] = (driver.Contents![r, c].Grapheme, driver.Contents[r, c].Attribute ?? default);
                }

                cells.Add(row);
            }

            screen = new Screen(cells, view.GetAttributeForRole(VisualRole.Focus), view.GetAttributeForRole(VisualRole.Normal));
            app.RequestStop(window);
            return false;
        });
        app.Run(window);
        return screen!;
    }

    /// <summary>The drawn cells of a screen, with the attributes of the focus and normal roles.</summary>
    private sealed record Screen(IReadOnlyList<(string Grapheme, Attribute Attribute)[]> Cells, Attribute Focus, Attribute Normal)
    {
        /// <summary>The text of a row, with trailing spaces trimmed.</summary>
        public string Text(int row) => string.Concat(Cells[row].Select(cell => cell.Grapheme)).TrimEnd();
    }
}
