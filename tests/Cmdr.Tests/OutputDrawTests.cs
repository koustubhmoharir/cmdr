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
