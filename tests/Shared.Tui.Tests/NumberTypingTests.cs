using Shared.Tui;

namespace Shared.Tui.Tests;

public sealed class NumberTypingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_single_digit_selects_its_row()
    {
        var typing = new NumberTyping();

        Assert.Equal(0, typing.Type('1', T0, 12));
    }

    [Fact]
    public void Two_quick_digits_form_a_number()
    {
        var typing = new NumberTyping();

        typing.Type('1', T0, 12);

        Assert.Equal(11, typing.Type('2', T0.AddMilliseconds(100), 12));
    }

    [Fact]
    public void A_number_out_of_range_starts_again_with_the_last_digit()
    {
        var typing = new NumberTyping();

        typing.Type('1', T0, 12);

        Assert.Equal(2, typing.Type('3', T0.AddMilliseconds(100), 12));
    }

    [Fact]
    public void A_pause_longer_than_the_timeout_starts_a_new_number()
    {
        var typing = new NumberTyping();

        typing.Type('1', T0, 12);

        Assert.Equal(1, typing.Type('2', T0.AddSeconds(1.5), 12));
    }

    [Fact]
    public void A_gap_of_exactly_the_timeout_still_continues_the_number()
    {
        var typing = new NumberTyping();

        typing.Type('1', T0, 12);

        Assert.Equal(11, typing.Type('2', T0 + NumberTyping.Timeout, 12));
    }

    [Fact]
    public void A_gap_just_over_the_timeout_starts_a_new_number()
    {
        var typing = new NumberTyping();

        typing.Type('1', T0, 12);

        Assert.Equal(1, typing.Type('2', T0 + NumberTyping.Timeout + TimeSpan.FromMilliseconds(1), 12));
    }

    [Fact]
    public void Zero_alone_selects_nothing()
    {
        var typing = new NumberTyping();

        Assert.Null(typing.Type('0', T0, 12));
    }

    [Fact]
    public void Zero_is_not_kept_as_a_leading_digit()
    {
        var typing = new NumberTyping();

        typing.Type('0', T0, 12);

        Assert.Equal(0, typing.Type('1', T0, 12));
    }

    [Fact]
    public void A_zero_after_a_digit_makes_a_two_digit_number_when_in_range()
    {
        var typing = new NumberTyping();

        typing.Type('1', T0, 12);

        Assert.Equal(9, typing.Type('0', T0.AddMilliseconds(100), 12));
    }

    [Fact]
    public void A_digit_beyond_the_count_selects_nothing()
    {
        var typing = new NumberTyping();

        Assert.Null(typing.Type('7', T0, 5));
    }

    [Fact]
    public void Reset_forgets_the_digits_typed_so_far()
    {
        var typing = new NumberTyping();

        typing.Type('1', T0, 12);
        typing.Reset();

        Assert.Equal(1, typing.Type('2', T0.AddMilliseconds(100), 12));
    }

    [Fact]
    public void A_non_digit_is_rejected()
    {
        var typing = new NumberTyping();

        Assert.Throws<ArgumentOutOfRangeException>(() => typing.Type('a', T0, 12));
    }
}
