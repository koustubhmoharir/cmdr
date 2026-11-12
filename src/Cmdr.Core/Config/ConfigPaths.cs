namespace Cmdr.Core.Config;

/// <summary>Resolves working directories and log file paths of items.</summary>
public static class ConfigPaths
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
    /// The effective working directory of a group: <c>cwd</c> of each group from the outermost one inwards, each
    /// resolved against the one before it. The first group's parent is <paramref name="configDir"/>.
    /// </summary>
    /// <param name="ancestorsIncludingGroup">The group and its ancestors, outermost first.</param>
    public static string ResolveGroupCwd(IReadOnlyList<GroupSpec> ancestorsIncludingGroup, string configDir)
    {
        ArgumentNullException.ThrowIfNull(ancestorsIncludingGroup);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        string cwd = Path.GetFullPath(configDir);
        foreach (GroupSpec group in ancestorsIncludingGroup)
        {
            cwd = ResolveDir(group.Cwd, cwd);
        }

        return cwd;
    }

    /// <summary>
    /// The effective working directory of a command: its <c>cwd</c> resolved against the effective working directory of
    /// its enclosing groups.
    /// </summary>
    /// <param name="ancestors">The groups that contain the command, outermost first (empty for a command at the top level).</param>
    public static string ResolveCwd(IReadOnlyList<GroupSpec> ancestors, CommandSpec cmd, string configDir)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        return ResolveDir(cmd.Cwd, ResolveGroupCwd(ancestors, configDir));
    }

    /// <summary>
    /// Resolves the logs directory: <see cref="CmdrConfig.LogsDir"/> against the config directory, or
    /// <c>&lt;configDir&gt;/logs</c> when it is not set.
    /// </summary>
    public static string ResolveLogsDir(CmdrConfig config, string configDir)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        string logsDir = string.IsNullOrWhiteSpace(config.LogsDir) ? "logs" : config.LogsDir;
        return Path.GetFullPath(Path.Combine(configDir, logsDir));
    }

    /// <summary>
    /// Resolves the log file path. If <see cref="LogSpec.Path"/> is set, a relative path is resolved against
    /// <paramref name="logsDir"/>. Otherwise the path is
    /// <c>&lt;logsDir&gt;/&lt;group&gt;/&lt;subgroup&gt;/…/&lt;safe command name&gt;.log</c>, one folder per enclosing group, with
    /// unsafe filename characters replaced by '_'; a blank group name becomes "group" and a blank command name "command".
    /// </summary>
    /// <param name="ancestors">The groups that contain the command, outermost first.</param>
    /// <param name="logsDir">The resolved logs directory (see <see cref="ResolveLogsDir"/>).</param>
    public static string ResolveLogPath(IReadOnlyList<GroupSpec> ancestors, CommandSpec cmd, string logsDir)
    {
        ArgumentNullException.ThrowIfNull(ancestors);
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrEmpty(logsDir);

        if (!string.IsNullOrWhiteSpace(cmd.Log.Path))
        {
            return Path.GetFullPath(Path.Combine(logsDir, cmd.Log.Path));
        }

        var segments = new List<string> { logsDir };
        segments.AddRange(ancestors.Select(group => SafeFileName(group.Name, "group")));
        segments.Add(SafeFileName(cmd.Name, "command") + ".log");
        return Path.GetFullPath(Path.Combine(segments.ToArray()));
    }

    private static string SafeFileName(string name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            chars[i] = Array.IndexOf(invalid, c) >= 0 || c is '/' or '\\' or ':' || char.IsWhiteSpace(c)
                ? '_'
                : c;
        }

        string safe = new(chars);

        // "." and ".." would be read as directory references and escape the logs folder.
        return safe.Trim('.').Length == 0 ? safe.Replace('.', '_') : safe;
    }
}
