namespace Shared.Processes.Tests;

public class LaunchPathsTests
{
    [Fact]
    public void ResolveDir_NullBlankRelativeAndAbsolute()
    {
        using var dir = new TempDir();
        string absolute = Path.Combine(dir.Path, "abs");

        Assert.Equal(Path.GetFullPath(dir.Path), LaunchPaths.ResolveDir(null, dir.Path));
        Assert.Equal(Path.GetFullPath(dir.Path), LaunchPaths.ResolveDir("  ", dir.Path));
        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "x")), LaunchPaths.ResolveDir("x", dir.Path));
        Assert.Equal(Path.GetFullPath(absolute), LaunchPaths.ResolveDir(absolute, dir.Path));
    }

    private static string? NoEnv(string _) => null;

    [Fact]
    public void ResolveDir_ExpandsEnvConfigDirAndPathSeparator()
    {
        using var dir = new TempDir();
        var env = new Dictionary<string, string?> { ["ROOT"] = Path.Combine(dir.Path, "root") };
        string configDir = Path.Combine(dir.Path, "cfg");

        Assert.Equal(Path.GetFullPath(Path.Combine(env["ROOT"]!, "sub")),
            LaunchPaths.ResolveDir("${env:ROOT}/sub", dir.Path, name => env.GetValueOrDefault(name), configDir, '/'));
        Assert.Equal(Path.GetFullPath(configDir), LaunchPaths.ResolveDir("${configDir}", dir.Path, NoEnv, configDir, '/'));
        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "a" + Path.PathSeparator + "b")),
            LaunchPaths.ResolveDir("a${pathsep}b", dir.Path, NoEnv, configDir, Path.PathSeparator));
    }

    [Fact]
    public void ResolveDir_EscapedOpeningIsLiteral()
    {
        using var dir = new TempDir();

        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "${env:X}")),
            LaunchPaths.ResolveDir("$${env:X}", dir.Path, name => "unused", dir.Path, '/'));
    }

    [Fact]
    public void ResolveDir_RelativeValueIsCombinedWithBaseAndAbsoluteValueReplacesIt()
    {
        using var dir = new TempDir();
        string absolute = Path.Combine(dir.Path, "abs", "..", "target");
        var env = new Dictionary<string, string?> { ["REL"] = Path.Combine("sub", "dir"), ["ABS"] = absolute };

        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "sub", "dir", "x")),
            LaunchPaths.ResolveDir("${env:REL}/x", dir.Path, name => env.GetValueOrDefault(name), dir.Path, '/'));
        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "target", "x")),
            LaunchPaths.ResolveDir("${env:ABS}/x", dir.Path, name => env.GetValueOrDefault(name), dir.Path, '/'));
    }

    [Fact]
    public void ResolveDir_VariableSetToEmpty_IsTheBaseDirectory()
    {
        using var dir = new TempDir();

        Assert.Equal(Path.GetFullPath(dir.Path), LaunchPaths.ResolveDir("${env:E}", dir.Path, name => "", dir.Path, '/'));
    }

    [Fact]
    public void ResolveDir_UnsetVariable_ThrowsNamingTheVariableAndTheText()
    {
        using var dir = new TempDir();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            LaunchPaths.ResolveDir("${env:MISSING_VAR}/bin", dir.Path, NoEnv, dir.Path, '/'));

        Assert.Contains("MISSING_VAR", ex.Message);
        Assert.Contains("${env:MISSING_VAR}/bin", ex.Message);
    }
}
