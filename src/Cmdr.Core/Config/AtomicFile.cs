namespace Cmdr.Core.Config;

/// <summary>
/// Replaces a file's content atomically. The content plus a trailing '\n' is written to a hidden temp file beside the destination,
/// flushed to disk, and then moved over the destination. Rules:
/// <list type="bullet">
/// <item>A symbolic link is followed. The final target is replaced and the link itself is left intact. A dangling link is an
/// <see cref="IOException"/>, thrown before anything is created.</item>
/// <item>For a path that is not a link, the parent directory is created if needed.</item>
/// <item>On Unix, an existing destination keeps its permission bits exactly. The temp file is created with that mode and then set
/// with File.SetUnixFileMode on the open handle before any content is written, so the umask cannot narrow it. A new file gets the
/// default mode.</item>
/// <item>On Windows, an existing destination is replaced with File.Replace so that its ACL is kept. A new file is moved into place.
/// The Windows path is unverified: it has not been run on Windows.</item>
/// <item>The temp file is deleted on failure.</item>
/// </list>
/// </summary>
internal static class AtomicFile
{
    /// <summary>Replaces the file at <paramref name="path"/> with <paramref name="content"/> followed by a single '\n'.</summary>
    /// <exception cref="IOException">The path is a dangling symbolic link, or the file cannot be written or replaced.</exception>
    public static void Write(string path, ReadOnlySpan<byte> content)
    {
        string destination = ResolveDestination(Path.GetFullPath(path));
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);

        bool destinationExists = File.Exists(destination);
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
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
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

        FileSystemInfo? final = link.ResolveLinkTarget(returnFinalTarget: true);
        if (final is null || !final.Exists)
        {
            string target = final?.FullName ?? link.LinkTarget;
            throw new IOException($"Cannot save '{fullPath}': it is a symbolic link to '{target}', which does not exist.");
        }

        return final.FullName;
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
