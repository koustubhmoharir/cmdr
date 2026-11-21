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
}
