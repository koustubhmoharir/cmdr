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
    public void ResolveLogPath_ExplicitRelative_ResolvesAgainstConfigDir()
    {
        using var dir = new TempDir();
        var cmd = new CommandSpec { Name = "web", Log = new LogSpec { Enabled = true, Path = Path.Combine("logs", "w.log") } };

        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "logs", "w.log"), ConfigPaths.ResolveLogPath(cmd, dir.Path));
    }

    [Fact]
    public void ResolveLogPath_ExplicitAbsolute_IsUsedAsIs()
    {
        using var dir = new TempDir();
        string absolute = Path.Combine(dir.Path, "x", "..", "out.log");
        var cmd = new CommandSpec { Name = "web", Log = new LogSpec { Path = absolute } };

        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "out.log")), ConfigPaths.ResolveLogPath(cmd, Path.Combine(dir.Path, "cfg")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ResolveLogPath_NoExplicitPath_UsesLogsDirAndName(string? logPath)
    {
        using var dir = new TempDir();
        var cmd = new CommandSpec { Name = "web", Log = new LogSpec { Path = logPath } };

        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "logs", "web.log"), ConfigPaths.ResolveLogPath(cmd, dir.Path));
    }

    [Theory]
    [InlineData("", "command")]
    [InlineData("   ", "command")]
    [InlineData("my/app\\v1: x\ty", "my_app_v1__x_y")]
    [InlineData("a b", "a_b")]
    public void ResolveLogPath_DefaultName_IsSanitized(string name, string expectedBase)
    {
        using var dir = new TempDir();
        var cmd = new CommandSpec { Name = name };

        string expected = Path.Combine(Path.GetFullPath(dir.Path), "logs", expectedBase + ".log");
        Assert.Equal(expected, ConfigPaths.ResolveLogPath(cmd, dir.Path));
    }

    [Fact]
    public void ResolveLogPath_DefaultName_ReplacesInvalidFileNameChars()
    {
        using var dir = new TempDir();
        string name = "x" + string.Concat(Path.GetInvalidFileNameChars()) + "y";
        var cmd = new CommandSpec { Name = name };

        string resolved = ConfigPaths.ResolveLogPath(cmd, dir.Path);

        Assert.Equal(Path.Combine(Path.GetFullPath(dir.Path), "logs"), Path.GetDirectoryName(resolved));
        string fileName = Path.GetFileName(resolved);
        Assert.Equal(".log", Path.GetExtension(fileName));
        Assert.Equal(-1, fileName.IndexOfAny(Path.GetInvalidFileNameChars()));
        Assert.StartsWith("x", fileName);
        Assert.EndsWith("y.log", fileName);
    }
}
