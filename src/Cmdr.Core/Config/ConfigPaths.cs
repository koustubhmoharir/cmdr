namespace Cmdr.Core.Config;

/// <summary>Resolves working directory and log file paths for a command.</summary>
public static class ConfigPaths
{
    /// <summary>
    /// Resolves the working directory. Null/empty means the config directory; relative paths
    /// are resolved against the config directory; absolute paths are normalized.
    /// </summary>
    public static string ResolveCwd(CommandSpec cmd, string configDir)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        return string.IsNullOrWhiteSpace(cmd.Cwd)
            ? Path.GetFullPath(configDir)
            : Path.GetFullPath(Path.Combine(configDir, cmd.Cwd));
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
    /// <c>&lt;logsDir&gt;/&lt;safe group name&gt;/&lt;safe command name&gt;.log</c>, where unsafe filename
    /// characters are replaced by '_'; a blank group name becomes "group" and a blank command name "command".
    /// </summary>
    /// <param name="logsDir">The resolved logs directory (see <see cref="ResolveLogsDir"/>).</param>
    public static string ResolveLogPath(GroupSpec group, CommandSpec cmd, string logsDir)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrEmpty(logsDir);

        if (!string.IsNullOrWhiteSpace(cmd.Log.Path))
        {
            return Path.GetFullPath(Path.Combine(logsDir, cmd.Log.Path));
        }

        string groupDir = SafeFileName(group.Name, "group");
        string fileName = SafeFileName(cmd.Name, "command") + ".log";
        return Path.GetFullPath(Path.Combine(logsDir, groupDir, fileName));
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
