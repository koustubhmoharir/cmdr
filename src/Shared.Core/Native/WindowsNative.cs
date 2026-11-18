using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Shared.Core;

/// <summary>P/Invoke declarations for the Win32 file information API.</summary>
/// <remarks>
/// The parameters are blittable (<see cref="nint"/> for the handle, and a struct passed by <c>out</c>). BOOL return values are declared
/// as <see cref="int"/>: non-zero is success.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static partial class WindowsNative
{
    private const string Kernel32 = "kernel32.dll";

    /// <summary>Fills <paramref name="info"/> for an open file. Non-zero is success.</summary>
    [LibraryImport(Kernel32, EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    internal static partial int GetFileInformationByHandle(nint file, out BY_HANDLE_FILE_INFORMATION info);

    /// <summary>BY_HANDLE_FILE_INFORMATION: the attributes, the volume serial number and the file index (which with the serial identifies a file).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
