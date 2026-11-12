using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class FocusCycleTests
{
    [Theory]
    [InlineData(0, false, 1)]
    [InlineData(1, false, 2)]
    [InlineData(2, false, 0)]
    [InlineData(0, true, 2)]
    [InlineData(1, true, 0)]
    [InlineData(2, true, 1)]
    public void Tab_moves_forward_and_shift_tab_backward_through_three_panes(int current, bool backward, int expected)
    {
        Assert.Equal(expected, FocusCycle.Next(current, 3, backward));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(-1, true)]
    [InlineData(7, false)]
    public void No_focused_pane_gives_the_first(int current, bool backward)
    {
        Assert.Equal(0, FocusCycle.Next(current, 3, backward));
    }

    [Fact]
    public void No_panes_gives_none()
    {
        Assert.Equal(-1, FocusCycle.Next(0, 0, backward: false));
    }
}
