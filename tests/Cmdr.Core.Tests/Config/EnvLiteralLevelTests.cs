using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

/// <summary>Literal levels (an environment script's output): values are set as they are, never expanded.</summary>
public class EnvLiteralLevelTests
{
    private const string ConfigDir = "/cfg";

    [Fact]
    public void Literal_ValuesAreSetWithoutExpansion()
    {
        var target = new Dictionary<string, string?> { ["KEEP"] = "k" };
        var literal = new EnvLevel(
            new Dictionary<string, string?>
            {
                ["X"] = "${env:KEEP}-${cwd}-${configDir}",
                ["Y"] = "${input:v}",
            },
            "/w")
        {
            Literal = true,
        };

        EnvExpansion.ApplyLevels(target, new[] { literal }, ConfigDir, ':', new Dictionary<string, string> { ["v"] = "V" });

        Assert.Equal("${env:KEEP}-${cwd}-${configDir}", target["X"]);
        Assert.Equal("${input:v}", target["Y"]);
        Assert.Equal("k", target["KEEP"]);
    }

    [Fact]
    public void Literal_NullRemovesTheVariable()
    {
        var target = new Dictionary<string, string?> { ["REMOVE"] = "present", ["KEEP"] = "k" };
        var literal = new EnvLevel(new Dictionary<string, string?> { ["REMOVE"] = null }, "/w") { Literal = true };

        EnvExpansion.ApplyLevels(target, new[] { literal }, ConfigDir, ':', inputs: null);

        Assert.False(target.ContainsKey("REMOVE"));
        Assert.Equal("k", target["KEEP"]);
    }

    [Fact]
    public void Literal_ValueIsReadByLaterExpandingLevels()
    {
        var target = new Dictionary<string, string?>();
        var literal = new EnvLevel(new Dictionary<string, string?> { ["SCRIPT"] = "from-script" }, "/w") { Literal = true };
        var later = new EnvLevel(new Dictionary<string, string?> { ["USES"] = "${env:SCRIPT}!" }, "/w");

        EnvExpansion.ApplyLevels(target, new[] { literal, later }, ConfigDir, ':', inputs: null);

        Assert.Equal("from-script!", target["USES"]);
    }

    [Fact]
    public void Literal_ValueCanBeOverriddenByALaterExpandingLevel()
    {
        var target = new Dictionary<string, string?>();
        var literal = new EnvLevel(new Dictionary<string, string?> { ["X"] = "script" }, "/w") { Literal = true };
        var later = new EnvLevel(new Dictionary<string, string?> { ["X"] = "${env:X}-overridden" }, "/w");

        EnvExpansion.ApplyLevels(target, new[] { literal, later }, ConfigDir, ':', inputs: null);

        Assert.Equal("script-overridden", target["X"]);
    }
}
