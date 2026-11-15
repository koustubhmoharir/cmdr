using System.Runtime.Versioning;
using Cmdr.Core.Processes;
using Microsoft.Win32.SafeHandles;

namespace Cmdr.Core.Config;

/// <summary>
/// The identity of an existing regular file, so that two paths naming one file (hard links) can be recognized. Symbolic links are
/// not resolved here: callers pass paths that are already resolved (see <see cref="PhysicalPath"/>).
/// </summary>
internal static class FileIdentity
{
    private const int AtFdcwd = -100;
    private const uint StatxTypeIno = 0x1 | 0x100; // STATX_TYPE | STATX_INO
    private const uint SIfmt = 0xF000; // S_IFMT
    private const uint SIfreg = 0x8000; // S_IFREG
    private const uint FileAttributeDirectory = 0x10;

    /// <summary>
    /// A string that is the same for every path naming one existing regular file, and different for other files. On Linux it is the
    /// device and inode; on Windows the volume serial number and file index. Null for a path that does not name an existing regular
    /// file, for a path whose identity cannot be read, and on other systems. Never throws for I/O or permission errors.
    /// </summary>
    internal static string? TryGet(string path)
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

    // Any failure of statx (including its absence from the C library) means no identity. The mask check matters: a file system that
    // does not provide inode numbers reports zero for them, and every file on it would then look like the same file.
    [UnsupportedOSPlatform("windows")]
    private static string? TryGetLinux(string path)
    {
        LinuxNative.StatxBuffer buffer;
        try
        {
            if (LinuxNative.Statx(AtFdcwd, path, flags: 0, StatxTypeIno, out buffer) != 0)
            {
                return null;
            }
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }

        if ((buffer.Mask & StatxTypeIno) != StatxTypeIno || (buffer.Mode & SIfmt) != SIfreg)
        {
            return null;
        }

        return $"{buffer.DevMajor}:{buffer.DevMinor}:{buffer.Ino}";
    }

    // NOT VERIFIED ON WINDOWS: written without a Windows machine to run it on. The file index is 64 bits; ReFS can have 128-bit file
    // IDs, which this does not use, so identities there are only as unique as the 64-bit index.
    [SupportedOSPlatform("windows")]
    private static string? TryGetWindows(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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
