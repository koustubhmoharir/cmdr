using System.Runtime.Versioning;

namespace Shared.Processes;

/// <summary>
/// Recognises Windows app execution aliases: the zero-byte reparse points in <c>%LOCALAPPDATA%\Microsoft\WindowsApps</c>, such as
/// the one for a Microsoft Store <c>pwsh.exe</c>. The normal launch (full path as the application name, created in its job by
/// PROC_THREAD_ATTRIBUTE_JOB_LIST) fails for one with access denied, so <see cref="WindowsProcessPlatform"/> launches these with
/// <see cref="WindowsCreateStrategy.AppAlias"/>. Which of the two differences is needed is recorded by WindowsAppAliasTests.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsAppAlias
{
    /// <summary>
    /// True when <paramref name="path"/> names an existing app execution alias: a reparse point whose reparse tag is
    /// IO_REPARSE_TAG_APPEXECLINK. A missing path, an ordinary file, or a failure to read the entry returns false.
    /// </summary>
    internal static unsafe bool IsAppExecLink(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        WIN32_FIND_DATAW data = default;
        nint handle = WindowsNative.FindFirstFileW(path, &data);
        if (handle == WindowsNative.InvalidHandleValue)
        {
            return false;
        }

        try
        {
            return (data.DwFileAttributes & WindowsNative.FileAttributeReparsePoint) != 0
                && data.DwReserved0 == WindowsNative.IoReparseTagAppExecLink;
        }
        finally
        {
            WindowsNative.FindClose(handle);
        }
    }
}
