namespace Shared.Processes.Tests;

public class LaunchSpecTests
{
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
    public void ForScript_UsesAStdinPipe_CtrlCStop_AndTracksLeftovers()
    {
        using var dir = new TempDir();

        LaunchSpec spec = LaunchSpec.ForScript("p", Array.Empty<string>(), null, Array.Empty<EnvLevel>(), dir.Path, dir.Path, new Dictionary<string, string>());

        Assert.Equal(StdinMode.Pipe, spec.Stdin);
        Assert.Equal(StopMethod.CtrlC, spec.StopMethod);
        Assert.Null(spec.StopInput);
        Assert.Equal(LeftoverMode.Track, spec.Leftovers);
        Assert.Null(spec.Ready);
    }
}
