using Logvu.Core.Config;
using Logvu.Core.Store;
using Logvu.Core.Detail;
using Logvu.Tui;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tests;

/// <summary>
/// The detail's cursor glyph and gutter, its line and character selection with the mouse and the keys, the copy of the selection or the
/// cursor line, and the seed that a selection gives a search.
/// </summary>
[Collection("Terminal GUI")]
public sealed class DetailSelectionTests
{
    private const int Width = 100;
    private const int Height = 30;

    [Fact]
    public void The_cursor_glyph_is_drawn_in_the_gutter_of_the_cursor_lines_first_row_and_the_text_starts_beside_it()
    {
        DetailView view = Loaded(DetailFixtures.Text(DetailFixtures.StackEvent), DetailFixtures.Config());

        List<TerminalScreen> screens = Draw(
            view,
            v => MoveTo(v, line => line.Text.StartsWith("message:", StringComparison.Ordinal)));

        // Screen 0 is the first draw, with the cursor on the first line; screen 1 is after the move to the message line.
        Assert.StartsWith(">level: ERROR", screens[0].Text(0), StringComparison.Ordinal);
        Assert.StartsWith(" time: 2026-11-18", screens[0].Text(1), StringComparison.Ordinal);
        Assert.StartsWith(" level: ERROR", screens[1].Text(0), StringComparison.Ordinal);
        Assert.StartsWith(">message: Run failed", screens[1].Text(4), StringComparison.Ordinal);
        Assert.All(
            Enumerable.Range(0, Height).Where(row => row != 4),
            row => Assert.NotEqual(">", screens[1].Cells[row][0].Grapheme));
    }

    [Fact]
    public void A_wrapped_line_wraps_to_the_width_less_the_gutter_and_only_its_first_row_has_the_glyph()
    {
        string message = string.Join(' ', Enumerable.Repeat("alpha", 12));
        var detail = DetailFixtures.Text("INFO 2026-11-18 10:20:31,007 [Worker 3] SheetKraft.Engine.Runner - " + message);
        DetailView view = Loaded(detail, DetailFixtures.Config());
        int line = -1;

        List<TerminalScreen> screens = DrawAt(
            view,
            40,
            v =>
            {
                line = v.Document!.Lines.ToList().FindIndex(l => l.Text.StartsWith("message:", StringComparison.Ordinal));
                MoveTo(v, l => l.Text.StartsWith("message:", StringComparison.Ordinal));
            });

        TerminalScreen screen = screens[1];
        int first = Enumerable.Range(0, Height).First(row => screen.Text(row).StartsWith(">", StringComparison.Ordinal));
        DetailLine text = view.Document!.Lines[line];
        DetailWrap wrap = new(text, 39);
        Assert.True(wrap.RowCount >= 2, "expected the message to wrap");
        for (int k = 0; k < wrap.RowCount; k++)
        {
            WrapRow row = wrap.Rows(k, 1)[0];
            string expected = (k == 0 ? ">" : " ")
                + new string(' ', row.Indent)
                + text.Text.Substring(row.Start, row.Length);
            Assert.Equal(expected.TrimEnd(), screen.Text(first + k));
        }

        Assert.True(screen.Cells[first][0].Attribute.Style.HasFlag(TextStyle.Bold));
    }

    [Fact]
    public void The_cursor_line_keeps_its_span_colours_and_has_no_background_of_its_own()
    {
        DetailView view = Loaded(DetailFixtures.Text(DetailFixtures.StackEvent), DetailFixtures.Config());

        TerminalScreen screen = Draw(view)[0];

        Attribute field = screen.At("level");
        Assert.True(field.Style.HasFlag(TextStyle.Bold));
        Assert.Equal(screen.Normal.Background, field.Background);
        Assert.Equal(screen.Normal.Background, screen.At("ERROR").Background);
    }

    [Fact]
    public void Shift_Down_selects_whole_lines_and_c_copies_them_with_the_shared_indentation_removed()
    {
        DetailView view = Loaded(DetailFixtures.Json(DetailFixtures.JsonEvent), DetailFixtures.Config());
        string? copied = null;
        view.CopyRequested += text => copied = text;

        List<TerminalScreen> screens = Draw(
            view,
            v => MoveTo(v, line => line.Text.Contains("Scope", StringComparison.Ordinal)),
            v => Assert.True(v.HandleKey(Key.CursorDown.WithShift)),
            v =>
            {
                Assert.True(v.HasSelection);
                Assert.True(v.HandleKey(Key.C));
            });

        // Screen 2 is after the Shift+Down: the Scope and Customer lines are selected, and their first letters are in reverse video.
        Assert.Equal("S", screens[2].Cells[4][5].Grapheme);
        Assert.True(screens[2].Cells[4][5].Attribute.Style.HasFlag(TextStyle.Reverse));
        Assert.True(screens[2].Cells[5][1].Attribute.Style.HasFlag(TextStyle.Reverse));
        Assert.False(screens[2].Cells[3][1].Attribute.Style.HasFlag(TextStyle.Reverse));
        Assert.Equal("  Scope: \"orders\"\n▾ Customer", copied);
    }

    [Fact]
    public void Shift_Page_Down_selects_to_the_last_line_and_a_plain_move_keeps_the_selection()
    {
        DetailView view = Loaded(DetailFixtures.Json(DetailFixtures.JsonEvent), DetailFixtures.Config());
        string? copied = null;
        view.CopyRequested += text => copied = text;

        Draw(
            view,
            v => MoveTo(v, line => line.Text.Contains("Scope", StringComparison.Ordinal)),
            v => Assert.True(v.HandleKey(Key.PageDown.WithShift)),
            v => Assert.True(v.HandleKey(Key.CursorUp)),
            v =>
            {
                Assert.True(v.HasSelection);
                Assert.True(v.HandleKey(Key.C));
            });

        Assert.Equal("  Scope: \"orders\"\n▾ Customer\n    Id: 7", copied);
    }

    [Fact]
    public void C_without_a_selection_copies_the_cursor_line_with_its_indentation_removed_and_its_marker_kept()
    {
        DetailView view = Loaded(DetailFixtures.Json(DetailFixtures.JsonEvent), DetailFixtures.Config());
        string? copied = null;
        view.CopyRequested += text => copied = text;

        Draw(
            view,
            v => MoveTo(v, line => line.Text.Contains("Customer", StringComparison.Ordinal)),
            v => Assert.True(v.HandleKey(Key.C)));

        Assert.Equal("▾ Customer", copied);
    }

    [Fact]
    public void A_drag_selects_characters_across_a_wrapped_row_and_c_copies_them()
    {
        string message = string.Join(' ', Enumerable.Repeat("alpha", 12));
        DetailView view = Loaded(DetailFixtures.Text("INFO 2026-11-18 10:20:31,007 [Worker 3] SheetKraft.Engine.Runner - " + message), DetailFixtures.Config());
        string? copied = null;
        view.CopyRequested += text => copied = text;
        string lineText = string.Empty;
        int second = -1;
        int first = -1;

        List<TerminalScreen> screens = DrawAt(
            view,
            30,
            v =>
            {
                int line = v.Document!.Lines.ToList().FindIndex(l => l.Text.StartsWith("message:", StringComparison.Ordinal));
                lineText = v.Document.Lines[line].Text;
                first = RowOf(v, line);

                // The continuation row starts at column 1 + 9, the message's indent. The first row's text starts at column 1.
                DetailWrap wrap = new(v.Document.Lines[line], 29);
                Assert.True(wrap.RowCount >= 2, "expected the message to wrap");
                second = wrap.Rows(1, 1)[0].Start;

                Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(1 + 5, first) }));
                Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.PositionReport, Position = new(1 + 9 + 4, first + 1) }));
                Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new(1 + 9 + 4, first + 1) }));
                Assert.True(v.HandleKey(Key.C));
            });

        // From the sixth character of the first row to the fifth of the continuation row.
        Assert.Equal(lineText.Substring(5, second + 4 - 5), copied);
        Assert.True(screens[1].Cells[first][1 + 5].Attribute.Style.HasFlag(TextStyle.Reverse));
        Assert.False(screens[1].Cells[first][1 + 4].Attribute.Style.HasFlag(TextStyle.Reverse));
        Assert.True(screens[1].Cells[first + 1][1 + 9].Attribute.Style.HasFlag(TextStyle.Reverse));
        Assert.False(screens[1].Cells[first + 1][1 + 9 + 5].Attribute.Style.HasFlag(TextStyle.Reverse));
    }

    [Fact]
    public void A_click_without_a_drag_moves_the_cursor_and_leaves_no_selection()
    {
        DetailView view = Loaded(DetailFixtures.Text(DetailFixtures.StackEvent), DetailFixtures.Config());

        Draw(
            view,
            v =>
            {
                Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(5, 5) }));
                Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new(5, 5) }));
                Assert.False(v.HasSelection);
                Assert.Equal(5, v.CursorLine);
            });
    }

    [Fact]
    public void A_drag_below_the_view_scrolls_one_row_per_event()
    {
        DetailView view = Loaded(DetailFixtures.Text(MessageLines(40)), DetailFixtures.Config());

        List<TerminalScreen> screens = Draw(
            view,
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(5, 3) })),
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.PositionReport, Position = new(5, Height) })),
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new(5, Height) })));

        // The press does not scroll; the drag below the view scrolls by one row; the release does not scroll.
        Assert.Equal(Body(screens[0], 0), Body(screens[1], 0));
        Assert.Equal(Body(screens[1], 1), Body(screens[2], 0));
        Assert.Equal(Body(screens[2], 0), Body(screens[3], 0));
        Assert.True(view.HasSelection);
    }

    [Fact]
    public void Esc_clears_a_selection_and_a_second_Esc_closes_the_detail()
    {
        DetailView view = Loaded(DetailFixtures.Json(DetailFixtures.JsonEvent), DetailFixtures.Config());
        var closes = new List<string>();
        view.CloseRequested += () => closes.Add("close");

        Draw(
            view,
            v => MoveTo(v, line => line.Text.Contains("Scope", StringComparison.Ordinal)),
            v => Press(v, Key.CursorDown.WithShift),
            v =>
            {
                Assert.True(v.HandleKey(Key.Esc));
                Assert.False(v.HasSelection);
                Assert.Empty(closes);
            },
            v =>
            {
                Assert.True(v.HandleKey(Key.Esc));
                Assert.Equal(new[] { "close" }, closes);
            });
    }

    [Fact]
    public void A_plain_cursor_move_keeps_the_selection()
    {
        DetailView view = Loaded(DetailFixtures.Json(DetailFixtures.JsonEvent), DetailFixtures.Config());

        Draw(
            view,
            v => MoveTo(v, line => line.Text.Contains("Scope", StringComparison.Ordinal)),
            v => Press(v, Key.CursorDown.WithShift),
            v =>
            {
                Press(v, Key.CursorDown);
                Assert.True(v.HasSelection);
            });
    }

    [Fact]
    public void A_rebuild_clears_the_selection()
    {
        DetailView view = Loaded(DetailFixtures.Json(DetailFixtures.JsonEvent), DetailFixtures.Config());

        Draw(
            view,
            v => MoveTo(v, line => line.Text.Contains("Scope", StringComparison.Ordinal)),
            v => Press(v, Key.CursorDown.WithShift),
            v =>
            {
                Assert.True(v.HasSelection);
                Press(v, Key.R);
                Assert.False(v.HasSelection);
            });
    }

    [Fact]
    public void A_character_selection_within_one_line_seeds_a_search_with_its_full_line_index_and_offset()
    {
        DetailView view = Loaded(DetailFixtures.Text(DetailFixtures.StackEvent), DetailFixtures.Config());
        SearchSeed? seed = null;

        Draw(
            view,
            v => SelectInRow(v, 4, from: 9, to: 19),
            v => seed = v.SelectionSeed());

        Assert.Equal(new SearchSeed("Run failed", Line: 4, Start: 9), seed);
    }

    [Fact]
    public void A_line_selection_seeds_nothing()
    {
        DetailView view = Loaded(DetailFixtures.Text(DetailFixtures.StackEvent), DetailFixtures.Config());
        SearchSeed? seed = null;

        Draw(
            view,
            v => SelectInRow(v, 4, from: 9, to: 19),
            v => Press(v, Key.CursorDown.WithShift),
            v => seed = v.SelectionSeed());

        Assert.Null(seed);
    }

    [Fact]
    public void A_character_selection_over_two_lines_seeds_nothing()
    {
        DetailView view = Loaded(DetailFixtures.Text(DetailFixtures.StackEvent), DetailFixtures.Config());
        SearchSeed? seed = null;

        Draw(
            view,
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(10, 4) })),
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.PositionReport, Position = new(10, 5) })),
            v =>
            {
                Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new(10, 5) }));
                seed = v.SelectionSeed();
            });

        Assert.Null(seed);
    }

    [Fact]
    public void A_selection_longer_than_200_units_seeds_nothing()
    {
        var detail = DetailFixtures.Text("INFO 2026-11-18 10:20:31,007 [Worker 3] SheetKraft.Engine.Runner - " + new string('x', 250));
        DetailView view = Loaded(detail, DetailFixtures.Config());
        SearchSeed? seed = null;

        DrawAt(
            view,
            300,
            v => SelectInRow(v, 4, from: 9, to: 9 + 200),
            v => seed = v.SelectionSeed());

        Assert.Equal(200, seed!.Value.Text.Length);

        DetailView longer = Loaded(detail, DetailFixtures.Config());
        DrawAt(
            longer,
            300,
            v => SelectInRow(v, 4, from: 9, to: 9 + 201),
            v => seed = v.SelectionSeed());

        Assert.Null(seed);
    }

    private static DetailView Loaded(EventDetail detail, LogvuConfig config)
    {
        var view = new DetailView();
        view.Load(detail, config);
        return view;
    }

    private static List<TerminalScreen> Draw(DetailView view, params Action<DetailView>[] steps) => DrawAt(view, Width, steps);

    private static List<TerminalScreen> DrawAt(DetailView view, int width, params Action<DetailView>[] steps) =>
        TerminalHarness.Run(view, width, Height, steps.Select(step => (Action)(() => step(view))).ToArray());

    private static void Press(DetailView view, Key key, int times = 1)
    {
        for (int i = 0; i < times; i++)
        {
            Assert.True(view.HandleKey(key), $"the detail did not handle {key}");
        }
    }

    /// <summary>Moves the cursor down to the first line from the cursor on that matches, failing the test when there is none.</summary>
    private static void MoveTo(DetailView view, Func<DetailLine, bool> match)
    {
        IReadOnlyList<DetailLine> lines = view.Document!.Lines;
        int index = -1;
        for (int i = view.CursorLine; i < lines.Count && index < 0; i++)
        {
            if (match(lines[i]))
            {
                index = i;
            }
        }

        Assert.True(index >= 0, "no line from the cursor on matches");
        Press(view, Key.CursorDown, index - view.CursorLine);
    }

    /// <summary>
    /// The screen row of the first row of a visible line, with the view at its current width and the viewport at the top of the document.
    /// </summary>
    private static int RowOf(DetailView view, int line)
    {
        int width = Math.Max(1, view.Viewport.Width - DetailView.Gutter);
        return Enumerable.Range(0, line).Sum(i => new DetailWrap(view.Document!.Lines[i], width).RowCount);
    }

    /// <summary>A press at the text's offset <paramref name="from"/> of a one-row line, a drag to <paramref name="to"/>, and a release.</summary>
    private static void SelectInRow(DetailView view, int line, int from, int to)
    {
        int row = RowOf(view, line);
        Assert.True(view.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(DetailView.Gutter + from, row) }));
        Assert.True(view.HandleMouse(new Mouse { Flags = MouseFlags.PositionReport, Position = new(DetailView.Gutter + to, row) }));
        Assert.True(view.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new(DetailView.Gutter + to, row) }));
    }

    /// <summary>The text of a screen row without its first column, the gutter where the cursor glyph is drawn.</summary>
    private static string Body(TerminalScreen screen, int row) => screen.Text(row) is { Length: > 0 } text ? text[1..] : string.Empty;

    /// <summary>A message of <paramref name="count"/> numbered lines after one log4net-core line.</summary>
    private static string MessageLines(int count) =>
        "INFO 2026-11-18 10:20:31,007 [Worker 3] SheetKraft.Engine.Runner - first"
        + string.Concat(Enumerable.Range(0, count).Select(i => $"\nline {i:00}"));
}
