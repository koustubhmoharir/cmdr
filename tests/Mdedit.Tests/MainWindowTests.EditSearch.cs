using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// The search in edit mode, in the window: Ctrl+F opens the bar over the editor, the matches are the source text (markup included), the active
/// match is selected with the caret at its start, F3 and Shift+F3 move with their messages, Enter and Esc move the focus, the query carries
/// between the modes, and the matches follow the edits.
/// </summary>
public sealed partial class MainWindowTests
{
    // The bold text of a paragraph: the "**" of "**bold**" starts at 4.
    private const string Bold = "Say **bold** word.\n";

    [Fact]
    public void Ctrl_F_in_edit_mode_opens_the_bar_above_the_message_line_with_the_focus_in_it()
    {
        OpenedFile file = Opened("notes.md", Found);

        bool fieldFocus = false;
        bool editing = false;
        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.F.WithCtrl),
            (_, window) =>
            {
                fieldFocus = window.Search.Bar.HasFieldFocus;
                editing = window.InEditMode;
            });

        Assert.StartsWith("Find:", session.Screens[2].Text(BarRow), StringComparison.Ordinal);
        Assert.True(fieldFocus);
        Assert.True(editing);
    }

    [Fact]
    public void A_query_finds_the_source_text_including_its_markup_and_selects_the_match_with_the_caret_at_its_start()
    {
        OpenedFile file = Opened("notes.md", Bold);

        SourceRange[] matches = [];
        (int Start, int End, int Caret) selection = default;
        bool focus = false;
        Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.F.WithCtrl),
            (_, window) => Type(window, "**b"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) =>
            {
                matches = window.Search.Matches.ToArray();
                selection = (window.Editor.SelectionStart, window.Editor.SelectionEnd, window.Editor.CaretOffset);
                focus = window.Editor.HasFocus;
            });

        Assert.Equal([new SourceRange(4, 7)], matches);
        Assert.Equal((4, 7, 4), selection);
        Assert.True(focus);
    }

    [Fact]
    public void F3_and_Shift_F3_move_between_the_matches_and_say_when_the_search_wrapped()
    {
        OpenedFile file = Opened("notes.md", Found);
        int first = Found.IndexOf("web", StringComparison.Ordinal);
        int second = Found.IndexOf("web", first + 1, StringComparison.Ordinal);

        var starts = new List<int>();
        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.F.WithCtrl),
            (_, window) => Type(window, "web"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => starts.Add(window.Editor.SelectionStart),
            (_, window) => window.NewKeyDownEvent(Key.F3),
            (_, window) => starts.Add(window.Editor.SelectionStart),
            (_, window) => window.NewKeyDownEvent(Key.F3),
            (_, window) => starts.Add(window.Editor.SelectionStart),
            (_, window) => window.NewKeyDownEvent(Key.F3.WithShift),
            (_, window) => starts.Add(window.Editor.SelectionStart));

        Assert.Equal([first, second, first, second], starts);
        Assert.Equal(DocumentSearch.Wrapped, session.Screens[^1].Text(10));
    }

    [Fact]
    public void Enter_with_no_match_says_No_matches_and_selects_nothing()
    {
        OpenedFile file = Opened("notes.md", Found);

        bool selected = true;
        int matches = -1;
        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.F.WithCtrl),
            (_, window) => Type(window, "zzz"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) =>
            {
                selected = window.Editor.HasSelection;
                matches = window.Search.Matches.Length;
            });

        Assert.Equal(DocumentSearch.NoMatches, session.Screens[^1].Text(10));
        Assert.Equal(0, matches);
        Assert.False(selected);
    }

    [Fact]
    public void Esc_in_the_bar_closes_it_and_returns_the_focus_to_the_editor()
    {
        OpenedFile file = Opened("notes.md", Found);

        var state = new List<(bool Open, bool Editing, bool Focus)>();
        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.F.WithCtrl),
            (_, window) => Type(window, "web"),
            (_, window) => window.NewKeyDownEvent(Key.Esc),
            (_, window) => window.NewKeyDownEvent(Key.Esc),
            (_, window) => state.Add((window.Search.IsOpen, window.InEditMode, window.Editor.HasFocus)));

        Assert.Equal((false, true, true), state[^1]);
        Assert.DoesNotContain("Find:", session.Screens[^1].Text(BarRow), StringComparison.Ordinal);
    }

    [Fact]
    public void Esc_in_the_editor_with_the_bar_open_closes_the_bar_and_stays_in_edit_mode()
    {
        OpenedFile file = Opened("notes.md", Found);

        (bool Open, bool Editing, bool Focus) state = default;
        Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.F.WithCtrl),
            (_, window) => Type(window, "web"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(Key.Esc),
            (_, window) => state = (window.Search.IsOpen, window.InEditMode, window.Editor.HasFocus));

        Assert.Equal((false, true, true), state);
    }

    [Fact]
    public void The_query_carries_from_edit_mode_to_view_mode_and_back_while_the_matches_are_those_of_each_mode()
    {
        OpenedFile file = Opened("notes.md", Found);

        (bool Editing, int Matches, string Selected) state = default;
        Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.F.WithCtrl),
            (_, window) => Type(window, "web"),
            (_, window) => window.NewKeyDownEvent(Key.Esc),
            (_, window) => window.NewKeyDownEvent(Key.Esc),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(new Key('n')),
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.F3),
            (_, window) => state = (window.InEditMode, window.Search.Matches.Length, window.Editor.SelectedText));

        // The view's search selection is backward (its caret at the start), and the editor's PlaceCaret puts its caret at the end of a
        // backward selection, so F3 in the editor goes on from there: it selects a match of the query in the source, not necessarily the first.
        Assert.True(state.Editing);
        Assert.Equal(2, state.Matches);
        Assert.Equal("web", state.Selected);
    }

    [Fact]
    public void The_matches_follow_the_edits_while_the_search_is_open()
    {
        OpenedFile file = Opened("notes.md", Found);

        (int Matches, string Text) state = default;
        Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.F.WithCtrl),
            (_, window) => Type(window, "web"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(Key.Delete),
            (_, window) => state = (window.Search.Matches.Length, window.Editor.Buffer.Text));

        Assert.Equal((1, "First .\n\nOther text.\n\nSecond web here.\n"), state);
    }

    [Fact]
    public void Closing_the_search_drops_its_active_match_and_its_matches()
    {
        OpenedFile file = Opened("notes.md", Found);

        (bool Open, int Active, int Matches) state = default;
        Run(
            file,
            40,
            12,
            (_, window) => window.NewKeyDownEvent(Key.T.WithCtrl),
            (_, window) => window.NewKeyDownEvent(Key.F.WithCtrl),
            (_, window) => Type(window, "web"),
            (_, window) => window.NewKeyDownEvent(Key.Enter),
            (_, window) => window.NewKeyDownEvent(Key.Esc),
            (_, window) => window.NewKeyDownEvent(Key.Esc),
            (_, window) => state = (window.Search.IsOpen, window.Search.ActiveIndex, window.Search.Matches.Length));

        Assert.Equal((false, -1, 0), state);
    }
}
