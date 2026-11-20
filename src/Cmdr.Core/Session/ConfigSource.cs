using Cmdr.Core.Config;

namespace Cmdr.Core.Session;

/// <summary>
/// One configuration file of a session's import chain (see <see cref="ConfigStore.LoadChain"/>). Every group records the
/// file it came from; the settings below are that file's own, and relative paths of its items resolve against its directory.
/// </summary>
public sealed class ConfigSource
{
    private readonly EnvScriptSpec? _envScript;

    /// <param name="file">The file.</param>
    /// <param name="openedDir">The directory of the opened file.</param>
    /// <param name="baseSource">The source of the file this one imports (its <c>base</c>), or null when it imports none.</param>
    internal ConfigSource(ConfigFile file, string openedDir, ConfigSource? baseSource)
    {
        Path = file.Path;
        Dir = file.Dir;
        IsBase = file.IsBase;
        BufferLines = file.Config.BufferLines;
        LogsDirSetting = file.Config.LogsDir;
        Env = file.Config.Env is null ? null : new Dictionary<string, string?>(file.Config.Env);
        _envScript = file.Config.EnvScript?.Clone();
        BaseEnv = file.Config.BaseEnv is null ? null : new List<string>(file.Config.BaseEnv);
        Secret = file.Config.Secret is null ? null : new List<string>(file.Config.Secret);
        EnvLevels = BuildEnvLevels(baseSource);
        try
        {
            LogsDir = ConfigPaths.ResolveLogsDir(file.Config, Dir);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ConfigException($"Configuration file '{file.Path}' has an invalid logsDir path: {ex.Message}", ex);
        }
        DisplayPath = IsBase
            ? System.IO.Path.GetRelativePath(openedDir, file.Path)
            : System.IO.Path.GetFileName(file.Path);
    }

    /// <summary>
    /// The file level of the environment chain (see <see cref="EnvLevel"/>): the import of <see cref="BaseEnv"/> from the base file when
    /// there is one, then the top-level <see cref="Env"/> when it is set. Both use <see cref="Dir"/> as their working directory.
    /// </summary>
    private IReadOnlyList<EnvLevel> BuildEnvLevels(ConfigSource? baseSource)
    {
        var levels = new List<EnvLevel>();
        if (baseSource is not null && BaseEnv is { Count: > 0 } names)
        {
            var imported = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string? name in names)
            {
                if (name is not null)
                {
                    imported[name] = null;
                }
            }

            // The import level's directory is the base file's: it is what ${cwd} and ${configDir} mean in the imported values.
            levels.Add(new EnvLevel(imported, baseSource.Dir) { ImportFrom = baseSource.EnvLevels });
        }

        if (Env is not null)
        {
            levels.Add(new EnvLevel(new Dictionary<string, string?>(Env), Dir));
        }

        return levels;
    }

    /// <summary>Full path of the file.</summary>
    public string Path { get; }

    /// <summary>Directory of the file; relative paths of its groups and commands resolve against it.</summary>
    public string Dir { get; }

    /// <summary>True for a base file, which is read-only in the session. False only for the opened file.</summary>
    public bool IsBase { get; }

    /// <summary>The file's <c>bufferLines</c> value as written. Process buffers use at least 1 line.</summary>
    public int BufferLines { get; }

    /// <summary>The file's <c>logsDir</c> value as written (null when not set).</summary>
    public string? LogsDirSetting { get; }

    /// <summary>Resolved directory for the log files of this file's commands.</summary>
    public string LogsDir { get; }

    /// <summary>
    /// The environment this file adds to every command of the session that is defined in it, outermost first, before the groups
    /// (see <see cref="EnvLevel"/>). Empty when the file has neither a top-level <c>env</c> nor a <c>baseEnv</c> import.
    /// </summary>
    public IReadOnlyList<EnvLevel> EnvLevels { get; }

    /// <summary>The file's top-level <c>env</c> as written (null when not set). A copy; changing it does not change the file's settings.</summary>
    public IReadOnlyDictionary<string, string?>? Env { get; }

    /// <summary>The file's top-level <c>envScript</c> as written (null when not set). Returns a new copy on each read.</summary>
    public EnvScriptSpec? EnvScript => _envScript?.Clone();

    /// <summary>The file's <c>baseEnv</c> names as written (null when not set). A copy.</summary>
    public IReadOnlyList<string>? BaseEnv { get; }

    /// <summary>The file's top-level <c>secret</c> names as written (null when not set). A copy.</summary>
    public IReadOnlyList<string>? Secret { get; }

    /// <summary>
    /// The file's path relative to the directory of the opened file (for example <c>shared/base.json</c>), or just the file
    /// name for the opened file. Used in messages.
    /// </summary>
    public string DisplayPath { get; }
}
