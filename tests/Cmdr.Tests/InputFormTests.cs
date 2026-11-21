using Cmdr.Tui;
using Shared.Processes;

namespace Cmdr.Tests;

public sealed class InputFormTests
{
    private static readonly InputRequest Required = new("token", false);
    private static readonly InputRequest Optional = new("note", true);

    [Fact]
    public void Required_input_that_is_empty_is_an_error_naming_it()
    {
        IReadOnlyList<string> errors = InputForm.Validate(new[] { Required, Optional }, new[] { string.Empty, string.Empty });

        Assert.Single(errors);
        Assert.Contains("'token'", errors[0]);
    }

    [Fact]
    public void Each_empty_required_input_gets_its_own_error()
    {
        var requests = new[] { new InputRequest("a", false), new InputRequest("b", false) };

        IReadOnlyList<string> errors = InputForm.Validate(requests, new[] { string.Empty, "x" });

        Assert.Single(errors);
        Assert.Contains("'a'", errors[0]);
    }

    [Fact]
    public void Optional_input_may_be_empty()
    {
        Assert.Empty(InputForm.Validate(new[] { Optional }, new[] { string.Empty }));
    }

    [Fact]
    public void Whitespace_counts_as_a_value_for_a_required_input()
    {
        Assert.Empty(InputForm.Validate(new[] { Required }, new[] { "   " }));
    }

    [Fact]
    public void Values_map_names_to_values_in_request_order()
    {
        var requests = new[] { new InputRequest("second", false), new InputRequest("first", true) };

        IReadOnlyDictionary<string, string> values = InputForm.Values(requests, new[] { "2", string.Empty });

        Assert.Equal(new[] { "second", "first" }, values.Keys);
        Assert.Equal("2", values["second"]);
        Assert.Equal(string.Empty, values["first"]);
    }

    [Fact]
    public void Names_are_case_sensitive()
    {
        var requests = new[] { new InputRequest("Token", false), new InputRequest("token", false) };

        IReadOnlyDictionary<string, string> values = InputForm.Values(requests, new[] { "upper", "lower" });

        Assert.Equal("upper", values["Token"]);
        Assert.Equal("lower", values["token"]);
    }

    [Fact]
    public void Mismatched_lengths_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => InputForm.Validate(new[] { Required }, Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => InputForm.Values(new[] { Required }, Array.Empty<string>()));
    }
}
