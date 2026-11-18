using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Shared.Core;

/// <summary>
/// Unix owner and group handling shared by the code that writes files. On Linux, when the application runs as root, a file or folder it
/// creates is given to the user who ran sudo (SUDO_UID and SUDO_GID), or to the owner of the existing parent folder without sudo.
/// Only new items are changed, and a failure to change the owner is ignored. CreateDirectory and GiveNewFileAway may be called on any
/// OS; the other members are Unix-only.
/// </summary>
internal static class UnixOwnership
{
    private const int AtFdcwd = -100;
    private const uint StatxTypeModeUidGid = 0x1 | 0x2 | 0x8 | 0x10; // STATX_TYPE | STATX_MODE | STATX_UID | STATX_GID
    private const int ENOSYS = 38;

    private const string SudoUidVariable = "SUDO_UID";
    private const string SudoGidVariable = "SUDO_GID";

    [UnsupportedOSPlatform("windows")]
    internal readonly record struct FileOwnership(uint Uid, uint Gid, UnixFileMode Mode);

    /// <summary>True when the effective user is root.</summary>
    [UnsupportedOSPlatform("windows")]
    internal static bool IsRoot => LinuxNative.GetEffectiveUserId() == 0;

    /// <summary>
    /// Creates <paramref name="directory"/> and any missing parents. When this process is root on Linux, each directory it created is
    /// given to the owner that <see cref="OwnerForNewItem"/> picks, using the nearest existing ancestor as the parent. Existing
    /// directories are not changed. Otherwise this is <see cref="Directory.CreateDirectory(string)"/>.
    /// </summary>
    /// <exception cref="IOException">The directory cannot be created.</exception>
    internal static void CreateDirectory(string directory)
    {
        if (!OperatingSystem.IsLinux() || !IsRoot)
        {
            Directory.CreateDirectory(directory);
            return;
        }

        string full = Path.GetFullPath(directory);
        var missing = new List<string>();
        string? existing = full;
        while (existing is not null && !Directory.Exists(existing))
        {
            missing.Add(existing);
            existing = Path.GetDirectoryName(existing);
        }

        Directory.CreateDirectory(full);
        if (existing is null || missing.Count == 0)
        {
            return;
        }

        if (OwnerForNewItemFromEnvironment(existing) is not { } owner)
        {
            return;
        }

        // Top-down. Directories keep their setgid bit across chown, so a setgid parent's group is inherited as intended.
        for (int i = missing.Count - 1; i >= 0; i--)
        {
            LinuxNative.Chown(missing[i], owner.Uid, owner.Gid); // best effort: the folder stays root-owned on failure
        }
    }

    /// <summary>
    /// Gives a file that this process has just created in <paramref name="directory"/> to the owner that
    /// <see cref="OwnerForNewItem"/> picks, through its open handle. Does nothing unless this process is root on Linux. Failures are ignored.
    /// </summary>
    internal static void GiveNewFileAway(SafeFileHandle handle, string directory)
    {
        if (!OperatingSystem.IsLinux() || !IsRoot)
        {
            return;
        }

        if (OwnerForNewItemFromEnvironment(directory) is { } owner)
        {
            TryChangeOwner(handle, owner.Uid, owner.Gid);
        }
    }

    /// <summary>
    /// The owner for a new file or folder whose parent is <paramref name="existingParent"/>, given the sudo values. The owner is the
    /// sudo user when <paramref name="sudoUid"/> parses. The group is the parent's group when the parent has setgid, otherwise
    /// <paramref name="sudoGid"/> when it parses, otherwise the parent's group. Without a sudo uid, the parent's owner and group.
    /// Where statx is unavailable the parent cannot be read: the sudo ids are used when both parse, and otherwise there is no owner.
    /// </summary>
    /// <returns>The uid and gid to give the item, or null when it should keep the owner it was created with.</returns>
    /// <exception cref="IOException">statx failed for a reason other than being unavailable.</exception>
    [UnsupportedOSPlatform("windows")]
    internal static (uint Uid, uint Gid)? OwnerForNewItem(string existingParent, string? sudoUid, string? sudoGid)
    {
        bool hasSudoUid = uint.TryParse(sudoUid, NumberStyles.None, CultureInfo.InvariantCulture, out uint uid);
        bool hasSudoGid = uint.TryParse(sudoGid, NumberStyles.None, CultureInfo.InvariantCulture, out uint gid);

        FileOwnership? parent = TryReadOwnership(existingParent);
        if (parent is not { } p)
        {
            return hasSudoUid && hasSudoGid ? (uid, gid) : null;
        }

        if (!hasSudoUid)
        {
            return (p.Uid, p.Gid);
        }

        bool parentIsSetgid = (p.Mode & UnixFileMode.SetGroup) != 0;
        uint newGid = parentIsSetgid ? p.Gid : hasSudoGid ? gid : p.Gid;
        return (uid, newGid);
    }

    /// <summary><see cref="OwnerForNewItem"/> with the SUDO_UID and SUDO_GID environment variables. Null when the owner cannot be read.</summary>
    [UnsupportedOSPlatform("windows")]
    private static (uint Uid, uint Gid)? OwnerForNewItemFromEnvironment(string existingParent)
    {
        try
        {
            return OwnerForNewItem(existingParent, Environment.GetEnvironmentVariable(SudoUidVariable), Environment.GetEnvironmentVariable(SudoGidVariable));
        }
        catch (IOException)
        {
            return null; // best effort: the item keeps the root owner
        }
    }

    /// <summary>The owner, group and mode of the file, or null where statx is unavailable.</summary>
    /// <exception cref="IOException">statx failed for another reason.</exception>
    [UnsupportedOSPlatform("windows")]
    internal static FileOwnership? TryReadOwnership(string file)
    {
        LinuxNative.StatxBuffer buffer;
        int result;
        try
        {
            result = LinuxNative.Statx(AtFdcwd, file, flags: 0, StatxTypeModeUidGid, out buffer);
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }

        if (result != 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            if (errno == ENOSYS)
            {
                return null;
            }

            throw new IOException($"Cannot read the owner of '{file}': statx failed with errno {errno}.");
        }

        return new FileOwnership(buffer.Uid, buffer.Gid, (UnixFileMode)(buffer.Mode & 0xFFF));
    }

    /// <summary>fchown on an open file's descriptor. False when the owner or group cannot be set, whatever the reason.</summary>
    [UnsupportedOSPlatform("windows")]
    internal static bool TryChangeOwner(SafeFileHandle handle, uint uid, uint gid)
    {
        bool added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            return LinuxNative.FChown(handle.DangerousGetHandle().ToInt32(), uid, gid) == 0;
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }
}
