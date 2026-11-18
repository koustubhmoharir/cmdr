using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tests;

/// <summary>Draws the output view with search highlights and reads the cells back, with their attributes.</summary>
[Collection("Terminal GUI")]
public sealed class OutputSearchDrawTests
{
    /// <summary>One cell of a frame: its grapheme and its attribute as the driver holds it.</summary>
    private readonly record struct Painted(string Grapheme, Attribute? Attribute);

    private static SearchPattern Pattern(string text, bool regex = false)
    {
        SearchPattern? pattern = SearchPattern.Create(new SearchQuery(text, true, false, regex), out string? error);
        Assert.Null(error);
        return Assert.IsType<SearchPattern>(pattern);
    }

    [Fact]
    public void Matched_cells_take_the_match_color_and_the_active_match_the_active_color_after_a_wide_character()
    {
        using TempSession temp = TempSession.Sample();
        SearchPattern pattern = Pattern("ab");

        // "界" is two columns wide and one UTF-16 unit, so the match "ab" starts at offset 2 but in column 3.
        IReadOnlyList<Painted[][]> frames = Frames(
            temp,
            20,
            3,
            "界 ab ab\n",
            _ => { },
            view =>
            {
                view.SetSearch(pattern);
                view.Find(backward: false, inclusive: true);
                view.Find(backward: false, inclusive: false);
            });

        Painted[] plain = frames[0][0];
        Painted[] highlighted = frames[1][0];
        Assert.Equal(string.Concat(plain.Select(c => c.Grapheme)), string.Concat(highlighted.Select(c => c.Grapheme)));

        // Columns 3-4 hold the first "ab" (not active), columns 6-7 the second, which is the active match. The other cells keep theirs.
        for (int column = 0; column < plain.Length; column++)
        {
            Attribute? before = plain[column].Attribute;
            Attribute? expected = before;
            if (column is 3 or 4)
            {
                expected = SearchColors.Match(before!.Value);
            }
            else if (column is 6 or 7)
            {
                expected = SearchColors.Active(before!.Value);
            }

            Assert.Equal(expected, highlighted[column].Attribute);
        }
    }

    [Fact]
    public void Each_row_of_a_wrapped_line_is_highlighted_at_the_columns_of_the_matches_on_it()
    {
        using TempSession temp = TempSession.Sample();

        // Four columns: "abcd", "efgh", "ijkl". "gh" is on row 1 (columns 2-3) and "ij" on row 2 (columns 0-1).
        IReadOnlyList<Painted[][]> frames = Frames(
            temp,
            4,
            4,
            "abcdefghijkl\n",
            _ => { },
            view => view.SetSearch(Pattern("gh|ij", regex: true)));

        var highlighted = new HashSet<(int Row, int Column)> { (1, 2), (1, 3), (2, 0), (2, 1) };
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                Painted before = frames[0][row][column];
                Painted after = frames[1][row][column];
                Assert.Equal(before.Grapheme, after.Grapheme);
                Attribute? expected = highlighted.Contains((row, column))
                    ? SearchColors.Match(before.Attribute!.Value)
                    : before.Attribute;
                Assert.Equal(expected, after.Attribute);
            }
        }
    }

    [Fact]
    public void Once_the_search_ends_no_cell_is_highlighted()
    {
        using TempSession temp = TempSession.Sample();
        SearchPattern pattern = Pattern("ab");

        IReadOnlyList<Painted[][]> frames = Frames(
            temp,
            20,
            3,
            "ab cd ab\n",
            _ => { },
            view =>
            {
                view.SetSearch(pattern);
                view.Find(backward: false, inclusive: true);
            },
            view => view.SetSearch(null));

        // The first match is the active one, so its two cells take the active color while the search is on.
        Painted[] plain = frames[0][0];
        Painted[] lit = frames[1][0];
        Assert.Equal(SearchColors.Active(plain[0].Attribute!.Value), lit[0].Attribute);
        Assert.Equal(SearchColors.Active(plain[1].Attribute!.Value), lit[1].Attribute);

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 20; column++)
            {
                Assert.Equal(frames[0][row][column], frames[2][row][column]);
            }
        }
    }

    /// <summary>
    /// Draws the output of one command in a window of <paramref name="width"/> × <paramref name="height"/>, one frame per step. Each step
    /// runs before its frame. Returns each frame's cells, row by row.
    /// </summary>
    private static IReadOnlyList<Painted[][]> Frames(
        TempSession temp,
        int width,
        int height,
        string stdout,
        params Action<OutputView>[] steps)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);

        var frames = new List<Painted[][]>();
        using var controller = new MainController(app, temp.Session);
        CommandEntry entry = temp.Session.AllEntries.First();
        entry.Process.Output.WriteStdOut(stdout);
        var view = new OutputView(controller) { X = 0, Y = 0, Width = width, Height = height };
        view.Show(entry);

        var window = new Window { X = 0, Y = 0, Width = width, Height = height, BorderStyle = LineStyle.None };
        window.Add(view);
        int frame = 0;
        app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            steps[frame](view);
            app.LayoutAndDraw();
            var driver = app.Driver!;
            var rows = new Painted[height][];
            for (int r = 0; r < height; r++)
            {
                rows[r] = new Painted[width];
                for (int c = 0; c < width; c++)
                {
                    rows[r][c] = new Painted(driver.Contents![r, c].Grapheme, driver.Contents[r, c].Attribute);
                }
            }

            frames.Add(rows);
            frame++;
            if (frame == steps.Length)
            {
                app.RequestStop(window);
                return false;
            }

            return true;
        });
        app.Run(window);
        return frames;
    }
}
