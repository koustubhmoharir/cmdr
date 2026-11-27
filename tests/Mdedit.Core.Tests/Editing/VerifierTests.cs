using Mdedit.Core.Editing;
using Mdedit.Core.Render;

namespace Mdedit.Core.Tests.Editing;

/// <summary>The edit verifier on its own: the structure, the other blocks, and the place and text of the edit.</summary>
public sealed class VerifierTests
{
    [Fact]
    public void An_insertion_at_its_place_holds()
    {
        Assert.True(Holds("ab", "axb", 0, string.Empty, ["x"], end: 2));
    }

    [Fact]
    public void An_insertion_reported_at_another_place_does_not_hold()
    {
        Assert.False(Holds("ab", "axb", 0, string.Empty, ["x"], end: 3));
    }

    [Fact]
    public void A_removal_holds_only_when_the_removed_text_is_the_text_that_went()
    {
        Assert.True(Holds("ab", "a", 0, "b", [string.Empty], end: 1));
        Assert.False(Holds("ab", "a", 0, "a", [string.Empty], end: 0));
    }

    [Fact]
    public void A_change_to_another_block_does_not_hold()
    {
        Assert.False(Holds("a\n\nb", "a\n\nc", 0, string.Empty, ["x"], end: 1));
    }

    [Fact]
    public void A_change_of_kind_does_not_hold()
    {
        Assert.False(Holds("a", "# a", 0, string.Empty, [string.Empty], end: 2));
    }

    [Fact]
    public void A_change_of_containers_does_not_hold()
    {
        Assert.False(Holds("- a", "a", 0, string.Empty, [string.Empty], end: 0));
    }

    [Fact]
    public void A_change_in_the_number_of_blocks_does_not_hold()
    {
        Assert.False(Holds("a", "a\n\nb", 0, string.Empty, [string.Empty], end: 1));
    }

    [Fact]
    public void A_space_that_does_not_show_holds_only_as_nothing()
    {
        Assert.True(Holds("ab", "ab ", 0, string.Empty, [string.Empty], end: 3));
        Assert.False(Holds("ab", "ab ", 0, string.Empty, [" "], end: 3));
    }

    private static bool Holds(string before, string after, int blockIndex, string removed, string[] inserted, int end) =>
        EditVerifier.Holds(RenderDocument.Parse(before), RenderDocument.Parse(after), blockIndex, removed, inserted, end);
}
