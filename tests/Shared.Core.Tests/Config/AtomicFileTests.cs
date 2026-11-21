using Shared.Core;

namespace Shared.Core.Tests.Config;

public class AtomicFileTests
{
    [Fact]
    public void Write_CreatesMissingDirectory_AndOverwritesExistingFile()
    {
        using var dir = new TempDir();
        string path = dir.File(Path.Combine("missing", "nested", "file.txt"));

        AtomicFile.Write(path, "first content that is longer"u8);
        AtomicFile.Write(path, "second"u8);

        Assert.Equal("second\n", File.ReadAllText(path));
    }

    [Fact]
    public void Write_CreatesMissingDirectoryAndLeavesNoTempFiles()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "nested", "deeper", "config.json");

        AtomicFile.Write(path, "{}"u8);

        Assert.True(File.Exists(path));
        Assert.Equal(new[] { "config.json" }, Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        using var dir = new TempDir();
        string path = dir.File("config.json");
        AtomicFile.Write(path, "1"u8);

        AtomicFile.Write(path, "2"u8);

        Assert.Equal("2\n", File.ReadAllText(path));
        Assert.Equal(new[] { "config.json" }, Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Write_WritesUtf8WithoutBomAndTrailingNewline()
    {
        using var dir = new TempDir();
        string path = dir.File("config.json");

        AtomicFile.Write(path, System.Text.Encoding.UTF8.GetBytes("grüppe ✓ héllo ✓"));
        byte[] bytes = File.ReadAllBytes(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.Equal("grüppe ✓ héllo ✓\n", File.ReadAllText(path));
    }

    [Fact]
    public void Write_KeepsExistingOwnerOnlyMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        string path = dir.File("config.json");
        AtomicFile.Write(path, "1"u8);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        AtomicFile.Write(path, "2"u8);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal("2\n", File.ReadAllText(path));
        Assert.Equal(new[] { "config.json" }, Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Write_KeepsExistingWorldWritableMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // The default umask would narrow a mode created from this one, so this proves the mode is set explicitly.
        const UnixFileMode everyone = UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite;
        using var dir = new TempDir();
        string path = dir.File("config.json");
        AtomicFile.Write(path, "1"u8);
        File.SetUnixFileMode(path, everyone);

        AtomicFile.Write(path, "3"u8);

        Assert.Equal(everyone, File.GetUnixFileMode(path));
    }

    [Fact]
    public void Write_ThroughRelativeSymlink_UpdatesTargetAndKeepsLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        string target = dir.File("target.json");
        string link = dir.File("linked.json");
        AtomicFile.Write(target, "1"u8);
        File.CreateSymbolicLink(link, "target.json");

        AtomicFile.Write(link, "5"u8);

        Assert.Equal("target.json", new FileInfo(link).LinkTarget);
        Assert.Equal("5\n", File.ReadAllText(target));
        Assert.Equal("5\n", File.ReadAllText(link));
        Assert.Equal(new[] { "linked.json", "target.json" }, Directory.GetFiles(dir.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Write_ThroughAbsoluteSymlink_UpdatesTargetAndKeepsLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        string target = dir.File("target.json");
        string link = dir.File("linked.json");
        AtomicFile.Write(target, "1"u8);
        File.CreateSymbolicLink(link, target);

        AtomicFile.Write(link, "6"u8);

        Assert.Equal(target, new FileInfo(link).LinkTarget);
        Assert.Equal("6\n", File.ReadAllText(target));
        Assert.Equal("6\n", File.ReadAllText(link));
    }

    [Fact]
    public void Write_ThroughDanglingSymlink_ThrowsAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("a"));
        Directory.CreateDirectory(dir.File("b"));
        string link = dir.File(Path.Combine("a", "linked.json"));
        string missing = Path.Combine("..", "b", "missing.json");
        File.CreateSymbolicLink(link, missing);

        IOException ex = Assert.Throws<IOException>(() => AtomicFile.Write(link, "{}"u8));

        Assert.Contains("which does not exist", ex.Message);
        Assert.Equal(missing, new FileInfo(link).LinkTarget);
        Assert.Equal(new[] { "linked.json" }, Directory.GetFiles(dir.File("a")).Select(Path.GetFileName));
        Assert.Empty(Directory.GetFiles(dir.File("b")));
        Assert.False(File.Exists(dir.File(Path.Combine("b", "missing.json"))));
    }

    [Fact]
    public void Write_ThroughSymlinkWithDotDotAfterADirectoryLink_UpdatesTheFileTheLinkReads()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // linked.json -> alias/../target.json, alias -> other/child: the file system reads other/target.json, not the
        // target.json beside the link (which lexical normalization of the link target would name).
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File(Path.Combine("other", "child")));
        Directory.CreateSymbolicLink(dir.File("alias"), Path.Combine("other", "child"));
        AtomicFile.Write(dir.File(Path.Combine("other", "target.json")), "10"u8);
        AtomicFile.Write(dir.File("target.json"), "20"u8);
        string link = dir.File("linked.json");
        File.CreateSymbolicLink(link, Path.Combine("alias", "..", "target.json"));
        Assert.Equal("10\n", File.ReadAllText(link));

        AtomicFile.Write(link, "30"u8);

        Assert.Equal("30\n", File.ReadAllText(link));
        Assert.Equal("30\n", File.ReadAllText(dir.File(Path.Combine("other", "target.json"))));
        Assert.Equal("20\n", File.ReadAllText(dir.File("target.json")));
    }

    [Fact]
    public void Write_ThroughLinkWithDotDotAfterAMissingDirectory_ThrowsAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // linked.json -> missing/../target.json. The file system cannot open it (ENOENT), but a walk that drops the missing
        // directory would reach the target.json beside the link, which must not be overwritten.
        using var dir = new TempDir();
        AtomicFile.Write(dir.File("target.json"), "1"u8);
        string link = dir.File("linked.json");
        File.CreateSymbolicLink(link, Path.Combine("missing", "..", "target.json"));
        byte[] before = File.ReadAllBytes(dir.File("target.json"));

        Assert.Throws<IOException>(() => AtomicFile.Write(link, "2"u8));

        Assert.Equal(before, File.ReadAllBytes(dir.File("target.json")));
        Assert.Equal(Path.Combine("missing", "..", "target.json"), new FileInfo(link).LinkTarget);
        Assert.Equal(new[] { "linked.json", "target.json" }, Directory.GetFileSystemEntries(dir.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Write_ThroughLinkWithDotDotAfterARegularFile_ThrowsAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // linked.json -> blocker/../target.json, with blocker a regular file: opening it fails with ENOTDIR.
        using var dir = new TempDir();
        AtomicFile.Write(dir.File("target.json"), "1"u8);
        File.WriteAllText(dir.File("blocker"), "not a directory");
        string link = dir.File("linked.json");
        File.CreateSymbolicLink(link, Path.Combine("blocker", "..", "target.json"));
        byte[] before = File.ReadAllBytes(dir.File("target.json"));

        Assert.Throws<IOException>(() => AtomicFile.Write(link, "2"u8));

        Assert.Equal(before, File.ReadAllBytes(dir.File("target.json")));
        Assert.Equal(Path.Combine("blocker", "..", "target.json"), new FileInfo(link).LinkTarget);
        Assert.Equal(new[] { "blocker", "linked.json", "target.json" }, Directory.GetFileSystemEntries(dir.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Write_InADirectoryThatIsNotWritable_OverwritesTheWritableFileInPlace()
    {
        if (!OperatingSystem.IsLinux() || IsRoot())
        {
            return; // root can rename into a directory it cannot write, so the in-place branch is not reached
        }

        using var dir = new TempDir();
        string sub = dir.File("locked");
        Directory.CreateDirectory(sub);
        string path = Path.Combine(sub, "config.json");
        AtomicFile.Write(path, "1"u8);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        string inode = Inode(path);
        UnixFileMode dirMode = File.GetUnixFileMode(sub);
        File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute); // no file can be created or removed
        try
        {
            AtomicFile.Write(path, "7"u8);

            Assert.Equal(inode, Inode(path)); // the same file, written in place: a replacement would have a new inode
            Assert.Equal("7\n", File.ReadAllText(path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, File.GetUnixFileMode(path));
            Assert.Equal(new[] { "config.json" }, Directory.GetFiles(sub).Select(Path.GetFileName));
        }
        finally
        {
            File.SetUnixFileMode(sub, dirMode);
        }
    }

    [Fact]
    public void Write_InPlaceOverAShorterFile_LeavesNoTrailingBytes()
    {
        if (!OperatingSystem.IsLinux() || IsRoot())
        {
            return; // root replaces the file instead of writing it in place
        }

        using var dir = new TempDir();
        string sub = dir.File("locked");
        Directory.CreateDirectory(sub);
        string path = Path.Combine(sub, "config.json");
        AtomicFile.Write(path, System.Text.Encoding.UTF8.GetBytes(new string('x', 500)));
        string inode = Inode(path);
        UnixFileMode dirMode = File.GetUnixFileMode(sub);
        File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            AtomicFile.Write(path, "3"u8);

            Assert.Equal(inode, Inode(path));
            Assert.Equal("3\n", File.ReadAllText(path));
            Assert.DoesNotContain("xxx", File.ReadAllText(path));
        }
        finally
        {
            File.SetUnixFileMode(sub, dirMode);
        }
    }

    [Fact]
    public void Write_FileThatIsNotWritableInADirectoryThatIsNotWritable_ThrowsAndChangesNothing()
    {
        if (OperatingSystem.IsWindows() || IsRoot())
        {
            return; // root writes a read-only file, so there is no denial to expect
        }

        using var dir = new TempDir();
        string sub = dir.File("locked");
        Directory.CreateDirectory(sub);
        string path = Path.Combine(sub, "config.json");
        AtomicFile.Write(path, "1"u8);
        File.SetUnixFileMode(path, UnixFileMode.UserRead);
        UnixFileMode dirMode = File.GetUnixFileMode(sub);
        File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => AtomicFile.Write(path, "7"u8));

            Assert.Equal("1\n", File.ReadAllText(path));
            Assert.Equal(new[] { "config.json" }, Directory.GetFiles(sub).Select(Path.GetFileName));
        }
        finally
        {
            File.SetUnixFileMode(sub, dirMode);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{}\n")]
    [InlineData("tail\n\n")]
    [InlineData("a\r\nb\r\n")]
    [InlineData("lone\rcarriage")]
    [InlineData("nul\0inside")]
    public void WriteExact_WritesTheExactBytesWithNoNewlineAdded(string text)
    {
        using var dir = new TempDir();
        string path = dir.File("config.json");

        AtomicFile.WriteExact(path, System.Text.Encoding.UTF8.GetBytes(text));

        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(text), File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteExact_AndWrite_DifferOnlyByTheTrailingNewline()
    {
        using var dir = new TempDir();
        string exact = dir.File("exact.json");
        string withNewline = dir.File("newline.json");

        AtomicFile.WriteExact(exact, "[1, 2]"u8);
        AtomicFile.Write(withNewline, "[1, 2]"u8);

        Assert.Equal("[1, 2]"u8.ToArray(), File.ReadAllBytes(exact));
        Assert.Equal("[1, 2]\n"u8.ToArray(), File.ReadAllBytes(withNewline));
    }

    [Fact]
    public void WriteExact_ShorterContentOverLongerFile_LeavesNoTrailingBytes()
    {
        using var dir = new TempDir();
        string path = dir.File("config.json");
        AtomicFile.WriteExact(path, System.Text.Encoding.UTF8.GetBytes(new string('x', 500)));

        AtomicFile.WriteExact(path, "3"u8);

        Assert.Equal("3"u8.ToArray(), File.ReadAllBytes(path));
        Assert.Equal(new[] { "config.json" }, Directory.GetFiles(dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void WriteExact_CreatesMissingDirectoryAndLeavesNoTempFiles()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "nested", "deeper", "config.json");

        AtomicFile.WriteExact(path, "{}"u8);

        Assert.Equal("{}"u8.ToArray(), File.ReadAllBytes(path));
        Assert.Equal(new[] { "config.json" }, Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    [Fact]
    public void WriteExact_KeepsExistingWorldWritableMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // The default umask would narrow a mode created from this one, so this proves the mode is set explicitly.
        const UnixFileMode everyone = UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite;
        using var dir = new TempDir();
        string path = dir.File("config.json");
        AtomicFile.WriteExact(path, "1"u8);
        File.SetUnixFileMode(path, everyone);

        AtomicFile.WriteExact(path, "3"u8);

        Assert.Equal(everyone, File.GetUnixFileMode(path));
        Assert.Equal("3"u8.ToArray(), File.ReadAllBytes(path));
    }

    [Fact]
    public void WriteExact_ThroughRelativeSymlink_UpdatesTargetAndKeepsLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        string target = dir.File("target.json");
        string link = dir.File("linked.json");
        AtomicFile.WriteExact(target, "1"u8);
        File.CreateSymbolicLink(link, "target.json");

        AtomicFile.WriteExact(link, "5"u8);

        Assert.Equal("target.json", new FileInfo(link).LinkTarget);
        Assert.Equal("5"u8.ToArray(), File.ReadAllBytes(target));
        Assert.Equal("5"u8.ToArray(), File.ReadAllBytes(link));
        Assert.Equal(new[] { "linked.json", "target.json" }, Directory.GetFiles(dir.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void WriteExact_ThroughDanglingSymlink_ThrowsAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        Directory.CreateDirectory(dir.File("a"));
        Directory.CreateDirectory(dir.File("b"));
        string link = dir.File(Path.Combine("a", "linked.json"));
        string missing = Path.Combine("..", "b", "missing.json");
        File.CreateSymbolicLink(link, missing);

        IOException ex = Assert.Throws<IOException>(() => AtomicFile.WriteExact(link, "{}"u8));

        Assert.Contains("which does not exist", ex.Message);
        Assert.Equal(missing, new FileInfo(link).LinkTarget);
        Assert.Equal(new[] { "linked.json" }, Directory.GetFiles(dir.File("a")).Select(Path.GetFileName));
        Assert.Empty(Directory.GetFiles(dir.File("b")));
    }

    [Fact]
    public void WriteExact_ThroughLinkWithDotDotAfterAMissingDirectory_ThrowsAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Same walk rule as Write: a missing directory on the way is an error, never dropped.
        using var dir = new TempDir();
        AtomicFile.WriteExact(dir.File("target.json"), "1"u8);
        string link = dir.File("linked.json");
        File.CreateSymbolicLink(link, Path.Combine("missing", "..", "target.json"));
        byte[] before = File.ReadAllBytes(dir.File("target.json"));

        Assert.Throws<IOException>(() => AtomicFile.WriteExact(link, "2"u8));

        Assert.Equal(before, File.ReadAllBytes(dir.File("target.json")));
        Assert.Equal(Path.Combine("missing", "..", "target.json"), new FileInfo(link).LinkTarget);
    }

    [Fact]
    public void WriteExact_InADirectoryThatIsNotWritable_OverwritesTheWritableFileInPlace()
    {
        if (!OperatingSystem.IsLinux() || IsRoot())
        {
            return; // root can rename into a directory it cannot write, so the in-place branch is not reached
        }

        using var dir = new TempDir();
        string sub = dir.File("locked");
        Directory.CreateDirectory(sub);
        string path = Path.Combine(sub, "config.json");
        AtomicFile.WriteExact(path, "1"u8);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        string inode = Inode(path);
        UnixFileMode dirMode = File.GetUnixFileMode(sub);
        File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute); // no file can be created or removed
        try
        {
            AtomicFile.WriteExact(path, "7"u8);

            Assert.Equal(inode, Inode(path)); // the same file, written in place: a replacement would have a new inode
            Assert.Equal("7"u8.ToArray(), File.ReadAllBytes(path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, File.GetUnixFileMode(path));
            Assert.Equal(new[] { "config.json" }, Directory.GetFiles(sub).Select(Path.GetFileName));
        }
        finally
        {
            File.SetUnixFileMode(sub, dirMode);
        }
    }

    [Fact]
    public void WriteExact_InPlaceOverAShorterFile_LeavesNoTrailingBytes()
    {
        if (!OperatingSystem.IsLinux() || IsRoot())
        {
            return; // root replaces the file instead of writing it in place
        }

        using var dir = new TempDir();
        string sub = dir.File("locked");
        Directory.CreateDirectory(sub);
        string path = Path.Combine(sub, "config.json");
        AtomicFile.WriteExact(path, System.Text.Encoding.UTF8.GetBytes(new string('x', 500)));
        string inode = Inode(path);
        UnixFileMode dirMode = File.GetUnixFileMode(sub);
        File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            AtomicFile.WriteExact(path, "3"u8);

            Assert.Equal(inode, Inode(path));
            Assert.Equal("3"u8.ToArray(), File.ReadAllBytes(path));
        }
        finally
        {
            File.SetUnixFileMode(sub, dirMode);
        }
    }

    [Fact]
    public void WriteExact_FileThatIsNotWritableInADirectoryThatIsNotWritable_ThrowsAndChangesNothing()
    {
        if (OperatingSystem.IsWindows() || IsRoot())
        {
            return; // root writes a read-only file, so there is no denial to expect
        }

        using var dir = new TempDir();
        string sub = dir.File("locked");
        Directory.CreateDirectory(sub);
        string path = Path.Combine(sub, "config.json");
        AtomicFile.WriteExact(path, "1"u8);
        File.SetUnixFileMode(path, UnixFileMode.UserRead);
        UnixFileMode dirMode = File.GetUnixFileMode(sub);
        File.SetUnixFileMode(sub, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => AtomicFile.WriteExact(path, "7"u8));

            Assert.Equal("1"u8.ToArray(), File.ReadAllBytes(path));
            Assert.Equal(new[] { "config.json" }, Directory.GetFiles(sub).Select(Path.GetFileName));
        }
        finally
        {
            File.SetUnixFileMode(sub, dirMode);
        }
    }

    // File.Replace failed with ERROR_UNABLE_TO_MOVE_REPLACEMENT (1176): the destination is gone and the new content is only in the temp file.
    // RecoverFromFailedReplace is called directly, so these run on any OS.
    [Fact]
    public void RecoverFromFailedReplace_UnableToMove_MovesTheTempFileIntoPlace()
    {
        using var dir = new TempDir();
        string temp = dir.File(".target.json.tmp");
        File.WriteAllText(temp, "new\n");

        AtomicFile.RecoverFromFailedReplace(UnableToMoveReplacement(), temp, dir.File("target.json"));

        Assert.Equal("new\n", File.ReadAllText(dir.File("target.json")));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public void RecoverFromFailedReplace_UnableToMoveAndTheMoveFails_ThrowsNamingTheKeptTempFile()
    {
        using var dir = new TempDir();
        string temp = dir.File(".target.json.tmp");
        File.WriteAllText(temp, "new\n");
        string destination = dir.File("target.json");
        Directory.CreateDirectory(destination); // occupies the destination, so the move cannot happen

        IOException ex = Assert.Throws<IOException>(() => AtomicFile.RecoverFromFailedReplace(UnableToMoveReplacement(), temp, destination));

        Assert.Contains(temp, ex.Message);
        Assert.Equal("new\n", File.ReadAllText(temp));
    }

    [Fact]
    public void RecoverFromFailedReplace_OtherError_RethrowsTheOriginalAndLeavesTheTempFile()
    {
        using var dir = new TempDir();
        string temp = dir.File(".target.json.tmp");
        File.WriteAllText(temp, "new\n");
        var original = new IOException("access", unchecked((int)0x80070005)); // ERROR_ACCESS_DENIED

        IOException thrown = Assert.Throws<IOException>(() => AtomicFile.RecoverFromFailedReplace(original, temp, dir.File("target.json")));

        Assert.Same(original, thrown);
        Assert.False(File.Exists(dir.File("target.json")));
        Assert.True(File.Exists(temp));
    }

    private static IOException UnableToMoveReplacement() => new("unable to move the replacement", unchecked((int)0x80070498));

    private static bool IsRoot() => LinuxNative.GetEffectiveUserId() == 0;

    /// <summary>The inode number of a path, read with stat(1) so that the test does not depend on the code under test.</summary>
    private static string Inode(string path) => RunBounded("stat", "-c", "%i", path);

    /// <summary>
    /// Runs a program and returns its trimmed stdout. Both streams are drained asynchronously; the wait is bounded, and a
    /// process that does not exit in time is killed and reported.
    /// </summary>
    private static string RunBounded(string fileName, params string[] args)
    {
        var info = new System.Diagnostics.ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(info)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        TimeSpan timeout = TimeSpan.FromSeconds(10);
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(timeout);
            throw new TimeoutException($"'{fileName} {string.Join(' ', args)}' did not exit within {timeout.TotalSeconds}s and was killed.");
        }

        if (!Task.WaitAll(new Task[] { stdout, stderr }, timeout))
        {
            throw new TimeoutException($"Output of '{fileName}' did not end within {timeout.TotalSeconds}s after it exited.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{fileName}' exited with {process.ExitCode}: {stderr.Result.Trim()}");
        }

        return stdout.Result.Trim();
    }
}
