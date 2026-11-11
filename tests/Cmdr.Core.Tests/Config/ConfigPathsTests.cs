using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

public class ConfigPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveCwd_NullOrEmpty_IsConfigDir(string? cwd)
    {
        using var dir = new TempDir();
        var cmd = new CommandSpec { Name = "a", Cwd = cwd };

        Assert.Equal(Path.GetFullPath(dir.Path), ConfigPaths.ResolveCwd(cmd, dir.Path));
    }

    [Fact]
    public void ResolveCwd_Relative_ResolvesAgainstConfigDir()
    {
        using var dir = new TempDir();
        string configDir = Path.Combine(dir.Path, "cfg");
        var cmd = new CommandSpec { Name = "a", Cwd = Path.Combine("..", "web") };

        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "web")), ConfigPaths.ResolveCwd(cmd, configDir));
    }

    [Fact]
    public void ResolveCwd_Dot_IsConfigDir()
    {
        using var dir = new TempDir();
        var cmd = new CommandSpec { Name = "a", Cwd = "." };

        Assert.Equal(Path.GetFullPath(dir.Path), ConfigPaths.ResolveCwd(cmd, dir.Path));
    }

    [Fact]
    public void ResolveCwd_Absolute_IsNormalizedAndNotRebased()
    {
        using var dir = new TempDir();
        string absolute = Path.Combine(dir.Path, "elsewhere", "..", "abs");
        var cmd = new CommandSpec { Name = "a", Cwd = absolute };

        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "abs")), ConfigPaths.ResolveCwd(cmd, Path.Combine(dir.Path, "cfg")));
    }

    [Fact]
    public void ResolveLogPath_ExplicitRelative_ResolvesAgainstLogsDir()
    {
        using var dir = new TempDir();
        var group = new GroupSpec { Name = "servers" };
        var cmd = new CommandSpec { Name = "web", Log = new LogSpec { Enabled = true, Path = Path.Combine("sub", "w.log") } };

        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "logs", "sub", "w.log"), ConfigPaths.ResolveLogPath(group, cmd, DefaultLogsDir(dir.Path)));
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
        var config = new CmdrConfig { LogsDir = Path.Combine("..", "out") };

        Assert.Equal(
            Path.GetFullPath(Path.Combine(dir.Path, "..", "out")),
            ConfigPaths.ResolveLogsDir(config, dir.Path));
    }

    [Fact]
    public void ResolveLogsDir_AbsoluteIsUsedAsIs()
    {
        using var dir = new TempDir();
        string absolute = Path.Combine(dir.Path, "abs-logs");

        Assert.Equal(absolute, ConfigPaths.ResolveLogsDir(new CmdrConfig { LogsDir = absolute }, Path.Combine(dir.Path, "cfg")));
    }

    [Fact]
    public void ResolveLogPath_DefaultPathUsesConfiguredLogsDir()
    {
        using var dir = new TempDir();
        string logsDir = ConfigPaths.ResolveLogsDir(new CmdrConfig { LogsDir = "custom" }, dir.Path);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(dir.Path), "custom", "servers", "web.log"),
            ConfigPaths.ResolveLogPath(new GroupSpec { Name = "servers" }, new CommandSpec { Name = "web" }, logsDir));
    }

    [Fact]
    public void ResolveLogPath_ExplicitAbsolute_IsUsedAsIs()
    {
        using var dir = new TempDir();
        string absolute = Path.Combine(dir.Path, "x", "..", "out.log");
        var group = new GroupSpec { Name = "servers" };
        var cmd = new CommandSpec { Name = "web", Log = new LogSpec { Path = absolute } };

        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "out.log")), ConfigPaths.ResolveLogPath(group, cmd, DefaultLogsDir(Path.Combine(dir.Path, "cfg"))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveLogPath_NoExplicitPath_UsesLogsDirGroupAndName(string? logPath)
    {
        using var dir = new TempDir();
        var group = new GroupSpec { Name = "servers" };
        var cmd = new CommandSpec { Name = "web", Log = new LogSpec { Path = logPath } };

        Assert.Equal(
            Path.Combine(Path.GetFullPath(dir.Path), "logs", "servers", "web.log"),
            ConfigPaths.ResolveLogPath(group, cmd, DefaultLogsDir(dir.Path)));
    }

    [Fact]
    public void ResolveLogPath_DefaultPath_DiffersPerGroup_ForSameCommandName()
    {
        using var dir = new TempDir();
        var build = new GroupSpec { Name = "build" };
        var servers = new GroupSpec { Name = "servers" };
        var cmd = new CommandSpec { Name = "web" };

        string a = ConfigPaths.ResolveLogPath(build, cmd, DefaultLogsDir(dir.Path));
        string b = ConfigPaths.ResolveLogPath(servers, cmd, DefaultLogsDir(dir.Path));

        Assert.NotEqual(a, b);
        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "logs", "build", "web.log"), a);
        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "logs", "servers", "web.log"), b);
    }

    [Theory]
    [InlineData("", "command")]
    [InlineData("   ", "command")]
    [InlineData("my/app\\v1: x\ty", "my_app_v1__x_y")]
    [InlineData("a b", "a_b")]
    public void ResolveLogPath_DefaultCommandName_IsSanitized(string name, string expectedBase)
    {
        using var dir = new TempDir();
        var group = new GroupSpec { Name = "servers" };
        var cmd = new CommandSpec { Name = name };

        string expected = Path.Combine(Path.GetFullPath(dir.Path), "logs", "servers", expectedBase + ".log");
        Assert.Equal(expected, ConfigPaths.ResolveLogPath(group, cmd, DefaultLogsDir(dir.Path)));
    }

    [Theory]
    [InlineData("", "group")]
    [InlineData("   ", "group")]
    [InlineData("my/app\\v1: x\ty", "my_app_v1__x_y")]
    [InlineData("a b", "a_b")]
    public void ResolveLogPath_DefaultGroupName_IsSanitized(string groupName, string expectedDir)
    {
        using var dir = new TempDir();
        var group = new GroupSpec { Name = groupName };
        var cmd = new CommandSpec { Name = "web" };

        string expected = Path.Combine(Path.GetFullPath(dir.Path), "logs", expectedDir, "web.log");
        Assert.Equal(expected, ConfigPaths.ResolveLogPath(group, cmd, DefaultLogsDir(dir.Path)));
    }

    [Fact]
    public void ResolveLogPath_DefaultName_ReplacesInvalidFileNameChars()
    {
        using var dir = new TempDir();
        string invalid = string.Concat(Path.GetInvalidFileNameChars());
        var group = new GroupSpec { Name = "g" + invalid + "h" };
        var cmd = new CommandSpec { Name = "x" + invalid + "y" };

        string resolved = ConfigPaths.ResolveLogPath(group, cmd, DefaultLogsDir(dir.Path));

        string groupDir = Path.GetFileName(Path.GetDirectoryName(resolved)!);
        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "logs"), Path.GetDirectoryName(Path.GetDirectoryName(resolved)));
        Assert.StartsWith("g", groupDir);
        Assert.EndsWith("h", groupDir);
        Assert.Equal(-1, groupDir.IndexOfAny(Path.GetInvalidFileNameChars()));

        string fileName = Path.GetFileName(resolved);
        Assert.Equal(".log", Path.GetExtension(fileName));
        Assert.Equal(-1, fileName.IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.StartsWith("x", fileName);
        Assert.EndsWith("y.log", fileName);
    }

    [Theory]
    [InlineData(".", "_")]
    [InlineData("..", "__")]
    public void ResolveLogPath_DotOnlyNamesStayInsideLogsFolder(string name, string expected)
    {
        string configDir = Path.Combine(Path.GetTempPath(), "cmdr-cfg");
        var group = new GroupSpec { Name = name };
        var cmd = new CommandSpec { Name = name };

        string path = ConfigPaths.ResolveLogPath(group, cmd, DefaultLogsDir(configDir));

        Assert.Equal(Path.Combine(configDir, "logs", expected, expected + ".log"), path);
    }

    private static string DefaultLogsDir(string configDir) => ConfigPaths.ResolveLogsDir(new CmdrConfig(), configDir);
}
