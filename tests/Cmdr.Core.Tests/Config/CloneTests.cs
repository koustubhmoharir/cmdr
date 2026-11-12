using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class CloneTests
{
    [Fact]
    public void Clone_CopiesAllValues()
    {
        var original = new CommandSpec
        {
            Name = "web",
            Description = "desc",
            Program = "pnpm",
            Args = { "a", "b" },
            Cwd = "../web",
            Env = new Dictionary<string, string?> { ["X"] = "1", ["Y"] = null },
            Stop = new StopSpec { Method = StopMethod.Input, Input = "q" },
            Log = new LogSpec { Enabled = true, Path = "w.log" },
            Leftovers = LeftoverMode.Detach,
        };

        CommandSpec clone = original.Clone();

        Assert.Equal("web", clone.Name);
        Assert.Equal("desc", clone.Description);
        Assert.Equal("pnpm", clone.Program);
        Assert.Equal(new[] { "a", "b" }, clone.Args);
        Assert.Equal("../web", clone.Cwd);
        Assert.Equal("1", clone.Env["X"]);
        Assert.Null(clone.Env["Y"]);
        Assert.Equal(StopMethod.Input, clone.Stop.Method);
        Assert.Equal("q", clone.Stop.Input);
        Assert.True(clone.Log.Enabled);
        Assert.Equal("w.log", clone.Log.Path);
        Assert.Equal(LeftoverMode.Detach, clone.Leftovers);
    }

    [Fact]
    public void Clone_IsDeep()
    {
        var original = new CommandSpec
        {
            Name = "web",
            Program = "pnpm",
            Args = { "a" },
            Env = new Dictionary<string, string?> { ["X"] = "1" },
            Stop = new StopSpec { Method = StopMethod.CtrlC },
            Log = new LogSpec { Enabled = false, Path = "w.log" },
        };

        CommandSpec clone = original.Clone();
        clone.Args.Add("b");
        clone.Env["X"] = "changed";
        clone.Env["NEW"] = "2";
        clone.Stop.Method = StopMethod.Input;
        clone.Stop.Input = "q";
        clone.Log.Enabled = true;
        clone.Log.Path = "other.log";

        Assert.Equal(new[] { "a" }, original.Args);
        Assert.Equal("1", original.Env["X"]);
        Assert.False(original.Env.ContainsKey("NEW"));
        Assert.Equal(StopMethod.CtrlC, original.Stop.Method);
        Assert.Null(original.Stop.Input);
        Assert.False(original.Log.Enabled);
        Assert.Equal("w.log", original.Log.Path);
    }

    [Fact]
    public void Clone_IsDeep_ForReverseMutation()
    {
        var original = new CommandSpec { Name = "web", Program = "pnpm" };
        CommandSpec clone = original.Clone();

        original.Args.Add("x");
        original.Env["Z"] = "1";
        original.Stop.Method = StopMethod.Input;
        original.Log.Enabled = true;

        Assert.Empty(clone.Args);
        Assert.Empty(clone.Env);
        Assert.Equal(StopMethod.CtrlC, clone.Stop.Method);
        Assert.False(clone.Log.Enabled);
    }
}
