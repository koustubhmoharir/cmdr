using Mdedit.Core.Editing;
using Mdedit.Core.Render;
using Mdedit.Tui;
using Shared.Tui;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Mdedit.Tests;

/// <summary>
/// Paste and cut in view mode: a bracketed paste (raised the way the driver raises it) writes Markdown source at the caret or over the selection, as
/// one undo step; Ctrl+V reads the application clipboard, and says so when there is none or it holds nothing; Ctrl+X cuts the selection as Markdown,
/// through OSC 52 and the clipboard, and says so with no selection. The right-click menu has Cut and Paste. Typing and Backspace over a selection
/// across blocks, and the table cell's typing and Enter, are edits of the engine here too.
/// </summary>
public sealed partial class MainWindowTests
{
    // The window of the paste tests: wide enough for the longest message on the message line.
    private const int PasteWidth = 100;
    private const int PasteHeight = 12;

    // A paragraph of three words, for a paste into its middle and a selection of its middle word.
    private const string Sentence = "Alpha beta gamma.\n";

    // Two paragraphs, for a selection that crosses the blank line between them.
    private const string TwoParagraphs = "First paragraph.\n\nSecond paragraph.\n";

    // A list of two items, for a paste into an item's text.
    private const string Bullets = "- one\n- two\n";

    // A paragraph with a code block of two lines after it, for a selection that starts on the second line and so is refused.
    private const string WithCode = "Before.\n\n```\nfirst\nsecond\n```\n\nAfter.\n";

    [Fact]
    public void A_bracketed_paste_of_one_line_is_written_as_Markdown_source_at_the_caret()
    {
        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (_, window) => window.View.PlaceCaret(Index(Sentence, "beta"), null),
            (app, _) => app.RaisePasteEvent("**big** "));

        // The text is not escaped: its asterisks are written as they are, so the paste renders as the Markdown it is.
        Assert.Equal("Alpha **big** beta gamma.\n", session.Window.Editor.Buffer.Text);
        Assert.Equal(Index("Alpha **big** beta gamma.\n", "beta"), session.Window.View.CursorOffset);
    }

    [Fact]
    public void A_bracketed_paste_of_several_lines_writes_each_line_and_does_not_press_Enter()
    {
        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (_, window) => window.View.PlaceCaret(Index(Sentence, "beta"), null),
            (app, _) => app.RaisePasteEvent("one\ntwo "));

        // Each line is a line of the same paragraph; an Enter would have split the paragraph in two.
        Assert.Equal("Alpha one\ntwo beta gamma.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_bracketed_paste_of_several_lines_into_a_list_item_keeps_the_item_and_indents_the_lines()
    {
        Session session = Run(
            Opened("notes.md", Bullets),
            PasteWidth,
            PasteHeight,
            (_, window) => window.View.PlaceCaret(Index(Bullets, "two"), null),
            (app, _) => app.RaisePasteEvent("x\ny "));

        Assert.Equal("- one\n- x\n  y two\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_paste_over_a_selection_replaces_it()
    {
        int beta = Index(Sentence, "beta");

        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (_, window) => window.View.SelectRange(new SourceRange(beta, beta + 4)),
            (app, _) => app.RaisePasteEvent("delta"));

        Assert.Equal("Alpha delta gamma.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_multi_line_paste_is_one_undo_step()
    {
        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (_, window) => window.View.PlaceCaret(Index(Sentence, "beta"), null),
            (app, _) => app.RaisePasteEvent("one\ntwo "),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        Assert.Equal(Sentence, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void A_paste_ends_the_typing_run_so_undo_takes_the_paste_and_not_the_typing_before_it()
    {
        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (_, window) => window.View.PlaceCaret(Index(Sentence, "beta"), null),
            (_, window) => window.NewKeyDownEvent(new Key('q')),
            (app, _) => app.RaisePasteEvent("Z"),
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        // Without the paste's own step, the typed Z would have continued the run of the typed q, and one undo would have taken both.
        Assert.Equal("Alpha qbeta gamma.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Ctrl_V_pastes_the_clipboard_text_at_the_caret()
    {
        var clipboard = new FakeClipboard();
        clipboard.SetClipboardData("gamma ");

        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (app, window) =>
            {
                app.Driver!.Clipboard = clipboard;
                window.View.PlaceCaret(Index(Sentence, "beta"), null);
            },
            (_, window) => window.NewKeyDownEvent(Key.V.WithCtrl));

        Assert.Equal("Alpha gamma beta gamma.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Ctrl_V_without_a_clipboard_says_it_is_not_available_here_and_changes_nothing()
    {
        var clipboard = new FakeClipboard(isSupportedAlwaysFalse: true);

        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (app, window) =>
            {
                app.Driver!.Clipboard = clipboard;
                window.View.PlaceCaret(Index(Sentence, "beta"), null);
            },
            (_, window) => window.NewKeyDownEvent(Key.V.WithCtrl));

        Assert.Equal(Sentence, session.Window.Editor.Buffer.Text);
        Assert.Equal(MainWindow.ClipboardUnavailable, session.Screens[^1].Text(10));
    }

    [Fact]
    public void Ctrl_V_with_an_empty_clipboard_says_there_is_nothing_to_paste()
    {
        var clipboard = new FakeClipboard();

        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (app, window) =>
            {
                app.Driver!.Clipboard = clipboard;
                window.View.PlaceCaret(Index(Sentence, "beta"), null);
            },
            (_, window) => window.NewKeyDownEvent(Key.V.WithCtrl));

        Assert.Equal(Sentence, session.Window.Editor.Buffer.Text);
        Assert.Equal(MainWindow.NothingToPaste, session.Screens[^1].Text(10));
    }

    [Fact]
    public void Ctrl_X_cuts_the_selection_as_Markdown_writes_it_with_OSC_52_and_deletes_it()
    {
        const string strong = "Some **bold text** here.\n";
        var written = new List<string>();
        int bold = Index(strong, "bold");

        Session session = Run(
            Opened("notes.md", strong),
            PasteWidth,
            PasteHeight,
            (_, window) =>
            {
                window.WriteRaw = written.Add;
                window.View.SelectRange(new SourceRange(bold, bold + 6));
            },
            (_, window) => window.NewKeyDownEvent(Key.X.WithCtrl));

        // The selection's Markdown has the delimiters of the strong text it cuts from, and the rest of the span stays strong.
        Assert.Equal(new[] { Osc52.Build("**bold t**") }, written);
        Assert.Equal("Some **ext** here.\n", session.Window.Editor.Buffer.Text);
        Assert.Equal(MainWindow.CutDone, session.Screens[^1].Text(10));
    }

    [Fact]
    public void Ctrl_X_then_Ctrl_Z_restores_the_cut_text_in_one_step()
    {
        const string strong = "Some **bold text** here.\n";
        int bold = Index(strong, "bold");

        string cut = string.Empty;
        Session session = Run(
            Opened("notes.md", strong),
            PasteWidth,
            PasteHeight,
            (_, window) =>
            {
                window.WriteRaw = _ => { };
                window.View.SelectRange(new SourceRange(bold, bold + 6));
            },
            (_, window) => window.NewKeyDownEvent(Key.X.WithCtrl),
            (_, window) => cut = window.Editor.Buffer.Text,
            (_, window) => window.NewKeyDownEvent(Key.Z.WithCtrl));

        Assert.Equal("Some **ext** here.\n", cut);
        Assert.Equal(strong, session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Ctrl_X_with_nothing_selected_does_nothing_and_says_so()
    {
        var written = new List<string>();

        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (_, window) =>
            {
                window.WriteRaw = written.Add;
                window.View.PlaceCaret(Index(Sentence, "beta"), null);
            },
            (_, window) => window.NewKeyDownEvent(Key.X.WithCtrl));

        Assert.Empty(written);
        Assert.Equal(Sentence, session.Window.Editor.Buffer.Text);
        Assert.Equal(MainWindow.NothingSelected, session.Screens[^1].Text(10));
    }

    [Fact]
    public void A_cut_that_is_refused_copies_nothing_and_deletes_nothing()
    {
        var written = new List<string>();
        int code = Index(WithCode, "second");

        // The selection starts on the code block's second line, which is not its first stop, so the block would be cut in two and the deletion is refused.
        Session session = Run(
            Opened("notes.md", WithCode),
            PasteWidth,
            PasteHeight,
            (_, window) =>
            {
                window.WriteRaw = written.Add;
                window.View.SelectRange(new SourceRange(code, Index(WithCode, "After")));
            },
            (_, window) => window.NewKeyDownEvent(Key.X.WithCtrl));

        Assert.Empty(written);
        Assert.Equal(WithCode, session.Window.Editor.Buffer.Text);
        Assert.Equal(EditMessages.WholeOnly, session.Screens[^1].Text(10));
    }

    [Fact]
    public void Ctrl_C_writes_the_text_to_the_application_clipboard_so_Ctrl_V_pastes_it_back()
    {
        var clipboard = new FakeClipboard();
        int beta = Index(Sentence, "beta");

        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (app, window) =>
            {
                app.Driver!.Clipboard = clipboard;
                window.WriteRaw = _ => { };
                window.View.SelectRange(new SourceRange(beta, beta + 4));
            },
            (_, window) => window.NewKeyDownEvent(Key.C.WithCtrl),
            (_, window) => window.View.PlaceCaret(Index(window.Editor.Buffer.Text, "gamma"), null),
            (_, window) => window.NewKeyDownEvent(Key.V.WithCtrl));

        Assert.Equal("beta", clipboard.GetClipboardData());
        Assert.Equal("Alpha beta betagamma.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Ctrl_X_then_Ctrl_V_pastes_the_cut_Markdown_back_in_the_same_session()
    {
        var clipboard = new FakeClipboard();
        int beta = Index(Sentence, "beta");

        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (app, window) =>
            {
                app.Driver!.Clipboard = clipboard;
                window.WriteRaw = _ => { };
                window.View.SelectRange(new SourceRange(beta, beta + 4));
            },
            (_, window) => window.NewKeyDownEvent(Key.X.WithCtrl),
            (_, window) => window.View.PlaceCaret(Index(window.Editor.Buffer.Text, "gamma"), null),
            (_, window) => window.NewKeyDownEvent(Key.V.WithCtrl));

        Assert.Equal("Alpha  betagamma.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void The_right_click_menu_has_Cut_over_a_selection_and_Paste_when_the_clipboard_holds_text()
    {
        var clipboard = new FakeClipboard();
        clipboard.SetClipboardData("pasted");
        int beta = Index(Sentence, "beta");

        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (app, window) =>
            {
                app.Driver!.Clipboard = clipboard;
                window.View.SelectRange(new SourceRange(beta, beta + 4));
            },
            (_, window) => window.NewKeyDownEvent(PopoverMenu.DefaultKey));

        string text = AllText(session.Screens[^1]);
        Assert.Contains("Cut", text, StringComparison.Ordinal);
        Assert.Contains("Paste", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_right_click_menu_has_neither_Cut_nor_Paste_without_a_selection_or_clipboard_text()
    {
        var clipboard = new FakeClipboard();

        Session session = Run(
            Opened("notes.md", Sentence),
            PasteWidth,
            PasteHeight,
            (app, window) =>
            {
                app.Driver!.Clipboard = clipboard;
                window.View.PlaceCaret(Index(Sentence, "beta"), null);
            },
            (_, window) => window.NewKeyDownEvent(PopoverMenu.DefaultKey));

        string text = AllText(session.Screens[^1]);
        Assert.Contains("Copy text", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Cut", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Paste", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Typing_over_a_selection_across_blocks_replaces_it_and_joins_the_blocks()
    {
        int start = Index(TwoParagraphs, "paragraph");
        int end = Index(TwoParagraphs, "Second") + "Sec".Length;

        Session session = Run(
            Opened("notes.md", TwoParagraphs),
            PasteWidth,
            PasteHeight,
            (_, window) => window.View.SelectRange(new SourceRange(start, end)),
            (_, window) => window.NewKeyDownEvent(new Key('X')));

        // The selection starts in the first paragraph and ends in the second: the two keep their text outside it, joined, with the typed text between.
        Assert.Equal("First Xond paragraph.\n", session.Window.Editor.Buffer.Text);
    }

    [Fact]
    public void Backspace_over_a_selection_across_blocks_deletes_it_and_joins_the_blocks()
    {
        int start = Index(TwoParagraphs, "paragraph");
        int end = Index(TwoParagraphs, "Second") + "Sec".Length;

        Session session = Run(
            Opened("notes.md", TwoParagraphs),
            PasteWidth,
            PasteHeight,
            (_, window) => window.View.SelectRange(new SourceRange(start, end)),
            (_, window) => window.NewKeyDownEvent(Key.Backspace));

        Assert.Equal("First ond paragraph.\n", session.Window.Editor.Buffer.Text);
    }
}
