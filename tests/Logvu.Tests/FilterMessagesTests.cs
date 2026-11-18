using Logvu.Tui;

namespace Logvu.Tests;

/// <summary>The filter's status texts: the active description truncated to fit, the scan's megabytes, and the cut of a long text.</summary>
public sealed class FilterMessagesTests
{
    [Fact]
    public void The_active_filter_is_named_and_a_short_description_is_kept_whole()
    {
        Assert.Equal("Filter: level ≥ WARN · text ~ timeout", FilterMessages.Active("level ≥ WARN · text ~ timeout"));
    }

    [Fact]
    public void A_long_description_is_cut_to_the_status_width_with_an_ellipsis()
    {
        string description = new('x', 100);

        string status = FilterMessages.Active(description);

        Assert.Equal("Filter: " + new string('x', FilterMessages.MaxDescriptionLength - 1) + "…", status);
    }

    [Fact]
    public void A_scan_shows_the_megabytes_it_has_read()
    {
        Assert.Equal("Scanning… 0.0 MB", FilterMessages.Scanning(0));
        Assert.Equal("Scanning… 12.0 MB", FilterMessages.Scanning(12L * 1024 * 1024));
    }

    [Theory]
    [InlineData("abc", 3, "abc")]
    [InlineData("abcd", 3, "ab…")]
    [InlineData("", 1, "")]
    public void Truncate_keeps_a_text_that_fits_and_ends_a_longer_one_with_an_ellipsis(string text, int max, string expected)
    {
        Assert.Equal(expected, FilterMessages.Truncate(text, max));
    }
}
