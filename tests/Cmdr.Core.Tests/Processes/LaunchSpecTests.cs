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

        LaunchSpec console = LaunchSpec.From(Array.Empty<GroupSpec>(), new CommandSpec { Name = "a", Program = "p", Stdin = StdinMode.Console }, dir.Path, Array.Empty<EnvLevel>());
        LaunchSpec pipe = LaunchSpec.From(Array.Empty<GroupSpec>(), new CommandSpec { Name = "a", Program = "p" }, dir.Path, Array.Empty<EnvLevel>());

        Assert.Equal(StdinMode.Console, console.Stdin);
        Assert.Equal(StdinMode.Pipe, pipe.Stdin);
    }

    [Fact]
    public void From_NoGroups_UsesConfigDirAsWorkingDirectory()
    {
        using var dir = new TempDir();

        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), new CommandSpec { Name = "a", Program = "p" }, dir.Path, Array.Empty<EnvLevel>());

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

        var spec = LaunchSpec.From(new[] { outer, inner, deepest }, cmd, configDir, Array.Empty<EnvLevel>());

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

        LaunchSpec spec = LaunchSpec.From(groups, cmd, dir.Path, Array.Empty<EnvLevel>());

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

        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path, Array.Empty<EnvLevel>());
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

        LaunchSpec spec = LaunchSpec.From(groups, new CommandSpec { Name = "run", Cwd = "x" }, dir.Path, Array.Empty<EnvLevel>());

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

        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path, Array.Empty<EnvLevel>());
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
        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), new CommandSpec { Name = "a", Program = "p" }, dir.Path, Array.Empty<EnvLevel>());
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
        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), new CommandSpec { Name = "a", Program = "p" }, dir.Path, Array.Empty<EnvLevel>()) with
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
        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path, Array.Empty<EnvLevel>());
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
        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), cmd, dir.Path, Array.Empty<EnvLevel>());

        string[] args = spec.ExpandArgs(_ => null, '/', inputs: null);

        Assert.Equal(new[] { "--db=${input:db}", Path.GetFullPath(dir.Path) }, args);
    }

    [Fact]
    public void ExpandArgs_MissingInput_Throws()
    {
        using var dir = new TempDir();
        var cmd = new CommandSpec { Name = "c", Program = "p", Args = { "${input:x}" } };
        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), cmd, dir.Path, Array.Empty<EnvLevel>());

        Assert.Throws<InvalidOperationException>(() => spec.ExpandArgs(_ => null, '/', new Dictionary<string, string>()));
    }

    [Fact]
    public void From_FileLevelsComeBeforeTheGroupsAndTheCommand()
    {
        using var dir = new TempDir();
        var file = new EnvLevel(new Dictionary<string, string?> { ["FILE"] = "1" }, dir.Path);
        var group = new GroupSpec { Name = "g", Env = new Dictionary<string, string?> { ["GROUP"] = "2" } };
        var cmd = new CommandSpec { Name = "c", Program = "p", Env = new Dictionary<string, string?> { ["OWN"] = "3" } };

        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path, new[] { file });

        Assert.Equal(3, spec.EnvLevels.Count);
        Assert.Equal(new[] { "FILE" }, spec.EnvLevels[0].Env.Keys);
        Assert.Equal(new[] { "GROUP" }, spec.EnvLevels[1].Env.Keys);
        Assert.Equal(new[] { "OWN" }, spec.EnvLevels[2].Env.Keys);
    }

    [Fact]
    public void From_NoFileLevels_LeavesOnlyGroupAndCommandLevels()
    {
        using var dir = new TempDir();
        var group = new GroupSpec { Name = "g", Env = new Dictionary<string, string?> { ["GROUP"] = "2" } };
        var cmd = new CommandSpec { Name = "c", Program = "p" };

        LaunchSpec spec = LaunchSpec.From(new[] { group }, cmd, dir.Path, Array.Empty<EnvLevel>());

        Assert.Equal(2, spec.EnvLevels.Count);
    }

    [Fact]
    public void Constructor_CopiesImportFromOfEachLevelDeeply()
    {
        using var dir = new TempDir();
        var inner = new EnvLevel(new Dictionary<string, string?> { ["X"] = "deep" }, dir.Path);
        var importLevel = new EnvLevel(new Dictionary<string, string?> { ["X"] = null }, dir.Path) { ImportFrom = new[] { inner } };
        var levels = new List<EnvLevel> { importLevel };
        var spec = new LaunchSpec("p", Array.Empty<string>(), dir.Path, levels, dir.Path, StopMethod.CtrlC, null);

        levels.Clear();
        EnvLevel copied = Assert.Single(spec.EnvLevels);
        Assert.NotNull(copied.ImportFrom);
        Assert.NotSame(importLevel.ImportFrom, copied.ImportFrom);
        EnvLevel copiedInner = Assert.Single(copied.ImportFrom);
        Assert.NotSame(inner, copiedInner);
        Assert.Equal("deep", copiedInner.Env["X"]);
        Assert.Null(copied.Env["X"]);
    }

    [Fact]
    public void From_FileLevelWithImport_KeepsTheImportInTheLaunch()
    {
        using var dir = new TempDir();
        var baseLevel = new EnvLevel(new Dictionary<string, string?> { ["X"] = "base" }, dir.Path);
        var fileLevel = new EnvLevel(new Dictionary<string, string?> { ["X"] = null }, dir.Path) { ImportFrom = new[] { baseLevel } };
        var cmd = new CommandSpec { Name = "c", Program = "p" };

        LaunchSpec spec = LaunchSpec.From(Array.Empty<GroupSpec>(), cmd, dir.Path, new[] { fileLevel });

        EnvLevel copied = spec.EnvLevels[0];
        Assert.Equal("base", Assert.Single(copied.ImportFrom!).Env["X"]);
    }

    [Fact]
    public void ForEnvScript_ResolvesCwdAgainstLevelCwd_AndDefaultsToIt()
    {
        using var dir = new TempDir();
        string levelCwd = Path.Combine(dir.Path, "base");
        var inputs = new Dictionary<string, string>();

        LaunchSpec relative = LaunchSpec.ForEnvScript(new EnvScriptSpec { Program = "p", Cwd = "sub" }, Array.Empty<EnvLevel>(), levelCwd, dir.Path, inputs);
        LaunchSpec defaulted = LaunchSpec.ForEnvScript(new EnvScriptSpec { Program = "p" }, Array.Empty<EnvLevel>(), levelCwd, dir.Path, inputs);

        Assert.Equal(Path.GetFullPath(Path.Combine(levelCwd, "sub")), relative.WorkingDirectory);
        Assert.Equal(Path.GetFullPath(levelCwd), defaulted.WorkingDirectory);
    }

    [Fact]
    public void ForEnvScript_CarriesLevelsInputsConfigDirProgramAndArgs()
    {
        using var dir = new TempDir();
        var levels = new[] { new EnvLevel(new Dictionary<string, string?> { ["X"] = "1" }, dir.Path) };
        var inputs = new Dictionary<string, string> { ["name"] = "typed" };
        var script = new EnvScriptSpec { Program = "prog", Args = { "a", "${input:name}" } };

        LaunchSpec spec = LaunchSpec.ForEnvScript(script, levels, dir.Path, dir.Path, inputs);

        Assert.Equal("prog", spec.Program);
        Assert.Equal(new[] { "a", "${input:name}" }, spec.Args);
        Assert.Equal(Path.GetFullPath(dir.Path), spec.ConfigDir);
        EnvLevel level = Assert.Single(spec.EnvLevels);
        Assert.Equal("1", level.Env["X"]);
        Assert.Equal("typed", spec.Inputs["name"]);
    }

    [Fact]
    public void ForEnvScript_UsesAStdinPipe_CtrlCStop_AndTracksLeftovers()
    {
        using var dir = new TempDir();

        LaunchSpec spec = LaunchSpec.ForEnvScript(new EnvScriptSpec { Program = "p" }, Array.Empty<EnvLevel>(), dir.Path, dir.Path, new Dictionary<string, string>());

        Assert.Equal(StdinMode.Pipe, spec.Stdin);
        Assert.Equal(StopMethod.CtrlC, spec.StopMethod);
        Assert.Null(spec.StopInput);
        Assert.Equal(LeftoverMode.Track, spec.Leftovers);
        Assert.Null(spec.Ready);
    }

    [Fact]
    public void ForEnvScript_CwdPlaceholderIsTheScriptWorkingDirectory()
    {
        using var dir = new TempDir();
        string levelCwd = Path.Combine(dir.Path, "base");
        var script = new EnvScriptSpec { Program = "p", Cwd = "sub", Args = { "${cwd}" } };

        LaunchSpec spec = LaunchSpec.ForEnvScript(script, Array.Empty<EnvLevel>(), levelCwd, dir.Path, new Dictionary<string, string>());

        string[] expanded = spec.ExpandArgs(_ => null, Path.PathSeparator, null);
        Assert.Equal(Path.GetFullPath(Path.Combine(levelCwd, "sub")), expanded[0]);
    }

    [Fact]
    public void ForEnvScript_RequiresLevelCwdAndConfigDir()
    {
        var script = new EnvScriptSpec { Program = "p" };
        var inputs = new Dictionary<string, string>();

        Assert.Throws<ArgumentException>(() => LaunchSpec.ForEnvScript(script, Array.Empty<EnvLevel>(), "", "/c", inputs));
        Assert.Throws<ArgumentException>(() => LaunchSpec.ForEnvScript(script, Array.Empty<EnvLevel>(), "/w", "", inputs));
    }
}
