using Shared.Core;

namespace Shared.Core.Tests.Output;

public class DisplayWidthTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("日", 2)]
    [InlineData("é", 1)]
    [InlineData("́", 0)]
    [InlineData("​", 0)]
    [InlineData("\U0001F600", 2)]
    [InlineData("\U0001F468‍\U0001F469‍\U0001F467", 2)]
    [InlineData("\U0001F1EE\U0001F1F3", 2)]
    [InlineData("❤️", 1)]
    [InlineData("가", 2)]
    [InlineData("­", 1)]
    [InlineData("\t", 1)]
    [InlineData("\u0007", 1)]
    [InlineData("ᅡ", 0)]
    public void DefaultGraphemeWidthOfSingleGrapheme(string grapheme, int expected)
    {
        Assert.Equal(expected, DisplayWidth.DefaultGraphemeWidth(grapheme));
    }

    [Fact]
    public void OfSumsGraphemeWidths()
    {
        Assert.Equal(4, DisplayWidth.Of("éx日"));
        Assert.Equal(4, DisplayWidth.Of("e\u0301x日"));
    }

    [Fact]
    public void OfEmptyIsZero()
    {
        Assert.Equal(0, DisplayWidth.Of(""));
    }

    [Fact]
    public void SettingNullGraphemeWidthThrows()
    {
        Assert.Throws<ArgumentNullException>(() => DisplayWidth.GraphemeWidth = null!);
    }
}
