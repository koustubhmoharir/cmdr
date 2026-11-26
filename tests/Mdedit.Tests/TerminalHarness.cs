using System.Runtime.ExceptionServices;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Mdedit.Tests;

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
        content.X = 0;
        content.Y = 0;
        content.Width = Dim.Fill();
        content.Height = Dim.Fill();
        var window = new Window { X = 0, Y = 0, Width = width, Height = height, BorderStyle = LineStyle.None };
        window.Add(content);

        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, height);
        return Drive(app, window, content, width, height, steps);
    }

    /// <summary>
    /// Runs a main window at a size, as the application does, and captures the screens like <see cref="Run"/>. The application must be
    /// initialised with the ANSI driver at the same size, and it is not disposed here: a controller built on it is run in it.
    /// </summary>
    /// <param name="app">The initialised application the window runs in.</param>
    /// <param name="window">The main window. It is sized to the screen.</param>
    /// <param name="width">The screen width in columns.</param>
    /// <param name="height">The screen height in rows.</param>
    /// <param name="steps">The steps, run in order.</param>
    public static List<TerminalScreen> RunWindow(IApplication app, Window window, int width, int height, params Action[] steps)
    {
        window.X = 0;
        window.Y = 0;
        window.Width = width;
        window.Height = height;
        return Drive(app, window, window, width, height, steps);
    }

    private static List<TerminalScreen> Drive(IApplication app, Window window, View captured, int width, int height, Action[] steps)
    {
        var screens = new List<TerminalScreen>();
        Exception? failure = null;
        app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
        {
            try
            {
                app.LayoutAndDraw();
                screens.Add(Capture(app, captured, width, height));
                foreach (Action step in steps)
                {
                    step();
                    app.LayoutAndDraw();
                    screens.Add(Capture(app, captured, width, height));
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

    /// <summary>
    /// Lays out and draws the application now, and captures the screen. For a check that runs while a dialog is open: a dialog's modal loop
    /// runs the timers, so a watch can read the screen there, where the steps cannot.
    /// </summary>
    /// <param name="app">The running application.</param>
    /// <param name="captured">The view whose attributes the screen reports (the focus and normal roles).</param>
    /// <param name="width">The screen width in columns.</param>
    /// <param name="height">The screen height in rows.</param>
    public static TerminalScreen Snapshot(IApplication app, View captured, int width, int height)
    {
        app.LayoutAndDraw();
        return Capture(app, captured, width, height);
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
