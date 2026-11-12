using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class DetailsScrollTests
{
    [Theory]
    [InlineData(0, 11, 4, 0)]
    [InlineData(-3, 11, 4, 0)]
    [InlineData(3, 11, 4, 3)]
    [InlineData(7, 11, 4, 7)]
    [InlineData(8, 11, 4, 7)]
    [InlineData(50, 11, 4, 7)]
    [InlineData(2, 3, 4, 0)]
    [InlineData(5, 0, 4, 0)]
    public void Offset_is_clamped_so_the_last_page_is_full(int offset, int total, int height, int expected)
    {
        Assert.Equal(expected, DetailsText.ClampOffset(offset, total, height));
    }

    [Fact]
    public void Title_is_the_path_when_everything_fits()
    {
        Assert.Equal("go-apps / nav / run", DetailsText.FrameTitle("go-apps / nav / run", new DetailsScroll(0, 4, 4)));
        Assert.Equal("go-apps / nav / run", DetailsText.FrameTitle("go-apps / nav / run", new DetailsScroll(0, 4, 2)));
    }

    [Fact]
    public void Title_shows_the_range_with_a_down_arrow_at_the_top()
    {
        Assert.Equal(
            "go-apps / nav / run  (1–4 of 11 ↓)",
            DetailsText.FrameTitle("go-apps / nav / run", new DetailsScroll(0, 4, 11)));
    }

    [Fact]
    public void Title_shows_both_arrows_in_the_middle()
    {
        Assert.Equal(
            "go-apps / nav / run  (4–7 of 11 ↕)",
            DetailsText.FrameTitle("go-apps / nav / run", new DetailsScroll(3, 4, 11)));
    }

    [Fact]
    public void Title_shows_an_up_arrow_at_the_bottom()
    {
        Assert.Equal(
            "go-apps / nav / run  (8–11 of 11 ↑)",
            DetailsText.FrameTitle("go-apps / nav / run", new DetailsScroll(7, 4, 11)));
    }

    [Fact]
    public void Title_without_a_path_is_only_the_range()
    {
        Assert.Equal("(1–4 of 11 ↓)", DetailsText.FrameTitle(string.Empty, new DetailsScroll(0, 4, 11)));
    }

    [Fact]
    public void Wrap_applies_to_each_logical_line()
    {
        Assert.Equal(
            new[] { "abc", "def", "g" },
            DetailsText.Wrap(new[] { "abc def", "g" }, 3));
    }

    [Fact]
    public void Description_lines_are_not_capped()
    {
        Assert.Equal(new[] { "a", "b", "c", "d" }, DetailsText.DescriptionLines("a\r\nb\nc\nd"));
        Assert.Equal(new[] { "a", "b" }, DetailsText.DescriptionLines("a\nb\n"));
        Assert.Empty(DetailsText.DescriptionLines(null));
        Assert.Empty(DetailsText.DescriptionLines(string.Empty));
    }
}
