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

    /// <summary>
    /// Resolves a directory setting that may contain placeholders, then resolves it as <see cref="ResolveDir(string?, string)"/> does.
    /// The placeholders are expanded by <see cref="Placeholders.Expand"/>: <c>${env:NAME}</c> is <paramref name="env"/>(NAME), <c>${configDir}</c>
    /// is <paramref name="configDir"/>, <c>${pathsep}</c> is <paramref name="pathSeparator"/>, and <c>${cwd}</c> is <paramref name="baseDir"/>.
    /// Configuration rejects <c>${cwd}</c> and inputs in a cwd, so neither occurs in a valid one. Unlike in an environment value (where an
    /// unset variable is empty), an unset variable here is an error, since it would silently change the directory.
    /// </summary>
    /// <param name="path">The setting as written. Null or blank means <paramref name="baseDir"/>.</param>
    /// <param name="baseDir">The directory that a relative result is combined with. The result is absolute and normalized.</param>
    /// <param name="env">The value of each variable, or null when it is not set.</param>
    /// <param name="configDir">The value of <c>${configDir}</c>.</param>
    /// <param name="pathSeparator">The value of <c>${pathsep}</c>.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="path"/> has an <c>${env:NAME}</c> whose variable is not set. The message names the variable and the setting.
    /// </exception>
    public static string ResolveDir(string? path, string baseDir, Func<string, string?> env, string configDir, char pathSeparator)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDir);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        if (string.IsNullOrWhiteSpace(path))
        {
            return ResolveDir(path, baseDir);
        }

        foreach (string name in Placeholders.EnvNames(path))
        {
            if (env(name) is null)
            {
                throw new InvalidOperationException($"cwd '{path}': variable {name} is not set");
            }
        }

        return ResolveDir(Placeholders.Expand(path, env, inputs: null, baseDir, configDir, pathSeparator), baseDir);
    }
}
