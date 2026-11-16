using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tests;

/// <summary>Draws the output header line into the Terminal.Gui driver and reads the row back, with its attributes.</summary>
[Collection("Terminal GUI")]
public sealed class OutputHeaderDrawTests
{
    private const string Following = "Output — following —";

    [Fact]
    public void Row_is_the_lead_rule_then_the_title_then_a_rule_to_the_end()
    {
        IReadOnlyList<(string Grapheme, Attribute? Attribute)[]> frames = Frames(40, Following, false);

        Assert.Equal("── Output — following — " + new string('─', 16), Text(frames[0]));
    }

    [Fact]
    public void Long_title_is_truncated_to_the_width_with_no_rule_left()
    {
        const string paused = "Output — paused (End to follow) —";

        IReadOnlyList<(string Grapheme, Attribute? Attribute)[]> frames = Frames(16, paused, false);

        // Four columns go to "── " and the space after the title, so the title gets twelve: eleven characters and an ellipsis.
        Assert.Equal("── Output — pa… ", Text(frames[0]));
    }

    [Fact]
    public void Focusing_the_output_changes_the_attribute_of_the_title_cells_and_not_the_rule()
    {
        IReadOnlyList<(string Grapheme, Attribute? Attribute)[]> frames = Frames(40, Following, false, true);

        (string Grapheme, Attribute? Attribute)[] unfocused = frames[0];
        (string Grapheme, Attribute? Attribute)[] focused = frames[1];

        Assert.NotEqual(unfocused[3].Attribute, focused[3].Attribute);
        Assert.Equal(unfocused[0].Attribute, focused[0].Attribute);
        Assert.Equal(unfocused[30].Attribute, focused[30].Attribute);
    }

    [Fact]
    public void Setting_the_same_title_or_focus_does_not_ask_for_a_draw()
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(40, 3);
        var header = new OutputHeader(() => { }) { HeaderTitle = Following, X = 0, Y = 0, Width = 40, Height = 1 };
        var window = new Window { X = 0, Y = 0, Width = 40, Height = 1, BorderStyle = LineStyle.None };
        window.Add(header);

        bool? afterSame = null;
        bool? afterFocus = null;
        bool? afterChange = null;
        app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            app.LayoutAndDraw();
            header.HeaderTitle = Following;
            header.PaneFocused = false;
            afterSame = header.NeedsDraw;
            header.PaneFocused = true;
            afterFocus = header.NeedsDraw;
            app.LayoutAndDraw();
            header.HeaderTitle = "Output — paused";
            afterChange = header.NeedsDraw;
            app.RequestStop(window);
            return false;
        });
        app.Run(window);

        Assert.False(afterSame);
        Assert.True(afterFocus);
        Assert.True(afterChange);
    }

    /// <summary>
    /// Draws the header once per focus state, in order, and returns the first row of each frame: its graphemes and their
    /// attributes. The title is set before the first frame.
    /// </summary>
    private static IReadOnlyList<(string Grapheme, Attribute? Attribute)[]> Frames(int width, string title, params bool[] focusStates)
    {
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(width, 3);

        var frames = new List<(string Grapheme, Attribute? Attribute)[]>();
        var header = new OutputHeader(() => { }) { HeaderTitle = title, X = 0, Y = 0, Width = width, Height = 1 };
        var window = new Window { X = 0, Y = 0, Width = width, Height = 1, BorderStyle = LineStyle.None };
        window.Add(header);

        app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
        {
            foreach (bool focused in focusStates)
            {
                header.PaneFocused = focused;
                app.LayoutAndDraw();
                var cells = new (string Grapheme, Attribute? Attribute)[width];
                for (int c = 0; c < width; c++)
                {
                    cells[c] = (app.Driver!.Contents![0, c].Grapheme, app.Driver.Contents[0, c].Attribute);
                }

                frames.Add(cells);
            }

            app.RequestStop(window);
            return false;
        });
        app.Run(window);
        return frames;
    }

    private static string Text((string Grapheme, Attribute? Attribute)[] row) => string.Concat(row.Select(c => c.Grapheme));
}
