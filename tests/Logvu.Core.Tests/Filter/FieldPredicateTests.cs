using Logvu.Core.Filter;

namespace Logvu.Core.Tests.Filter;

/// <summary>Each field operator compares a value with the predicate's value, ignoring case.</summary>
public sealed class FieldPredicateTests
{
    [Theory]
    [InlineData(FieldOperator.Equals, "Worker 3", "Worker 3", true)]
    [InlineData(FieldOperator.Equals, "worker 3", "WORKER 3", true)]
    [InlineData(FieldOperator.Equals, "Worker 3", "Worker", false)]
    [InlineData(FieldOperator.Equals, "Worker", "Worker 3", false)]
    [InlineData(FieldOperator.Equals, "", "", true)]
    [InlineData(FieldOperator.Equals, "", "x", false)]
    [InlineData(FieldOperator.Prefix, "Worker 3", "work", true)]
    [InlineData(FieldOperator.Prefix, "Worker 3", "WORKER 3", true)]
    [InlineData(FieldOperator.Prefix, "Worker 3", "ker", false)]
    [InlineData(FieldOperator.Prefix, "Worker", "Worker 3", false)]
    [InlineData(FieldOperator.Prefix, "Worker 3", "", true)]
    [InlineData(FieldOperator.Prefix, "", "", true)]
    [InlineData(FieldOperator.Suffix, "Worker 3", " 3", true)]
    [InlineData(FieldOperator.Suffix, "Worker 3", "WORKER 3", true)]
    [InlineData(FieldOperator.Suffix, "Worker 3", "worker 3", true)]
    [InlineData(FieldOperator.Suffix, "Worker 3", "ker", false)]
    [InlineData(FieldOperator.Suffix, "Worker 3", "", true)]
    [InlineData(FieldOperator.Suffix, "", "", true)]
    [InlineData(FieldOperator.Contains, "Orders.Engine", "ENGINE", true)]
    [InlineData(FieldOperator.Contains, "Orders.Engine", "rders.e", true)]
    [InlineData(FieldOperator.Contains, "Orders.Engine", "Payments", false)]
    [InlineData(FieldOperator.Contains, "Orders.Engine", "", true)]
    [InlineData(FieldOperator.Contains, "", "", true)]
    [InlineData(FieldOperator.Contains, "", "x", false)]
    public void Each_operator_compares_the_value_ignoring_case(FieldOperator op, string value, string predicateValue, bool expected)
    {
        var predicate = new FieldPredicate("thread", op, predicateValue);

        Assert.Equal(expected, predicate.Matches(value));
    }

    [Fact]
    public void An_empty_value_matches_every_value_for_the_other_operators_and_only_an_empty_one_for_equals()
    {
        Assert.True(new FieldPredicate("a", FieldOperator.Prefix, string.Empty).Matches("anything"));
        Assert.True(new FieldPredicate("a", FieldOperator.Suffix, string.Empty).Matches("anything"));
        Assert.True(new FieldPredicate("a", FieldOperator.Contains, string.Empty).Matches("anything"));
        Assert.False(new FieldPredicate("a", FieldOperator.Equals, string.Empty).Matches("anything"));
    }

    [Fact]
    public void A_value_is_never_matched_by_a_longer_predicate_value()
    {
        var predicate = new FieldPredicate("a", FieldOperator.Contains, "abcdef");

        Assert.False(predicate.Matches("abc"));
    }

    [Fact]
    public void The_path_is_not_compared_by_Matches()
    {
        Assert.True(new FieldPredicate("Thread", FieldOperator.Equals, "x").Matches("x"));
    }

    [Fact]
    public void Predicates_with_the_same_parts_are_equal_and_the_operator_is_one_of_them()
    {
        Assert.Equal(new FieldPredicate("a", FieldOperator.Prefix, "b"), new FieldPredicate("a", FieldOperator.Prefix, "b"));
        Assert.NotEqual(new FieldPredicate("a", FieldOperator.Prefix, "b"), new FieldPredicate("a", FieldOperator.Suffix, "b"));
    }

    [Fact]
    public void A_null_value_is_an_error()
    {
        Assert.Throws<ArgumentNullException>(() => new FieldPredicate("a", FieldOperator.Equals, "b").Matches(null!));
    }
}
