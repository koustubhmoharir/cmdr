using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class EnvironmentOverlayTests
{
    [Fact]
    public void Apply_NonNullValue_SetsNewKey()
    {
        var target = new Dictionary<string, string?> { ["PATH"] = "/bin" };
        var overlay = new Dictionary<string, string?> { ["FORCE_COLOR"] = "1" };

        EnvironmentOverlay.Apply(target, overlay);

        Assert.Equal("1", target["FORCE_COLOR"]);
        Assert.Equal("/bin", target["PATH"]);
    }

    [Fact]
    public void Apply_NonNullValue_OverwritesExistingKey()
    {
        var target = new Dictionary<string, string?> { ["MODE"] = "old" };
        var overlay = new Dictionary<string, string?> { ["MODE"] = "new" };

        EnvironmentOverlay.Apply(target, overlay);

        Assert.Equal("new", target["MODE"]);
    }

    [Fact]
    public void Apply_NullValue_RemovesExistingKey()
    {
        var target = new Dictionary<string, string?> { ["SOME_VAR"] = "x", ["KEEP"] = "y" };
        var overlay = new Dictionary<string, string?> { ["SOME_VAR"] = null };

        EnvironmentOverlay.Apply(target, overlay);

        Assert.False(target.ContainsKey("SOME_VAR"));
        Assert.Equal("y", target["KEEP"]);
    }

    [Fact]
    public void Apply_NullValue_MissingKey_IsNoOp()
    {
        var target = new Dictionary<string, string?> { ["KEEP"] = "y" };
        var overlay = new Dictionary<string, string?> { ["NOT_THERE"] = null };

        EnvironmentOverlay.Apply(target, overlay);

        Assert.False(target.ContainsKey("NOT_THERE"));
        Assert.Single(target);
    }

    [Fact]
    public void Apply_EmptyOverlay_LeavesTargetUnchanged()
    {
        var target = new Dictionary<string, string?> { ["A"] = "1" };

        EnvironmentOverlay.Apply(target, new Dictionary<string, string?>());

        Assert.Equal("1", target["A"]);
        Assert.Single(target);
    }
}
