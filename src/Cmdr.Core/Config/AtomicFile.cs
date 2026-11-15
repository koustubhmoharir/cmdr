namespace Cmdr.Core.Config;

/// <summary>
/// Replaces a file's content, atomically when it can. The content plus a trailing '\n' is written to a hidden temp file beside the
/// destination, flushed to disk, and then moved over the destination. Rules:
/// <list type="bullet">
/// <item>A symbolic link is followed. The final target (see <see cref="PhysicalPath"/>) is replaced and the link itself is left
/// intact. A dangling link is an <see cref="IOException"/>, thrown before anything is created.</item>
/// <item>For a path that is not a link, the parent directory is created if needed.</item>
/// <item>On Unix, an existing destination keeps its permission bits exactly. The temp file is created with that mode and then set
/// with File.SetUnixFileMode on the open handle before any content is written, so the umask cannot narrow it. A new file gets the
/// default mode.</item>
/// <item>On Windows, an existing destination is replaced with File.Replace so that its ACL is kept. A new file is moved into place.
/// The Windows path is unverified: it has not been run on Windows.</item>
/// <item>An existing destination is overwritten in place instead (not atomic, but the file, its owner and its permissions stay as
/// they are) when replacing it would change its owner or is not permitted: on Unix when this process does not own the file (a
/// rename would make the saving user its owner), and anywhere when creating the temp file or replacing the destination is denied
/// (a directory that is not writable, a sticky directory, or a Windows ACL that allows writing the file but not deleting it).</item>
/// <item>The temp file is deleted on failure, if it can be. A failed delete does not hide the error that caused it.</item>
/// </list>
/// </summary>
internal static class AtomicFile
{
    /// <summary>Replaces the file at <paramref name="path"/> with <paramref name="content"/> followed by a single '\n'.</summary>
    /// <exception cref="IOException">The path is a dangling symbolic link or a link loop, or the file cannot be written or replaced.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be written.</exception>
    public static void Write(string path, ReadOnlySpan<byte> content)
    {
        string destination = ResolveDestination(Path.GetFullPath(path));
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);

        bool destinationExists = File.Exists(destination);
        if (destinationExists && !OwnedByThisProcess(destination))
        {
            WriteInPlace(destination, content);
            return;
        }

        string tempPath = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            UnixFileMode? existingMode = !OperatingSystem.IsWindows() && destinationExists ? File.GetUnixFileMode(destination) : null;
            using (FileStream stream = CreateTempFile(tempPath, existingMode))
            {
                stream.Write(content);
                stream.WriteByte((byte)'\n');
                stream.Flush(flushToDisk: true);
            }

            if (OperatingSystem.IsWindows() && destinationExists)
            {
                File.Replace(tempPath, destination, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, destination, overwrite: true);
            }
        }
        catch (UnauthorizedAccessException) when (destinationExists)
        {
            DeleteQuietly(tempPath);
            WriteInPlace(destination, content);
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }

    /// <summary>
    /// Overwrites the existing file's content through the file itself, so its identity, owner and permissions are kept. Not atomic:
    /// a failure part way leaves it partly written. The content is written before the file is shortened, so it is never empty.
    /// </summary>
    private static void WriteInPlace(string destination, ReadOnlySpan<byte> content)
    {
        using var stream = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.Read);
        stream.Write(content);
        stream.WriteByte((byte)'\n');
        stream.SetLength(stream.Position);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// On Unix, whether this process owns the file: only the owner (or root) may change its mode, so setting the mode it already
    /// has succeeds exactly then. Always true on Windows, where File.Replace keeps the file's security descriptor.
    /// </summary>
    private static bool OwnedByThisProcess(string file)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

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

    // A path that is not a link is returned unchanged. A link is resolved to its final target, which is what gets replaced.
    private static string ResolveDestination(string fullPath)
    {
        var link = new FileInfo(fullPath);
        if (link.LinkTarget is null)
        {
            return fullPath;
        }

        string final = PhysicalPath.Resolve(fullPath);
        if (!File.Exists(final))
        {
            throw new IOException($"Cannot save '{fullPath}': it is a symbolic link to '{final}', which does not exist.");
        }

        return final;
    }

    // On Unix the mode is set on the open handle before any write, so the file is never readable with a broader mode than the original.
    private static FileStream CreateTempFile(string tempPath, UnixFileMode? existingMode)
    {
        if (OperatingSystem.IsWindows() || existingMode is not UnixFileMode mode)
        {
            return new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = mode,
        };
        FileStream stream = new(tempPath, options);
        try
        {
            File.SetUnixFileMode(stream.SafeFileHandle, mode);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        return stream;
    }
}
