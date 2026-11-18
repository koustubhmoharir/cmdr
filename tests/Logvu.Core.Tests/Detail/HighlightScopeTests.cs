using Logvu.Core.Detail;

namespace Logvu.Core.Tests.Detail;

public sealed class HighlightScopeTests
{
    [Theory]
    [InlineData("message", "message", true)]
    [InlineData("err.stack", "err.stack", true)]
    [InlineData("err.stack", "err.stack[0]", true)]
    [InlineData("err.stack", "err.stack[12]", true)]
    [InlineData("err.stack", "err.stack[0].x", false)]
    [InlineData("err.stack", "err.stacks[0]", false)]
    [InlineData("err.stack", "err.stack[]", false)]
    [InlineData("err.stack", "err.stack[a]", false)]
    [InlineData("err.stack[0]", "err.stack", false)]
    [InlineData("cause", "cause[1]", true)]
    [InlineData("Properties", "Properties.SourceContext", false)]
    public void A_rule_covers_its_path_and_the_elements_of_an_array_at_that_path(string field, string scope, bool expected) =>
        Assert.Equal(expected, HighlightApplier.ScopeMatches(field, scope));
}
