using System.Text;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>Draws the details strip into the Terminal.Gui driver and reads the cells back.</summary>
[Collection("Terminal GUI")]
public sealed class DetailsDrawTests
{
    private const int Width = 12;

    [Fact]
    public void Standalone_zero_width_character_is_not_drawn_so_the_row_keeps_its_columns()
    {
        using TempSession temp = DescribedGroup("a​bcdefghijkl");

        IReadOnlyList<string[]> screen = Draw(temp, Width, 40);

        // Twelve characters in twelve cells: nothing is pushed out of the row, so the last one is drawn.
        Assert.Contains(screen, cells => cells.SequenceEqual(new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l" }));
    }

    [Fact]
    public void Combining_sequence_is_drawn_in_one_cell()
    {
        using TempSession temp = DescribedGroup("éx");

        IReadOnlyList<string[]> screen = Draw(temp, Width, 40);

        string[] row = screen.Single(cells => cells[1] == "x");
        Assert.Equal("é", row[0].Normalize(NormalizationForm.FormC));
    }

    [Fact]
    public void Copying_a_selection_across_the_zero_width_character_keeps_it()
    {
        const string text = "a​bcdefghijkl";
        IReadOnlyList<DetailsLine> lines = new[] { new DetailsLine(new[] { new DetailsSpan(text, DetailsStyle.Normal) }) };

        string copied = DetailsSelection.Extract(
            DetailsSelection.Texts(lines),
            LogicalRange.Between(new LogicalPoint(0, 0), new LogicalPoint(0, text.Length)));

        Assert.Equal(text, copied);
    }

    private static TempSession DescribedGroup(string description) =>
        TempSession.Open(new GroupSpec { Name = "g", Description = description });

    /// <summary>
    /// Shows the group's details in a view of <paramref name="width"/> columns and returns the screen as drawn: for each row, the
    /// grapheme of each cell. A row holds <paramref name="width"/> cells.
    /// </summary>
    private static IReadOnlyList<string[]> Draw(TempSession temp, int width, int height)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);

        IReadOnlyList<string[]> screen = Array.Empty<string[]>();
        using var controller = new MainController(app, temp.Session);
        var view = new DetailsView(controller) { X = 0, Y = 0, Width = width, Height = height };
        view.Show(temp.Session.BuildRows().OfType<GroupRow>().Single(), Path.GetFullPath(temp.Session.ConfigDir));

        // No border: the view's cells are the screen's cells, so the rows can be read from column 0.
        var window = new Window { X = 0, Y = 0, Width = width, Height = height, BorderStyle = LineStyle.None };
        window.Add(view);
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            app.LayoutAndDraw();
            screen = ReadCells(app, width, height);
            app.RequestStop(window);
            return false;
        });
        app.Run(window);
        return screen;
    }

    private static IReadOnlyList<string[]> ReadCells(IApplication app, int width, int height)
    {
        var rows = new List<string[]>(height);
        for (int row = 0; row < height; row++)
        {
            var cells = new string[width];
            for (int col = 0; col < width; col++)
            {
                cells[col] = app.Driver!.Contents[row, col].Grapheme;
            }

            rows.Add(cells);
        }

        return rows;
    }
}
