namespace Shared.Processes.Tests;

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

        EnvExpansion.ApplyLevels(target, new[] { level }, "/cfg", ':', inputs: null);

        Assert.Equal("1:/w/item/cfg", target["X"]);
        Assert.Equal("${env:A}", target["Y"]);
        Assert.Equal("$$5", target["Z"]);
        Assert.Equal("[]", target["U"]);
    }

    [Fact]
    public void ApplyLevels_WithInputs_SubstitutesInputsInEnvValues()
    {
        var target = new Dictionary<string, string?> { ["A"] = "1" };
        var level = new EnvLevel(
            new Dictionary<string, string?> { ["X"] = "${input:v}-${env:A}", ["Y"] = "${input:e?}" },
            "/w");

        EnvExpansion.ApplyLevels(target, new[] { level }, "/cfg", ':', new Dictionary<string, string> { ["v"] = "hi", ["e"] = "" });

        Assert.Equal("hi-1", target["X"]);
        Assert.Equal("", target["Y"]);
    }

    [Fact]
    public void ApplyLevels_NullInputs_KeepsInputPlaceholdersAsWritten()
    {
        var target = new Dictionary<string, string?>();
        var level = new EnvLevel(new Dictionary<string, string?> { ["X"] = "a${input:v}b" }, "/w");

        EnvExpansion.ApplyLevels(target, new[] { level }, "/cfg", ':', inputs: null);

        Assert.Equal("a${input:v}b", target["X"]);
    }

    [Fact]
    public void ApplyLevels_InputValueThatLooksLikeAPlaceholder_IsNotExpandedAgain()
    {
        var target = new Dictionary<string, string?> { ["A"] = "secret" };
        var level = new EnvLevel(new Dictionary<string, string?> { ["X"] = "${input:v}" }, "/w");

        EnvExpansion.ApplyLevels(target, new[] { level }, "/cfg", ':', new Dictionary<string, string> { ["v"] = "${env:A}" });

        Assert.Equal("${env:A}", target["X"]);
    }

    [Fact]
    public void ApplyLevels_MissingInput_Throws()
    {
        var target = new Dictionary<string, string?>();
        var level = new EnvLevel(new Dictionary<string, string?> { ["X"] = "${input:v}" }, "/w");

        Assert.Throws<InvalidOperationException>(() =>
            EnvExpansion.ApplyLevels(target, new[] { level }, "/cfg", ':', new Dictionary<string, string>()));
    }
}
