using System.Diagnostics;
using System.Runtime.Versioning;
using Shared.Core;

namespace Shared.Core.Tests.Config;

/// <summary>
/// Owner and group of files and folders that cmdr creates when it runs as root on Linux. The rule itself is tested with explicit
/// sudo values; the root tests use the SUDO_UID and SUDO_GID that sudo sets, and return early without them. Results are read with
/// stat(1), not with the code under test.
/// </summary>
public class RootCreatedOwnershipTests
{
    private const uint NobodyUid = 65534;

    private sealed record Stat(uint Uid, uint Gid, string Mode);

    [Fact]
    public void OwnerForNewItem_SudoIdsAndPlainParent_UsesSudoIds()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string parent = PlainFolder(dir, "parent");

        (uint uid, uint gid)? owner = UnixOwnership.OwnerForNewItem(parent, "1000", "1000");

        Assert.Equal((1000u, 1000u), owner);
    }

    [Fact]
    public void OwnerForNewItem_SetgidParent_KeepsParentGroup()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string parent = dir.File("parent");
        Directory.CreateDirectory(parent);
        File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.SetGroup);
        Stat parentStat = StatOf(parent);

        (uint uid, uint gid)? owner = UnixOwnership.OwnerForNewItem(parent, "1000", "4242");

        Assert.Equal((1000u, parentStat.Gid), owner);
    }

    [Fact]
    public void OwnerForNewItem_NoSudo_UsesParentOwnerAndGroup()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string parent = PlainFolder(dir, "parent");
        Stat parentStat = StatOf(parent);

        (uint uid, uint gid)? owner = UnixOwnership.OwnerForNewItem(parent, null, null);

        Assert.Equal((parentStat.Uid, parentStat.Gid), owner);
    }

    [Fact]
    public void OwnerForNewItem_SudoUidWithoutSudoGid_UsesSudoUidAndParentGroup()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string parent = PlainFolder(dir, "parent");
        Stat parentStat = StatOf(parent);

        (uint uid, uint gid)? owner = UnixOwnership.OwnerForNewItem(parent, "1000", null);

        Assert.Equal((1000u, parentStat.Gid), owner);
    }

    [Fact]
    public void AtomicWrite_NewFileInNewFolder_OwnedBySudoUserWithNoTempFileLeft()
    {
        if (!OperatingSystem.IsLinux() || !IsRoot() || SudoIds() is not { } sudo) return;

        using var dir = new TempDir();
        string file = dir.File("made/sub/config.json");

        AtomicFile.Write(file, "new"u8, keepOtherOwners: false);

        Assert.Equal((sudo.Uid, sudo.Gid), OwnerOf(dir.File("made")));
        Assert.Equal((sudo.Uid, sudo.Gid), OwnerOf(dir.File("made/sub")));
        Assert.Equal((sudo.Uid, sudo.Gid), OwnerOf(file));
        Assert.Equal("new\n", File.ReadAllText(file));
        Assert.Equal(["config.json"], FileNames(dir.File("made/sub")));
    }

    [UnsupportedOSPlatform("windows")]
    private static string PlainFolder(TempDir dir, string name)
    {
        string folder = dir.File(name);
        Directory.CreateDirectory(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return folder;
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

    private static string[] FileNames(string folder) =>
        Directory.GetFileSystemEntries(folder).Select(p => Path.GetFileName(p)).ToArray();

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
