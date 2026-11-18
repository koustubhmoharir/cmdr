using System.Runtime.ExceptionServices;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tests;

/// <summary>
/// Runs a view in a Terminal.Gui application with the ANSI driver, and reads the drawn cells back after each step. The steps run on the
/// application's thread, one after another, each followed by a layout and draw.
/// </summary>
internal static class TerminalHarness
{
    /// <summary>
    /// Lays out and draws the view at a size, then runs each step and draws again. Returns one screen after the first draw and one after
    /// each step, in order. An exception in a step is rethrown after the application has stopped.
    /// </summary>
    /// <param name="content">The view to fill the window. It is laid out to the window's size.</param>
    /// <param name="width">The screen width in columns.</param>
    /// <param name="height">The screen height in rows.</param>
    /// <param name="steps">The steps, run in order.</param>
    public static List<TerminalScreen> Run(View content, int width, int height, params Action[] steps)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);

        content.X = 0;
        content.Y = 0;
        content.Width = Dim.Fill();
        content.Height = Dim.Fill();
        var window = new Window { X = 0, Y = 0, Width = width, Height = height, BorderStyle = LineStyle.None };
        window.Add(content);

        var screens = new List<TerminalScreen>();
        Exception? failure = null;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            try
            {
                app.LayoutAndDraw();
                screens.Add(Capture(app, content, width, height));
                foreach (Action step in steps)
                {
                    step();
                    app.LayoutAndDraw();
                    screens.Add(Capture(app, content, width, height));
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                app.RequestStop(window);
            }

            return false;
        });
        app.Run(window);

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return screens;
    }

    private static TerminalScreen Capture(IApplication app, View content, int width, int height)
    {
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

        return new TerminalScreen(cells, content.GetAttributeForRole(VisualRole.Focus), content.GetAttributeForRole(VisualRole.Normal));
    }
}

/// <summary>The drawn cells of a screen, with the attributes of the focus and normal roles of the view that was drawn.</summary>
/// <param name="Cells">The cells, row by row: each cell's grapheme and attribute.</param>
/// <param name="Focus">The focus attribute of the view.</param>
/// <param name="Normal">The normal attribute of the view.</param>
internal sealed record TerminalScreen(IReadOnlyList<(string Grapheme, Attribute Attribute)[]> Cells, Attribute Focus, Attribute Normal)
{
    /// <summary>The text of a row, with trailing spaces trimmed. Columns of the text are the cell indices, for single-column text.</summary>
    public string Text(int row) => string.Concat(Cells[row].Select(cell => cell.Grapheme)).TrimEnd();

    /// <summary>The index of the first row whose text contains <paramref name="text"/>. Fails the test when there is none.</summary>
    public int RowOf(string text)
    {
        for (int row = 0; row < Cells.Count; row++)
        {
            if (Text(row).Contains(text, StringComparison.Ordinal))
            {
                return row;
            }
        }

        Assert.Fail($"No row contains '{text}'. Rows:\n{string.Join("\n", Enumerable.Range(0, Cells.Count).Select(Text))}");
        return -1;
    }

    /// <summary>The attribute of the first cell of <paramref name="text"/> in the first row that contains it.</summary>
    public Attribute At(string text)
    {
        int row = RowOf(text);
        int column = Text(row).IndexOf(text, StringComparison.Ordinal);
        return Cells[row][column].Attribute;
    }
}
