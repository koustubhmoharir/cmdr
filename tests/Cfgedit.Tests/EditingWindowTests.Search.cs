using Cfgedit.Tui;
using Shared.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cfgedit.Tests;

/// <summary>
/// Search and copy in the tree, as drawn: the bar and its keys, the moves to matches (with wrapping and expansion of collapsed nodes), the
/// highlights, the bar and the inline editor never open together, the search again after an edit, and the OSC 52 copy of a value or a path.
/// Rows are the 80 by 24 screen unless a test says otherwise: the bar is row 21, the message line row 22 and the hints row 23.
/// </summary>
public sealed partial class EditingWindowTests
{
    // The tree's rows: name, hosts (with items "web", "alpha", "web2"). The cursor starts on the name.
    private const string SearchJson = "{\n  \"name\": \"web\",\n  \"hosts\": [\"web\", \"alpha\", \"web2\"]\n}";

    private const int BarRow = 21;
    private const int MessageRow = 22;

    private static readonly Color MatchBackground = new(ColorName16.BrightYellow);
    private static readonly Color ActiveBackground = new(ColorName16.BrightCyan);
    private const string BarHint = "F3 next \x00B7 Shift+F3 prev";

    /// <summary>
    /// The text of the highlighted cells, row by row: every cell of a match or of the current match. Only the tree's rows are read: the
    /// rows from the first under the top border to the one above the bar (the bar's toggles use the same colours).
    /// </summary>
    private static string Highlighted(TerminalScreen screen) => string.Concat(
        screen.Cells.Take(screen.Cells.Count - 3).Skip(1).SelectMany(row => row)
            .Where(cell => cell.Attribute.Background == MatchBackground || cell.Attribute.Background == ActiveBackground)
            .Select(cell => cell.Grapheme));

    /// <summary>The attribute of the cell at the first <paramref name="part"/> of the row that contains <paramref name="rowText"/>.</summary>
    private static Attribute AttributeOf(TerminalScreen screen, string rowText, string part)
    {
        int row = screen.RowOf(rowText);
        return screen.Cells[row][screen.Text(row).IndexOf(part, StringComparison.Ordinal)].Attribute;
    }

    [Fact]
    public void Slash_opens_the_bar_above_the_detail_line_and_typing_highlights_every_match()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('/')), (_, w) => Type(w, "web"));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("\x2570", screen.Cells[BarRow - 1][0].Grapheme);
        Assert.StartsWith("Find: web", screen.Text(BarRow), StringComparison.Ordinal);
        Assert.Equal("webwebweb", Highlighted(screen));
        AssertHints(screen, TreeHints);
    }

    [Fact]
    public void Enter_in_the_bar_goes_to_the_first_match_and_returns_the_focus_to_the_tree()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "web"),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Keys(w, new Key('n')));

        Assert.Equal("$.name  string  web", session.Screens[3].Text(MessageRow));
        Assert.Equal("$.hosts[0]  string  web", session.Screens[4].Text(MessageRow));
        Assert.EndsWith(BarHint, session.Screens[4].Text(BarRow), StringComparison.Ordinal);
    }

    [Fact]
    public void n_and_N_go_through_the_matches_in_order_and_wrap_with_a_message()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "web"),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Keys(w, new Key('n')),
            (_, w) => Keys(w, new Key('n')),
            (_, w) => Keys(w, new Key('n')),
            (_, w) => Keys(w, new Key('N')),
            (_, w) => Keys(w, new Key('N')),
            (_, w) => Keys(w, new Key('N')));

        // Screens: 0 the start, 1 after "/", 2 after the query, 3 after Enter, then n, n, n (the third wraps), N, N, N (the first wraps, the
        // third wraps again after the first match).
        Assert.Equal("$.name  string  web", session.Screens[3].Text(MessageRow));
        Assert.Equal("$.hosts[0]  string  web", session.Screens[4].Text(MessageRow));
        Assert.Equal("$.hosts[2]  string  web2", session.Screens[5].Text(MessageRow));
        Assert.Equal("Search wrapped", session.Screens[6].Text(MessageRow));
        Assert.Equal("Search wrapped", session.Screens[7].Text(MessageRow));
        Assert.Equal("$.hosts[0]  string  web", session.Screens[8].Text(MessageRow));
        Assert.Equal("$.name  string  web", session.Screens[9].Text(MessageRow));
    }

    [Fact]
    public void F3_and_Shift_F3_go_to_the_next_and_previous_match_as_n_and_N_do()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "web"),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Keys(w, Key.F3),
            (_, w) => Keys(w, Key.F3.WithShift));

        Assert.Equal("$.hosts[0]  string  web", session.Screens[4].Text(MessageRow));
        Assert.Equal("$.name  string  web", session.Screens[5].Text(MessageRow));
    }

    [Fact]
    public void n_with_no_earlier_query_does_nothing()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(file, 80, 24, null, null, (_, w) => Keys(w, new Key('n')));

        Assert.Equal(session.Screens[0].Text(MessageRow), session.Screens[1].Text(MessageRow));
        Assert.Equal(string.Empty, Highlighted(session.Screens[1]));
    }

    [Fact]
    public void A_query_with_no_match_says_so_on_the_message_line()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "zzz"),
            (_, w) => Keys(w, Key.Enter));

        Assert.Equal("No matches", session.Screens[^1].Text(MessageRow));
        Assert.Equal(string.Empty, Highlighted(session.Screens[^1]));
    }

    [Fact]
    public void An_invalid_pattern_is_shown_in_the_bar_and_highlights_nothing()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Keys(w, Key.R.WithAlt),
            (_, w) => Type(w, "("));

        TerminalScreen screen = session.Screens[^1];
        Assert.EndsWith("invalid pattern", screen.Text(BarRow), StringComparison.Ordinal);
        Assert.Equal(string.Empty, Highlighted(screen));
    }

    [Fact]
    public void Esc_clears_the_text_then_closes_the_bar_and_the_last_query_stays_for_n()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "web"),
            (_, w) => Keys(w, Key.Esc),
            (_, w) => Keys(w, Key.Esc),
            (_, w) => Keys(w, new Key('n')));

        Assert.StartsWith("Find: ", session.Screens[3].Text(BarRow), StringComparison.Ordinal);
        Assert.Equal(string.Empty, Highlighted(session.Screens[3]));
        Assert.Equal("\x2570", session.Screens[4].Cells[BarRow][0].Grapheme);
        Assert.Equal("$.name  string  web", session.Screens[5].Text(MessageRow));
        Assert.StartsWith("Find: web", session.Screens[5].Text(BarRow), StringComparison.Ordinal);
    }

    [Fact]
    public void Esc_in_the_tree_after_Enter_closes_the_bar_and_the_last_query_stays_for_n()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "web"),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Keys(w, Key.Esc),
            (_, w) => Keys(w, new Key('n')));

        Assert.StartsWith("Find: web", session.Screens[3].Text(BarRow), StringComparison.Ordinal);
        Assert.Equal("\x2570", session.Screens[4].Cells[BarRow][0].Grapheme);
        Assert.Equal(string.Empty, Highlighted(session.Screens[4]));
        Assert.StartsWith("Find: web", session.Screens[5].Text(BarRow), StringComparison.Ordinal);
    }

    [Fact]
    public void A_match_inside_a_collapsed_node_expands_its_ancestors_and_the_cursor_goes_to_it()
    {
        // "d" is four levels down, so it starts collapsed, and its child "target" is not on screen.
        OpenedFile file = Open("settings.json", "{\"a\": {\"b\": {\"c\": {\"d\": {\"target\": \"needle\"}}}}}");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "needle"),
            (_, w) => Keys(w, Key.Enter));

        Assert.DoesNotContain("target", Drawn(session.Screens[0]), StringComparison.Ordinal);
        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("$.a.b.c.d.target  string  needle", screen.Text(MessageRow));
        Assert.Contains("target: needle", Drawn(screen), StringComparison.Ordinal);
        Assert.Equal("needle", Highlighted(screen));
    }

    [Fact]
    public void Matches_are_highlighted_with_the_current_match_drawn_apart_from_the_others()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "web"),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Keys(w, new Key('n')));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal(SearchColors.Match(screen.Normal), AttributeOf(screen, "name: web", "web"));
        Assert.Equal(TreeColors.Focused(SearchColors.Active(screen.Normal)), AttributeOf(screen, "0: web", "web"));
        Assert.Equal(SearchColors.Match(screen.Normal), AttributeOf(screen, "2: web2", "web"));
    }

    [Fact]
    public void A_match_after_a_control_character_is_highlighted_on_its_own_cells()
    {
        OpenedFile file = Open("app.config", "<r>a\tweb</r>");

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "web"));

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("a\x2192web", Drawn(screen), StringComparison.Ordinal);
        Assert.Equal("web", Highlighted(screen));
        Assert.Equal(SearchColors.Match(screen.Normal), AttributeOf(screen, "a\x2192web", "web"));
    }

    [Fact]
    public void A_match_in_a_truncated_value_is_highlighted_only_where_the_value_is_shown()
    {
        // At 30 columns the tree is 28 wide. The member is at depth 1, so its value starts at column 10 and is cut at column 27, which is
        // the ellipsis: "abcdefghijklmnopq" is shown. "pqrs" is then highlighted on "pq" only.
        OpenedFile file = Open("settings.json", "{\"note\": \"abcdefghijklmnopqrstuvwxyz\"}");

        Session session = Run(
            file,
            30,
            12,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "pqrs"));

        TerminalScreen screen = session.Screens[^1];
        Assert.Contains("    note: abcdefghijklmnopq\x2026", Drawn(screen), StringComparison.Ordinal);
        Assert.Equal("pq", Highlighted(screen));
    }

    [Fact]
    public void The_bar_and_the_inline_editor_are_never_open_together_and_Ctrl_F_commits_the_field_first()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight, Key.Enter),
            (_, w) => Keys(w, Key.End, Key.Backspace, Key.Backspace, Key.Backspace),
            (_, w) => Type(w, "api"),
            (_, w) => Keys(w, Key.F.WithCtrl));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("{\n  \"name\": \"api\",\n  \"hosts\": [\"web\", \"alpha\", \"web2\"]\n}", Text(session));
        Assert.StartsWith("Find: ", screen.Text(BarRow), StringComparison.Ordinal);
        AssertHints(screen, TreeHints);
    }

    [Fact]
    public void Ctrl_F_while_the_field_has_an_error_leaves_the_field_open_and_the_bar_closed()
    {
        OpenedFile file = Open("settings.json", JsonText);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorDown, Key.CursorRight, Key.Enter),
            (_, w) => Type(w, "x"),
            (_, w) => Keys(w, Key.F.WithCtrl));

        TerminalScreen screen = session.Screens[^1];
        Assert.DoesNotContain("Find:", screen.Text(BarRow), StringComparison.Ordinal);
        AssertHints(screen, EditingHints);
    }

    [Fact]
    public void Enter_on_a_value_while_the_bar_is_open_closes_the_bar_and_opens_the_editor()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "web"),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Keys(w, Key.Enter));

        TerminalScreen screen = session.Screens[^1];
        Assert.Equal("\x2570", screen.Cells[BarRow][0].Grapheme);
        AssertHints(screen, EditingHints);
    }

    [Fact]
    public void The_search_history_recalls_an_earlier_term_with_the_up_key()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "nest"),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "zz"),
            (_, w) => Keys(w, Key.CursorUp));

        Assert.StartsWith("Find: nest", session.Screens[^1].Text(BarRow), StringComparison.Ordinal);
    }

    [Fact]
    public void Entering_text_mode_closes_the_bar()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "web"),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Keys(w, Key.T.WithCtrl));

        // The bar's field takes Ctrl+T, so the focus goes back to the tree first (Enter does that).
        Assert.DoesNotContain("Find:", session.Screens[^1].Text(BarRow), StringComparison.Ordinal);
        Assert.Equal(string.Empty, Highlighted(session.Screens[^1]));
    }

    [Fact]
    public void A_structure_edit_searches_the_new_document_so_a_match_never_points_at_a_removed_node()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, new Key('/')),
            (_, w) => Type(w, "web"),
            (_, w) => Keys(w, Key.Enter),
            (_, w) => Keys(w, new Key('n')),
            (_, w) => Keys(w, new Key('d')),
            (_, w) => Keys(w, new Key('n')),
            (_, w) => Keys(w, Key.Z.WithCtrl),
            (_, w) => Keys(w, new Key('n')));

        // Screens: 3 after Enter, 4 after the first n (hosts[0]), 5 after d, 6 after n, 7 after the undo, 8 after the last n.
        Assert.Equal("webweb", Highlighted(session.Screens[5]));
        Assert.Equal("$.hosts[1]  string  web2", session.Screens[6].Text(MessageRow));
        Assert.Equal("webwebweb", Highlighted(session.Screens[7]));
        Assert.Equal("$.hosts[0]  string  web", session.Screens[8].Text(MessageRow));
    }

    [Fact]
    public void y_copies_the_focused_value_as_OSC_52_and_says_so()
    {
        OpenedFile file = Open("settings.json", SearchJson);
        var written = new List<string>();

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight),
            (_, w) =>
            {
                w.WriteRaw = written.Add;
                Keys(w, new Key('y'));
            });

        Assert.Equal(new[] { Osc52.Build("web") }, written);
        Assert.Equal("Copied value", session.Screens[^1].Text(MessageRow));
    }

    [Fact]
    public void Y_copies_the_focused_path_and_says_so()
    {
        OpenedFile file = Open("settings.json", SearchJson);
        var written = new List<string>();

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) =>
            {
                w.WriteRaw = written.Add;
                Keys(w, Key.CursorDown, new Key('Y'));
            });

        Assert.Equal(new[] { Osc52.Build("$.name") }, written);
        Assert.Equal("Copied path", session.Screens[^1].Text(MessageRow));
    }

    [Fact]
    public void y_copies_the_logical_value_with_its_control_characters_and_not_their_glyphs()
    {
        OpenedFile file = Open("app.config", "<r>a\tb</r>");
        var written = new List<string>();

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorRight),
            (_, w) =>
            {
                w.WriteRaw = written.Add;
                Keys(w, new Key('y'));
            });

        Assert.Contains("a\x2192b", Drawn(session.Screens[0]), StringComparison.Ordinal);
        Assert.Equal(new[] { Osc52.Build("a\tb") }, written);
        Assert.Equal("Copied value", session.Screens[^1].Text(MessageRow));
    }

    [Fact]
    public void A_value_over_the_copy_limit_is_not_copied_and_the_message_says_so()
    {
        OpenedFile file = Open("settings.json", "{\"big\": \"" + new string('x', Osc52.MaxBytes + 1) + "\"}");
        var written = new List<string>();

        Session session = Run(
            file,
            80,
            24,
            null,
            null,
            (_, w) => Keys(w, Key.CursorDown, Key.CursorRight),
            (_, w) =>
            {
                w.WriteRaw = written.Add;
                Keys(w, new Key('y'));
            });

        Assert.Empty(written);
        Assert.Equal("Too large to copy", session.Screens[^1].Text(MessageRow));
    }

    [Fact]
    public void The_hints_show_find_first_and_copy_when_the_row_is_wide_enough()
    {
        OpenedFile file = Open("settings.json", SearchJson);

        Session session = Run(file, 100, 24, null, null);

        TerminalScreen screen = session.Screens[0];
        Assert.StartsWith("Enter Edit  ^S Save  / Find  ", screen.Text(23), StringComparison.Ordinal);
        Assert.Contains("^T Text  y Copy  Tab Next", screen.Text(23), StringComparison.Ordinal);
        Assert.EndsWith(QuitHint, screen.Text(23), StringComparison.Ordinal);
    }
}
