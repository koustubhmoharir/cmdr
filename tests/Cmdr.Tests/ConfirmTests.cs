using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class ConfirmTests
{
    [Theory]
    [InlineData(0, 3, 2)]
    [InlineData(1, 3, 1)]
    [InlineData(2, 3, 0)]
    [InlineData(0, 2, 1)]
    [InlineData(1, 2, 0)]
    [InlineData(0, 1, 0)]
    public void Last_button_is_the_first_choice(int button, int count, int expected)
    {
        Assert.Equal(expected, Confirm.ChoiceIndex(button, count));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Cancelled_dialog_stays_null(int count)
    {
        Assert.Null(Confirm.ChoiceIndex(null, count));
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(3, 3)]
    [InlineData(0, 0)]
    public void Button_outside_the_choices_is_rejected(int button, int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Confirm.ChoiceIndex(button, count));
    }
}
