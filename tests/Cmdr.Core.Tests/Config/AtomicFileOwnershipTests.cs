using System.Diagnostics;
using System.Runtime.Versioning;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Tests.Config;

/// <summary>
/// Owner, group and mode handling of <see cref="AtomicFile"/> on Linux. Results are read with stat(1), not with the code under test.
/// Tests that need passwordless sudo, or a supplementary group, or root, return early when those are not available.
/// </summary>
public class AtomicFileOwnershipTests
{
    private const uint NobodyUid = 65534;

    private sealed record Stat(uint Uid, uint Gid, string Mode, string Inode);

    [Theory]
    [InlineData(false, "660")]
    [InlineData(true, "2660")] // setgid survives: the mode is set after the chown, which clears it for a non-root user
    public void Owner_FileInSupplementaryGroup_KeepsOwnerGroupAndMode(bool setgid, string expectedMode)
    {
        if (!OperatingSystem.IsLinux() || SupplementaryGroup() is not uint group) return;

        using var dir = new TempDir();
        string file = dir.File("config.json");
        File.WriteAllText(file, "old\n");
        Require("chgrp", group.ToString(), file);
        UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite;
        File.SetUnixFileMode(file, setgid ? mode | UnixFileMode.SetGroup : mode);
        Stat before = StatOf(file);

        AtomicFile.Write(file, "new"u8, keepOtherOwners: false);

        Stat after = StatOf(file);
        Assert.Equal(before.Uid, after.Uid);
        Assert.Equal(before.Gid, after.Gid);
        Assert.Equal(expectedMode, after.Mode);
        Assert.NotEqual(before.Inode, after.Inode);
        Assert.Equal("new\n", File.ReadAllText(file));
        Assert.Equal(["config.json"], FileNames(dir));
    }

    [Fact]
    public void NotOwner_WithoutOptOut_ReplacesFileAndSavingUserOwnsIt()
    {
        if (!OperatingSystem.IsLinux() || IsRoot() || SupplementaryGroup() is not uint group || !SudoAvailable()) return;

        using var dir = new TempDir();
        string file = dir.File("config.json");
        File.WriteAllText(file, "old\n");
        GiveToOtherUser(file, group, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead);
        Stat before = StatOf(file);

        AtomicFile.Write(file, "new"u8, keepOtherOwners: false);

        Stat after = StatOf(file);
        Assert.Equal(CurrentUid(), after.Uid);
        Assert.Equal(before.Gid, after.Gid);
        Assert.Equal("664", after.Mode);
        Assert.NotEqual(before.Inode, after.Inode);
        Assert.Equal("new\n", File.ReadAllText(file));
        Assert.Equal(["config.json"], FileNames(dir));
    }

    [Fact]
    public void NotOwner_WithOptOut_WritesInPlaceAndKeepsOwner()
    {
        if (!OperatingSystem.IsLinux() || IsRoot() || SupplementaryGroup() is not uint group || !SudoAvailable()) return;

        using var dir = new TempDir();
        string file = dir.File("config.json");
        File.WriteAllText(file, "old\n");
        GiveToOtherUser(file, group, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead);
        Stat before = StatOf(file);

        AtomicFile.Write(file, "new"u8, keepOtherOwners: true);

        Stat after = StatOf(file);
        Assert.Equal(NobodyUid, after.Uid);
        Assert.Equal(before.Gid, after.Gid);
        Assert.Equal(before.Inode, after.Inode);
        Assert.Equal("new\n", File.ReadAllText(file));
    }

    [Fact]
    public void NotOwner_NotWritable_ThrowsAndLeavesFileUnchanged()
    {
        if (!OperatingSystem.IsLinux() || IsRoot() || SupplementaryGroup() is not uint group || !SudoAvailable()) return;

        using var dir = new TempDir();
        string file = dir.File("config.json");
        File.WriteAllText(file, "old\n");
        GiveToOtherUser(file, group, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        Stat before = StatOf(file);

        Assert.Throws<UnauthorizedAccessException>(() => AtomicFile.Write(file, "new"u8, keepOtherOwners: false));

        Assert.Equal("old\n", File.ReadAllText(file));
        Assert.Equal(before.Inode, StatOf(file).Inode);
        Assert.Equal(["config.json"], FileNames(dir));
    }

    [Fact]
    public void Owner_NotInFileGroup_WritesInPlaceBecauseChownIsRefused()
    {
        if (!OperatingSystem.IsLinux() || IsRoot() || !SudoAvailable()) return;

        using var dir = new TempDir();
        string file = dir.File("config.json");
        File.WriteAllText(file, "old\n");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead);
        Require("sudo", "-n", "chgrp", "0", file);
        Stat before = StatOf(file);

        AtomicFile.Write(file, "new"u8, keepOtherOwners: false);

        Stat after = StatOf(file);
        Assert.Equal(CurrentUid(), after.Uid);
        Assert.Equal(0u, after.Gid);
        Assert.Equal(before.Inode, after.Inode);
        Assert.Equal("new\n", File.ReadAllText(file));
        Assert.Equal(["config.json"], FileNames(dir));
    }

    [Fact]
    public void Root_ReplacesFileAndKeepsOwnerGroupAndMode()
    {
        if (!OperatingSystem.IsLinux() || !IsRoot()) return;

        AssertRootSaveKeepsOwnershipAndMode(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, "640");
    }

    [Fact]
    public void Root_WithSetgid_KeepsSetgidInMode()
    {
        if (!OperatingSystem.IsLinux() || !IsRoot()) return;

        AssertRootSaveKeepsOwnershipAndMode(
            UnixFileMode.SetGroup | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead,
            "2664");
    }

    [UnsupportedOSPlatform("windows")]
    private static void AssertRootSaveKeepsOwnershipAndMode(UnixFileMode mode, string expectedMode)
    {
        using var dir = new TempDir();
        string file = dir.File("root.txt");
        File.WriteAllText(file, "old\n");
        Require("chown", $"{NobodyUid}:{NobodyUid}", file);
        File.SetUnixFileMode(file, mode);
        Stat before = StatOf(file);

        AtomicFile.Write(file, "new"u8, keepOtherOwners: false);

        Stat after = StatOf(file);
        Assert.Equal(NobodyUid, after.Uid);
        Assert.Equal(NobodyUid, after.Gid);
        Assert.Equal(expectedMode, after.Mode);
        Assert.NotEqual(before.Inode, after.Inode);
        Assert.Equal("new\n", File.ReadAllText(file));
    }

    /// <summary>Gives the file to another user, with the mode set first because the test user can no longer change it.</summary>
    [UnsupportedOSPlatform("windows")]
    private static void GiveToOtherUser(string file, uint group, UnixFileMode mode)
    {
        File.SetUnixFileMode(file, mode);
        Require("sudo", "-n", "chown", $"{NobodyUid}:{group}", file);
    }

    /// <summary>A group the current user belongs to, other than its primary group; null when there is none.</summary>
    private static uint? SupplementaryGroup()
    {
        uint primary = uint.Parse(Output("id", "-g"));
        return Output("id", "-G").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(g => uint.Parse(g))
            .Where(g => g != primary)
            .Cast<uint?>()
            .FirstOrDefault();
    }

    // Root takes the owner's path whatever the file, so the tests of a non-owner user cannot run as root.
    private static bool IsRoot() => LinuxNative.GetEffectiveUserId() == 0;

    private static bool SudoAvailable()
    {
        try
        {
            return Run("sudo", "-n", "true").ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // no sudo installed
        }
    }

    private static uint CurrentUid() => uint.Parse(Output("id", "-u"));

    private static Stat StatOf(string path)
    {
        string[] fields = Output("stat", "-c", "%u %g %a %i", path).Split(' ');
        return new Stat(uint.Parse(fields[0]), uint.Parse(fields[1]), fields[2], fields[3]);
    }

    private static string[] FileNames(TempDir dir) =>
        Directory.GetFileSystemEntries(dir.Path).Select(p => Path.GetFileName(p)).ToArray();

    private static string Output(string fileName, params string[] args)
    {
        (int exitCode, string output) = Run(fileName, args);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"'{fileName} {string.Join(' ', args)}' exited with {exitCode}.");
        }

        return output;
    }

    private static void Require(string fileName, params string[] args) => Output(fileName, args);

    private static (int ExitCode, string Output) Run(string fileName, params string[] args)
    {
        var info = new ProcessStartInfo(fileName) { RedirectStandardOutput = true };
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(info)!;
        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
