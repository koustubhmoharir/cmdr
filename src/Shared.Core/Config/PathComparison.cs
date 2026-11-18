namespace Shared.Core;

/// <summary>How two file paths are compared for equality: the file system's convention, as far as it can be told from the OS.</summary>
public static class PathComparison
{
    /// <summary>Case-insensitive on Windows and macOS (their default file systems), case-sensitive elsewhere.</summary>
    public static StringComparer Comparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
