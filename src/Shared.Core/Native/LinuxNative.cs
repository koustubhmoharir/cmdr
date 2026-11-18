using System.Runtime.InteropServices;

namespace Shared.Core;

/// <summary>P/Invoke declarations for libc.</summary>
internal static partial class LinuxNative
{
    /// <summary>Reads a file's metadata by path, following symbolic links. Fails with ENOSYS where the C library lacks statx.</summary>
    /// <returns>0 on success, -1 on failure (the error is available from <see cref="Marshal.GetLastPInvokeError"/>).</returns>
    [LibraryImport("libc", EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Statx(int dirfd, string path, int flags, uint mask, out StatxBuffer buffer);

    /// <summary>Changes the owner and group of an open file. A uid or gid of uint.MaxValue leaves that id unchanged.</summary>
    /// <returns>0 on success, -1 on failure (the error is available from <see cref="Marshal.GetLastPInvokeError"/>).</returns>
    [LibraryImport("libc", EntryPoint = "fchown", SetLastError = true)]
    internal static partial int FChown(int fd, uint uid, uint gid);

    /// <summary>Changes the owner and group of a file by path, following symbolic links. A uid or gid of uint.MaxValue leaves that id unchanged.</summary>
    /// <returns>0 on success, -1 on failure (the error is available from <see cref="Marshal.GetLastPInvokeError"/>).</returns>
    [LibraryImport("libc", EntryPoint = "chown", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Chown(string path, uint uid, uint gid);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    internal static partial uint GetEffectiveUserId();

    /// <summary>The leading fields of <c>struct statx</c> (256 bytes, the same layout on every architecture), at their fixed offsets.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    internal struct StatxBuffer
    {
        /// <summary>The fields the kernel filled in (STATX_* bits).</summary>
        [FieldOffset(0)]
        public uint Mask;

        [FieldOffset(20)]
        public uint Uid;

        [FieldOffset(24)]
        public uint Gid;

        [FieldOffset(28)]
        public ushort Mode;

        [FieldOffset(32)]
        public ulong Ino;

        [FieldOffset(136)]
        public uint DevMajor;

        [FieldOffset(140)]
        public uint DevMinor;
    }
}
