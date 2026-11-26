using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Point = System.Drawing.Point;

namespace Mdedit.Tests;

/// <summary>
/// The rendered view: the cursor's moves over mapped cells, the scrolling and the wheel, the clicks, the terminal cursor, and the
/// reflow on a width change. Each expected offset is found in the source text, not in the layout, and the screen text is the drawn one.
/// </summary>
[Collection("Terminal GUI")]
public sealed class RenderedViewTests
{
    private const int Width = 40;
    private const int Height = 12;

    // Rows at width 40: 0 Title, 2 and 3 the wrapped intro, 5 and 6 the bullets, 8 the quote, 10 the last paragraph.
    private const string Mixed =
        "# Title\n\nIntro line one, which is long enough to wrap when the width is small.\n\n- first item\n- second item\n\n> quoted\n\nLast paragraph.\n";

    private const string Long = "alpha beta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau upsilon phi chi psi omega ";

    /// <summary>What the tests read after each action: the cursor, the top line and the terminal cursor position.</summary>
    private sealed record Snap(int Offset, int Line, int Top, int TopOffset, Point? Cursor);

    [Fact]
    public void Right_crosses_a_bullet_prefix_to_the_next_line_and_Left_goes_back()
    {
        RenderedView view = View(Mixed);

        Result result = Run(
            view,
            v => v.HandleKey(Key.CursorDown),
            v => v.HandleKey(Key.CursorDown),
            v => v.HandleKey(Key.CursorDown),
            v => v.HandleKey(Key.End),
            v => v.HandleKey(Key.CursorRight),
            v => v.HandleKey(Key.CursorLeft));

        Assert.Equal(Offset(Mixed, "first item"), result.Snaps[2].Offset);
        Assert.Equal(5, result.Snaps[2].Line);
        Assert.Equal(Offset(Mixed, "first item") + 9, result.Snaps[3].Offset);
        Assert.Equal(Offset(Mixed, "second item"), result.Snaps[4].Offset);
        Assert.Equal(6, result.Snaps[4].Line);
        Assert.Equal(Offset(Mixed, "first item") + 9, result.Snaps[5].Offset);
        Assert.Equal(new Point(11, 5), result.Snaps[3].Cursor);
    }

    [Fact]
    public void Up_and_Down_keep_the_goal_column_across_a_shorter_line_and_a_blank_separator()
    {
        RenderedView view = View(Mixed);

        Result result = Run(
            view,
            v => v.HandleMouse(Click(25, 2)),
            v => v.HandleKey(Key.CursorDown),
            v => v.HandleKey(Key.CursorDown),
            v => v.HandleKey(Key.CursorUp));

        Assert.Equal(Offset(Mixed, "Intro") + 25, result.Snaps[0].Offset);
        Assert.Equal(Offset(Mixed, "wrap") + 25, result.Snaps[1].Offset);
        Assert.Equal(3, result.Snaps[1].Line);
        // The line of the bullets is shorter than the goal column, so the cursor goes to its last character.
        Assert.Equal(Offset(Mixed, "first item") + 9, result.Snaps[2].Offset);
        Assert.Equal(5, result.Snaps[2].Line);
        // Up skips the blank line and returns to the wrapped line at the goal column.
        Assert.Equal(Offset(Mixed, "wrap") + 25, result.Snaps[3].Offset);
    }

    [Fact]
    public void Home_and_End_go_to_the_first_and_last_mapped_cell_of_the_display_line()
    {
        RenderedView view = View(Mixed);

        Result result = Run(
            view,
            v => v.HandleMouse(Click(10, 2)),
            v => v.HandleKey(Key.End),
            v => v.HandleKey(Key.Home));

        int intro = Offset(Mixed, "Intro");
        Assert.Equal(intro + 10, result.Snaps[0].Offset);
        // The line ends with the space that wraps the next word: it is a mapped cell, so it is the last one on the line.
        Assert.Equal(intro + "Intro line one, which is long enough to ".Length - 1, result.Snaps[1].Offset);
        Assert.Equal(intro, result.Snaps[2].Offset);
    }

    [Fact]
    public void G_and_the_document_end_keys_go_to_the_first_and_last_mapped_cell()
    {
        RenderedView view = View(Numbered(30));

        Result result = Run(
            view,
            v => v.HandleKey(Key.CursorDown),
            v => v.HandleKey(Key.G.WithShift),
            v => v.HandleKey(Key.Home.WithCtrl),
            v => v.HandleKey(Key.End.WithCtrl),
            v => v.HandleKey(Key.G));

        Assert.Equal(Offset(Numbered(30), "p30") + 2, result.Snaps[1].Offset);
        Assert.Equal(0, result.Snaps[2].Offset);
        Assert.Equal(Offset(Numbered(30), "p30") + 2, result.Snaps[3].Offset);
        Assert.Equal(0, result.Snaps[4].Offset);
    }

    [Fact]
    public void PgDn_moves_the_cursor_and_the_top_by_a_page_and_keeps_the_cursor_row()
    {
        string text = Numbered(30);
        RenderedView view = View(text);

        Result result = Run(view, v => v.HandleKey(Key.PageDown));

        // A page is 12 display lines; p7 is on display line 12 (each paragraph and its blank line take two lines).
        Assert.Equal(Offset(text, "p7\n"), result.Snaps[0].Offset);
        Assert.Equal(12, result.Snaps[0].Line);
        Assert.Equal(12, result.Snaps[0].Top);
        Assert.Equal(new Point(0, 0), result.Snaps[0].Cursor);
    }

    [Fact]
    public void The_cursor_scrolls_into_view_by_the_least_lines()
    {
        RenderedView view = View(Numbered(30));

        Result result = Run(view, Enumerable.Repeat<Action<RenderedView>>(v => v.HandleKey(Key.CursorDown), 6).ToArray());

        // The paragraphs are two display lines apart: the sixth Down reaches line 12, the first line below the view.
        Assert.Equal(0, result.Snaps[4].Top);
        Assert.Equal(10, result.Snaps[4].Line);
        Assert.Equal(1, result.Snaps[5].Top);
        Assert.Equal(new Point(0, 11), result.Snaps[5].Cursor);
    }

    [Fact]
    public void The_wheel_scrolls_three_lines_and_leaves_the_cursor_offset_and_moves_the_terminal_cursor_off_screen()
    {
        RenderedView view = View(Numbered(30));

        Result result = Run(
            view,
            v => v.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown }),
            v => v.HandleMouse(new Mouse { Flags = MouseFlags.WheeledUp }));

        Assert.Equal(0, result.Snaps[0].Offset);
        Assert.Equal(3, result.Snaps[0].Top);
        Assert.Null(result.Snaps[0].Cursor);
        Assert.Equal(0, result.Snaps[1].Offset);
        Assert.Equal(0, result.Snaps[1].Top);
        Assert.Equal(new Point(0, 0), result.Snaps[1].Cursor);
    }

    [Fact]
    public void A_handled_key_or_mouse_event_asks_for_a_redraw_so_that_a_scroll_is_drawn()
    {
        // The harness draws every step in full, so the screens alone would not show a view that forgets to ask for a redraw.
        RenderedView view = View(Numbered(30));
        var needed = new List<bool>();

        Run(
            view,
            v =>
            {
                v.HandleKey(Key.G.WithShift);
                needed.Add(v.NeedsDraw);
            },
            v =>
            {
                v.HandleMouse(new Mouse { Flags = MouseFlags.WheeledUp });
                needed.Add(v.NeedsDraw);
            });

        Assert.Equal(new[] { true, true }, needed);
    }

    [Fact]
    public void A_click_on_a_bullet_lands_on_the_first_character_of_the_item()
    {
        RenderedView view = View(Mixed);

        Result result = Run(view, v => v.HandleMouse(Click(0, 5)));

        Assert.Equal(Offset(Mixed, "first item"), result.Snaps[0].Offset);
        Assert.Equal(5, result.Snaps[0].Line);
        Assert.Equal(new Point(2, 5), result.Snaps[0].Cursor);
    }

    [Fact]
    public void The_terminal_cursor_is_drawn_at_the_cursor_cell()
    {
        RenderedView view = View(Mixed);

        Result result = Run(view, v => v.HandleKey(Key.CursorRight), v => v.HandleKey(Key.CursorRight));

        Assert.Equal(Offset(Mixed, "Title") + 2, result.Snaps[1].Offset);
        // The heading's fold glyph and its space take the first two columns, so the second character of the title is in column 4.
        Assert.Equal(new Point(4, 0), result.Snaps[1].Cursor);
    }

    [Fact]
    public void A_width_change_keeps_the_cursor_offset_and_its_row_on_screen()
    {
        string text = Repeat(Long, 12).TrimEnd();
        RenderedView view = View(text);
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(Width, Height);
        using var window = new Window { BorderStyle = LineStyle.None };
        view.X = 0;
        view.Y = 0;
        view.Width = Dim.Fill();
        view.Height = Dim.Fill();
        window.Add(view);

        Result result = RunResizing(
            app,
            window,
            view,
            v => { for (int i = 0; i < 5; i++) { v.HandleKey(Key.CursorDown); } },
            _ => window.Width = 25);

        Snap middle = result.Snaps[0];
        Snap resized = result.Snaps[1];
        Assert.Equal(5, middle.Line - middle.Top);
        Assert.Equal(middle.Offset, resized.Offset);
        Assert.Equal(5, resized.Cursor!.Value.Y);
        Assert.Equal(resized.Line - 5, resized.Top);
    }

    [Fact]
    public void A_width_change_with_the_cursor_off_screen_keeps_the_top_lines_anchor_on_the_top_row()
    {
        string text = Repeat(Long, 12).TrimEnd();
        RenderedView view = View(text);
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(Width, Height);
        using var window = new Window { BorderStyle = LineStyle.None };
        view.X = 0;
        view.Y = 0;
        view.Width = Dim.Fill();
        view.Height = Dim.Fill();
        window.Add(view);

        Result result = RunResizing(
            app,
            window,
            view,
            v => v.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown }),
            _ => window.Width = 25);

        Snap scrolled = result.Snaps[0];
        Snap resized = result.Snaps[1];
        Assert.Equal(0, scrolled.Offset);
        Assert.Null(resized.Cursor);
        Assert.Equal(0, resized.Offset);

        // The first word of the old top line starts on the new top row.
        int anchor = scrolled.TopOffset;
        string word = text[anchor..].Split(' ')[0];
        Assert.Contains(word, result.Screens[^1].Text(0), StringComparison.Ordinal);
        Assert.True(resized.TopOffset <= anchor);
    }

    // ----- Helpers -----

    private static RenderedView View(string markdown) => new(RenderDocument.Parse(markdown));

    private static int Offset(string text, string find)
    {
        int index = text.IndexOf(find, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new ArgumentException($"'{find}' is not in the text", nameof(find));
        }

        return index;
    }

    private static string Repeat(string text, int count) => string.Concat(Enumerable.Repeat(text, count));

    private static string Numbered(int count) => string.Concat(Enumerable.Range(1, count).Select(i => $"p{i}\n\n"));

    private static Mouse Click(int x, int y) => new() { Flags = MouseFlags.LeftButtonPressed, Position = new Point(x, y) };

    /// <summary>The outcome of a run: the screens (the first, then one after each action and its snapshot) and the snapshots.</summary>
    private sealed record Result(List<TerminalScreen> Screens, List<Snap> Snaps);

    // Runs the actions in a harness of the view's size. A snapshot is taken after each action's draw, so each action has one.
    private static Result Run(RenderedView view, params Action<RenderedView>[] actions)
    {
        var snaps = new List<Snap>();
        var steps = new List<Action>();
        foreach (Action<RenderedView> action in actions)
        {
            steps.Add(() => action(view));
            steps.Add(() => snaps.Add(Capture(view)));
        }

        List<TerminalScreen> screens = TerminalHarness.Run(view, Width, Height, steps.ToArray());
        return new Result(screens, snaps);
    }

    // Runs the steps in a window that the test can resize. The window's first step is an action on the view, and the resize is a step too.
    private static Result RunResizing(IApplication app, Window window, RenderedView view, Action<RenderedView> first, Action<Window> resize)
    {
        var snaps = new List<Snap>();
        List<TerminalScreen> screens = TerminalHarness.RunWindow(
            app,
            window,
            Width,
            Height,
            () => first(view),
            () => snaps.Add(Capture(view)),
            () => resize(window),
            () => snaps.Add(Capture(view)));
        return new Result(screens, snaps);
    }

    private static Snap Capture(RenderedView view) => new(view.CursorOffset, view.CursorLine, view.TopLine, view.TopOffset, view.Cursor.Position);
}
