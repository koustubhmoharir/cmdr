using Cmdr.Core.Output;
using Cmdr.Tui;
using Terminal.Gui.App;

namespace Cmdr.Tests;

/// <summary>
/// A selection or the paused cursor on a partial line keeps covering that line when another stream or a system line completes
/// a line in front of it, which moves the partial to a later number.
/// </summary>
[Collection("Terminal GUI")]
public sealed class OutputViewPartialSelectionTests
{
    /// <summary>A view with no completed lines and room for the partial lines of both streams.</summary>
    private static OutputViewHarness Open(IApplication app)
    {
        OutputViewHarness h = OutputViewHarness.Create(app);
        h.Resize(40, 6);
        return h;
    }

    /// <summary>Selects columns 0 to 2 of the row at the top of the view.</summary>
    private static void SelectFirstThreeColumns(OutputViewHarness h)
    {
        h.Press(0, 0);
        h.Drag(2, 0);
        h.Release(2, 0);
    }

    [Fact]
    public void Completing_stderr_in_front_of_a_selected_stdout_partial_keeps_the_selection_on_it()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteStdOut("OUT-PARTIAL");
        h.Output.WriteStdErr("ERR-PARTIAL");
        SelectFirstThreeColumns(h);
        Assert.Equal("OUT", h.SelectedText());

        h.Output.WriteStdErr("\n");

        Assert.Equal("OUT", h.SelectedText());
        Assert.Equal(new SelectionPoint(1, 0), h.Selection!.Value.Anchor);
        Assert.Equal(new SelectionPoint(1, 2), h.Selection!.Value.Active);
    }

    [Fact]
    public void Completing_stdout_first_keeps_the_selection_on_the_now_completed_line()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteStdOut("OUT-PARTIAL");
        h.Output.WriteStdErr("ERR-PARTIAL");
        SelectFirstThreeColumns(h);

        h.Output.WriteStdOut("\n");

        Assert.Equal("OUT", h.SelectedText());
        Assert.Equal(new SelectionPoint(0, 0), h.Selection!.Value.Anchor);
        Assert.Equal(new SelectionPoint(0, 2), h.Selection!.Value.Active);
    }

    [Fact]
    public void Stdout_partial_appearing_in_front_pushes_a_selected_stderr_partial_down()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteStdErr("ERR-PARTIAL");
        SelectFirstThreeColumns(h);
        Assert.Equal("ERR", h.SelectedText());

        h.Output.WriteStdOut("OUT");

        Assert.Equal("ERR", h.SelectedText());
        Assert.Equal(new SelectionPoint(1, 0), h.Selection!.Value.Anchor);
        Assert.Equal(new SelectionPoint(1, 2), h.Selection!.Value.Active);
    }

    [Fact]
    public void System_line_appended_while_selecting_a_partial_moves_the_selection_with_it()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteStdOut("OUT-PARTIAL");
        SelectFirstThreeColumns(h);

        h.Output.WriteSystem("system line");
        h.Tick();

        Assert.Equal("OUT", h.SelectedText());
        Assert.Equal(new SelectionPoint(1, 0), h.Selection!.Value.Anchor);
    }

    [Fact]
    public void Growing_the_selected_partial_keeps_the_selection()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteStdOut("OUT");
        h.Press(0, 0);
        h.Drag(1, 0);
        h.Release(1, 0);

        h.Output.WriteStdErr("ERR-PARTIAL");
        h.Output.WriteStdOut("-PARTIAL");
        h.Tick();

        Assert.Equal("OU", h.SelectedText());
        Assert.Equal(new SelectionPoint(0, 0), h.Selection!.Value.Anchor);
    }

    [Fact]
    public void Carriage_return_overwrite_of_the_selected_partial_clears_the_selection()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteStdOut("OUT-PARTIAL");
        SelectFirstThreeColumns(h);

        h.Output.WriteStdOut("\rnew");
        h.Tick();

        Assert.Null(h.Selection);
    }

    [Fact]
    public void Selection_on_completed_lines_is_unaffected_by_later_completions()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteSystem("sys1");
        h.Output.WriteStdOut("done\nOUT-PARTIAL");
        h.Press(0, 0);
        h.Drag(3, 1);
        h.Release(3, 1);
        Assert.Equal("sys1\ndone", h.SelectedText());

        h.Output.WriteStdErr("ERR\n");
        h.Output.WriteSystem("later");
        h.Tick();

        Assert.Equal("sys1\ndone", h.SelectedText());
        Assert.Equal(new SelectionPoint(0, 0), h.Selection!.Value.Anchor);
        Assert.Equal(new SelectionPoint(1, 3), h.Selection!.Value.Active);
    }

    [Fact]
    public void Paused_cursor_on_a_partial_line_follows_the_line()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteStdOut("OUT-PARTIAL");
        h.Output.WriteStdErr("ERR-PARTIAL");
        h.Home();
        Assert.Equal(0, h.CursorLine);

        h.Output.WriteStdErr("\n");

        Assert.Equal(1, h.CursorLine);
    }

    [Fact]
    public void Rewriting_a_selected_partial_does_not_move_the_selection_to_a_later_line_with_the_same_text()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteStdOut("OUT");
        SelectFirstThreeColumns(h);
        Assert.Equal("OUT", h.SelectedText());

        h.Output.WriteStdOut("\rOTHER\nOUT-UNRELATED\n");

        Assert.Null(h.Selection);
    }

    [Fact]
    public void Completing_stderr_under_a_selected_stderr_partial_moves_the_selection_and_the_cursor_back_with_it()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteStdOut("OUT-PARTIAL");
        h.Output.WriteStdErr("ERR-PARTIAL");
        h.Press(0, 1);
        h.Drag(2, 1);
        h.Release(2, 1);
        Assert.Equal("ERR", h.SelectedText());
        Assert.Equal(1, h.CursorLine);

        h.Output.WriteStdErr("\n");

        Assert.Equal("ERR", h.SelectedText());
        Assert.Equal(new SelectionPoint(0, 0), h.Selection!.Value.Anchor);
        Assert.Equal(new SelectionPoint(0, 2), h.Selection!.Value.Active);
        Assert.Equal(0, h.CursorLine);
    }

    [Fact]
    public void Copy_takes_the_selected_stream_from_the_snapshot_it_was_taken_from()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteStdOut("OUT-PARTIAL");
        h.Output.WriteStdErr("ERR-PARTIAL");
        h.Press(0, 1);
        h.Drag(2, 1);
        h.Release(2, 1);

        BufferSnapshot snapshot = h.View.CaptureForCopy()!;
        h.Output.WriteStdErr("\n");
        h.Output.WriteStdOut("\n");

        // Live, line 1 is now the completed stdout line. The copy works from the snapshot, where line 1 is still the stderr partial.
        Assert.Equal("ERR", h.View.CopyTextFrom(snapshot));
    }

    [Fact]
    public void Copy_revalidates_its_snapshot_before_extracting_the_selection()
    {
        using var app = Application.Create();
        app.Init("ansi");
        using OutputViewHarness h = Open(app);
        h.Output.WriteStdOut("OUT-PARTIAL");
        h.Output.WriteStdErr("ERR-PARTIAL");
        h.Press(0, 1);
        h.Drag(2, 1);
        h.Release(2, 1);

        h.Output.WriteStdErr("\n");
        BufferSnapshot snapshot = h.View.CaptureForCopy()!;
        h.Output.WriteStdOut("later\n");

        Assert.Equal("ERR", h.View.CopyTextFrom(snapshot));
    }
}
