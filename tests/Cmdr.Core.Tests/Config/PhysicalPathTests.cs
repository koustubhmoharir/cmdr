using Cmdr.Core.Config;

namespace Cmdr.Core.Tests.Config;

/// <summary>Link resolution as the file system does it. Symbolic links need a privilege on Windows, so these run elsewhere.</summary>
public class PhysicalPathTests
{
    [Fact]
    public void PathWithoutLinks_IsUnchanged()
    {
        if (OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("a"));
        string path = Path.Combine(dir.Path, "a", "missing", "file.log");

        Assert.Equal(path, PhysicalPath.Resolve(path));
    }

    [Fact]
    public void RelativeDirectoryLink_IsReplacedByItsTarget_AndMissingComponentsAreKept()
    {
        if (OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("real"));
        Directory.CreateSymbolicLink(dir.File("alias"), "real");

        Assert.Equal(
            Path.Combine(dir.Path, "real", "new", "x.log"),
            PhysicalPath.Resolve(Path.Combine(dir.Path, "alias", "new", "x.log")));
    }

    [Fact]
    public void DotDotAfterADirectoryLink_GoesToTheParentOfTheLinkTarget()
    {
        if (OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File(Path.Combine("other", "child")));
        Directory.CreateSymbolicLink(dir.File("alias"), Path.Combine("other", "child"));
        File.CreateSymbolicLink(dir.File("linked"), Path.Combine("alias", "..", "target"));

        Assert.Equal(Path.Combine(dir.Path, "other", "target"), PhysicalPath.Resolve(dir.File("linked")));
    }

    [Fact]
    public void ChainedAbsoluteAndRelativeLinks_ResolveToTheFinalTarget()
    {
        if (OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("real"));
        File.WriteAllText(dir.File(Path.Combine("real", "f")), "");
        File.CreateSymbolicLink(dir.File("one"), dir.File(Path.Combine("real", "f")));
        File.CreateSymbolicLink(dir.File("two"), "one");

        Assert.Equal(Path.Combine(dir.Path, "real", "f"), PhysicalPath.Resolve(dir.File("two")));
    }

    [Fact]
    public void LinkLoop_IsAnIOException()
    {
        if (OperatingSystem.IsWindows()) return;

        using var dir = new TempDir();
        File.CreateSymbolicLink(dir.File("a"), "b");
        File.CreateSymbolicLink(dir.File("b"), "a");

        Assert.Throws<IOException>(() => PhysicalPath.Resolve(dir.File("a")));
    }

    [Fact]
    public void RelativePath_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => PhysicalPath.Resolve("relative/path"));
    }
}
