using System.Text;
using Mdedit.Core.Render;
using Mdedit.Tui;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Mdedit.Tests;

/// <summary>
/// The view search in the window, as drawn: the bar above the message line, the moves to matches (from the cursor, with wrapping and its
/// message), the selection of the active match, the highlights, the messages for no match and an invalid pattern, and Esc.
/// </summary>
public sealed partial class MainWindowTests
{
    // Two matches of "web", in the first and the third paragraph. The bar is row 9 and the message line row 10 at 40 by 12.
    private const string Found = "First web.\n\nOther text.\n\nSecond web here.\n";

    private const int BarRow = 9;

    private static readonly Color MatchBackground = new(ColorName16.BrightYellow);
    private static readonly Color ActiveBackground = new(ColorName16.BrightCyan);

    [Fact]
    public void Slash_opens_the_bar_above_the_message_line_with_the_focus_in_it()
    {
        OpenedFile file = Opened("notes.md", Found);

        bool fieldFocus = false;

        Session session = Run(file, 40, 12, (_, window) => window.NewKeyDownEvent(new Key('/')), (_, window) => fieldFocus = window.Search.Bar.HasFieldFocus);

        TerminalScreen screen = session.Screens[1];
        Assert.StartsWith("Find:", screen.Text(BarRow), StringComparison.Ordinal);
        Assert.Equal("\x2570", screen.Cells[BarRow - 1][0].Grapheme);
        Assert.True(fieldFocus);
    }

    [Fact]
    public void Enter_selects_the_first_match_after_the_cursor_scrolls_to_it_and_returns_the_focus_to_the_view()
    {
        OpenedFile file = Opened("notes.md", Found);
        int first = Found.IndexOf("web", StringComparison.Ordinal);

        bool viewFocus = false;

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(new Key('/')),
            (_, window) => Type(window, "web"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => viewFocus = window.View.HasFocus);

        Assert.Equal(new SourceRange(first, first + 3), session.Window.View.SelectionRange);
        Assert.Equal(first, session.Window.View.CursorOffset);
        Assert.True(viewFocus);
        Assert.Equal(2, session.Window.Search.Matches.Length);
    }

    [Fact]
    public void Every_match_is_drawn_in_the_match_colour_and_the_active_one_in_the_active_colour()
    {
        OpenedFile file = Opened("notes.md", Found);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(new Key('/')),
            (_, window) => Type(window, "web"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(new Key('n')));

        TerminalScreen after = session.Screens[^1];
        Assert.Equal(new[] { "web", "web" }, Highlighted(after));
        Assert.Equal(MatchBackground, AttributeOf(after, "web.").Background);
        Assert.Equal(ActiveBackground, AttributeOf(after, "web here").Background);
    }

    [Fact]
    public void n_and_N_and_F3_and_Shift_F3_move_through_the_matches_and_wrap_with_a_message()
    {
        OpenedFile file = Opened("notes.md", Found);
        int first = Found.IndexOf("web", StringComparison.Ordinal);
        int second = Found.LastIndexOf("web", StringComparison.Ordinal);
        var starts = new List<int>();
        void Record(MainWindow window) => starts.Add(window.View.CursorOffset);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(new Key('/')),
            (_, window) => Type(window, "web"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => { window.NewKeyDownEvent(new Key('n')); Record(window); },
            (_, window) => { window.NewKeyDownEvent(new Key('n')); Record(window); },
            (_, window) => { window.NewKeyDownEvent(Key.F3.WithShift); Record(window); },
            (_, window) => { window.NewKeyDownEvent(Key.F3.WithShift); Record(window); },
            (_, window) => { window.NewKeyDownEvent(Key.F3); Record(window); },
            (_, window) => { window.NewKeyDownEvent(new Key('N')); Record(window); });

        // After Enter the first match is selected; n goes to the second, n wraps to the first, Shift+F3 wraps to the second, Shift+F3
        // goes to the first, F3 goes to the second, and N goes back to the first.
        Assert.Equal(first, session.Window.View.SelectionRange!.Value.Start);
        Assert.Equal(new[] { second, first, second, first, second, first }, starts);
        // Screen k is the one after step k: the second n (5) and the first Shift+F3 (6) wrap, the others do not.
        Assert.Equal(DocumentSearch.Wrapped, session.Screens[5].Text(10));
        Assert.Equal(DocumentSearch.Wrapped, session.Screens[6].Text(10));
    }

    [Fact]
    public void A_search_starts_from_the_cursor_so_the_next_match_after_it_is_selected()
    {
        OpenedFile file = Opened("notes.md", Found);
        int second = Found.LastIndexOf("web", StringComparison.Ordinal);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.View.SelectRange(new SourceRange(Found.IndexOf("Other", StringComparison.Ordinal), Found.IndexOf("Other", StringComparison.Ordinal) + 1)),
            (_, window) => window.NewKeyDownEvent(new Key('/')),
            (_, window) => Type(window, "web"),
            (_, window) => window.NewKeyDownEvent(Key.Enter));

        Assert.Equal(second, session.Window.View.CursorOffset);
    }

    [Fact]
    public void A_query_with_no_match_says_No_matches_and_highlights_nothing()
    {
        OpenedFile file = Opened("notes.md", Found);

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(new Key('/')),
            (_, window) => Type(window, "zzz"),
            (_, window) => window.NewKeyDownEvent(Key.Enter));

        Assert.Equal(DocumentSearch.NoMatches, session.Screens[^1].Text(10));
        Assert.Empty(Highlighted(session.Screens[^1]));
    }

    [Fact]
    public void An_invalid_pattern_is_shown_in_the_bar_and_matches_nothing()
    {
        OpenedFile file = Opened("notes.md", Found);

        // The status of the bar needs room: at 40 columns it would be cut to three characters.
        Session session = Run(
            file,
            80,
            12,
            (_, window) => window.NewKeyDownEvent(new Key('/')),
            (_, window) => window.NewKeyDownEvent(Key.R.WithAlt),
            (_, window) => Type(window, "("));

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains(DocumentSearch.InvalidPattern, screen.Text(BarRow), StringComparison.Ordinal);
        Assert.Empty(Highlighted(screen));
    }

    [Fact]
    public void Esc_clears_the_text_then_closes_the_bar_and_returns_the_focus_to_the_view()
    {
        OpenedFile file = Opened("notes.md", Found);
        bool viewFocus = false;

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(new Key('/')),
            (_, window) => Type(window, "web"),
            (_, window) => window.NewKeyDownEvent(Key.Esc),
            (_, window) => window.NewKeyDownEvent(Key.Esc),
            (_, window) => viewFocus = window.View.HasFocus);

        TerminalScreen screen = session.Screens[^1];
        Assert.False(session.Window.Search.IsOpen);
        Assert.False(session.Window.Stopped);
        Assert.True(viewFocus);
        Assert.DoesNotContain("Find:", screen.Text(BarRow), StringComparison.Ordinal);
        Assert.Empty(Highlighted(screen));
    }

    // ----- Helpers -----

    private static void Type(MainWindow window, string text)
    {
        foreach (char c in text)
        {
            window.NewKeyDownEvent(new Key(c));
        }
    }

    /// <summary>The attribute of the first cell of <paramref name="text"/> in the first row that contains it.</summary>
    private static Attribute AttributeOf(TerminalScreen screen, string text) => screen.At(text);

    /// <summary>
    /// The texts of the runs of cells drawn in a match colour, in reading order (a run ends at a cell of another colour or a row). Only
    /// the rows above the bar are read: the bar's option toggles are drawn in the active colour too.
    /// </summary>
    private static List<string> Highlighted(TerminalScreen screen)
    {
        var runs = new List<string>();
        var run = new StringBuilder();
        foreach (var row in screen.Cells.Take(BarRow))
        {
            foreach (var cell in row)
            {
                if (cell.Attribute.Background == MatchBackground || cell.Attribute.Background == ActiveBackground)
                {
                    run.Append(cell.Grapheme);
                }
                else if (run.Length > 0)
                {
                    runs.Add(run.ToString());
                    run.Clear();
                }
            }

            if (run.Length > 0)
            {
                runs.Add(run.ToString());
                run.Clear();
            }
        }

        return runs;
    }
}
