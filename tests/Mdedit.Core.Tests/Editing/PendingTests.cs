using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>Typed text that does not show is held as a pending insertion, and is written with the next character.</summary>
public sealed class PendingTests
{
    [Fact]
    public void A_space_at_the_end_of_a_block_is_held_not_written()
    {
        Assert.Equal(new ViewEditResult.Pending(2, " "), Hold("ab", 2, " "));
    }

    [Fact]
    public void A_leading_space_at_the_start_of_a_block_is_held_not_written()
    {
        Assert.Equal(new ViewEditResult.Pending(0, " "), Hold("ab", 0, " "));
    }

    [Fact]
    public void A_second_space_after_a_held_space_at_the_end_is_held_with_it()
    {
        ViewEditResult.Pending first = Hold("ab", 2, " ");

        Assert.Equal(new ViewEditResult.Pending(2, "  "), ViewEdit.Insert(RenderDocument.Parse("ab"), 2, " ", first));
    }

    [Fact]
    public void A_space_in_the_middle_of_a_line_shows_and_is_written()
    {
        Assert.Equal(new EditCase.Outcome("a b", 2), EditCase.Type("ab", 1, " "));
    }

    [Fact]
    public void A_space_after_a_space_in_the_middle_of_a_line_shows_both()
    {
        Assert.Equal(new EditCase.Outcome("a  b", 3), EditCase.Type("a b", 2, " "));
    }

    [Fact]
    public void A_held_space_is_written_with_the_next_character_as_one_edit()
    {
        ViewEditResult.Pending held = Hold("ab", 2, " ");

        Assert.Equal(new EditCase.Outcome("ab c", 4), EditCase.Type("ab", 2, "c", held));
    }

    [Fact]
    public void A_held_leading_space_with_the_next_character_writes_the_character_and_keeps_the_space_in_the_source()
    {
        ViewEditResult.Pending held = Hold("ab", 0, " ");

        Assert.Equal(new EditCase.Outcome(" cab", 2), EditCase.Type("ab", 0, "c", held));
    }

    [Fact]
    public void A_held_space_at_another_offset_is_ignored()
    {
        Assert.Equal(new EditCase.Outcome("acb", 2), EditCase.Type("ab", 1, "c", new ViewEditResult.Pending(2, " ")));
    }

    [Fact]
    public void A_held_space_in_a_heading_end_is_held()
    {
        Assert.Equal(new ViewEditResult.Pending(4, " "), Hold("# ab", 4, " "));
    }

    [Fact]
    public void A_space_that_would_leave_only_whitespace_after_a_removal_is_not_held()
    {
        // Replacing the last letter with a space: the space is written, since the selection's removal is already part of the edit.
        Assert.Equal(new EditCase.Outcome("a ", 2), EditCase.Replace("ab", 1, 2, " "));
    }

    private static ViewEditResult.Pending Hold(string source, int caret, string typed) =>
        ViewEdit.Insert(RenderDocument.Parse(source), caret, typed) is ViewEditResult.Pending pending
            ? pending
            : throw new Xunit.Sdk.XunitException("Expected a pending insertion");
}
