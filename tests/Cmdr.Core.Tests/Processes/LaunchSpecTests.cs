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

    [Fact]
    public void Constructor_CopiesArgsLevelsAndReady_SoCallerEditsDoNotReachTheSpec()
    {
        var args = new List<string> { "a" };
        var env = new Dictionary<string, string?> { ["X"] = "1" };
        var levels = new List<EnvLevel> { new(env, "/w") };
        var ready = new ReadySpec { Pattern = "up" };

        var spec = new LaunchSpec("p", args, "/w", levels, "/w", StopMethod.CtrlC, null, Ready: ready);
        args.Add("b");
        env["X"] = "2";
        env["Y"] = "3";
        levels.Add(new EnvLevel(new Dictionary<string, string?>(), "/v"));
        ready.Pattern = "changed";

        Assert.Equal(new[] { "a" }, spec.Args);
        Assert.Single(spec.EnvLevels);
        Assert.Equal("1", spec.EnvLevels[0].Env["X"]);
        Assert.False(spec.EnvLevels[0].Env.ContainsKey("Y"));
        Assert.Equal("up", spec.Ready!.Pattern);
        Assert.IsNotType<List<string>>(spec.Args);
    }

    [Fact]
    public void With_CopiesTheReplacedCollections()
    {
        var spec = new LaunchSpec("p", new[] { "a" }, "/w", Array.Empty<EnvLevel>(), "/w", StopMethod.CtrlC, null);
        var args = new List<string> { "b" };
        var ready = new ReadySpec { Pattern = "up" };

        LaunchSpec changed = spec with { Args = args, Ready = ready };
        args.Add("c");
        ready.Pattern = "changed";

        Assert.Equal(new[] { "b" }, changed.Args);
        Assert.Equal("up", changed.Ready!.Pattern);
    }

    [Fact]
    public void From_LaterConfigEditsDoNotReachTheSpec()
    {
        using var dir = new TempDir();
        var group = new GroupSpec { Name = "g", Env = new Dictionary<string, string?> { ["G"] = "1" } };
        var cmd = new CommandSpec { Name = "a", Program = "p", Args = new List<string> { "x" }, Env = new Dictionary<string, string?> { ["C"] = "1" }, Ready = new ReadySpec { Pattern = "up" } };

        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path);
        group.Env["G"] = "2";
        cmd.Env["C"] = "2";
        cmd.Args.Add("y");
        cmd.Ready.Pattern = "changed";

        Assert.Equal("1", spec.EnvLevels[0].Env["G"]);
        Assert.Equal("1", spec.EnvLevels[1].Env["C"]);
        Assert.Equal(new[] { "x" }, spec.Args);
        Assert.Equal("up", spec.Ready!.Pattern);
    }

    [Fact]
    public void Inputs_AreAPrivateCopy_AndEmptyByDefault()
    {
        using var dir = new TempDir();
        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), new CommandSpec { Name = "a", Program = "p" }, dir.Path);
        Assert.Empty(spec.Inputs);

        var values = new Dictionary<string, string> { ["x"] = "1" };
        LaunchSpec withInputs = spec with { Inputs = values };
        values["x"] = "changed";
        values["y"] = "2";

        Assert.Equal("1", withInputs.Inputs["x"]);
        Assert.False(withInputs.Inputs.ContainsKey("y"));
    }

    [Fact]
    public void Inputs_AreNotInToString()
    {
        using var dir = new TempDir();
        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), new CommandSpec { Name = "a", Program = "p" }, dir.Path) with
        {
            Inputs = new Dictionary<string, string> { ["secret"] = "hunter2" },
        };

        string text = spec.ToString();

        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandArgs_ExpandsEnvCwdConfigDirPathsepEscapeAndInputs()
    {
        using var dir = new TempDir();
        var group = new GroupSpec { Name = "g", Cwd = "sub", Env = new Dictionary<string, string?> { ["FOO"] = "group" } };
        var cmd = new CommandSpec
        {
            Name = "c",
            Program = "p",
            Args =
            {
                "${env:FOO}", "${cwd}", "${configDir}", "a${pathsep}b", "$${env:FOO}", "${input:x}", "${input:y?}", "${env:GONE}",
            },
        };
        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path);
        var environment = new Dictionary<string, string?> { ["FOO"] = "looked-up" };
        var inputs = new Dictionary<string, string> { ["x"] = "in", ["y"] = "" };

        string[] args = spec.ExpandArgs(name => environment.TryGetValue(name, out string? value) ? value : null, ':', inputs);

        Assert.Equal(
            new[]
            {
                "looked-up",
                Path.Combine(Path.GetFullPath(dir.Path), "sub"),
                Path.GetFullPath(dir.Path),
                "a:b",
                "${env:FOO}",
                "in",
                "",
                "",
            },
            args);
    }

    [Fact]
    public void ExpandArgs_NullInputs_KeepsInputPlaceholdersAsWritten()
    {
        using var dir = new TempDir();
        var cmd = new CommandSpec { Name = "c", Program = "p", Args = { "--db=${input:db}", "${cwd}" } };
        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), cmd, dir.Path);

        string[] args = spec.ExpandArgs(_ => null, '/', inputs: null);

        Assert.Equal(new[] { "--db=${input:db}", Path.GetFullPath(dir.Path) }, args);
    }

    [Fact]
    public void ExpandArgs_MissingInput_Throws()
    {
        using var dir = new TempDir();
        var cmd = new CommandSpec { Name = "c", Program = "p", Args = { "${input:x}" } };
        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), cmd, dir.Path);

        Assert.Throws<InvalidOperationException>(() => spec.ExpandArgs(_ => null, '/', new Dictionary<string, string>()));
    }
}
