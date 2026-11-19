using Logvu.Core.Parsing;
using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Logvu.Tui;
using Shared.Tui;
using Terminal.Gui.App;

using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tests;

/// <summary>
/// The hints line as drawn and as clicked: a left press performs the key under it, through the application's keyboard, and the view takes
/// no focus. A press on text with no target does nothing.
/// </summary>
public sealed class HintsLineTests
{
    [Fact]
    public void The_line_cannot_take_the_focus()
    {
        Assert.False(new HintsLine().CanFocus);
    }

    [Fact]
    public void Columns_are_display_columns_so_a_wide_glyph_takes_the_columns_it_is_drawn_in()
    {
        // Two wide characters take four columns, so the plain space is column 4 and the key after it is column 5.
        Assert.Equal(4, TextWidth.Columns("文件"));
        var line = new HintsLine();
        line.Show(KeyHints.Concat(KeyHints.Bound('f', "文件"), KeyHints.Plain(" "), KeyHints.Bound('x', "x")));

        Assert.Equal((Key)'f', line.KeyAt(0));
        Assert.Equal((Key)'f', line.KeyAt(3));
        Assert.Null(line.KeyAt(4));
        Assert.Equal((Key)'x', line.KeyAt(5));
        Assert.Null(line.KeyAt(6));
    }

    [Fact]
    public void A_left_press_on_text_without_a_target_is_consumed_and_performs_nothing()
    {
        var line = new HintsLine();
        line.Show(KeyHints.Timeline);

        // "level" is at index 12 of the timeline's text: no target, no key.
        Assert.True(line.HandleMouse(Press(KeyHints.Timeline.Text.IndexOf("level", StringComparison.Ordinal))));
        Assert.Null(line.KeyAt(KeyHints.Timeline.Text.IndexOf("level", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_press_past_the_text_and_a_release_are_not_key_presses()
    {
        var line = new HintsLine();
        line.Show(KeyHints.Timeline);

        Assert.True(line.HandleMouse(Press(200)));
        Assert.False(line.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new(0, 0) }));
    }

    [Fact]
    public void The_target_cells_are_drawn_as_buttons_and_the_rest_of_the_row_on_the_bar_colours()
    {
        var line = new HintsLine();
        line.Show(KeyHints.Timeline);

        TerminalScreen screen = TerminalHarness.Run(line, 120, 1)[0];
        string text = KeyHints.Timeline.Text;

        Assert.Equal(text, screen.Text(0));
        AssertButton(screen, 0, "f filter");
        AssertButton(screen, text.IndexOf("-", StringComparison.Ordinal), "-");
        AssertButton(screen, text.IndexOf("+", StringComparison.Ordinal), "+");
        AssertBar(screen, text.IndexOf("level", StringComparison.Ordinal), "level");
        AssertBar(screen, text.IndexOf("/", StringComparison.Ordinal), "/");
        AssertBar(screen, text.IndexOf("  ", StringComparison.Ordinal), "separator");
        AssertBar(screen, 119, "the empty end of the row");
    }

    private static Mouse Press(int x) => new() { Flags = MouseFlags.LeftButtonPressed, Position = new(x, 0) };

    private static void AssertButton(TerminalScreen screen, int column, string what)
    {
        Attribute cell = screen.Cells[0][column].Attribute;
        Assert.True(cell.Background.Equals(BarColors.TargetBackground), $"{what} background");
        Assert.True(cell.Foreground.Equals(BarColors.TargetText), $"{what} text");
    }

    private static void AssertBar(TerminalScreen screen, int column, string what)
    {
        Attribute cell = screen.Cells[0][column].Attribute;
        Assert.True(cell.Background.Equals(BarColors.Background), $"{what} background");
        Assert.True(cell.Foreground.Equals(BarColors.Text), $"{what} text");
    }
}

/// <summary>
/// The hints of a real controller, clicked through the window with injected mouse events: each label performs its key in the focused view
/// and the window, and the focus and the selection stay where they were.
/// </summary>
[Collection("Terminal GUI")]
public sealed class HintsLineClickTests : IDisposable
{
    private const int Width = 120;
    private const int Height = 10;

    private readonly LogSet _set = new(12, "info");
    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly HeldDetailSource _details;
    private readonly PostQueue _posted = new();
    private readonly TimelineController _controller;
    private readonly List<string> _written = [];
    private bool _disposed;
    private int _editCalls;

    public HintsLineClickTests()
    {
        _app = Application.Create();
        _app.Init("ansi");
        _app.Driver!.SetScreenSize(Width, Height);
        _store = new LogStore(_set.Config);
        _details = new HeldDetailSource(new LogStoreDetailSource(_store)) { Hold = false };
        _controller = new TimelineController(
            _app,
            _store,
            _details,
            _set.Config,
            _posted.Post,
            editFilter: _ =>
            {
                _editCalls++;
                return null;
            });
        _controller.WriteRaw = _written.Add;
        _controller.Start();
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count > 0, "the first rows");
    }

    [Fact]
    public void Clicking_f_filter_opens_the_filter_editor_and_keeps_the_focus_on_the_timeline()
    {
        Run(
            () => Click("f filter"),
            () =>
            {
                Assert.Equal(1, _editCalls);
                Assert.True(_controller.Timeline.HasFocus);
            });
    }

    [Fact]
    public void Clicking_End_newest_runs_End_in_the_timeline()
    {
        Run(
            () => Assert.True(_controller.Timeline.HandleKey(Key.Space)),
            () => Assert.Equal(TimelineMode.Paused, _controller.Model.Mode),
            () => Click("End newest"),
            () =>
            {
                Assert.Equal(TimelineMode.Follow, _controller.Model.Mode);
                Assert.True(_controller.Timeline.HasFocus);
            });
    }

    [Fact]
    public void Clicking_minus_and_plus_lower_and_raise_the_minimum_level()
    {
        Run(
            () => Click("-/+", offset: 0),
            () => Assert.Equal(LogLevel.Debug, _controller.Filter!.MinLevel),
            () => _posted.PumpUntil(() => _controller.StatusLeft.Contains("DEBUG", StringComparison.Ordinal), "the debug level"),
            () => Click("-/+", offset: 2),
            () => Assert.Equal(LogLevel.Info, _controller.Filter!.MinLevel));
    }

    [Fact]
    public void Clicking_text_without_a_key_does_nothing_and_leaves_the_focus_and_the_selection()
    {
        Run(
            () => Select(fromX: 1, toX: 6, y: 3),
            () => Assert.True(_controller.Timeline.HasFocus),
            () => Assert.True(_controller.Timeline.HasSelection),
            () => Click("level", offset: 1),
            () => Click("-/+", offset: 1),
            () => ClickColumn(Width - 1),
            () =>
            {
                Assert.Equal(LogLevel.Info, _controller.Filter!.MinLevel);
                // The selecting press paused the timeline; the hints clicks must not change that.
                Assert.Equal(TimelineMode.Paused, _controller.Model.Mode);
                Assert.Equal(0, _editCalls);
                Assert.True(_controller.Timeline.HasFocus);
                Assert.True(_controller.Timeline.HasSelection);
            });
    }

    [Fact]
    public void Clicking_c_copy_event_copies_the_selected_event_through_the_terminal_seam()
    {
        Run(
            () => Click("c copy event"),
            () => _posted.PumpUntil(() => _written.Count == 1, "the copy"));

        Assert.Single(_written);
        Assert.Contains("\u001b]52;", _written[0], StringComparison.Ordinal);
    }

    /// <summary>Runs the controller's window at the test size, with the steps after the first draw.</summary>
    private void Run(params Action[] steps) => TerminalHarness.RunWindow(_app, _controller.Window, Width, Height, steps);

    /// <summary>Clicks the label's first character in the hints row, plus <paramref name="offset"/> columns.</summary>
    private void Click(string label, int offset = 0)
    {
        int column = _controller.StatusHints.IndexOf(label, StringComparison.Ordinal) + offset;
        Assert.True(column >= offset, $"'{label}' is not in '{_controller.StatusHints}'");
        ClickColumn(column);
    }

    /// <summary>Clicks a column of the hints row with the real input pipeline.</summary>
    private void ClickColumn(int column) => _app.InjectSequence(InputInjectionExtensions.LeftButtonClick(new(column, Height - 1)));

    /// <summary>A press at <paramref name="fromX"/> on the screen row <paramref name="y"/>, a drag to <paramref name="toX"/> and a release.</summary>
    private void Select(int fromX, int toX, int y)
    {
        _controller.Timeline.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(fromX, y) });
        _controller.Timeline.HandleMouse(new Mouse { Flags = MouseFlags.PositionReport, Position = new(toX, y) });
        _controller.Timeline.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new(toX, y) });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.Dispose();
        _store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _app.Dispose();
        _set.Dispose();
    }
}

/// <summary>
/// The hints of the detail, clicked through the window: a JSON event with a node to fold, opened with Enter.
/// </summary>
[Collection("Terminal GUI")]
public sealed class HintsLineDetailClickTests : IDisposable
{
    private const int Width = 120;
    private const int Height = 10;

    private readonly LogSet _set = new(0);
    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly PostQueue _posted = new();
    private readonly TimelineController _controller;
    private readonly List<string> _written = [];
    private bool _disposed;

    public HintsLineDetailClickTests()
    {
        File.WriteAllText(Path.Combine(_set.Folder, "app.log"), DetailFixtures.JsonEvent + "\n");
        _app = Application.Create();
        _app.Init("ansi");
        _app.Driver!.SetScreenSize(Width, Height);
        _store = new LogStore(_set.Config);
        _controller = new TimelineController(
            _app,
            _store,
            new LogStoreDetailSource(_store),
            _set.Config,
            _posted.Post);
        _controller.WriteRaw = _written.Add;
        _controller.Start();
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count > 0, "the first rows");
    }

    [Fact]
    public void Clicking_Enter_toggle_folds_the_node_under_the_cursor_and_keeps_the_detail_focused()
    {
        int open = 0;
        int folded = 0;
        Run(
            () => _controller.Timeline.HandleKey(Key.Enter),
            () => _posted.PumpUntil(() => _controller.Detail?.Document is not null, "the detail"),
            () => MoveCursorTo("Properties"),
            () => open = _controller.Detail!.Document!.Lines.Count,
            () => Click("Enter toggle"),
            () =>
            {
                folded = _controller.Detail!.Document!.Lines.Count;
                Assert.True(folded < open);
                Assert.True(_controller.Detail!.HasFocus);
            },
            () => Click("Enter toggle"),
            () => Assert.Equal(open, _controller.Detail!.Document!.Lines.Count));
    }

    [Fact]
    public void Clicking_c_copy_line_copies_the_cursor_line()
    {
        Run(
            () => _controller.Timeline.HandleKey(Key.Enter),
            () => _posted.PumpUntil(() => _controller.Detail?.Document is not null, "the detail"),
            () => Click("c copy line"),
            () => _posted.PumpUntil(() => _written.Count == 1, "the copy"));

        Assert.Single(_written);
    }

    [Fact]
    public void Clicking_Esc_back_closes_the_detail_and_the_timeline_takes_the_focus()
    {
        Run(
            () => _controller.Timeline.HandleKey(Key.Enter),
            () => _posted.PumpUntil(() => _controller.Detail?.Document is not null, "the detail"),
            () => Click("←/Esc back"),
            () =>
            {
                Assert.Null(_controller.Detail);
                Assert.True(_controller.Timeline.HasFocus);
            });
    }

    [Fact]
    public void Clicking_f_filter_on_value_on_a_node_shows_that_it_is_not_a_field_and_keeps_the_detail_open()
    {
        Run(
            () => _controller.Timeline.HandleKey(Key.Enter),
            () => _posted.PumpUntil(() => _controller.Detail?.Document is not null, "the detail"),
            () => MoveCursorTo("Properties"),
            () => Click("f filter on value"),
            () =>
            {
                Assert.NotNull(_controller.Detail);
                Assert.Contains(FilterMessages.NotAField, _controller.StatusLeft, StringComparison.Ordinal);
            });
    }

    /// <summary>Moves the detail's cursor down to the line of the node with <paramref name="nodeId"/>.</summary>
    private void MoveCursorTo(string nodeId)
    {
        DetailView detail = _controller.Detail!;
        for (int i = 0; i < 50 && detail.Document!.Lines[detail.CursorLine].NodeId != nodeId; i++)
        {
            detail.HandleKey(Key.CursorDown);
        }

        Assert.Equal(nodeId, detail.Document!.Lines[detail.CursorLine].NodeId);
    }

    /// <summary>Runs the controller's window at the test size, with the steps after the first draw.</summary>
    private void Run(params Action[] steps) => TerminalHarness.RunWindow(_app, _controller.Window, Width, Height, steps);

    /// <summary>Clicks the label's first character in the hints row.</summary>
    private void Click(string label)
    {
        int column = _controller.StatusHints.IndexOf(label, StringComparison.Ordinal);
        Assert.True(column >= 0, $"'{label}' is not in '{_controller.StatusHints}'");
        _app.InjectSequence(InputInjectionExtensions.LeftButtonClick(new(column, Height - 1)));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.Dispose();
        _store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _app.Dispose();
        _set.Dispose();
    }
}

/// <summary>The top bar: the bar colours, and a message in bold.</summary>
public sealed class StatusBarTests
{
    [Fact]
    public void The_text_is_drawn_on_the_bar_colours_and_a_message_is_bold()
    {
        var bar = new StatusBar();

        List<TerminalScreen> screens = TerminalHarness.Run(bar, 40, 1, () => bar.Show("Copied", message: true));

        Assert.Equal(TextStyle.None, screens[0].Cells[0][0].Attribute.Style);
        Assert.Equal(TextStyle.Bold, screens[1].Cells[0][0].Attribute.Style);
        Assert.Equal("Copied", screens[1].Text(0));
        Assert.Equal(BarColors.Background, screens[1].Cells[0][0].Attribute.Background);
        Assert.Equal(BarColors.Text, screens[1].Cells[0][0].Attribute.Foreground);
        Assert.Equal(BarColors.Background, screens[1].Cells[0][39].Attribute.Background);
    }
}
