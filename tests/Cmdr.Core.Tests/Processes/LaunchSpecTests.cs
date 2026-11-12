using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;

namespace Cmdr.Core.Tests.Processes;

public class LaunchSpecTests
{
    [Fact]
    public void From_CarriesStdinMode_PipeIsTheDefault()
    {
        using var dir = new TempDir();

        LaunchSpec console = LaunchSpec.From(Array.Empty<GroupSpec>(), new CommandSpec { Name = "a", Program = "p", Stdin = StdinMode.Console }, dir.Path);
        LaunchSpec pipe = LaunchSpec.From(Array.Empty<GroupSpec>(), new CommandSpec { Name = "a", Program = "p" }, dir.Path);

        Assert.Equal(StdinMode.Console, console.Stdin);
        Assert.Equal(StdinMode.Pipe, pipe.Stdin);
    }

    [Fact]
    public void From_NoGroups_UsesConfigDirAsWorkingDirectory()
    {
        using var dir = new TempDir();

        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), new CommandSpec { Name = "a", Program = "p" }, dir.Path);

        Assert.Equal(Path.GetFullPath(dir.Path), spec.WorkingDirectory);
        Assert.Equal(Path.GetFullPath(dir.Path), spec.ConfigDir);
        Assert.Single(spec.EnvLevels);
        Assert.Equal(Path.GetFullPath(dir.Path), spec.EnvLevels[0].Cwd);
    }

    [Fact]
    public void From_BuildsLevelsOutermostFirst_WithEffectiveCwdOfEachDefiner()
    {
        using var dir = new TempDir();
        var outer = new GroupSpec { Name = "outer", Cwd = "a", Env = new Dictionary<string, string?> { ["X"] = "1" } };
        var inner = new GroupSpec { Name = "inner", Cwd = "b", Env = null };
        var cmd = new CommandSpec { Name = "run", Cwd = "c", Env = new Dictionary<string, string?> { ["Y"] = "2" } };
        var deepest = new GroupSpec { Name = "deepest", Env = new Dictionary<string, string?> { ["Z"] = null } };
        var configDir = dir.Path;

        var spec = LaunchSpec.From(new[] { outer, inner, deepest }, cmd, configDir);

        string root = Path.GetFullPath(dir.Path);
        string a = Path.Combine(root, "a");
        string ab = Path.Combine(root, "a", "b");
        string abc = Path.Combine(root, "a", "b", "c");

        Assert.Equal(abc, spec.WorkingDirectory);
        Assert.Equal(root, spec.ConfigDir);
        Assert.Equal(3, spec.EnvLevels.Count);

        Assert.Equal(a, spec.EnvLevels[0].Cwd);
        Assert.Equal("1", spec.EnvLevels[0].Env["X"]);

        Assert.Equal(ab, spec.EnvLevels[1].Cwd);
        Assert.True(spec.EnvLevels[1].Env.ContainsKey("Z"));
        Assert.Null(spec.EnvLevels[1].Env["Z"]);
    }

    [Fact]
    public void From_SkipsGroupsWithoutEnv_AndKeepsCommandLevelLast()
    {
        using var dir = new TempDir();
        var groups = new[]
        {
            new GroupSpec { Name = "g1", Env = null },
            new GroupSpec { Name = "g2", Env = new Dictionary<string, string?> { ["G"] = "2" } },
            new GroupSpec { Name = "g3", Env = null },
        };
        var cmd = new CommandSpec { Name = "run", Env = new Dictionary<string, string?> { ["C"] = "3" } };

        LaunchSpec spec = LaunchSpec.From(groups, cmd, dir.Path);

        Assert.Equal(2, spec.EnvLevels.Count);
        Assert.Equal("2", spec.EnvLevels[0].Env["G"]);
        Assert.Equal("3", spec.EnvLevels[1].Env["C"]);
        Assert.Equal(spec.WorkingDirectory, spec.EnvLevels[1].Cwd);
    }

    [Fact]
    public void From_CopiesEnvironmentAndArgs()
    {
        using var dir = new TempDir();
        var group = new GroupSpec { Name = "g", Env = new Dictionary<string, string?> { ["A"] = "1" } };
        var cmd = new CommandSpec { Name = "run", Program = "p", Args = { "x" }, Env = new Dictionary<string, string?> { ["B"] = "2" } };

        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path);
        group.Env["A"] = "changed";
        cmd.Env["B"] = "changed";
        cmd.Args.Add("y");

        Assert.Equal("1", spec.EnvLevels[0].Env["A"]);
        Assert.Equal("2", spec.EnvLevels[1].Env["B"]);
        Assert.Equal(new[] { "x" }, spec.Args);
    }

    [Fact]
    public void From_AbsoluteGroupCwd_ResetsChainForCommand()
    {
        using var dir = new TempDir();
        string absolute = Path.Combine(dir.Path, "root");
        var groups = new[] { new GroupSpec { Name = "outer", Cwd = "ignored" }, new GroupSpec { Name = "inner", Cwd = absolute } };

        LaunchSpec spec = LaunchSpec.From(groups, new CommandSpec { Name = "run", Cwd = "x" }, dir.Path);

        Assert.Equal(Path.GetFullPath(Path.Combine(absolute, "x")), spec.WorkingDirectory);
    }
}
