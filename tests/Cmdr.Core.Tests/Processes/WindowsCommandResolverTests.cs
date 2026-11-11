using Cmdr.Core.Processes;

namespace Cmdr.Core.Tests.Processes;

/// <summary>Pure tests of Windows program resolution (runs on every OS, with a fake file system).</summary>
public class WindowsCommandResolverTests
{
    private const string WorkDir = @"C:\work";

    /// <summary>A file-exists predicate over a fixed, case-insensitive set of paths.</summary>
    private static Func<string, bool> Files(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    [Fact]
    public void BareNameIsFoundInWorkingDirectoryBeforePath()
    {
        Func<string, bool> fs = Files(@"C:\work\tool.exe", @"C:\bin\tool.exe");

        string? found = WindowsCommandResolver.Resolve("tool", WorkDir, @"C:\bin", ".exe", fs);

        Assert.Equal(@"C:\work\tool.exe", found);
    }

    [Fact]
    public void PathEntriesAreSearchedInOrder()
    {
        Func<string, bool> fs = Files(@"C:\a\x.exe", @"C:\b\x.exe");

        Assert.Equal(@"C:\a\x.exe", WindowsCommandResolver.Resolve("x", WorkDir, @"C:\a;C:\b", ".exe", fs));
        Assert.Equal(@"C:\b\x.exe", WindowsCommandResolver.Resolve("x", WorkDir, @"C:\b;C:\a", ".exe", fs));
    }

    [Fact]
    public void PathExtOrderDecidesBetweenSameNamedFiles()
    {
        Func<string, bool> fs = Files(@"C:\bin\npm.cmd", @"C:\bin\npm.exe");

        Assert.Equal(@"C:\bin\npm.cmd", WindowsCommandResolver.Resolve("npm", WorkDir, @"C:\bin", ".cmd;.exe", fs));
        Assert.Equal(@"C:\bin\npm.exe", WindowsCommandResolver.Resolve("npm", WorkDir, @"C:\bin", ".exe;.cmd", fs));
    }

    [Fact]
    public void ExplicitExtensionInPathExtIsTriedBeforeAppendedExtensions()
    {
        Func<string, bool> fs = Files(@"C:\bin\tool.cmd", @"C:\bin\tool.cmd.exe");

        string? found = WindowsCommandResolver.Resolve("tool.cmd", WorkDir, @"C:\bin", ".exe;.cmd", fs);

        Assert.Equal(@"C:\bin\tool.cmd", found);
    }

    [Fact]
    public void ExtensionNotInPathExtIsNotTriedBare()
    {
        Func<string, bool> fs = Files(@"C:\bin\tool.txt");

        Assert.Null(WindowsCommandResolver.Resolve("tool.txt", WorkDir, @"C:\bin", ".exe;.cmd", fs));

        Func<string, bool> withExe = Files(@"C:\bin\tool.txt", @"C:\bin\tool.txt.exe");
        Assert.Equal(@"C:\bin\tool.txt.exe", WindowsCommandResolver.Resolve("tool.txt", WorkDir, @"C:\bin", ".exe;.cmd", withExe));
    }

    [Fact]
    public void ExtensionlessNameIsNeverTriedBare()
    {
        Func<string, bool> fs = Files(@"C:\bin\tool");

        Assert.Null(WindowsCommandResolver.Resolve("tool", WorkDir, @"C:\bin", ".exe;.cmd", fs));
    }

    [Fact]
    public void QuotedPathEntriesAreUnquoted()
    {
        Func<string, bool> fs = Files(@"C:\Program Files\nodejs\npm.cmd");

        string? found = WindowsCommandResolver.Resolve("npm", WorkDir, @"C:\other;""C:\Program Files\nodejs""", null, fs);

        Assert.Equal(@"C:\Program Files\nodejs\npm.CMD", found); // default PATHEXT spelling
    }

    [Fact]
    public void EmptyPathEntriesAreIgnored()
    {
        Func<string, bool> fs = Files(@"C:\bin\tool.exe");

        Assert.Equal(@"C:\bin\tool.exe", WindowsCommandResolver.Resolve("tool", WorkDir, @";;C:\bin;", ".exe", fs));
    }

    [Fact]
    public void RelativePathLikeProgramIsResolvedAgainstWorkingDirectoryOnly()
    {
        Func<string, bool> fs = Files(@"C:\app\sub\run.bat", @"C:\bin\sub\run.bat");

        Assert.Equal(@"C:\app\sub\run.BAT", WindowsCommandResolver.Resolve(@"sub\run", @"C:\app", @"C:\bin", null, fs));
        Assert.Null(WindowsCommandResolver.Resolve(@"sub\run", @"C:\nowhere", @"C:\bin", null, fs));
    }

    [Fact]
    public void ForwardSlashIsPathLike()
    {
        Func<string, bool> fs = Files(@"C:\app\sub/run.bat");

        Assert.Equal(@"C:\app\sub/run.BAT", WindowsCommandResolver.Resolve("sub/run", @"C:\app", @"C:\bin", null, fs));
    }

    [Fact]
    public void RootedDrivePathIgnoresWorkingDirectoryAndPath()
    {
        Func<string, bool> fs = Files(@"D:\tools\x.exe", @"C:\app\D:\tools\x.exe");

        Assert.Equal(@"D:\tools\x.exe", WindowsCommandResolver.Resolve(@"D:\tools\x", @"C:\app", @"C:\app", ".exe", fs));
    }

    [Fact]
    public void UncPathIsRootedAndTriedWithPathExt()
    {
        Func<string, bool> fs = Files(@"\\server\share\run.cmd");

        Assert.Equal(@"\\server\share\run.CMD", WindowsCommandResolver.Resolve(@"\\server\share\run", @"C:\app", @"C:\bin", null, fs));
    }

    [Fact]
    public void PathLikeProgramDoesNotSearchPath()
    {
        Func<string, bool> fs = Files(@"C:\bin\tool.exe");

        Assert.Null(WindowsCommandResolver.Resolve(@"..\bin\tool", @"C:\app", @"C:\bin", ".exe", fs));
    }

    [Fact]
    public void NullPathExtUsesDefaultList()
    {
        Assert.Equal(@"C:\bin\tool.CMD", WindowsCommandResolver.Resolve("tool", WorkDir, @"C:\bin", null, Files(@"C:\bin\tool.cmd")));
        Assert.Equal(@"C:\bin\tool.COM", WindowsCommandResolver.Resolve("tool", WorkDir, @"C:\bin", "", Files(@"C:\bin\tool.com")));
    }

    [Fact]
    public void DefaultPathExtDoesNotIncludeScripts()
    {
        Func<string, bool> fs = Files(@"C:\bin\tool.ps1");

        Assert.Null(WindowsCommandResolver.Resolve("tool", WorkDir, @"C:\bin", null, fs));
    }

    [Fact]
    public void ReturnsNullWhenNothingExists()
    {
        Assert.Null(WindowsCommandResolver.Resolve("missing", WorkDir, @"C:\bin", null, Files()));
    }
}
