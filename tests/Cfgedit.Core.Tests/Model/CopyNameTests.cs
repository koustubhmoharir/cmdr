using Cfgedit.Core.Model;

namespace Cfgedit.Core.Tests.Model;

/// <summary>EditLayout.CopyName and NextName: the name of a copy, the same in every format.</summary>
public sealed class CopyNameTests
{
    [Theory]
    [InlineData("port", "port2")]
    [InlineData("server1", "server2")]
    [InlineData("a09", "a10")]
    [InlineData("a007", "a008")]
    [InlineData("a99", "a100")]
    [InlineData("9", "10")]
    [InlineData("999", "1000")]
    [InlineData("0", "1")]
    [InlineData("", "2")]
    [InlineData("v1x", "v1x2")]
    [InlineData("a٣", "a٣2")]
    public void NextName_steps_a_trailing_ASCII_number_or_appends_two(string name, string expected)
    {
        Assert.Equal(expected, EditLayout.NextName(name));
    }

    [Fact]
    public void A_long_digit_run_is_incremented_without_overflow()
    {
        string nines = new('9', 30);
        string zeros = new('0', 30);

        Assert.Equal("x1" + zeros, EditLayout.NextName("x" + nines));
        Assert.Equal("1" + zeros, EditLayout.NextName(nines));
    }

    [Fact]
    public void A_copy_takes_the_first_free_step()
    {
        string name = EditLayout.CopyName("port", candidate => candidate is "port2" or "port3");

        Assert.Equal("port4", name);
    }

    [Fact]
    public void A_copy_of_a_numbered_name_skips_taken_numbers()
    {
        string name = EditLayout.CopyName("a1", candidate => candidate == "a2");

        Assert.Equal("a3", name);
    }

    [Fact]
    public void A_copy_of_a_name_with_no_taken_candidate_is_the_first_step()
    {
        Assert.Equal("port2", EditLayout.CopyName("port", _ => false));
    }
}
