using Shared.Tui;
using Terminal.Gui.Input;

namespace Shared.Tui.Tests;

/// <summary>
/// The key hint builders: a bound hint performs its key over its whole text, a plain hint has no targets, and joining and concatenating
/// keep each part's targets at its place in the text.
/// </summary>
public sealed class KeyHintsTests
{
    [Fact]
    public void A_bound_hint_performs_its_key_over_its_whole_text()
    {
        Hint hint = KeyHints.Bound(Key.F5, "F5 Restart");

        Assert.Equal("F5 Restart", hint.Text);
        Assert.Equal(new[] { new HintTarget(0, 10, Key.F5) }, hint.Targets);
    }

    [Fact]
    public void A_plain_hint_has_no_targets()
    {
        Hint hint = KeyHints.Plain("Esc Close");

        Assert.Equal("Esc Close", hint.Text);
        Assert.Empty(hint.Targets);
    }

    [Fact]
    public void Join_separates_the_hints_with_the_separator_and_the_separator_has_no_target()
    {
        Hint hint = KeyHints.Join(KeyHints.Bound(Key.F5, "F5 Restart"), KeyHints.Bound(Key.Space, "Space Mark"));

        Assert.Equal("F5 Restart  Space Mark", hint.Text);
        Assert.Equal(new[] { new HintTarget(0, 10, Key.F5), new HintTarget(12, 10, Key.Space) }, hint.Targets);
    }

    [Fact]
    public void Join_and_Concat_of_nothing_are_empty()
    {
        Assert.Equal(string.Empty, KeyHints.Join().Text);
        Assert.Empty(KeyHints.Join().Targets);
        Assert.Equal(string.Empty, KeyHints.Concat().Text);
        Assert.Empty(KeyHints.Concat().Targets);
    }

    [Fact]
    public void Concat_moves_each_part_s_targets_past_the_text_before_it()
    {
        Hint hint = KeyHints.Concat(KeyHints.Bound('a', "ab"), KeyHints.Plain("c"), KeyHints.Bound('d', "d"));

        Assert.Equal("abcd", hint.Text);
        Assert.Equal(new[] { new HintTarget(0, 2, (Key)'a'), new HintTarget(3, 1, (Key)'d') }, hint.Targets);
    }

    [Fact]
    public void Concat_splits_a_label_into_bound_and_plain_text()
    {
        Hint hint = KeyHints.Concat(KeyHints.Bound((Key)'-', "-"), KeyHints.Plain("/"), KeyHints.Bound((Key)'+', "+"), KeyHints.Plain(" level"));

        Assert.Equal("-/+ level", hint.Text);
        Assert.Equal(new[] { new HintTarget(0, 1, (Key)'-'), new HintTarget(2, 1, (Key)'+') }, hint.Targets);
    }
}
