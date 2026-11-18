namespace Logvu.Core.Tests;

public sealed class LogvuCoreTests
{
    // Logvu.Core has no code yet. This keeps the test project running, and checks that the shared library it builds on resolves.
    [Fact]
    public void Shared_core_is_referenced()
    {
        Assert.Equal(1, Shared.Core.DisplayWidth.DefaultGraphemeWidth("a"));
    }
}
