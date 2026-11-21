using Cmdr.Core.Config;
using Shared.Processes;

namespace Cmdr.Core.Session;

/// <summary>
/// One configuration file of a session's import chain (see <see cref="ConfigStore.LoadChain"/>). Every group records the
/// file it came from; the settings below are that file's own, and relative paths of its items resolve against its directory.
/// </summary>
public sealed class ConfigSource
{
    private EnvScriptSpec? _envScript;
    private readonly ConfigSource? _base;

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
        BaseSetting = file.Config.Base;
        Env = file.Config.Env is null ? null : new Dictionary<string, string?>(file.Config.Env);
        _envScript = file.Config.EnvScript?.Clone();
        _base = baseSource;
        BaseEnv = file.Config.BaseEnv is null ? null : new List<string>(file.Config.BaseEnv);
        Secret = file.Config.Secret is null ? null : new List<string>(file.Config.Secret);
        EnvLevels = EnvLevelsWith(ScriptValues.Empty);
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
    /// The file's environment levels (see <see cref="EnvLevel"/>) with the values its environment script and its base's produced, given
    /// as <paramref name="values"/>: the import of <see cref="BaseEnv"/> from the base file's levels (themselves with their values) when
    /// there is one, then the literal level of this file's script when it has one and <paramref name="values"/> has its output, then
    /// the top-level <see cref="Env"/> when it is set. Each uses <see cref="Dir"/> as its working directory, except the import, which
    /// uses the base file's. With <see cref="ScriptValues.Empty"/> this is <see cref="EnvLevels"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    public IReadOnlyList<EnvLevel> EnvLevelsWith(ScriptValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var levels = new List<EnvLevel>(ImportLevelsWith(values));
        if (_envScript is not null && values.TryGet(this, out IReadOnlyDictionary<string, string?>? scriptValues))
        {
            levels.Add(new EnvLevel(scriptValues, Dir) { Literal = true });
        }

        if (Env is not null)
        {
            levels.Add(new EnvLevel(new Dictionary<string, string?>(Env), Dir));
        }

        return levels;
    }

    /// <summary>
    /// Replaces the file's own top-level settings and recomputes <see cref="EnvLevels"/>. Only the opened file is changed this way: a base
    /// file is read-only, and the opened file is the last of the chain, so no other source imports from it and their levels do not change.
    /// The values are copied.
    /// </summary>
    /// <exception cref="InvalidOperationException">This is a base file.</exception>
    internal void ApplySettings(
        IReadOnlyDictionary<string, string?>? env,
        EnvScriptSpec? envScript,
        IReadOnlyList<string>? baseEnv,
        IReadOnlyList<string>? secret)
    {
        if (IsBase)
        {
            throw new InvalidOperationException($"'{DisplayPath}' is a base file; edit that file.");
        }

        Env = env is null ? null : new Dictionary<string, string?>(env);
        _envScript = envScript?.Clone();
        BaseEnv = baseEnv is null ? null : new List<string>(baseEnv);
        Secret = secret is null ? null : new List<string>(secret);
        EnvLevels = EnvLevelsWith(ScriptValues.Empty);
    }

    /// <summary>
    /// The levels of <see cref="EnvLevelsWith"/> that come before this file's own script: only the <see cref="BaseEnv"/> import (empty when
    /// there is none). This is the environment a file's script runs in.
    /// </summary>
    internal IReadOnlyList<EnvLevel> ImportLevelsWith(ScriptValues values)
    {
        var levels = new List<EnvLevel>();
        if (_base is not null && BaseEnv is { Count: > 0 } names)
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
            levels.Add(new EnvLevel(imported, _base.Dir) { ImportFrom = _base.EnvLevelsWith(values) });
        }

        return levels;
    }

    /// <summary>The source of the file this one imports (its <c>base</c>), or null when it imports none.</summary>
    internal ConfigSource? BaseSource => _base;

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

    /// <summary>The file's <c>base</c> value as written (null when it imports no file).</summary>
    public string? BaseSetting { get; }

    /// <summary>Resolved directory for the log files of this file's commands.</summary>
    public string LogsDir { get; }

    /// <summary>
    /// The environment this file adds to every command of the session that is defined in it, outermost first, before the groups
    /// (see <see cref="EnvLevel"/>), without any environment script's values (see <see cref="EnvLevelsWith"/>). Empty when the file has
    /// neither a top-level <c>env</c> nor a <c>baseEnv</c> import.
    /// </summary>
    public IReadOnlyList<EnvLevel> EnvLevels { get; private set; }

    /// <summary>The file's top-level <c>env</c> as written (null when not set). A copy; changing it does not change the file's settings.</summary>
    public IReadOnlyDictionary<string, string?>? Env { get; private set; }

    /// <summary>The file's top-level <c>envScript</c> as written (null when not set). Returns a new copy on each read.</summary>
    public EnvScriptSpec? EnvScript => _envScript?.Clone();

    /// <summary>The file's <c>baseEnv</c> names as written (null when not set). A copy.</summary>
    public IReadOnlyList<string>? BaseEnv { get; private set; }

    /// <summary>The file's top-level <c>secret</c> names as written (null when not set). A copy.</summary>
    public IReadOnlyList<string>? Secret { get; private set; }

    /// <summary>
    /// The file's path relative to the directory of the opened file (for example <c>shared/base.json</c>), or just the file
    /// name for the opened file. Used in messages.
    /// </summary>
    public string DisplayPath { get; }
}
