using Mdedit.Core.Render;
using Mdedit.Tui;
using Terminal.Gui.Input;

namespace Mdedit.Tests;

/// <summary>
/// The rendered view's word moves: Ctrl+Right to the start of the next word, Ctrl+Left to the start of this word or the one before, a pause at a
/// paragraph's end, and Shift with them to select. Punctuation is a word of its own and spaces are skipped.
/// </summary>
public sealed partial class RenderedViewTests
{
    // "Hello" 0–4, "," 5, "world" 7–11, "foo" 13–15, "." 16, the paragraph's end 17, "Next" from 19.
    private const string Words = "Hello, world foo.\n\nNext para.\n";

    [Fact]
    public void Ctrl_Right_goes_from_word_to_word_and_pauses_at_the_paragraphs_end()
    {
        RenderedView view = View(Words);

        Result result = Run(
            view,
            v => v.HandleKey(Key.CursorRight.WithCtrl),
            v => v.HandleKey(Key.CursorRight.WithCtrl),
            v => v.HandleKey(Key.CursorRight.WithCtrl),
            v => v.HandleKey(Key.CursorRight.WithCtrl),
            v => v.HandleKey(Key.CursorRight.WithCtrl),
            v => v.HandleKey(Key.CursorRight.WithCtrl));

        Assert.Equal([5, 7, 13, 16, 17, 19], result.Snaps.Select(snap => snap.Offset));
    }

    [Fact]
    public void Ctrl_Left_goes_back_to_the_start_of_each_word_and_pauses_at_the_previous_paragraphs_end()
    {
        RenderedView view = View(Words);

        Result result = Run(
            view,
            v => v.PlaceCaret(Offset(Words, "Next"), null),
            v => v.HandleKey(Key.CursorLeft.WithCtrl),
            v => v.HandleKey(Key.CursorLeft.WithCtrl),
            v => v.HandleKey(Key.CursorLeft.WithCtrl),
            v => v.HandleKey(Key.CursorLeft.WithCtrl),
            v => v.HandleKey(Key.CursorLeft.WithCtrl),
            v => v.HandleKey(Key.CursorLeft.WithCtrl),
            v => v.HandleKey(Key.CursorLeft.WithCtrl));

        Assert.Equal([19, 17, 16, 13, 7, 5, 0, 0], result.Snaps.Select(snap => snap.Offset));
    }

    [Fact]
    public void Ctrl_Left_from_inside_a_word_goes_to_its_start()
    {
        RenderedView view = View(Words);

        Result result = Run(
            view,
            v => v.PlaceCaret(Offset(Words, "world") + 3, null),
            v => v.HandleKey(Key.CursorLeft.WithCtrl));

        Assert.Equal(Offset(Words, "world"), result.Snaps[^1].Offset);
    }

    [Fact]
    public void Shift_Ctrl_Right_selects_to_the_start_of_the_next_word()
    {
        RenderedView view = View(Words);

        Run(
            view,
            v => v.HandleKey(Key.CursorRight.WithCtrl.WithShift),
            v => v.HandleKey(Key.CursorRight.WithCtrl.WithShift));

        Assert.Equal(new SourceRange(0, 7), view.SelectionRange);
    }
}
