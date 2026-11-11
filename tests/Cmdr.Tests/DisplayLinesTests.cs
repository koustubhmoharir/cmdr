using Cmdr.Core.Session;
using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class DisplayLinesTests
{
    // Only the row kind matters to DisplayLines, so the entries are left null.
    private static readonly ListRow Header = new GroupRow(null!);
    private static readonly ListRow Command = new CommandRow(null!);

    // Rows: header, command, command, header, command. Lines: 0 header, 1 cmd, 2 detail, 3 cmd, 4 detail, 5 header, 6 cmd, 7 detail.
    private static DisplayLines Sample() => DisplayLines.Build(new[] { Header, Command, Command, Header, Command });

    [Fact]
    public void Build_gives_one_line_to_a_header_and_two_to_a_command()
    {
        DisplayLines lines = Sample();

        Assert.Equal(8, lines.Count);
        Assert.Equal(new DisplayLine(0, false), lines.Lines[0]);
        Assert.Equal(new DisplayLine(1, false), lines.Lines[1]);
        Assert.Equal(new DisplayLine(1, true), lines.Lines[2]);
        Assert.Equal(new DisplayLine(2, false), lines.Lines[3]);
        Assert.Equal(new DisplayLine(2, true), lines.Lines[4]);
        Assert.Equal(new DisplayLine(3, false), lines.Lines[5]);
        Assert.Equal(new DisplayLine(4, false), lines.Lines[6]);
        Assert.Equal(new DisplayLine(4, true), lines.Lines[7]);
    }

    [Fact]
    public void RowToLine_gives_the_main_line_of_each_row()
    {
        DisplayLines lines = Sample();

        Assert.Equal(new[] { 0, 1, 3, 5, 6 }, Enumerable.Range(0, 5).Select(lines.RowToLine));
        Assert.Equal(-1, lines.RowToLine(5));
        Assert.Equal(-1, lines.RowToLine(-1));
    }

    [Fact]
    public void LastLineOfRow_is_the_detail_line_of_a_command_and_the_line_of_a_header()
    {
        DisplayLines lines = Sample();

        Assert.Equal(0, lines.LastLineOfRow(0));
        Assert.Equal(2, lines.LastLineOfRow(1));
        Assert.Equal(4, lines.LastLineOfRow(2));
        Assert.Equal(5, lines.LastLineOfRow(3));
        Assert.Equal(7, lines.LastLineOfRow(4));
        Assert.Equal(-1, lines.LastLineOfRow(9));
    }

    [Fact]
    public void LineToRow_maps_both_lines_of_a_command_to_its_row()
    {
        DisplayLines lines = Sample();

        Assert.Equal(1, lines.LineToRow(1));
        Assert.Equal(1, lines.LineToRow(2));
        Assert.Equal(4, lines.LineToRow(7));
        Assert.Equal(-1, lines.LineToRow(8));
        Assert.Equal(-1, lines.LineToRow(-1));
    }

    [Fact]
    public void IsDetail_is_true_only_for_detail_lines()
    {
        DisplayLines lines = Sample();

        Assert.False(lines.IsDetail(1));
        Assert.True(lines.IsDetail(2));
        Assert.False(lines.IsDetail(5));
        Assert.False(lines.IsDetail(99));
    }

    [Fact]
    public void MainLineOf_moves_a_detail_line_to_its_command_main_line()
    {
        DisplayLines lines = Sample();

        Assert.Equal(1, lines.MainLineOf(2));
        Assert.Equal(0, lines.MainLineOf(0));
        Assert.Equal(6, lines.MainLineOf(7));
    }

    [Fact]
    public void Down_skips_detail_lines()
    {
        DisplayLines lines = Sample();

        Assert.Equal(1, lines.NextFocusableLine(0, 1));
        Assert.Equal(3, lines.NextFocusableLine(1, 1));
        Assert.Equal(5, lines.NextFocusableLine(3, 1));
        Assert.Equal(6, lines.NextFocusableLine(5, 1));
    }

    [Fact]
    public void Up_skips_detail_lines()
    {
        DisplayLines lines = Sample();

        Assert.Equal(5, lines.NextFocusableLine(6, -1));
        Assert.Equal(3, lines.NextFocusableLine(5, -1));
        Assert.Equal(1, lines.NextFocusableLine(3, -1));
        Assert.Equal(0, lines.NextFocusableLine(1, -1));
    }

    [Fact]
    public void Movement_from_a_detail_line_starts_at_its_command()
    {
        DisplayLines lines = Sample();

        Assert.Equal(3, lines.NextFocusableLine(2, 1));
        Assert.Equal(0, lines.NextFocusableLine(2, -1));
    }

    [Fact]
    public void Movement_clamps_at_both_ends()
    {
        DisplayLines lines = Sample();

        Assert.Equal(0, lines.NextFocusableLine(0, -1));
        Assert.Equal(6, lines.NextFocusableLine(6, 1));
        Assert.Equal(6, lines.NextFocusableLine(7, 1));
        Assert.Equal(6, lines.NextFocusableLine(99, 1));
    }

    [Fact]
    public void Without_a_current_line_movement_starts_at_the_first_line()
    {
        DisplayLines lines = Sample();

        Assert.Equal(0, lines.NextFocusableLine(-1, 1));
        Assert.Equal(0, lines.NextFocusableLine(-1, -1));
    }

    [Fact]
    public void First_and_last_focusable_lines_are_main_lines()
    {
        DisplayLines lines = Sample();

        Assert.Equal(0, lines.FirstFocusableLine);
        Assert.Equal(6, lines.LastFocusableLine);
    }

    [Fact]
    public void A_list_of_headers_only_moves_one_line_at_a_time()
    {
        DisplayLines lines = DisplayLines.Build(new[] { Header, Header, Header });

        Assert.Equal(3, lines.Count);
        Assert.Equal(1, lines.NextFocusableLine(0, 1));
        Assert.Equal(2, lines.NextFocusableLine(1, 1));
        Assert.Equal(2, lines.NextFocusableLine(2, 1));
        Assert.Equal(1, lines.NextFocusableLine(2, -1));
        Assert.Equal(2, lines.LastFocusableLine);
    }

    [Fact]
    public void A_single_command_is_two_lines_with_one_focusable_line()
    {
        DisplayLines lines = DisplayLines.Build(new[] { Command });

        Assert.Equal(2, lines.Count);
        Assert.Equal(0, lines.NextFocusableLine(0, 1));
        Assert.Equal(0, lines.LastFocusableLine);
    }

    [Fact]
    public void An_empty_list_has_no_lines_and_no_focusable_line()
    {
        DisplayLines lines = DisplayLines.Build(Array.Empty<ListRow>());

        Assert.Equal(0, lines.Count);
        Assert.Equal(-1, lines.NextFocusableLine(-1, 1));
        Assert.Equal(-1, lines.NextFocusableLine(0, 1));
        Assert.Equal(-1, lines.NextFocusableLine(0, -1));
        Assert.Equal(-1, lines.FirstFocusableLine);
        Assert.Equal(-1, lines.LastFocusableLine);
        Assert.Equal(-1, lines.RowToLine(0));
        Assert.Equal(-1, lines.LineToRow(0));
    }
}
