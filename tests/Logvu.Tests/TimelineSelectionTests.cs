using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Logvu.Tui;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tests;

/// <summary>
/// The timeline's gutter and cursor glyph, and its character selection within a row: the press that selects the row and starts the selection,
/// the drag that stays in the row, the rules that clear it, the reverse video, and the copy of the selection or of the selected event.
/// </summary>
/// <remarks>
/// The rows are <c>r0</c>, <c>r1</c>, … with the times 10:00:00, 10:00:01, … in UTC. A row's text starts with its time, <c>10:00:0N.000</c>,
/// so the characters at offsets 6 and 7 are the seconds: <c>00</c> on <c>r0</c>, and <c>02</c> on <c>r2</c>.
/// </remarks>
[Collection("Terminal GUI")]
public sealed class TimelineSelectionTests
{
    private const int Width = 60;

    // A header and three rows: the model's viewport is three rows.
    private const int Height = 4;

    [Fact]
    public void The_selected_row_shows_the_cursor_in_the_gutter_and_the_header_and_other_rows_are_blank()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);

        TerminalScreen screen = Run(model)[0];

        Assert.Equal(" ", screen.Cells[0][0].Grapheme);
        Assert.Equal(" ", screen.Cells[1][0].Grapheme);
        Assert.Equal(">", screen.Cells[3][0].Grapheme);
        Assert.True(screen.Cells[3][0].Attribute.Style.HasFlag(TextStyle.Bold));
        Assert.Equal("Time", screen.Text(0)[1..5]);
        Assert.StartsWith("10:00:02.000", screen.Text(3)[1..], StringComparison.Ordinal);
    }

    [Fact]
    public void The_selected_row_keeps_its_level_colors_and_has_no_focus_attribute()
    {
        var source = new FakeTimelineSource([Row(0, LogLevel.Info), Row(1, LogLevel.Warn)]);
        TimelineModel model = Model(source);
        model.Start(2);

        TerminalScreen screen = Run(model, height: 3)[0];

        Attribute warn = screen.At("WARN");
        Assert.Equal(">", screen.Cells[2][0].Grapheme);
        Assert.Equal(LevelColors.Warn, warn.Foreground);
        Assert.Equal(screen.Normal.Background, warn.Background);
        Assert.False(warn.Style.HasFlag(TextStyle.Reverse));
    }

    [Fact]
    public void A_press_selects_the_row_and_a_drag_selects_characters_of_that_row_only()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var copied = new List<string>();
        view.CopyRequested += copied.Add;

        List<TerminalScreen> screens = TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 7, y: 1)),
            () => view.HandleMouse(Drag(x: 9, y: 3)),
            () => view.HandleMouse(Release(x: 9, y: 3)),
            () => view.HandleKey(Key.C));

        Assert.Equal(TimelineMode.Paused, model.Mode);
        Assert.Equal("r0", model.SelectedRow!.Preview);
        Assert.Equal(new[] { "00." }, copied);
    }

    [Fact]
    public void The_selected_characters_are_drawn_in_reverse_video_and_nothing_else_is()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        List<TerminalScreen> screens = TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 7, y: 1)),
            () => view.HandleMouse(Drag(x: 9, y: 3)),
            () => view.HandleMouse(Release(x: 9, y: 3)));

        // The offsets 6 to 8 of the row are drawn in columns 7 to 9, after the gutter: the pointer's cell at offset 8 is included.
        TerminalScreen dragged = screens[2];
        Assert.True(dragged.Cells[1][7].Attribute.Style.HasFlag(TextStyle.Reverse));
        Assert.True(dragged.Cells[1][8].Attribute.Style.HasFlag(TextStyle.Reverse));
        Assert.True(dragged.Cells[1][9].Attribute.Style.HasFlag(TextStyle.Reverse));
        Assert.False(dragged.Cells[1][6].Attribute.Style.HasFlag(TextStyle.Reverse));
        Assert.False(dragged.Cells[1][10].Attribute.Style.HasFlag(TextStyle.Reverse));
        Assert.False(dragged.Cells[3][8].Attribute.Style.HasFlag(TextStyle.Reverse));
    }

    [Fact]
    public void A_release_without_a_drag_leaves_no_selection_and_c_copies_the_event()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var copied = new List<string>();
        var events = new List<RowKey>();
        view.CopyRequested += copied.Add;
        view.CopyEventRequested += events.Add;

        TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 8, y: 1)),
            () => view.HandleMouse(Release(x: 8, y: 1)),
            () =>
            {
                Assert.False(view.HasSelection);
                Assert.True(view.HandleKey(Key.C));
            });

        Assert.Empty(copied);
        Assert.Equal(new[] { model.SelectedRow!.Key }, events);
    }

    [Fact]
    public void A_drag_from_the_first_character_of_a_word_to_its_last_selects_the_whole_word()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var copied = new List<string>();
        view.CopyRequested += copied.Add;

        // The row's text starts with the hour, "10": offsets 0 and 1, drawn in columns 1 and 2.
        TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 1, y: 1)),
            () => view.HandleMouse(Drag(x: 2, y: 1)),
            () => view.HandleMouse(Release(x: 2, y: 1)),
            () => Assert.True(view.HandleKey(Key.C)));

        Assert.Equal(new[] { "10" }, copied);
    }

    [Fact]
    public void A_backward_drag_from_the_last_character_of_a_word_to_its_first_selects_the_whole_word()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var copied = new List<string>();
        view.CopyRequested += copied.Add;

        TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 2, y: 1)),
            () => view.HandleMouse(Drag(x: 1, y: 1)),
            () => view.HandleMouse(Release(x: 1, y: 1)),
            () => Assert.True(view.HandleKey(Key.C)));

        Assert.Equal(new[] { "10" }, copied);
    }

    [Fact]
    public void A_drag_back_onto_the_press_cell_selects_that_one_character()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var copied = new List<string>();
        view.CopyRequested += copied.Add;

        TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 4, y: 1)),
            () => view.HandleMouse(Drag(x: 4, y: 1)),
            () => view.HandleMouse(Release(x: 4, y: 1)),
            () => Assert.True(view.HandleKey(Key.C)));

        Assert.Equal(new[] { "0" }, copied);
    }

    [Fact]
    public void A_drag_to_the_right_of_the_row_selects_to_its_end()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var copied = new List<string>();
        view.CopyRequested += copied.Add;

        TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 7, y: 1)),
            () => view.HandleMouse(Drag(x: 59, y: 1)),
            () => view.HandleMouse(Release(x: 59, y: 1)),
            () => Assert.True(view.HandleKey(Key.C)));

        // From the first character of the row's text to its end, which is the row's padding too.
        Assert.StartsWith("00.000 -     r0", copied[0], StringComparison.Ordinal);
        Assert.Equal("00.000 -     r0", copied[0].TrimEnd());
    }

    [Fact]
    public void Esc_clears_the_selection_and_a_second_esc_is_left_to_the_window()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 8, y: 3)),
            () => view.HandleMouse(Drag(x: 10, y: 3)),
            () => view.HandleMouse(Release(x: 10, y: 3)),
            () =>
            {
                Assert.True(view.HasSelection);
                Assert.True(view.HandleKey(Key.Esc));
                Assert.False(view.HasSelection);
                Assert.False(view.HandleKey(Key.Esc));
            });

        Assert.Equal("r2", model.SelectedRow!.Preview);
    }

    [Fact]
    public void Moving_the_selected_row_clears_the_selection()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var events = new List<RowKey>();
        view.CopyEventRequested += events.Add;

        TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 8, y: 3)),
            () => view.HandleMouse(Drag(x: 10, y: 3)),
            () => view.HandleMouse(Release(x: 10, y: 3)),
            () => Assert.True(view.HandleKey(Key.CursorUp)),
            () =>
            {
                Assert.False(view.HasSelection);
                Assert.True(view.HandleKey(Key.C));
            });

        Assert.Equal("r1", model.SelectedRow!.Preview);
        Assert.Equal(new[] { model.SelectedRow!.Key }, events);
    }

    [Fact]
    public void Enter_clears_the_selection_before_the_row_opens()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        var opened = new List<(RowKey Key, bool Selected)>();
        view.OpenRequested += key => opened.Add((key, view.HasSelection));

        TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 8, y: 3)),
            () => view.HandleMouse(Drag(x: 10, y: 3)),
            () => view.HandleMouse(Release(x: 10, y: 3)),
            () => Assert.True(view.HandleKey(Key.Enter)));

        Assert.Equal(new[] { (model.SelectedRow!.Key, false) }, opened);
    }

    [Fact]
    public void A_press_on_the_header_clears_the_selection()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 8, y: 3)),
            () => view.HandleMouse(Drag(x: 10, y: 3)),
            () => view.HandleMouse(Release(x: 10, y: 3)),
            () => view.HandleMouse(Press(x: 8, y: 0)),
            () => Assert.False(view.HasSelection));
    }

    [Fact]
    public void A_selection_is_forgotten_when_its_row_scrolls_out_of_view()
    {
        TimelineModel model = Model(Store(100));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);

        TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 8, y: 3)),
            () => view.HandleMouse(Drag(x: 10, y: 3)),
            () => view.HandleMouse(Release(x: 10, y: 3)),
            () => Assert.True(view.HandleMouse(new Mouse { Flags = MouseFlags.WheeledUp })),
            () => Assert.False(view.HasSelection));

        Assert.Equal("r94", model.VisibleRows[0].Preview);
    }

    [Fact]
    public void The_seed_of_a_selection_is_the_selected_characters_of_the_row()
    {
        TimelineModel model = Model(Store(3));
        model.Start(3);
        var view = new TimelineView(model, TimeZoneInfo.Utc);
        SearchSeed? seed = null;

        TerminalHarness.Run(
            view,
            Width,
            Height,
            () => view.HandleMouse(Press(x: 1, y: 1)),
            () => view.HandleMouse(Drag(x: 6, y: 3)),
            () => view.HandleMouse(Release(x: 6, y: 3)),
            () => seed = view.SelectionSeed());

        Assert.Equal(new SearchSeed("10:00:", 0, 0), seed);
    }

    private static Mouse Press(int x, int y) => new() { Flags = MouseFlags.LeftButtonPressed, Position = new(x, y) };

    private static Mouse Drag(int x, int y) => new() { Flags = MouseFlags.PositionReport, Position = new(x, y) };

    private static Mouse Release(int x, int y) => new() { Flags = MouseFlags.LeftButtonReleased, Position = new(x, y) };

    /// <summary>Runs a view of the model at the test's size with no steps, and returns its first screen.</summary>
    private static List<TerminalScreen> Run(TimelineModel model, int height = Height) =>
        TerminalHarness.Run(new TimelineView(model, TimeZoneInfo.Utc), Width, height);

    private static TimelineModel Model(FakeTimelineSource source) =>
        new(source, action => action(), FakeTimelineSource.CompareKeys);

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
}
