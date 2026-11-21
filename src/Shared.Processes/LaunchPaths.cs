namespace Shared.Processes;

/// <summary>Resolves the working directories of launches.</summary>
public static class LaunchPaths
{
    /// <summary>
    /// Resolves a directory setting against <paramref name="baseDir"/>. Null or blank means <paramref name="baseDir"/>;
    /// a relative path is combined with it; an absolute path replaces it. The result is normalized.
    /// </summary>
    public static string ResolveDir(string? path, string baseDir)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDir);

        return Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? baseDir : Path.Combine(baseDir, path));
    }
}
