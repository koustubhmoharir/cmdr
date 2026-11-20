using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;

namespace Shared.Core;

/// <summary>
/// Replaces a file's content, atomically when it can. The content plus a trailing '\n' is written to a hidden temp file beside the
/// destination, flushed to disk, and then moved over the destination. Rules:
/// <list type="bullet">
/// <item>A symbolic link is followed. The final target (see <see cref="PhysicalPath"/>) is replaced and the link itself is left
/// intact. A dangling link is an <see cref="IOException"/>, thrown before anything is created. So is a link whose target goes
/// through a missing or non-directory component (such as <c>missing/../file</c>), which the file system would refuse to open.</item>
/// <item>For a path that is not a link, the parent directory is created if needed.</item>
/// <item>On Unix, an existing destination keeps its owner, group and permission bits exactly, including setuid, setgid and sticky.
/// The temp file is given the destination's owner and group, then its mode, before any content is written, so the umask cannot
/// narrow the mode and Linux's clearing of setuid and setgid on a chown is undone. A write by a non-root user clears setuid and
/// setgid too, so the mode is set once more after the content is written. An in-place write restores the mode the same way when
/// this process owns the file.</item>
/// <item>On Unix, a process that is not root and does not own an existing destination must be able to write it, or the save throws
/// <see cref="UnauthorizedAccessException"/>. The destination is then replaced, and the saved file is owned by the saving user,
/// unless the CMDR_KEEP_OWNER environment variable is set to a non-empty value, in which case it is written in place. A root
/// process always replaces the destination and keeps its owner and group.</item>
/// <item>On Unix, when the temp file cannot be given the destination's owner or group (for example the user is not a member of the
/// group), the destination is written in place instead.</item>
/// <item>On Unix where statx is unavailable, an existing destination is replaced only when this process owns it, and is otherwise
/// written in place. No owner is changed.</item>
/// <item>On Linux, when the application runs as root, a new file or folder it creates is owned by the user who ran sudo, or by the parent folder's
/// owner without sudo (see <see cref="UnixOwnership"/>). An existing file keeps its owner.</item>
/// <item>On Windows, an existing destination is replaced with File.Replace so that its ACL is kept. A new file is moved into place.
/// If File.Replace fails with error 1176 the destination has already been removed, so the temp file is moved into place instead,
/// or kept and named in the error when that fails too. The Windows path is unverified: it has not been run on Windows.</item>
/// <item>An existing destination is overwritten in place instead (not atomic, but the file, its owner and its permissions stay as
/// they are) when creating the temp file or replacing the destination is denied (a directory that is not writable, a sticky
/// directory, or a Windows ACL that allows writing the file but not deleting it).</item>
/// <item>The temp file is deleted on failure, if it can be. A failed delete does not hide the error that caused it.</item>
/// </list>
/// </summary>
public static class AtomicFile
{
    private const string KeepOwnerVariable = "CMDR_KEEP_OWNER";

    /// <summary>Replaces the file at <paramref name="path"/> with <paramref name="content"/> followed by a single '\n'.</summary>
    /// <exception cref="IOException">The path is a dangling symbolic link or a link loop, or the file cannot be written or replaced.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be written.</exception>
    public static void Write(string path, ReadOnlySpan<byte> content)
    {
        Write(path, content, keepOtherOwners: !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(KeepOwnerVariable)));
    }

    /// <summary>
    /// <see cref="Write(string, ReadOnlySpan{byte})"/> with the opt-out given explicitly. When <paramref name="keepOtherOwners"/> is
    /// true, an existing file this process does not own (and which is not root) is written in place, so its owner stays the same.
    /// </summary>
    internal static void Write(string path, ReadOnlySpan<byte> content, bool keepOtherOwners)
    {
        string destination = ResolveDestination(Path.GetFullPath(path));
        UnixOwnership.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (!File.Exists(destination))
        {
            Replace(destination, content, replacing: false, mode: null, owner: null);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Replace(destination, content, replacing: true, mode: null, owner: null);
            return;
        }

        UnixOwnership.FileOwnership? existing = UnixOwnership.TryReadOwnership(destination);
        if (existing is not { } file)
        {
            if (OwnedByThisProcess(destination))
            {
                Replace(destination, content, replacing: true, mode: File.GetUnixFileMode(destination), owner: null);
            }
            else
            {
                WriteInPlace(destination, content);
            }

            return;
        }

        uint euid = LinuxNative.GetEffectiveUserId();
        bool root = euid == 0;
        if (!root && euid != file.Uid)
        {
            if (keepOtherOwners)
            {
                WriteInPlace(destination, content);
                return;
            }

            VerifyWritable(destination);
        }

        // A root save gives the file's own owner the file. Otherwise uint.MaxValue keeps the temp file's owner, the saving user.
        uint uid = root ? file.Uid : uint.MaxValue;
        Replace(destination, content, replacing: true, mode: file.Mode, owner: (uid, file.Gid));
    }

    /// <summary>
    /// Replaces <paramref name="destination"/> through a temp file, or writes it in place when the temp file cannot be given the
    /// owner or the rename is denied. A null <paramref name="owner"/> leaves the temp file owned by the saving user.
    /// </summary>
    private static void Replace(string destination, ReadOnlySpan<byte> content, bool replacing, UnixFileMode? mode, (uint Uid, uint Gid)? owner)
    {
        if (!TryReplaceFile(destination, content, replacing, mode, owner))
        {
            WriteInPlace(destination, content);
        }
    }

    /// <summary>
    /// Writes the temp file and renames it over the destination. Returns false, having deleted the temp file, when the owner cannot
    /// be set, or when <paramref name="replacing"/> is true and the temp file cannot be created or renamed for lack of permission.
    /// </summary>
    private static bool TryReplaceFile(string destination, ReadOnlySpan<byte> content, bool replacing, UnixFileMode? mode, (uint Uid, uint Gid)? owner)
    {
        string tempPath = Path.Combine(Path.GetDirectoryName(destination)!, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        bool keepTemp = false;
        try
        {
            using (FileStream stream = CreateTempFile(tempPath, mode))
            {
                if (!replacing)
                {
                    // A new file: best effort, and its failure is not a reason to stop saving.
                    UnixOwnership.GiveNewFileAway(stream.SafeFileHandle, Path.GetDirectoryName(destination)!);
                }

                if (owner is { } o && !OperatingSystem.IsWindows() && !UnixOwnership.TryChangeOwner(stream.SafeFileHandle, o.Uid, o.Gid))
                {
                    return false;
                }

                // After the chown, which clears setuid and setgid for a non-root user.
                if (!OperatingSystem.IsWindows() && mode is UnixFileMode unixMode)
                {
                    File.SetUnixFileMode(stream.SafeFileHandle, unixMode);
                }

                stream.Write(content);
                stream.WriteByte((byte)'\n');

                // The write above clears setuid and setgid again for a non-root user, so the mode is set once more. The buffered bytes
                // go to the file first, so that no write can follow this chmod.
                stream.Flush();
                if (!OperatingSystem.IsWindows() && mode is UnixFileMode writtenMode)
                {
                    File.SetUnixFileMode(stream.SafeFileHandle, writtenMode);
                }

                stream.Flush(flushToDisk: true);
            }

            if (OperatingSystem.IsWindows() && replacing)
            {
                try
                {
                    File.Replace(tempPath, destination, destinationBackupFileName: null);
                }
                catch (IOException error)
                {
                    // Set before recovering: a failed recovery must keep the temp file, and a recovered one is no longer there to delete.
                    keepTemp = IsUnableToMoveReplacement(error);
                    RecoverFromFailedReplace(error, tempPath, destination);
                }
            }
            else
            {
                File.Move(tempPath, destination, overwrite: true);
            }

            return true;
        }
        catch (UnauthorizedAccessException) when (replacing)
        {
            return false;
        }
        finally
        {
            if (!keepTemp)
            {
                DeleteQuietly(tempPath);
            }
        }
    }

    /// <summary>ERROR_UNABLE_TO_MOVE_REPLACEMENT: File.Replace removed the destination but could not move the replacement into its place.</summary>
    private const int ErrorUnableToMoveReplacement = 1176;

    /// <summary>
    /// Completes a save whose <c>File.Replace</c> failed on Windows. Replace is called without a backup file, so after error 1176 the
    /// destination is already gone and the new content exists only at <paramref name="tempPath"/>. The temp file is moved into place
    /// then. When that is not possible, the temp file is kept and an <see cref="IOException"/> names it. Any other error, or 1176 with
    /// no temp file to save, is rethrown unchanged. Returns normally only when the save is complete.
    /// </summary>
    internal static void RecoverFromFailedReplace(IOException error, string tempPath, string destination)
    {
        if (!IsUnableToMoveReplacement(error) || !File.Exists(tempPath))
        {
            ExceptionDispatchInfo.Throw(error);
        }

        Exception cause = error;
        if (!Path.Exists(destination))
        {
            try
            {
                File.Move(tempPath, destination);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                cause = ex;
            }
        }

        throw new IOException(
            $"The save to '{destination}' failed, and the new content could not be moved into its place. The new content is kept in '{tempPath}'.",
            cause);
    }

    /// <summary>True when the error is ERROR_UNABLE_TO_MOVE_REPLACEMENT (1176), the low 16 bits of the Win32 error in its HRESULT.</summary>
    internal static bool IsUnableToMoveReplacement(IOException error) => (error.HResult & 0xFFFF) == ErrorUnableToMoveReplacement;

    /// <summary>
    /// Overwrites the existing file's content through the file itself, so its identity, owner and permissions are kept. Not atomic:
    /// a failure part way leaves it partly written. The content is written before the file is shortened, so it is never empty.
    /// </summary>
    private static void WriteInPlace(string destination, ReadOnlySpan<byte> content)
    {
        UnixFileMode? ownedMode = OperatingSystem.IsWindows() ? null : ModeIfOwned(destination);
        using var stream = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.Read);
        stream.Write(content);
        stream.WriteByte((byte)'\n');
        stream.SetLength(stream.Position);

        // A write or truncate by a non-root user clears setuid and setgid, so the mode is restored on the handle. Best effort: a failure
        // is ignored, and the content is already written.
        if (!OperatingSystem.IsWindows() && ownedMode is UnixFileMode mode)
        {
            try
            {
                File.SetUnixFileMode(stream.SafeFileHandle, mode);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left as the write left it.
            }
        }

        stream.Flush(flushToDisk: true);
    }

    /// <summary>The mode of the file when this process owns it, otherwise null. Unix only.</summary>
    [UnsupportedOSPlatform("windows")]
    private static UnixFileMode? ModeIfOwned(string file)
    {
        if (UnixOwnership.TryReadOwnership(file) is { } existing)
        {
            return existing.Uid == LinuxNative.GetEffectiveUserId() ? existing.Mode : null;
        }

        return OwnedByThisProcess(file) ? File.GetUnixFileMode(file) : null;
    }

    /// <summary>Opens the file for writing without changing it, so that a file this process cannot write is never replaced.</summary>
    private static void VerifyWritable(string file)
    {
        new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose();
    }

    /// <summary>
    /// Fallback where statx is unavailable: whether this process owns the file, shown by the fact that only the owner (or root) may
    /// set the mode it already has. Never called on Windows.
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    private static bool OwnedByThisProcess(string file)
    {
        try
        {
            File.SetUnixFileMode(file, File.GetUnixFileMode(file));
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void DeleteQuietly(string file)
    {
        try
        {
            File.Delete(file); // no error when it does not exist
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: the save's own outcome is what is reported.
        }
    }

    // A path that is not a link is returned unchanged. A link is resolved to its final target, which is what gets replaced. The walk is
    // strict, so a link through a missing or non-directory component is an error here, as opening it would be.
    private static string ResolveDestination(string fullPath)
    {
        var link = new FileInfo(fullPath);
        if (link.LinkTarget is null)
        {
            return fullPath;
        }

        string final = PhysicalPath.ResolveExisting(fullPath);
        if (!File.Exists(final))
        {
            throw new IOException($"Cannot save '{fullPath}': it is a symbolic link to '{final}', which does not exist.");
        }

        return final;
    }

    // On Unix, a non-null mode is applied by the caller on the open handle; here the file is created with it, as far as the umask allows.
    private static FileStream CreateTempFile(string tempPath, UnixFileMode? mode)
    {
        if (OperatingSystem.IsWindows() || mode is not UnixFileMode unixMode)
        {
            return new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = unixMode,
        };
        return new FileStream(tempPath, options);
    }
}
