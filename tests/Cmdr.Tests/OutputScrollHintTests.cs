using System.Reflection;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;

namespace Cmdr.Tests;

/// <summary>
/// The scroll hint: when the viewport's rows move up between draws, the view asks the driver to scroll them on the terminal
/// (<see cref="OutputView.ScrollRowsFor"/> decides, and the draw passes the answer on).
/// </summary>
[Collection("Terminal GUI")]
public sealed class OutputScrollHintTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Same_rows_ask_for_no_scroll()
    {
        (long, int)[] rows = Rows(0, 4);

        Assert.Equal(0, OutputView.ScrollRowsFor(rows, rows, 4));
    }

    [Fact]
    public void Rows_shifted_up_by_three_ask_for_three()
    {
        Assert.Equal(3, OutputView.ScrollRowsFor(Rows(0, 6), Rows(3, 9), 6));
    }

    [Fact]
    public void Rows_shifted_up_by_the_height_or_more_ask_for_no_scroll()
    {
        Assert.Equal(0, OutputView.ScrollRowsFor(Rows(0, 6), Rows(6, 12), 6));
        Assert.Equal(0, OutputView.ScrollRowsFor(Rows(0, 6), Rows(7, 13), 6));
    }

    [Fact]
    public void Rows_moved_down_ask_for_no_scroll()
    {
        Assert.Equal(0, OutputView.ScrollRowsFor(Rows(3, 9), Rows(0, 6), 6));
    }

    [Fact]
    public void Wrapped_lines_shifted_up_by_one_row_ask_for_one()
    {
        (long, int)[] previous = [(5, 0), (5, 1), (6, 0)];
        (long, int)[] current = [(5, 1), (6, 0), (7, 0)];

        Assert.Equal(1, OutputView.ScrollRowsFor(previous, current, 3));
    }

    [Fact]
    public void An_empty_previous_draw_asks_for_no_scroll()
    {
        Assert.Equal(0, OutputView.ScrollRowsFor([], Rows(0, 4), 4));
        Assert.Equal(0, OutputView.ScrollRowsFor(Rows(0, 4), [], 4));
    }

    [Fact]
    public void A_mismatch_in_the_overlap_asks_for_no_scroll()
    {
        // The first row is in the previous draw at 3, but the row after it is not the one that followed it there.
        (long, int)[] current = [(3, 0), (4, 0), (20, 0), (21, 0), (22, 0), (23, 0)];

        Assert.Equal(0, OutputView.ScrollRowsFor(Rows(0, 6), current, 6));
    }

    [Fact]
    public void The_view_asks_the_driver_to_scroll_its_rows_up_when_following_output_moves_them()
    {
        using TempSession temp = TempSession.Sample();
        CommandEntry entry = temp.Session.AllEntries.First();
        var hints = new List<(int Top, int Height, int Lines)[]>();

        IReadOnlyList<string> frames = OutputDrawTests.DrawFrames(
            temp,
            20,
            () => entry.Process.Output.WriteStdOut(string.Concat(Enumerable.Range(0, 10).Select(i => $"L{i}\n"))),
            () => entry.Process.Output.WriteStdOut(string.Concat(Enumerable.Range(10, 3).Select(i => $"L{i}\n"))),
            onView: view => view.DrawComplete += (_, _) => hints.Add(PendingRowScrolls(view)));

        // The viewport is six rows, so the view shows L7 to L12 after the third line arrives: the old rows moved up by three.
        Assert.Equal("L7\nL8\nL9\nL10\nL11\nL12", frames[1]);
        Assert.Empty(hints[0]);
        Assert.Contains((0, 6, 3), hints.SelectMany(h => h));
    }

    [Fact]
    public void A_viewport_that_moves_asks_for_no_scroll_in_the_frame_it_moved_in()
    {
        using TempSession temp = TempSession.Sample();
        CommandEntry entry = temp.Session.AllEntries.First();
        var hints = new List<(int Top, int Height, int Lines)[]>();
        OutputView? shown = null;

        OutputDrawTests.DrawFrames(
            temp,
            20,
            () => entry.Process.Output.WriteStdOut(string.Concat(Enumerable.Range(0, 10).Select(i => $"L{i}\n"))),
            () => entry.Process.Output.WriteStdOut(string.Concat(Enumerable.Range(10, 3).Select(i => $"L{i}\n"))),
            onView: view =>
            {
                shown = view;
                view.DrawComplete += (_, _) => hints.Add(PendingRowScrolls(view));
            },
            between: frame => { if (frame == 1) { shown!.Width = 19; } });

        // The width changed with the new lines, so the rectangle changed too: the rows are drawn without a hint.
        Assert.Empty(hints.Last());
    }

    /// <summary>The scroll requests the driver's output buffer holds now, before the frame is written.</summary>
    private static (int Top, int Height, int Lines)[] PendingRowScrolls(View view)
    {
        IDriver driver = view.App!.Driver!;
        object buffer = driver.GetOutputBuffer();
        var pending = (List<(int Top, int Height, int Lines)>)buffer.GetType().GetField("_pendingRowScrolls", Members)!.GetValue(buffer)!;
        return [.. pending];
    }

    /// <summary>Rows (first, first + 1, ... up to last) of a line each, all at row 0.</summary>
    private static (long, int)[] Rows(long first, long last) =>
        Enumerable.Range(0, (int)(last - first)).Select(i => (first + i, 0)).ToArray();
}
