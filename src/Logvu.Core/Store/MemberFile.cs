using Shared.Core;

namespace Logvu.Core.Store;

/// <summary>
/// Opens a member's file for one read. The file is opened with the sharing that lets the producer keep writing, rename and
/// delete it, and the identity is taken from the open handle, so the caller knows which file it actually read. Callers dispose
/// the stream right after use: no handle is kept between reads (the "No long-lived handles" rule of the logvu architecture notes).
/// </summary>
public static class MemberFile
{
    /// <summary>The <c>error</c> of <see cref="TryOpen"/> when the path names no file.</summary>
    public const string NotFound = "not found";

    /// <summary>The <c>error</c> of <see cref="TryOpen"/> when the file at the path is not the expected one.</summary>
    public const string IdentityChanged = "identity changed";

    /// <summary>
    /// Opens the file at <paramref name="path"/> for reading. Never throws for I/O or permission errors.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="expectedIdentity">
    /// The identity the member had, or <c>null</c> to accept whatever file the path names. When the opened file has a different
    /// identity, the stream is closed and the open fails with <see cref="IdentityChanged"/>.
    /// </param>
    /// <param name="stream">When the method returns <c>true</c>, the open stream, which the caller disposes; otherwise <c>null</c>.</param>
    /// <param name="identity">When the method returns <c>true</c>, the identity of the opened file; otherwise <c>null</c>.</param>
    /// <param name="error">
    /// When the method returns <c>false</c>, why: <see cref="NotFound"/>, <see cref="IdentityChanged"/>, or the message of the I/O error.
    /// </param>
    /// <returns><c>true</c> when the file was opened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
    public static bool TryOpen(string path, string? expectedIdentity, out FileStream? stream, out string? identity, out string? error)
    {
        ArgumentNullException.ThrowIfNull(path);
        stream = null;
        identity = null;
        error = null;

        FileStream opened;
        try
        {
            // Unbuffered: LineReader and EventText read in blocks of their own.
            opened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            error = NotFound;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }

        string? actual = FileIdentity.TryGet(opened.SafeFileHandle);
        if (actual is null)
        {
            opened.Dispose();
            error = "the identity of the file cannot be read";
            return false;
        }

        if (expectedIdentity is not null && !string.Equals(actual, expectedIdentity, StringComparison.Ordinal))
        {
            opened.Dispose();
            error = IdentityChanged;
            return false;
        }

        stream = opened;
        identity = actual;
        return true;
    }
}
