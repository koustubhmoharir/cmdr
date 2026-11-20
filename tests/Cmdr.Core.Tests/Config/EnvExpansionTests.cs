using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

/// <summary>What the env levels make of the placeholder syntax; the expansion rules themselves are in <see cref="PlaceholdersTests"/>.</summary>
public class EnvExpansionTests
{
    [Fact]
    public void ApplyLevels_ValuesUseThePlaceholderSyntax()
    {
        var target = new Dictionary<string, string?> { ["A"] = "1" };
        var level = new EnvLevel(
            new Dictionary<string, string?>
            {
                ["X"] = "${env:A}${pathsep}${cwd}${configDir}",
                ["Y"] = "$${env:A}",
                ["Z"] = "$$5",
                ["U"] = "[${env:NOPE}]",
            },
            "/w/item");

        EnvExpansion.ApplyLevels(target, new[] { level }, "/cfg", ':');

        Assert.Equal("1:/w/item/cfg", target["X"]);
        Assert.Equal("${env:A}", target["Y"]);
        Assert.Equal("$$5", target["Z"]);
        Assert.Equal("[]", target["U"]);
    }
}
