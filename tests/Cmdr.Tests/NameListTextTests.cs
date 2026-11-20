using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>The comma-separated name field (secret and baseEnv names): parsing, formatting, the read-only rule and the configuration's name errors.</summary>
public sealed class NameListTextTests
{
    [Fact]
    public void Parse_splits_on_commas_trims_each_name_and_drops_blank_entries()
    {
        Assert.Equal(new[] { "A", "B", "C" }, NameListText.Parse(" A, B ,,C ,"));
        Assert.Empty(NameListText.Parse("  ,  "));
    }

    [Fact]
    public void Format_joins_with_commas_and_null_is_empty_text()
    {
        Assert.Equal("A, B", NameListText.Format(new[] { "A", "B" }));
        Assert.Equal(string.Empty, NameListText.Format(null));
        Assert.Equal(string.Empty, NameListText.Format(Array.Empty<string>()));
    }

    [Fact]
    public void Names_that_the_comma_form_can_hold_are_editable()
    {
        Assert.True(NameListText.CanEdit(new[] { "A", "B" }));
        Assert.True(NameListText.CanEdit(new[] { "A B" }));
        Assert.True(NameListText.CanEdit(new[] { "A", "A" }));
        Assert.True(NameListText.CanEdit(Array.Empty<string>()));
    }

    [Fact]
    public void Null_is_editable()
    {
        Assert.True(NameListText.CanEdit(null));
    }

    [Theory]
    [InlineData("A,B")]
    [InlineData(" A")]
    [InlineData("A ")]
    [InlineData("")]
    [InlineData("A\nB")]
    [InlineData("A\r")]
    public void A_name_the_comma_form_cannot_hold_makes_the_field_read_only(string name)
    {
        Assert.False(NameListText.CanEdit(new[] { "GOOD", name }));
    }

    [Fact]
    public void NamesOrNull_is_null_for_no_names_and_a_copy_otherwise()
    {
        Assert.Null(NameListText.NamesOrNull(Array.Empty<string>()));

        var names = new List<string> { "TOKEN" };
        List<string>? copy = NameListText.NamesOrNull(names);
        names.Add("OTHER");

        Assert.Equal(new[] { "TOKEN" }, copy);
    }

    [Fact]
    public void Errors_follow_the_configuration_rules_for_names()
    {
        Assert.Empty(NameListText.Errors("TOKEN, OTHER", "Secret"));

        Assert.Single(NameListText.Errors("A=B", "Secret"));
        IReadOnlyList<string> duplicate = NameListText.Errors("A, A", "Secret");
        Assert.Single(duplicate);
        Assert.StartsWith("Secret:", duplicate[0], StringComparison.Ordinal);
    }
}
