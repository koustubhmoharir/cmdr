using System.Diagnostics;
using Cmdr.Core.Output;
using Shared.Processes;

namespace Cmdr.Core.Tests.Config;

/// <summary>
/// Owner and group of files and folders that cmdr creates when it runs as root on Linux. The rule itself is tested with explicit
/// sudo values; the root tests use the SUDO_UID and SUDO_GID that sudo sets, and return early without them. Results are read with
/// stat(1), not with the code under test.
/// </summary>
public class RootCreatedOwnershipTests
{
    private const uint NobodyUid = 65534;
    private const uint OtherGid = 65533;

    private sealed record Stat(uint Uid, uint Gid, string Mode);

    [Fact]
    public void Log_UnderRoot_NewFoldersAndLogFileOwnedBySudoUser()
    {
        if (!OperatingSystem.IsLinux() || !IsRoot() || SudoIds() is not { } sudo) return;

        using var dir = new TempDir();
        string log = dir.File("a/b/x.log");
        using (var output = new CommandOutput(100))
        {
            output.EnableLog(log);
            output.WriteSystem("hello");
            output.DisableLog();
            Assert.True(output.WaitForLogWrites(TimeSpan.FromSeconds(10)), "log writes did not finish");
        }

        Assert.Equal((sudo.Uid, sudo.Gid), OwnerOf(dir.File("a")));
        Assert.Equal((sudo.Uid, sudo.Gid), OwnerOf(dir.File("a/b")));
        Assert.Equal((sudo.Uid, sudo.Gid), OwnerOf(log));
        Assert.Equal("hello\n", File.ReadAllText(log));
    }

    [Fact]
    public void Log_UnderSetgidParent_InheritsParentGroupAndKeepsSetgid()
    {
        if (!OperatingSystem.IsLinux() || !IsRoot() || SudoIds() is not { } sudo) return;

        using var dir = new TempDir();
        string parent = dir.File("parent");
        Directory.CreateDirectory(parent);
        Require("chown", $"{NobodyUid}:{OtherGid}", parent);
        File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute | UnixFileMode.SetGroup);

        string log = Path.Combine(parent, "a", "b", "x.log");
        using (var output = new CommandOutput(100))
        {
            output.EnableLog(log);
            output.DisableLog();
            Assert.True(output.WaitForLogWrites(TimeSpan.FromSeconds(10)), "log writes did not finish");
        }

        foreach (string folder in new[] { Path.Combine(parent, "a"), Path.Combine(parent, "a", "b") })
        {
            Stat folderStat = StatOf(folder);
            Assert.Equal(sudo.Uid, folderStat.Uid);
            Assert.Equal(OtherGid, folderStat.Gid);
            int bits = Convert.ToInt32(folderStat.Mode, 8);
            Assert.True((bits & 0x400) != 0, $"setgid lost on {folder}: {folderStat.Mode}"); // 0o2000, as UnixFileMode.SetGroup
        }

        Assert.Equal((sudo.Uid, OtherGid), OwnerOf(log));
    }

    [Fact]
    public void Log_UnderRoot_ExistingLogAndFolderKeepTheirOwner()
    {
        if (!OperatingSystem.IsLinux() || !IsRoot()) return;

        using var dir = new TempDir();
        string existingFolder = dir.File("existing");
        Directory.CreateDirectory(existingFolder);
        string log = Path.Combine(existingFolder, "x.log");
        File.WriteAllText(log, "old\n");
        Require("chown", $"{NobodyUid}:{NobodyUid}", existingFolder, log);
        var folderBefore = OwnerOf(existingFolder);
        var logBefore = OwnerOf(log);

        using (var output = new CommandOutput(100))
        {
            output.EnableLog(log);
            output.DisableLog();
            Assert.True(output.WaitForLogWrites(TimeSpan.FromSeconds(10)), "log writes did not finish");
        }

        Assert.Equal(folderBefore, OwnerOf(existingFolder));
        Assert.Equal(logBefore, OwnerOf(log));
        Assert.Equal((NobodyUid, NobodyUid), logBefore);
        Assert.Equal("old\n", File.ReadAllText(log));
    }

    private static (uint Uid, uint Gid) OwnerOf(string path)
    {
        Stat stat = StatOf(path);
        return (stat.Uid, stat.Gid);
    }

    private static bool IsRoot() => LinuxNative.GetEffectiveUserId() == 0;

    /// <summary>The ids sudo recorded in the environment; null unless both are set and parse.</summary>
    private static (uint Uid, uint Gid)? SudoIds()
    {
        return uint.TryParse(Environment.GetEnvironmentVariable("SUDO_UID"), out uint uid)
            && uint.TryParse(Environment.GetEnvironmentVariable("SUDO_GID"), out uint gid)
                ? (uid, gid)
                : null;
    }

    private static Stat StatOf(string path)
    {
        string[] fields = Output("stat", "-c", "%u %g %a", path).Split(' ');
        return new Stat(uint.Parse(fields[0]), uint.Parse(fields[1]), fields[2]);
    }

    private static void Require(string fileName, params string[] args) => Output(fileName, args);

    private static string Output(string fileName, params string[] args)
    {
        var info = new ProcessStartInfo(fileName) { RedirectStandardOutput = true };
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{fileName} {string.Join(' ', args)}' exited with {process.ExitCode}.");
        }

        return output;
    }
}
