using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Shared.Core;

/// <summary>
/// The identity of an existing regular file, so that two paths naming one file (hard links) can be recognized. Symbolic links are
/// not resolved here: callers pass paths that are already resolved (the physical-path resolution of the Shared.Core config code).
/// </summary>
public static class FileIdentity
{
    private const int AtFdcwd = -100;
    private const int AtEmptyPath = 0x1000; // AT_EMPTY_PATH
    private const uint StatxTypeIno = 0x1 | 0x100; // STATX_TYPE | STATX_INO
    private const uint StatxBtime = 0x800; // STATX_BTIME: the birth time, which tells a re-created file from one that reused its inode
    private const uint StatxRequested = StatxTypeIno | StatxBtime;
    private const uint SIfmt = 0xF000; // S_IFMT
    private const uint SIfreg = 0x8000; // S_IFREG
    private const uint FileAttributeDirectory = 0x10;

    /// <summary>
    /// A string that is the same for every path naming one existing regular file, and different for other files. On Linux it is the
    /// device, the inode and, when the file system reports it, the birth time, so that a file re-created in place of a deleted one, which
    /// may reuse its inode number, has another identity. On Windows the volume serial number and file index. Null for a path that does not name an existing regular
    /// file, for a path whose identity cannot be read, and on other systems. Never throws for I/O or permission errors.
    /// </summary>
    public static string? TryGet(string path)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                return TryGetLinux(path);
            }

            return OperatingSystem.IsWindows() ? TryGetWindows(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The same identity as <see cref="TryGet(string)"/> gives for the path that the open file was opened from, read from the handle
    /// itself. So the identity of a file can be taken when it is opened and compared with a path's identity later: after a rename
    /// or a re-creation the path may name another file, while the handle's identity stays that of the file it opened. A closed
    /// handle has no identity. Null for a handle whose file is not a regular file, for a handle whose identity cannot be read, and
    /// on other systems. Never throws for I/O or permission errors.
    /// </summary>
    public static string? TryGet(SafeFileHandle handle)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                return TryGetLinux(handle);
            }

            return OperatingSystem.IsWindows() ? TryGetWindows(handle) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // Any failure of statx (including its absence from the C library) means no identity. The mask check matters: a file system that
    // does not provide inode numbers reports zero for them, and every file on it would then look like the same file.
    [UnsupportedOSPlatform("windows")]
    private static string? TryGetLinux(string path)
    {
        LinuxNative.StatxBuffer buffer;
        try
        {
            if (LinuxNative.Statx(AtFdcwd, path, flags: 0, StatxRequested, out buffer) != 0)
            {
                return null;
            }
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }

        return FromStatx(buffer);
    }

    // The handle's descriptor is passed as the directory descriptor with an empty path (AT_EMPTY_PATH), which statx reads as the file
    // itself. Failures are handled as in TryGetLinux(string).
    [UnsupportedOSPlatform("windows")]
    private static string? TryGetLinux(SafeFileHandle handle)
    {
        LinuxNative.StatxBuffer buffer;
        try
        {
            if (LinuxNative.Statx((int)handle.DangerousGetHandle(), string.Empty, AtEmptyPath, StatxRequested, out buffer) != 0)
            {
                return null;
            }
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }

        return FromStatx(buffer);
    }

    // The identity of a regular file from the statx fields. Shared by both Linux variants, so that a path and a handle to one file give
    // the same string. The birth time is part of the identity when the file system reports it: an inode number is reused as soon as its
    // file is deleted, so dev:ino alone cannot tell a re-created file from the one it replaced. When the file system does not report a
    // birth time the identity is dev:ino, as before.
    [UnsupportedOSPlatform("windows")]
    private static string? FromStatx(LinuxNative.StatxBuffer buffer)
    {
        if ((buffer.Mask & StatxTypeIno) != StatxTypeIno || (buffer.Mode & SIfmt) != SIfreg)
        {
            return null;
        }

        string identity = $"{buffer.DevMajor}:{buffer.DevMinor}:{buffer.Ino}";
        return (buffer.Mask & StatxBtime) == StatxBtime
            ? $"{identity}:{buffer.BtimeSec}.{buffer.BtimeNsec}"
            : identity;
    }

    // NOT VERIFIED ON WINDOWS: written without a Windows machine to run it on. The file index is 64 bits; ReFS can have 128-bit file
    // IDs, which this does not use, so identities there are only as unique as the 64-bit index.
    [SupportedOSPlatform("windows")]
    private static string? TryGetWindows(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return TryGetWindows(handle);
    }

    // The identity of the file an open handle refers to; TryGetWindows(string) opens the path and then calls this.
    [SupportedOSPlatform("windows")]
    private static string? TryGetWindows(SafeFileHandle handle)
    {
        if (WindowsNative.GetFileInformationByHandle(handle.DangerousGetHandle(), out WindowsNative.BY_HANDLE_FILE_INFORMATION info) == 0)
        {
            return null;
        }

        if ((info.FileAttributes & FileAttributeDirectory) != 0)
        {
            return null;
        }

        return $"{info.VolumeSerialNumber}:{info.FileIndexHigh}:{info.FileIndexLow}";
    }
}
