using Logvu.Tui;
using Shared.Tui;
using Terminal.Gui.Input;

namespace Logvu.Tests;

/// <summary>
/// The key hints of logvu as data: the texts are the ones the status line has always shown, each label's span performs its key, and the
/// spans stay inside the text. The builders are tested with the shared hints (in Shared.Tui.Tests).
/// </summary>
public sealed class LogvuHintsTests
{
    [Fact]
    public void The_texts_are_the_hints_the_status_line_has_always_shown()
    {
        Assert.Equal("f filter  -/+ level  Space pause/follow  c copy event  End newest  ^O open  ^Q quit", LogvuHints.Timeline.Text);
        Assert.Equal("f filter  -/+ level  Space pause/follow  c copy selection  End newest  ^O open  ^Q quit", LogvuHints.TimelineSelection.Text);
        Assert.Equal("f filter on value  Enter toggle  z folds  r raw  c copy line  ←/Esc back", LogvuHints.Detail.Text);
        Assert.Equal("f filter on value  Enter toggle  z folds  r raw  c copy selection  ←/Esc back", LogvuHints.DetailSelection.Text);
        Assert.Equal("n older  N newer", SearchMessages.TimelineKeys.Text);
        Assert.Equal("n next  N prev", SearchMessages.DetailKeys.Text);
    }

    [Fact]
    public void The_controller_strings_are_the_texts_of_the_data()
    {
        Assert.Equal(LogvuHints.Timeline.Text, TimelineController.Hints);
        Assert.Equal(LogvuHints.TimelineSelection.Text, TimelineController.SelectionHints);
        Assert.Equal(LogvuHints.Detail.Text, TimelineController.DetailHints);
        Assert.Equal(LogvuHints.DetailSelection.Text, TimelineController.DetailSelectionHints);
    }

    [Fact]
    public void Minus_and_plus_are_clickable_and_the_level_word_and_the_slash_are_not()
    {
        Hint hint = LogvuHints.Timeline;

        Assert.Equal((Key)'-', KeyAt(hint, "-/+"));
        Assert.Equal((Key)'+', KeyAt(hint, "+ level"));
        Assert.Null(KeyAt(hint, "/+"));
        Assert.Null(KeyAt(hint, "level"));
    }

    [Theory]
    [InlineData("f filter", "f")]
    [InlineData("Space pause", "Space")]
    [InlineData("c copy event", "c")]
    [InlineData("End newest", "End")]
    [InlineData("^O open", "^O")]
    [InlineData("^Q quit", "^Q")]
    public void Each_label_performs_its_key(string label, string key)
    {
        Key expected = key switch
        {
            "^O" => Key.O.WithCtrl,
            "^Q" => Key.Q.WithCtrl,
            "Space" => Key.Space,
            "End" => Key.End,
            _ => (Key)key[0],
        };

        Assert.Equal(expected, KeyAt(LogvuHints.Timeline, label));
    }

    [Fact]
    public void The_whole_label_of_a_detail_hint_performs_its_key_and_the_escape_hint_is_esc()
    {
        Hint hint = LogvuHints.Detail;

        Assert.Equal((Key)'f', KeyAt(hint, "f filter on value"));
        Assert.Equal(Key.Enter, KeyAt(hint, "Enter toggle"));
        Assert.Equal((Key)'z', KeyAt(hint, "z folds"));
        Assert.Equal((Key)'r', KeyAt(hint, "r raw"));
        Assert.Equal((Key)'c', KeyAt(hint, "c copy line"));
        Assert.Equal(Key.Esc, KeyAt(hint, "←/Esc back"));
        Assert.Equal(Key.Esc, KeyAt(hint, "←"));
    }

    [Fact]
    public void The_search_hints_perform_n_and_shift_n()
    {
        Assert.Equal((Key)'n', KeyAt(SearchMessages.TimelineKeys, "n older"));
        Assert.Equal((Key)'N', KeyAt(SearchMessages.TimelineKeys, "N newer"));
        Assert.Equal((Key)'n', KeyAt(SearchMessages.DetailKeys, "n next"));
        Assert.Equal((Key)'N', KeyAt(SearchMessages.DetailKeys, "N prev"));
    }

    [Fact]
    public void Joined_search_and_timeline_hints_keep_their_targets_at_their_places_in_the_joined_text()
    {
        Hint joined = KeyHints.Join(SearchMessages.TimelineKeys, LogvuHints.Timeline);

        Assert.Equal("n older  N newer  " + LogvuHints.Timeline.Text, joined.Text);
        Assert.Equal((Key)'n', KeyAt(joined, "n older"));
        Assert.Equal((Key)'f', KeyAt(joined, "f filter"));
        Assert.Equal(Key.Q.WithCtrl, KeyAt(joined, "^Q quit"));
    }

    [Fact]
    public void Every_hint_has_its_targets_inside_its_text_and_not_overlapping()
    {
        Hint[] all = [LogvuHints.Timeline, LogvuHints.TimelineSelection, LogvuHints.Detail, LogvuHints.DetailSelection, SearchMessages.TimelineKeys, SearchMessages.DetailKeys];

        foreach (Hint hint in all)
        {
            int end = 0;
            foreach (HintTarget target in hint.Targets)
            {
                Assert.True(target.Start >= end, hint.Text);
                Assert.True(target.Length > 0 && target.Start + target.Length <= hint.Text.Length, hint.Text);
                end = target.Start + target.Length;
            }
        }
    }

    /// <summary>The key of the target that covers the first character of <paramref name="label"/> in the hint's text, or <c>null</c>.</summary>
    private static Key? KeyAt(Hint hint, string label)
    {
        int index = hint.Text.IndexOf(label, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{label}' is not in '{hint.Text}'");
        foreach (HintTarget target in hint.Targets)
        {
            if (index >= target.Start && index < target.Start + target.Length)
            {
                return target.Key;
            }
        }

        return null;
    }
}
