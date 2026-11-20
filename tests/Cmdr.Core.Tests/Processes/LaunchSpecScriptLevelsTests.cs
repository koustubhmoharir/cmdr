using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;

namespace Cmdr.Core.Tests.Processes;

public class LaunchSpecScriptLevelsTests
{
    private static GroupSpec Group(string name, Dictionary<string, string?>? env = null, string? cwd = null) =>
        new() { Name = name, Env = env, Cwd = cwd };

    [Fact]
    public void GroupScriptValues_LiteralLevelComesBeforeTheGroupEnv_AndTheGroupEnvOverridesIt()
    {
        using var dir = new TempDir();
        GroupSpec group = Group("g", new Dictionary<string, string?>
        {
            ["X"] = "${env:X}-group",
            ["FROM_SCRIPT"] = "${env:S}",
        });
        var produced = new Dictionary<string, string?> { ["X"] = "script", ["S"] = "${env:NOT_EXPANDED}" };
        var cmd = new CommandSpec { Name = "a", Program = "p" };

        LaunchSpec spec = LaunchSpec.From(
            new[] { group },
            cmd,
            dir.Path,
            Array.Empty<EnvLevel>(),
            new IReadOnlyDictionary<string, string?>?[] { produced });

        // Literal, the group's env, then the command's own (empty) env.
        Assert.Equal(3, spec.EnvLevels.Count);
        Assert.True(spec.EnvLevels[0].Literal);
        Assert.False(spec.EnvLevels[1].Literal);

        var target = new Dictionary<string, string?>();
        EnvExpansion.ApplyLevels(target, spec.EnvLevels, spec.ConfigDir, ':', inputs: null);

        Assert.Equal("script-group", target["X"]);
        Assert.Equal("${env:NOT_EXPANDED}", target["FROM_SCRIPT"]);
    }

    [Fact]
    public void GroupScriptValues_LiteralLevelUsesTheGroupsEffectiveWorkingDirectory()
    {
        using var dir = new TempDir();
        GroupSpec outer = Group("outer", cwd: "sub");
        GroupSpec inner = Group("inner", new Dictionary<string, string?> { ["E"] = "1" });
        var produced = new Dictionary<string, string?> { ["P"] = "1" };
        var cmd = new CommandSpec { Name = "a", Program = "p" };

        LaunchSpec spec = LaunchSpec.From(
            new[] { outer, inner },
            cmd,
            dir.Path,
            Array.Empty<EnvLevel>(),
            new IReadOnlyDictionary<string, string?>?[] { null, produced });

        // The outer group has no values and no env, so the literal level is the first one, with the inner group's values.
        Assert.Equal(3, spec.EnvLevels.Count);
        Assert.True(spec.EnvLevels[0].Literal);
        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "sub"), spec.EnvLevels[0].Cwd);
        Assert.Equal(spec.EnvLevels[0].Cwd, spec.EnvLevels[1].Cwd);
    }

    [Fact]
    public void GroupScriptValues_ANullEntry_AddsNoLevel()
    {
        using var dir = new TempDir();
        GroupSpec group = Group("g", new Dictionary<string, string?> { ["E"] = "1" });
        var cmd = new CommandSpec { Name = "a", Program = "p" };

        LaunchSpec spec = LaunchSpec.From(
            new[] { group },
            cmd,
            dir.Path,
            Array.Empty<EnvLevel>(),
            new IReadOnlyDictionary<string, string?>?[] { null });

        Assert.Equal(2, spec.EnvLevels.Count);
        Assert.False(spec.EnvLevels[0].Literal);
        Assert.Equal("1", spec.EnvLevels[0].Env["E"]);
    }

    [Fact]
    public void GroupScriptValues_WithoutValues_LevelsAreUnchanged()
    {
        using var dir = new TempDir();
        GroupSpec group = Group("g", new Dictionary<string, string?> { ["E"] = "1" });
        var cmd = new CommandSpec { Name = "a", Program = "p" };

        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path, Array.Empty<EnvLevel>());

        Assert.Equal(2, spec.EnvLevels.Count);
        Assert.All(spec.EnvLevels, level => Assert.False(level.Literal));
        Assert.Equal("1", spec.EnvLevels[0].Env["E"]);
    }

    [Fact]
    public void GroupScriptValues_MustHaveOneEntryPerAncestor()
    {
        using var dir = new TempDir();
        var cmd = new CommandSpec { Name = "a", Program = "p" };

        Assert.Throws<ArgumentException>(() => LaunchSpec.From(
            new[] { Group("g") },
            cmd,
            dir.Path,
            Array.Empty<EnvLevel>(),
            Array.Empty<IReadOnlyDictionary<string, string?>?>()));
    }

    [Fact]
    public void CopyLevels_PreservesLiteral()
    {
        using var dir = new TempDir();
        var literal = new EnvLevel(new Dictionary<string, string?> { ["L"] = "${cwd}" }, dir.Path) { Literal = true };
        var cmd = new CommandSpec { Name = "a", Program = "p" };

        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), cmd, dir.Path, new[] { literal });

        EnvLevel copied = Assert.Single(spec.EnvLevels, level => level.Env.ContainsKey("L"));
        Assert.True(copied.Literal);
        Assert.False(ReferenceEquals(copied, literal));
    }

    [Fact]
    public void ForEnvScript_KeepsLiteralLevels()
    {
        using var dir = new TempDir();
        var literal = new EnvLevel(new Dictionary<string, string?> { ["L"] = "v" }, dir.Path) { Literal = true };
        var script = new EnvScriptSpec { Program = "p" };

        LaunchSpec spec = LaunchSpec.ForEnvScript(script, new[] { literal }, dir.Path, dir.Path, new Dictionary<string, string>());

        EnvLevel level = Assert.Single(spec.EnvLevels);
        Assert.True(level.Literal);
        Assert.Equal("v", level.Env["L"]);
    }
}
