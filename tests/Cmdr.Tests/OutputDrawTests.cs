using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tests;

/// <summary>Draws command output into the Terminal.Gui driver and reads the cells back, with their attributes.</summary>
[Collection("Terminal GUI")]
public sealed class OutputDrawTests
{
    private const int Width = 20;
    private const int Height = 6;

    [Fact]
    public void Each_style_run_of_a_colored_line_is_drawn_in_its_own_colors()
    {
        using TempSession temp = TempSession.Sample();

        // Red "ab", default "cd", bold green "ef", then 256-color 208 "g".
        IReadOnlyList<(string Grapheme, Attribute? Attribute)[]> screen =
            Draw(temp, "\x1b[31mab\x1b[0mcd\x1b[1;32mef\x1b[0;38;5;208mg\x1b[0m\n");

        (string Grapheme, Attribute? Attribute)[] row = screen.Single(cells => string.Concat(cells.Take(7).Select(c => c.Grapheme)) == "abcdefg");
        Color Fg(int column) => row[column].Attribute!.Value.Foreground;

        Assert.Equal(ColorMap.IndexedColor(1), Fg(0));
        Assert.Equal(ColorMap.IndexedColor(1), Fg(1));
        Assert.NotEqual(ColorMap.IndexedColor(1), Fg(2));
        Assert.Equal(Fg(2), Fg(3));
        Assert.Equal(ColorMap.IndexedColor(2), Fg(4));
        Assert.Equal(ColorMap.IndexedColor(2), Fg(5));
        Assert.True(row[4].Attribute!.Value.Style.HasFlag(TextStyle.Bold));
        Assert.False(row[2].Attribute!.Value.Style.HasFlag(TextStyle.Bold));
        Assert.Equal(ColorMap.IndexedColor(208), Fg(6));
    }

    [Fact]
    public void A_partial_line_that_grows_between_frames_is_drawn_with_its_new_text()
    {
        using TempSession temp = TempSession.Sample();
        CommandEntry entry = temp.Session.AllEntries.First();

        IReadOnlyList<string> frames = DrawFrames(
            temp,
            Width,
            () => entry.Process.Output.WriteStdOut("abc"),
            () => entry.Process.Output.WriteStdOut("def"),
            () => entry.Process.Output.WriteStdOut("\x1b[31mg\x1b[0m\n"));

        Assert.Contains("abc", frames[0]);
        Assert.DoesNotContain("abcd", frames[0]);
        Assert.Contains("abcdef", frames[1]);
        Assert.Contains("abcdefg", frames[2]);
    }

    [Fact]
    public void A_line_is_wrapped_again_when_the_width_changes()
    {
        using TempSession temp = TempSession.Sample();
        CommandEntry entry = temp.Session.AllEntries.First();
        OutputView? view = null;

        IReadOnlyList<string> frames = DrawFrames(
            temp,
            Width,
            () => entry.Process.Output.WriteStdOut("0123456789abcdefghij\n"),
            () => { },
            onView: v => view = v,
            between: frame => { if (frame == 1) { view!.Width = 10; } });

        // 20 characters: one row at width 20, two rows of 10 at width 10.
        Assert.Contains("0123456789abcdefghij", frames[0]);
        Assert.Contains("0123456789\nabcdefghij", frames[1]);
    }

    [Fact]
    public void Content_that_shrinks_between_frames_leaves_no_stale_cells()
    {
        using TempSession temp = TempSession.Sample();
        CommandEntry longer = temp.Session.AllEntries.First();
        CommandEntry shorter = temp.Session.AllEntries.Last();
        OutputView? view = null;

        IReadOnlyList<string> frames = DrawFrames(
            temp,
            50,
            () => longer.Process.Output.WriteStdOut(new string('x', 40) + "\nyyyyyyyy\n"),
            () => shorter.Process.Output.WriteStdOut("ab\n"),
            onView: v => view = v,
            between: frame => { if (frame == 1) { view!.Show(shorter); } });

        Assert.Contains(new string('x', 40), frames[0]);
        Assert.Contains("yyyyyyyy", frames[0]);
        // Row 0 is "ab" with nothing after it, and rows 1 to 5 (past the content) are blank.
        Assert.Equal("ab\n\n\n\n\n", frames[1]);
    }

    /// <summary>
    /// Draws one frame per action (each action runs before its frame) and returns each frame's screen as text, rows joined by
    /// newlines with trailing spaces trimmed.
    /// </summary>
    internal static IReadOnlyList<string> DrawFrames(
        TempSession temp,
        int width,
        Action first,
        Action second,
        Action? third = null,
        Action<OutputView>? onView = null,
        Action<int>? between = null)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, Height);

        var frames = new List<string>();
        using var controller = new MainController(app, temp.Session);
        CommandEntry entry = temp.Session.AllEntries.First();
        var view = new OutputView(controller) { X = 0, Y = 0, Width = width, Height = Height };
        view.Show(entry);
        onView?.Invoke(view);
        var window = new Window { X = 0, Y = 0, Width = width, Height = Height, BorderStyle = LineStyle.None };
        window.Add(view);

        Action[] actions = third is null ? new[] { first, second } : new[] { first, second, third };
        int frame = 0;
        app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            between?.Invoke(frame);
            actions[frame]();
            view.Tick();
            app.LayoutAndDraw();
            var driver = app.Driver!;
            var rows = new List<string>(Height);
            for (int r = 0; r < Height; r++)
            {
                var text = new System.Text.StringBuilder();
                for (int c = 0; c < width; c++)
                {
                    text.Append(driver.Contents![r, c].Grapheme);
                }

                rows.Add(text.ToString().TrimEnd());
            }

            frames.Add(string.Join("\n", rows));
            frame++;
            if (frame == actions.Length)
            {
                app.RequestStop(window);
                return false;
            }

            return true;
        });
        app.Run(window);
        return frames;
    }

    private static IReadOnlyList<(string, Attribute?)[]> Draw(TempSession temp, string stdout)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(Width, Height);

        IReadOnlyList<(string, Attribute?)[]> screen = Array.Empty<(string, Attribute?)[]>();
        using var controller = new MainController(app, temp.Session);
        CommandEntry entry = temp.Session.AllEntries.First();
        entry.Process.Output.WriteStdOut(stdout);
        var view = new OutputView(controller) { X = 0, Y = 0, Width = Width, Height = Height };
        view.Show(entry);

        var window = new Window { X = 0, Y = 0, Width = Width, Height = Height, BorderStyle = LineStyle.None };
        window.Add(view);
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            var driver = app.Driver!;
            var rows = new List<(string, Attribute?)[]>(Height);
            for (int r = 0; r < Height; r++)
            {
                var cells = new (string, Attribute?)[Width];
                for (int c = 0; c < Width; c++)
                {
                    cells[c] = (driver.Contents![r, c].Grapheme, driver.Contents[r, c].Attribute);
                }

                rows.Add(cells);
            }

            screen = rows;
            app.RequestStop(window);
            return false;
        });
        app.Run(window);
        return screen;
    }
}
