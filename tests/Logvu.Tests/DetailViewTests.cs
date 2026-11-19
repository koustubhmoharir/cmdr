using Logvu.Core.Config;
using Logvu.Core.Detail;
using Logvu.Core.Store;
using Logvu.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Logvu.Tests;

/// <summary>The detail view: its drawn rows and their attributes, the cursor and the scrolling, the folds, the raw view and the JSON tree.</summary>
[Collection("Terminal GUI")]
public sealed class DetailViewTests
{
    private const int Width = 100;
    private const int Height = 30;

    [Fact]
    public void The_fields_come_first_and_an_exception_line_is_styled_by_its_parts()
    {
        DetailView view = Loaded(DetailFixtures.Text(DetailFixtures.StackEvent), DetailFixtures.Config());

        TerminalScreen screen = Draw(view)[0];

        Assert.StartsWith("level: ERROR", screen.Text(0));
        Assert.StartsWith("Message: Run failed", screen.Text(4));

        Attribute type = screen.At("System.InvalidOperationException");
        Assert.Equal(new Color(ColorName16.BrightRed), type.Foreground);
        Assert.True(type.Style.HasFlag(TextStyle.Bold));

        Attribute message = screen.At("Sheet not found");
        Assert.Equal(new Color(ColorName16.Red), message.Foreground);
        Assert.True(message.Style.HasFlag(TextStyle.Bold));

        Attribute method = screen.At("Execute(Int32");
        Assert.Equal(screen.Normal.Foreground, method.Foreground);
        Assert.True(method.Style.HasFlag(TextStyle.Bold));

        Attribute directory = screen.At("C:\\src\\Engine\\");
        Assert.True(directory.Style.HasFlag(TextStyle.Faint));

        Attribute location = screen.At("Runner.cs:line 88");
        Assert.Equal(new Color(ColorName16.Cyan), location.Foreground);
        Assert.False(location.Style.HasFlag(TextStyle.Underline));

        Assert.NotEqual(type, method);
        Assert.NotEqual(method, directory);
    }

    [Fact]
    public void Up_and_down_move_the_cursor_line_by_line_and_the_viewport_keeps_it_in_view()
    {
        DetailView view = Loaded(DetailFixtures.Text(MessageLines(40)), DetailFixtures.Config());

        int last = 0;
        List<TerminalScreen> screens = Draw(
            view,
            v => MoveTo(v, line => line.Text.Trim() == "line 00"),
            v => Press(v, Key.CursorDown, 35),
            v => Press(v, Key.CursorUp),
            v =>
            {
                Press(v, Key.Home);
                Assert.Equal(0, v.CursorLine);
            },
            v =>
            {
                Press(v, Key.End);
                last = v.Document!.Lines.Count - 1;
                Assert.Equal(last, v.CursorLine);
            },
            v =>
            {
                Press(v, Key.PageUp);
                Assert.Equal(last - Height, v.CursorLine);
            });

        // Screen 0 is the first draw; screen n is the draw after step n. Moved past the bottom, the cursor line is on the last row.
        Assert.Equal("line 35", screens[2].Text(Height - 1).Trim());
        Assert.Equal("line 34", screens[3].Text(Height - 2).Trim());
        Assert.StartsWith("level: INFO", screens[4].Text(0));
        Assert.Equal("line 39", screens[5].Text(Height - 1).Trim());
    }

    [Fact]
    public void The_wheel_scrolls_three_rows_and_leaves_the_cursor_where_it_was()
    {
        DetailView view = Loaded(DetailFixtures.Text(MessageLines(40)), DetailFixtures.Config());

        List<TerminalScreen> screens = Draw(view, v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.WheeledDown })));

        Assert.Equal(view.Document!.Lines[DetailView.WheelRows].Text.TrimEnd(), screens[1].Text(0));
        Assert.Equal(0, view.CursorLine);
    }

    [Fact]
    public void A_left_press_selects_the_line_of_its_row_without_scrolling()
    {
        DetailView view = Loaded(DetailFixtures.Text(MessageLines(40)), DetailFixtures.Config());

        List<TerminalScreen> screens = Draw(
            view,
            v => Assert.True(v.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(0, 5) })));

        Assert.Equal(5, view.CursorLine);
        Assert.Equal(screens[0].Text(0), screens[1].Text(0));
    }

    [Fact]
    public void A_wrapped_cursor_line_is_drawn_in_the_focus_colors_on_all_its_rows()
    {
        string message = string.Join(' ', Enumerable.Repeat("alpha", 12));
        DetailView view = Loaded(
            DetailFixtures.Text("INFO 2026-11-18 10:20:31,007 [Worker 3] SheetKraft.Engine.Runner - " + message),
            DetailFixtures.Config());

        List<TerminalScreen> screens = DrawAt(
            view,
            40,
            v => MoveTo(v, line => line.Text.Contains("alpha alpha", StringComparison.Ordinal)));

        TerminalScreen screen = screens[1];
        int[] rows = Enumerable.Range(0, Height).Where(r => screen.Text(r).Contains("alpha", StringComparison.Ordinal)).ToArray();
        Assert.True(rows.Length >= 2, $"expected the message to wrap, got {rows.Length} row(s)");
        foreach (int row in rows)
        {
            Assert.Equal(screen.Focus.Background, screen.Cells[row][0].Attribute.Background);
            Assert.Equal(screen.Focus.Background, screen.Cells[row][39].Attribute.Background);
        }

        Assert.NotEqual(screen.Focus.Background, screen.Normal.Background);
    }

    [Fact]
    public void Enter_on_a_fold_marker_collapses_the_fold_and_the_cursor_stays_on_the_marker()
    {
        DetailView view = Loaded(DetailFixtures.Text(DetailFixtures.StackEvent), DetailFixtures.Config());

        List<TerminalScreen> screens = Draw(
            view,
            v => MoveTo(v, line => line.Kind == LineKind.Fold),
            v => Press(v, Key.Enter),
            v => Press(v, Key.Enter));

        // Screen 0 is the first draw; screen n is the draw after step n.
        Assert.Contains("RunInternal", string.Join("\n", Rows(screens[0])), StringComparison.Ordinal);
        Assert.Contains("RunInternal", string.Join("\n", Rows(screens[1])), StringComparison.Ordinal);
        Assert.DoesNotContain("RunInternal", string.Join("\n", Rows(screens[2])), StringComparison.Ordinal);
        Assert.Contains("RunInternal", string.Join("\n", Rows(screens[3])), StringComparison.Ordinal);

        Assert.Equal(LineKind.Fold, view.Document!.Lines[view.CursorLine].Kind);
        Assert.Contains("framework frames", view.Document.Lines[view.CursorLine].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Z_folds_and_unfolds_every_fold_and_r_shows_the_raw_text_with_the_cursor_on_top()
    {
        DetailView view = Loaded(DetailFixtures.Text(DetailFixtures.StackEvent), DetailFixtures.Config());

        List<TerminalScreen> screens = Draw(
            view,
            v => Press(v, Key.Z),
            v => Press(v, Key.Z),
            v =>
            {
                Press(v, Key.CursorDown, 5);
                Press(v, Key.R);
                Assert.True(v.Document!.Options.Raw);
                Assert.Equal(0, v.CursorLine);
            },
            v =>
            {
                Press(v, Key.R);
                Assert.False(v.Document!.Options.Raw);
            });

        // Screen 0 is the first draw; screen n is the draw after step n.
        Assert.Contains("RunInternal", string.Join("\n", Rows(screens[0])), StringComparison.Ordinal);
        Assert.DoesNotContain("RunInternal", string.Join("\n", Rows(screens[1])), StringComparison.Ordinal);
        Assert.Contains("RunInternal", string.Join("\n", Rows(screens[2])), StringComparison.Ordinal);
        Assert.StartsWith("ERROR 2026-11-18 10:20:31,007", screens[3].Text(0));
        Assert.StartsWith("level: ERROR", screens[4].Text(0));
    }

    [Fact]
    public void Enter_and_space_toggle_a_json_node_while_the_arrows_only_move_the_cursor()
    {
        DetailView view = Loaded(DetailFixtures.Json(DetailFixtures.JsonEvent), DetailFixtures.Config());

        List<TerminalScreen> screens = Draw(
            view,
            v => MoveTo(v, line => line.NodeId == "Properties"),
            v =>
            {
                Press(v, Key.CursorDown);
                Press(v, Key.CursorUp);
            },
            v => Press(v, Key.Enter),
            v => Press(v, Key.Space));

        // Screen 0 is the first draw; screen n is the draw after step n.
        Assert.Contains("▾ Properties", string.Join("\n", Rows(screens[1])), StringComparison.Ordinal);
        Assert.Contains("Scope", string.Join("\n", Rows(screens[2])), StringComparison.Ordinal);
        Assert.Contains("▸ Properties", string.Join("\n", Rows(screens[3])), StringComparison.Ordinal);
        Assert.DoesNotContain("Scope", string.Join("\n", Rows(screens[3])), StringComparison.Ordinal);
        Assert.Contains("▾ Properties", string.Join("\n", Rows(screens[4])), StringComparison.Ordinal);
        Assert.Contains("Scope", string.Join("\n", Rows(screens[4])), StringComparison.Ordinal);
    }

    [Fact]
    public void Right_and_l_are_not_bound_and_leave_the_node_and_the_cursor_as_they_are()
    {
        DetailView view = Loaded(DetailFixtures.Json(DetailFixtures.JsonEvent), DetailFixtures.Config());
        Draw(view, v => MoveTo(v, line => line.NodeId == "Properties"));
        int lines = view.Document!.Lines.Count;
        int cursor = view.CursorLine;
        Assert.True(view.Document.IsExpanded("Properties"));

        Assert.False(view.HandleKey(Key.CursorRight));
        Assert.False(view.HandleKey(Key.L));

        Assert.True(view.Document.IsExpanded("Properties"));
        Assert.Equal(lines, view.Document.Lines.Count);
        Assert.Equal(cursor, view.CursorLine);
    }

    [Fact]
    public void Esc_left_and_h_ask_to_close_the_detail_while_it_is_loading_and_once_it_is_loaded()
    {
        var loading = new DetailView { Placeholder = "Loading…" };
        var closes = new List<string>();
        loading.CloseRequested += () => closes.Add("loading");

        Assert.True(loading.HandleKey(Key.Esc));
        Assert.True(loading.HandleKey(Key.CursorLeft));
        Assert.True(loading.HandleKey(Key.H));

        DetailView loaded = Loaded(DetailFixtures.Text(DetailFixtures.StackEvent), DetailFixtures.Config());
        loaded.CloseRequested += () => closes.Add("loaded");

        Assert.True(loaded.HandleKey(Key.CursorLeft));
        Assert.True(loaded.HandleKey(Key.H));

        Assert.Equal(new[] { "loading", "loading", "loading", "loaded", "loaded" }, closes);
    }

    [Fact]
    public void J_and_k_move_the_cursor_like_down_and_up()
    {
        DetailView view = Loaded(DetailFixtures.Text(MessageLines(40)), DetailFixtures.Config());

        Draw(
            view,
            v =>
            {
                Press(v, Key.J, 2);
                Assert.Equal(2, v.CursorLine);
            },
            v =>
            {
                Press(v, Key.K);
                Assert.Equal(1, v.CursorLine);
            });
    }

    [Fact]
    public void An_unavailable_event_shows_its_reason_and_escape_asks_to_close()
    {
        var view = new DetailView();
        view.Load(new EventDetail.Unavailable(new RowKey(1, 0), "Source file expired"), DetailFixtures.Config());
        bool closed = false;
        view.CloseRequested += () => closed = true;

        TerminalScreen screen = Draw(view, v => Assert.True(v.HandleKey(Key.Esc)))[0];

        Assert.Contains("Source file expired", screen.Text(0), StringComparison.Ordinal);
        Assert.Equal(1, view.Document!.Lines.Count);
        Assert.True(closed);
    }

    [Fact]
    public void A_highlight_rule_with_a_configured_style_is_drawn_in_its_color()
    {
        LogvuConfig config = DetailFixtures.Config(
            "\"styles\": { \"deploy\": { \"foreground\": \"#102030\", \"bold\": true } }, "
            + "\"highlights\": [ { \"field\": \"message\", \"pattern\": \"Deploy\", \"style\": \"deploy\" } ], ");
        DetailView view = Loaded(
            DetailFixtures.Text("INFO 2026-11-18 10:20:31,007 [Worker 3] SheetKraft.Engine.Runner - Deploy finished"),
            config);

        TerminalScreen screen = Draw(view)[0];

        Attribute highlight = screen.At("Deploy finished");
        Assert.Equal(new Color(0x10, 0x20, 0x30), highlight.Foreground);
        Assert.True(highlight.Style.HasFlag(TextStyle.Bold));
        Assert.Equal(screen.Normal.Foreground, screen.At("finished").Foreground);
    }

    [Theory]
    [InlineData("error", ColorName16.Red)]
    [InlineData("warning", ColorName16.Yellow)]
    [InlineData("Success", ColorName16.Green)]
    public void Built_in_highlight_style_names_map_to_their_colors(string name, ColorName16 expected)
    {
        var normal = new Attribute(ColorName16.White, ColorName16.Black, TextStyle.None);

        Attribute attribute = DetailColors.Highlight(name, normal, DetailFixtures.Config());

        Assert.Equal(new Color(expected), attribute.Foreground);
        Assert.Equal(normal.Background, attribute.Background);
    }

    private static DetailView Loaded(EventDetail detail, LogvuConfig config)
    {
        var view = new DetailView();
        view.Load(detail, config);
        return view;
    }

    /// <summary>
    /// Draws the view at a width and runs the steps: one screen after the first draw and one after each step, in order.
    /// </summary>
    private static List<TerminalScreen> Draw(DetailView view, params Action<DetailView>[] steps) => DrawAt(view, Width, steps);

    /// <summary>Draws the view at the given width, as <see cref="Draw(DetailView, Action{DetailView}[])"/> does at the default one.</summary>
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

    private static IEnumerable<string> Rows(TerminalScreen screen) => Enumerable.Range(0, Height).Select(screen.Text);

    /// <summary>A log4net-core event whose message is one line of <paramref name="count"/> numbered lines.</summary>
    private static string MessageLines(int count) =>
        "INFO 2026-11-18 10:20:31,007 [Worker 3] SheetKraft.Engine.Runner - first"
        + string.Concat(Enumerable.Range(0, count).Select(i => $"\nline {i:00}"));
}
