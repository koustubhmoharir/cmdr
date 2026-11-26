using System.Text;
using Mdedit.Core.Render;
using Mdedit.Tui;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// The copy keys of the window: <c>y</c> and Ctrl+C copy the selection (or the block under the cursor) as plain text, <c>Y</c> as
/// Markdown, through OSC 52. The decoded payloads are compared with the source text, and the messages with their texts.
/// </summary>
public sealed partial class MainWindowTests
{
    // The bold word sits in the second line, so that the Markdown copy has its delimiters to add.
    private const string Copyable = "# Title\n\nSome **bold** text.\n\nSecond paragraph.\n";

    [Fact]
    public void y_with_a_selection_copies_its_plain_text_and_says_so()
    {
        OpenedFile file = Opened("notes.md", Copyable);
        var written = new List<string>();
        int bold = Copyable.IndexOf("bold", StringComparison.Ordinal);

        Session session = Run(
            file,
            40,
            12,
            (_, window) =>
            {
                window.WriteRaw = written.Add;
                window.View.SelectRange(new SourceRange(bold, bold + 4));
            },
            (_, window) => window.NewKeyDownEvent(new Key('y')));

        Assert.Equal(new[] { Osc52.Build("bold") }, written);
        Assert.Equal("bold", Decoded(written[0]));
        Assert.Equal(MainWindow.CopiedText, session.Screens[^1].Text(10));
    }

    [Fact]
    public void Y_with_a_selection_copies_its_Markdown_with_the_delimiters_of_the_strong_text_it_cuts()
    {
        OpenedFile file = Opened("notes.md", Copyable);
        var written = new List<string>();
        int bold = Copyable.IndexOf("bold", StringComparison.Ordinal);

        Session session = Run(
            file,
            40,
            12,
            (_, window) =>
            {
                window.WriteRaw = written.Add;
                window.View.SelectRange(new SourceRange(bold, bold + 4));
            },
            (_, window) => window.NewKeyDownEvent(new Key('Y')));

        Assert.Equal(new[] { Osc52.Build("**bold**") }, written);
        Assert.Equal("**bold**", Decoded(written[0]));
        Assert.Equal(MainWindow.CopiedMarkdown, session.Screens[^1].Text(10));
    }

    [Fact]
    public void y_without_a_selection_copies_the_block_under_the_cursor()
    {
        OpenedFile file = Opened("notes.md", Copyable);
        var written = new List<string>();

        // The cursor starts on the heading; two Down presses reach the last paragraph.
        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.WriteRaw = written.Add,
            (_, window) => window.View.HandleKey(Key.CursorDown),
            (_, window) => window.View.HandleKey(Key.CursorDown),
            (_, window) => window.NewKeyDownEvent(new Key('y')));

        int second = Copyable.IndexOf("Second", StringComparison.Ordinal);
        string block = Copyable.Substring(second, "Second paragraph.".Length);
        Assert.Equal(new[] { Osc52.Build(block) }, written);
        Assert.Equal(MainWindow.CopiedText, session.Screens[^1].Text(10));
    }

    [Fact]
    public void Ctrl_C_copies_like_y()
    {
        OpenedFile file = Opened("notes.md", Copyable);
        var written = new List<string>();
        int bold = Copyable.IndexOf("bold", StringComparison.Ordinal);

        Run(
            file,
            40,
            12,
            (_, window) =>
            {
                window.WriteRaw = written.Add;
                window.View.SelectRange(new SourceRange(bold, bold + 4));
            },
            (_, window) => window.NewKeyDownEvent(Key.C.WithCtrl));

        Assert.Equal(new[] { Osc52.Build("bold") }, written);
    }

    [Fact]
    public void y_after_Shift_End_on_a_paragraph_copies_through_its_last_letter()
    {
        OpenedFile file = Opened("notes.md", Copyable);
        var written = new List<string>();

        // Down twice reaches the last paragraph, Home its first letter, and Shift+End selects to the end of the line.
        Run(
            file,
            40,
            12,
            (_, window) => window.WriteRaw = written.Add,
            (_, window) => window.View.HandleKey(Key.CursorDown),
            (_, window) => window.View.HandleKey(Key.CursorDown),
            (_, window) => window.View.HandleKey(Key.Home),
            (_, window) => window.View.HandleKey(Key.End.WithShift),
            (_, window) => window.NewKeyDownEvent(new Key('y')));

        Assert.Equal(new[] { Osc52.Build("Second paragraph.") }, written);
    }

    [Fact]
    public void A_copy_over_the_limit_is_not_written_and_says_so()
    {
        // One paragraph of a million and a bit characters: its plain text is over Osc52.MaxBytes, so nothing is written.
        string text = new string('a', Osc52.MaxBytes + 1) + "\n";
        OpenedFile file = Opened("big.md", text);
        var written = new List<string>();

        Session session = Run(
            file,
            40,
            12,
            (_, window) => window.WriteRaw = written.Add,
            (_, window) => window.NewKeyDownEvent(new Key('y')));

        Assert.Empty(written);
        Assert.Equal(MainWindow.TooLargeToCopy, session.Screens[^1].Text(10));
    }

    private static string Decoded(string sequence)
    {
        const string Prefix = "\u001b]52;c;";
        Assert.StartsWith(Prefix, sequence, StringComparison.Ordinal);
        string payload = sequence[Prefix.Length..^1];
        return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
    }
}
