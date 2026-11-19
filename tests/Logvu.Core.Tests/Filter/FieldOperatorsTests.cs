using Logvu.Core.Filter;

namespace Logvu.Core.Tests.Filter;

/// <summary>The keywords of the field operators: each is the operator's lowercase name, and a keyword is read ignoring case.</summary>
public sealed class FieldOperatorsTests
{
    [Theory]
    [InlineData(FieldOperator.Equals, "equals")]
    [InlineData(FieldOperator.Prefix, "prefix")]
    [InlineData(FieldOperator.Suffix, "suffix")]
    [InlineData(FieldOperator.Contains, "contains")]
    public void Each_operator_has_its_lowercase_name_as_its_keyword(FieldOperator op, string keyword)
    {
        Assert.Equal(keyword, FieldOperators.Keyword(op));
    }

    [Fact]
    public void The_keywords_are_listed_in_the_order_the_operators_are_declared()
    {
        Assert.Equal(["equals", "prefix", "suffix", "contains"], FieldOperators.Keywords);
    }

    [Theory]
    [InlineData("equals", FieldOperator.Equals)]
    [InlineData("Prefix", FieldOperator.Prefix)]
    [InlineData("SUFFIX", FieldOperator.Suffix)]
    [InlineData("contains", FieldOperator.Contains)]
    public void A_keyword_is_read_ignoring_case(string keyword, FieldOperator expected)
    {
        Assert.True(FieldOperators.TryParse(keyword, out FieldOperator op));
        Assert.Equal(expected, op);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" equals")]
    [InlineData("equals ")]
    [InlineData("equal")]
    [InlineData("equals_case")]
    [InlineData("0")]
    [InlineData("=")]
    public void A_text_that_is_not_a_keyword_is_not_read(string keyword)
    {
        Assert.False(FieldOperators.TryParse(keyword, out _));
    }

    [Fact]
    public void Every_operator_round_trips_through_its_keyword()
    {
        foreach (FieldOperator op in Enum.GetValues<FieldOperator>())
        {
            Assert.True(FieldOperators.TryParse(FieldOperators.Keyword(op), out FieldOperator parsed));
            Assert.Equal(op, parsed);
        }
    }

    [Fact]
    public void A_value_that_is_not_an_operator_has_no_keyword()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FieldOperators.Keyword((FieldOperator)99));
    }

    [Fact]
    public void A_null_keyword_is_an_error()
    {
        Assert.Throws<ArgumentNullException>(() => FieldOperators.TryParse(null!, out _));
    }
}
