namespace Shared.Core;

/// <summary>
/// Replaces a file's content through a temp file: the directory is created if needed, the bytes are written to a hidden temp
/// file beside the destination and flushed to disk, and the temp file is then moved over the destination. The temp file is
/// deleted when the save fails. A reader sees either the old content or the new content, never a partial file.
/// </summary>
internal static class AtomicFile
{
    /// <summary>Replaces the file at <paramref name="path"/> with <paramref name="content"/>.</summary>
    /// <exception cref="IOException">The file or its directory cannot be written or replaced.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or its directory cannot be written.</exception>
    public static void Write(string path, ReadOnlySpan<byte> content)
    {
        string destination = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, destination, overwrite: true);
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }

    // After a successful move the temp file no longer exists, so this only cleans up a failed save. A failed delete does not hide
    // the error that caused the save to fail.
    private static void DeleteQuietly(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: the save's own outcome is what is reported.
        }
    }
}
