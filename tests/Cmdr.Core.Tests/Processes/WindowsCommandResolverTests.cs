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

    [Theory]
    [InlineData(@"tools", @"C:\child\tools\tool.exe")]
    [InlineData(@".\node_modules\.bin", @"C:\child\.\node_modules\.bin\tool.exe")]
    public void RelativePathEntriesAreBasedAtChildWorkingDirectory(string pathEntry, string expected)
    {
        string? found = WindowsCommandResolver.Resolve(
            "tool", @"C:\child", pathEntry, ".exe", Files(expected));

        Assert.Equal(expected, found);
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
    public void ExtensionNotInPathExtIsTriedLastAsExplicitlyNamed()
    {
        Func<string, bool> fs = Files(@"C:\bin\tool.txt");

        Assert.Equal(@"C:\bin\tool.txt", WindowsCommandResolver.Resolve("tool.txt", WorkDir, @"C:\bin", ".exe;.cmd", fs));

        Func<string, bool> withExe = Files(@"C:\bin\tool.txt", @"C:\bin\tool.txt.exe");
        Assert.Equal(@"C:\bin\tool.txt.exe", WindowsCommandResolver.Resolve("tool.txt", WorkDir, @"C:\bin", ".exe;.cmd", withExe));
    }

    [Fact]
    public void PathExtCandidateWinsOverExplicitNonPathExtFileOfSameName()
    {
        Func<string, bool> fs = Files(@"C:\work\x.bin.EXE", @"C:\work\x.bin");

        Assert.Equal(@"C:\work\x.bin.EXE", WindowsCommandResolver.Resolve("x.bin", WorkDir, null, ".EXE;.CMD", fs));
    }

    [Fact]
    public void PathLikeNonPathExtFileUnderWorkingDirectoryResolves()
    {
        Func<string, bool> fs = Files(@"C:\child\.\tools\tool.bin");

        Assert.Equal(@"C:\child\.\tools\tool.bin", WindowsCommandResolver.Resolve(@".\tools\tool.bin", @"C:\child", null, null, fs));
    }

    [Fact]
    public void PathLikeProgramMissingUnderWorkingDirectoryIsNotFoundEvenIfElsewhere()
    {
        // The file exists under cmdr's own directory (or any other place) but not under the child's working directory.
        Func<string, bool> fs = Files(@"C:\cmdr\tools\tool.exe", @"C:\cmdr\tools\tool.bin");

        Assert.Null(WindowsCommandResolver.Resolve(@".\tools\tool.exe", @"C:\child", @"C:\cmdr;C:\bin", ".exe", fs));
        Assert.Null(WindowsCommandResolver.Resolve(@".\tools\tool.bin", @"C:\child", @"C:\cmdr", ".exe", fs));
    }

    [Fact]
    public void BareNameInCmdrDirectoryOnlyIsNotFound()
    {
        // cmdr's own directory is never searched: only the working directory and PATH entries are.
        Func<string, bool> fs = Files(@"C:\cmdr\tool.exe", @"C:\cmdr\tool.bin");

        Assert.Null(WindowsCommandResolver.Resolve("tool", WorkDir, @"C:\bin", ".exe", fs));
        Assert.Null(WindowsCommandResolver.Resolve("tool.bin", WorkDir, @"C:\bin", ".exe", fs));
    }

    [Fact]
    public void ExtensionlessPathLikeProgramIsNeverTriedBare()
    {
        Func<string, bool> fs = Files(@"C:\child\.\tools\tool");

        Assert.Null(WindowsCommandResolver.Resolve(@".\tools\tool", @"C:\child", null, ".exe;.cmd", fs));
    }

    [Fact]
    public void BareNonPathExtNameInPathDirectoryResolves()
    {
        Func<string, bool> fs = Files(@"C:\bin\tool.bin");

        Assert.Equal(@"C:\bin\tool.bin", WindowsCommandResolver.Resolve("tool.bin", WorkDir, @"C:\bin", ".exe", fs));
    }

    [Fact]
    public void NotFoundMessageForBareName()
    {
        Assert.Equal(
            "'pnpm' was not found in the working directory or on PATH (PATHEXT extensions tried: .COM;.EXE;.BAT;.CMD)",
            WindowsCommandResolver.NotFoundMessage("pnpm", WorkDir, null));
    }

    [Fact]
    public void NotFoundMessageForRelativePathNamesWorkingDirectory()
    {
        Assert.Equal(
            @"'.\tools\tool.exe' was not found relative to the working directory 'C:\child' (PATHEXT extensions tried: .EXE;.CMD)",
            WindowsCommandResolver.NotFoundMessage(@".\tools\tool.exe", @"C:\child", ".EXE;.CMD"));
    }

    [Fact]
    public void NotFoundMessageForRootedPathOmitsWorkingDirectory()
    {
        Assert.Equal(
            @"'D:\tools\x' was not found (PATHEXT extensions tried: .EXE)",
            WindowsCommandResolver.NotFoundMessage(@"D:\tools\x", WorkDir, ".EXE"));
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

    [Theory]
    [InlineData("//server/share/run")]
    [InlineData(@"\\server/share/run")]
    [InlineData(@"/\server\share\run")]
    public void UncPathWithForwardSlashesIsTheNetworkPathNotOneOnTheLocalDrive(string program)
    {
        var candidates = new List<string>();
        Func<string, bool> fs = path =>
        {
            candidates.Add(path);
            return string.Equals(path, @"\\server\share\run.CMD", StringComparison.OrdinalIgnoreCase);
        };

        Assert.Equal(@"\\server\share\run.CMD", WindowsCommandResolver.Resolve(program, @"C:\child", null, ".CMD", fs));
        Assert.DoesNotContain(candidates, c => c.StartsWith("C:", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(@"C:tools\run")]
    [InlineData("C:run")]
    [InlineData("d:")]
    public void DriveRelativeProgramIsRejected(string program)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => WindowsCommandResolver.Resolve(program, @"C:\child", null, ".CMD", _ => true));

        Assert.Contains("current directory of drive", ex.Message);
    }

    [Fact]
    public void DriveRelativePathEntryIsSkipped()
    {
        var candidates = new List<string>();
        Func<string, bool> fs = path =>
        {
            candidates.Add(path);
            return string.Equals(path, @"C:\bin\tool.CMD", StringComparison.OrdinalIgnoreCase);
        };

        Assert.Equal(@"C:\bin\tool.CMD", WindowsCommandResolver.Resolve("tool", @"C:\child", @"C:tools;C:\bin", ".CMD", fs));
        Assert.DoesNotContain(candidates, c => c.Contains("C:tools", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PathEntryWithForwardSlashUncIsSearchedOnTheShare()
    {
        Func<string, bool> fs = Files(@"\\server\share\bin\tool.cmd");

        Assert.Equal(
            @"\\server\share\bin\tool.CMD",
            WindowsCommandResolver.Resolve("tool", @"C:\child", "//server/share/bin", ".CMD", fs));
    }

    [Theory]
    [InlineData(@"\tools\run", @"C:\tools\run.CMD")]
    [InlineData("/tools/run", @"C:\tools\run.CMD")]
    public void DriveRootedPathUsesDriveRootRatherThanChildDirectory(string program, string expected)
    {
        var candidates = new List<string>();
        Func<string, bool> fs = path =>
        {
            candidates.Add(path);
            return string.Equals(path, expected, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, @"C:\child\tools\run.CMD", StringComparison.OrdinalIgnoreCase);
        };

        Assert.Equal(expected, WindowsCommandResolver.Resolve(program, @"C:\child", null, null, fs));
        Assert.Contains(expected, candidates, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void DriveRootedPathUsesUncShareRootWhenWorkingDirectoryIsUnc()
    {
        const string expected = @"\\server\share\tools\run.CMD";

        Assert.Equal(expected, WindowsCommandResolver.Resolve(
            @"\tools\run", @"\\server\share\child", null, null, Files(expected)));
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
