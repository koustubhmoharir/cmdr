using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class ConfigPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveCwd_NullOrBlank_InheritsGroupCwd(string? cwd)
    {
        using var dir = new TempDir();
        var group = new GroupSpec { Name = "g", Cwd = "sub" };
        var cmd = new CommandSpec { Name = "a", Cwd = cwd };

        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "sub")), ConfigPaths.ResolveCwd(new[] { group }, cmd, dir.Path));
    }

    [Fact]
    public void ResolveCwd_NoGroups_NullCwd_IsConfigDir()
    {
        using var dir = new TempDir();

        Assert.Equal(Path.GetFullPath(dir.Path), ConfigPaths.ResolveCwd(Array.Empty<GroupSpec>(), new CommandSpec { Name = "a" }, dir.Path));
    }

    [Fact]
    public void ResolveCwd_RelativeUnderRelative_ChainsThroughEveryLevel()
    {
        using var dir = new TempDir();
        var outer = new GroupSpec { Name = "outer", Cwd = "../golang" };
        var inner = new GroupSpec { Name = "inner", Cwd = "nav" };
        var cmd = new CommandSpec { Name = "run", Cwd = "bin" };

        string expected = Path.GetFullPath(Path.Combine(dir.Path, "..", "golang", "nav", "bin"));
        Assert.Equal(expected, ConfigPaths.ResolveCwd(new[] { outer, inner }, cmd, dir.Path));
    }

    [Fact]
    public void ResolveCwd_AbsoluteGroupCwd_ResetsTheChain()
    {
        using var dir = new TempDir();
        string absolute = Path.Combine(dir.Path, "abs", "..", "root");
        var outer = new GroupSpec { Name = "outer", Cwd = "ignored" };
        var inner = new GroupSpec { Name = "inner", Cwd = absolute };
        var cmd = new CommandSpec { Name = "run", Cwd = "x" };

        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "root", "x")), ConfigPaths.ResolveCwd(new[] { outer, inner }, cmd, dir.Path));
    }

    [Fact]
    public void ResolveCwd_AbsoluteCommandCwd_IsNormalizedAndNotRebased()
    {
        using var dir = new TempDir();
        string absolute = Path.Combine(dir.Path, "abs", "..", "elsewhere");
        var group = new GroupSpec { Name = "g", Cwd = "sub" };

        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "elsewhere")),
            ConfigPaths.ResolveCwd(new[] { group }, new CommandSpec { Name = "a", Cwd = absolute }, dir.Path));
    }

    [Fact]
    public void ResolveGroupCwd_IncludesTheGroupItself()
    {
        using var dir = new TempDir();
        var outer = new GroupSpec { Name = "outer", Cwd = "a" };
        var inner = new GroupSpec { Name = "inner", Cwd = "b" };

        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "a")), ConfigPaths.ResolveGroupCwd(new[] { outer }, dir.Path));
        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "a", "b")), ConfigPaths.ResolveGroupCwd(new[] { outer, inner }, dir.Path));
    }

    [Fact]
    public void ResolveLogPath_ExplicitRelative_ResolvesAgainstLogsDir()
    {
        using var dir = new TempDir();
        var group = new GroupSpec { Name = "servers" };
        var cmd = new CommandSpec { Name = "web", Log = new LogSpec { Enabled = true, Path = Path.Combine("sub", "w.log") } };

        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "logs", "sub", "w.log"),
            ConfigPaths.ResolveLogPath(new[] { group }, cmd, DefaultLogsDir(dir.Path)));
    }

    [Fact]
    public void ResolveLogPath_ExplicitAbsolute_IsUsedAsIs()
    {
        using var dir = new TempDir();
        string absolute = Path.Combine(dir.Path, "elsewhere", "..", "out.log");
        var cmd = new CommandSpec { Name = "web", Log = new LogSpec { Path = absolute } };

        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "out.log")),
            ConfigPaths.ResolveLogPath(new[] { new GroupSpec { Name = "servers" } }, cmd, DefaultLogsDir(dir.Path)));
    }

    [Fact]
    public void ResolveLogPath_DefaultPath_IsOneFolderPerAncestorThenCommand()
    {
        using var dir = new TempDir();
        var ancestors = new[] { new GroupSpec { Name = "go-apps" }, new GroupSpec { Name = "nav" } };
        var cmd = new CommandSpec { Name = "run" };

        Assert.Equal(
            Path.GetFullPath(Path.Combine(DefaultLogsDir(dir.Path), "go-apps", "nav", "run.log")),
            ConfigPaths.ResolveLogPath(ancestors, cmd, DefaultLogsDir(dir.Path)));
    }

    [Fact]
    public void ResolveLogPath_DefaultPath_WithoutGroups_IsDirectlyInLogsDir()
    {
        using var dir = new TempDir();

        Assert.Equal(
            Path.GetFullPath(Path.Combine(DefaultLogsDir(dir.Path), "run.log")),
            ConfigPaths.ResolveLogPath(Array.Empty<GroupSpec>(), new CommandSpec { Name = "run" }, DefaultLogsDir(dir.Path)));
    }

    [Fact]
    public void ResolveLogPath_DefaultPath_SameCommandNameInDifferentBranches_DiffersByAncestors()
    {
        using var dir = new TempDir();
        string logs = DefaultLogsDir(dir.Path);
        var cmd = new CommandSpec { Name = "web" };

        string a = ConfigPaths.ResolveLogPath(new[] { new GroupSpec { Name = "build" }, new GroupSpec { Name = "x" } }, cmd, logs);
        string b = ConfigPaths.ResolveLogPath(new[] { new GroupSpec { Name = "build" }, new GroupSpec { Name = "y" } }, cmd, logs);
        string c = ConfigPaths.ResolveLogPath(new[] { new GroupSpec { Name = "build" } }, cmd, logs);

        Assert.NotEqual(a, b);
        Assert.NotEqual(a, c);
    }

    [Theory]
    [InlineData("", "command")]
    [InlineData("   ", "command")]
    [InlineData("my/app\\v1: x\ty", "my_app_v1__x_y")]
    [InlineData("a b", "a_b")]
    public void ResolveLogPath_DefaultCommandName_IsSanitized(string name, string expectedBase)
    {
        using var dir = new TempDir();
        string logs = DefaultLogsDir(dir.Path);
        var group = new GroupSpec { Name = "servers" };

        string expected = Path.GetFullPath(Path.Combine(logs, "servers", expectedBase + ".log"));
        Assert.Equal(expected, ConfigPaths.ResolveLogPath(new[] { group }, new CommandSpec { Name = name }, logs));
    }

    [Theory]
    [InlineData("", "group")]
    [InlineData("   ", "group")]
    [InlineData("my/app\\v1: x\ty", "my_app_v1__x_y")]
    [InlineData("a b", "a_b")]
    public void ResolveLogPath_AncestorName_IsSanitizedPerSegment(string groupName, string expectedDir)
    {
        using var dir = new TempDir();
        string logs = DefaultLogsDir(dir.Path);
        var ancestors = new[] { new GroupSpec { Name = "outer" }, new GroupSpec { Name = groupName } };

        string expected = Path.GetFullPath(Path.Combine(logs, "outer", expectedDir, "web.log"));
        Assert.Equal(expected, ConfigPaths.ResolveLogPath(ancestors, new CommandSpec { Name = "web" }, logs));
    }

    [Theory]
    [InlineData(".", "_")]
    [InlineData("..", "__")]
    public void ResolveLogPath_DotOnlyNamesStayInsideLogsFolder(string name, string expected)
    {
        using var dir = new TempDir();
        string logs = DefaultLogsDir(dir.Path);
        var ancestors = new[] { new GroupSpec { Name = name } };

        string path = ConfigPaths.ResolveLogPath(ancestors, new CommandSpec { Name = name }, logs);

        Assert.Equal(Path.GetFullPath(Path.Combine(logs, expected, expected + ".log")), path);
        Assert.StartsWith(Path.GetFullPath(logs) + Path.DirectorySeparatorChar, path);
    }

    [Fact]
    public void ResolveLogsDir_DefaultsToLogsUnderConfigDir()
    {
        using var dir = new TempDir();

        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "logs"), ConfigPaths.ResolveLogsDir(new CmdrConfig(), dir.Path));
    }

    [Fact]
    public void ResolveLogsDir_RelativeResolvesAgainstConfigDir()
    {
        using var dir = new TempDir();

        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "custom"),
            ConfigPaths.ResolveLogsDir(new CmdrConfig { LogsDir = "custom" }, dir.Path));
    }

    [Fact]
    public void ResolveLogsDir_AbsoluteIsUsedAsIs()
    {
        using var dir = new TempDir();
        string absolute = Path.Combine(dir.Path, "abs-logs");

        Assert.Equal(absolute, ConfigPaths.ResolveLogsDir(new CmdrConfig { LogsDir = absolute }, Path.Combine(dir.Path, "cfg")));
    }

    private static string DefaultLogsDir(string configDir) => ConfigPaths.ResolveLogsDir(new CmdrConfig(), configDir);
}
