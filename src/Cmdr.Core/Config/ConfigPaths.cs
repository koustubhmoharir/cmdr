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
    /// Resolves the log file path. If <see cref="LogSpec.Path"/> is set, it is resolved like
    /// <see cref="ResolveCwd"/>. Otherwise the path is
    /// <c>&lt;configDir&gt;/logs/&lt;safe group name&gt;/&lt;safe command name&gt;.log</c>, where unsafe filename
    /// characters are replaced by '_'; a blank group name becomes "group" and a blank command name "command".
    /// </summary>
    public static string ResolveLogPath(GroupSpec group, CommandSpec cmd, string configDir)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        if (!string.IsNullOrWhiteSpace(cmd.Log.Path))
        {
            return Path.GetFullPath(Path.Combine(configDir, cmd.Log.Path));
        }

        string groupDir = SafeFileName(group.Name, "group");
        string fileName = SafeFileName(cmd.Name, "command") + ".log";
        return Path.GetFullPath(Path.Combine(configDir, "logs", groupDir, fileName));
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
