using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;
using Shared.Processes;

namespace Cmdr.Core.Tests.Processes;

/// <summary>
/// The working directory of a launch resolved with <c>${env:NAME}</c>: which environment each cwd sees. The base environment is empty, so an
/// unset variable is one that no level sets.
/// </summary>
public class CommandLaunchCwdTests
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyEnvironment = new Dictionary<string, string?>();

    private static CommandSpec Cmd(string? cwd = null, Dictionary<string, string?>? env = null) =>
        new() { Name = "cmd", Program = "echo", Cwd = cwd, Env = env ?? new Dictionary<string, string?>() };

    private static GroupSpec Group(string name, string? cwd = null, Dictionary<string, string?>? env = null) =>
        new() { Name = name, Cwd = cwd, Env = env };

    [Fact]
    public void GroupCwd_SeesAFileLevelVariable()
    {
        using var dir = new TempDir();
        string configDir = Path.GetFullPath(dir.Path);
        var fileLevels = new[] { new EnvLevel(new Dictionary<string, string?> { ["D"] = "sub" }, configDir) };

        LaunchSpec launch = CommandLaunch.From(
            new[] { Group("g", cwd: "${env:D}") }, Cmd(), configDir, fileLevels, baseEnvironment: EmptyEnvironment);

        Assert.Equal(Path.Combine(configDir, "sub"), launch.WorkingDirectory);
    }

    [Fact]
    public void GroupCwd_DoesNotSeeItsOwnEnv()
    {
        using var dir = new TempDir();
        string configDir = Path.GetFullPath(dir.Path);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => CommandLaunch.From(
            new[] { Group("g", cwd: "${env:D}", env: new Dictionary<string, string?> { ["D"] = "sub" }) },
            Cmd(),
            configDir,
            Array.Empty<EnvLevel>(),
            baseEnvironment: EmptyEnvironment));

        Assert.Contains("variable D is not set", ex.Message);
    }

    [Fact]
    public void GroupCwd_DoesNotSeeItsOwnScriptValues()
    {
        using var dir = new TempDir();
        string configDir = Path.GetFullPath(dir.Path);
        var values = new Dictionary<string, string?> { ["S"] = "sub" };

        Assert.Throws<InvalidOperationException>(() => CommandLaunch.From(
            new[] { Group("g", cwd: "${env:S}") },
            Cmd(),
            configDir,
            Array.Empty<EnvLevel>(),
            new IReadOnlyDictionary<string, string?>?[] { values },
            baseEnvironment: EmptyEnvironment));
    }

    [Fact]
    public void GroupCwd_SeesTheScriptValuesAndEnvOfTheGroupsAbove()
    {
        using var dir = new TempDir();
        string configDir = Path.GetFullPath(dir.Path);
        var outerValues = new Dictionary<string, string?> { ["S"] = "script" };

        LaunchSpec launch = CommandLaunch.From(
            new[]
            {
                Group("outer", env: new Dictionary<string, string?> { ["A"] = "env" }),
                Group("inner", cwd: "${env:S}/${env:A}"),
            },
            Cmd(),
            configDir,
            Array.Empty<EnvLevel>(),
            new IReadOnlyDictionary<string, string?>?[] { outerValues, null },
            baseEnvironment: EmptyEnvironment);

        Assert.Equal(Path.Combine(configDir, "script", "env"), launch.WorkingDirectory);
    }

    [Fact]
    public void CommandCwd_SeesItsGroupsEnv_NotItsOwn()
    {
        using var dir = new TempDir();
        string configDir = Path.GetFullPath(dir.Path);
        var groups = new[] { Group("g", env: new Dictionary<string, string?> { ["D"] = "sub" }) };

        LaunchSpec launch = CommandLaunch.From(
            groups, Cmd(cwd: "${env:D}"), configDir, Array.Empty<EnvLevel>(), baseEnvironment: EmptyEnvironment);
        Assert.Equal(Path.Combine(configDir, "sub"), launch.WorkingDirectory);

        Assert.Throws<InvalidOperationException>(() => CommandLaunch.From(
            groups,
            Cmd(cwd: "${env:E}", env: new Dictionary<string, string?> { ["E"] = "x" }),
            configDir,
            Array.Empty<EnvLevel>(),
            baseEnvironment: EmptyEnvironment));
    }

    [Fact]
    public void CommandCwd_UnsetVariable_Throws()
    {
        using var dir = new TempDir();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => CommandLaunch.From(
            Array.Empty<GroupSpec>(), Cmd(cwd: "${env:NOPE}/bin"), dir.Path, Array.Empty<EnvLevel>(), baseEnvironment: EmptyEnvironment));

        Assert.Contains("'${env:NOPE}/bin'", ex.Message);
        Assert.Contains("variable NOPE is not set", ex.Message);
    }

    [Fact]
    public void GroupDirectory_ResolvesInnermostCwdBeforeItsOwnLevels()
    {
        using var dir = new TempDir();
        string configDir = Path.GetFullPath(dir.Path);
        var groups = new[] { Group("outer", env: new Dictionary<string, string?> { ["A"] = "a" }), Group("inner", cwd: "${env:A}") };

        Assert.Equal(Path.Combine(configDir, "a"),
            CommandLaunch.GroupDirectory(groups, Array.Empty<EnvLevel>(), null, configDir, EmptyEnvironment));
        Assert.Throws<InvalidOperationException>(() =>
            CommandLaunch.GroupDirectory(new[] { Group("g", cwd: "${env:A}", env: new Dictionary<string, string?> { ["A"] = "a" }) },
                Array.Empty<EnvLevel>(), null, configDir, EmptyEnvironment));
    }
}
