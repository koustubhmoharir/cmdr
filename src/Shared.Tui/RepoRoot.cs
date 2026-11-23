namespace Shared.Tui;

/// <summary>
/// Finds the git repository a folder lies in, for the picker's colouring of a directory. Pure apart from <see cref="HasGitEntry"/>,
/// which is the file system's check; <see cref="Outermost"/> takes the check as a parameter.
/// </summary>
public static class RepoRoot
{
    /// <summary>
    /// The outermost folder, from <paramref name="directory"/> up to the file system root, that <paramref name="hasGit"/> reports as a
    /// repository, or null when none does. A repository nested in another (a submodule, a worktree's folder inside a checkout) is
    /// therefore reported as its outermost enclosing folder. The walk uses the Path APIs, so drive roots and UNC shares end it.
    /// </summary>
    /// <param name="directory">The directory to start from. It is included in the walk.</param>
    /// <param name="hasGit">Whether a folder holds a <c>.git</c> entry.</param>
    public static string? Outermost(string directory, Func<string, bool> hasGit)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(hasGit);

        if (directory.Length == 0)
        {
            return null;
        }

        string? outermost = null;
        for (string? folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
             folder is not null;
             folder = Path.GetDirectoryName(folder))
        {
            if (hasGit(folder))
            {
                outermost = folder;
            }
        }

        return outermost;
    }

    /// <summary>
    /// Whether a folder holds a <c>.git</c> entry: a folder (a clone) or a file (a worktree or a submodule). An IO or permission
    /// failure means no entry.
    /// </summary>
    /// <param name="folder">The folder to look in.</param>
    public static bool HasGitEntry(string folder)
    {
        try
        {
            return Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
