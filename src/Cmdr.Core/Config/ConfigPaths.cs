using Cmdr.Core.Processes;
using Shared.Processes;

namespace Cmdr.Core.Config;

/// <summary>Resolves working directories and log file paths of items.</summary>
public static class ConfigPaths
{
    /// <summary>
    /// The effective working directory of the innermost group of <paramref name="ancestorsIncludingGroup"/> (outermost first), resolved as a
    /// launch resolves it (see <see cref="CommandLaunch.GroupDirectory"/>): each group's <c>cwd</c> is resolved against the one before it, in
    /// the environment of <paramref name="fileLevels"/> and of the groups above it. The first group's parent is <paramref name="configDir"/>.
    /// </summary>
    /// <param name="ancestorsIncludingGroup">The group and its ancestors, outermost first.</param>
    /// <param name="configDir">The config directory.</param>
    /// <param name="fileLevels">The file's environment levels (see <c>ConfigSource.EnvLevels</c>). Empty when none.</param>
    /// <param name="groupValues">The environment script values of the groups, aligned with <paramref name="ancestorsIncludingGroup"/>; null for none.</param>
    /// <exception cref="InvalidOperationException">A group's cwd has an <c>${env:NAME}</c> whose variable is not set in that environment.</exception>
    public static string ResolveGroupCwd(
        IReadOnlyList<GroupSpec> ancestorsIncludingGroup,
        string configDir,
        IReadOnlyList<EnvLevel>? fileLevels = null,
        IReadOnlyList<IReadOnlyDictionary<string, string?>?>? groupValues = null) =>
        CommandLaunch.GroupDirectory(ancestorsIncludingGroup, fileLevels ?? Array.Empty<EnvLevel>(), groupValues, configDir);

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
