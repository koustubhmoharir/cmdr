using Cmdr.Core.Config;

namespace Cmdr.Core.Session;

/// <summary>
/// One configuration file of a session's import chain (see <see cref="ConfigStore.LoadChain"/>). Every group records the
/// file it came from; the settings below are that file's own, and relative paths of its items resolve against its directory.
/// </summary>
public sealed class ConfigSource
{
    internal ConfigSource(ConfigFile file, string openedDir)
    {
        Path = file.Path;
        Dir = file.Dir;
        IsBase = file.IsBase;
        BufferLines = file.Config.BufferLines;
        LogsDirSetting = file.Config.LogsDir;
        LogsDir = ConfigPaths.ResolveLogsDir(file.Config, Dir);
        DisplayPath = IsBase
            ? System.IO.Path.GetRelativePath(openedDir, file.Path)
            : System.IO.Path.GetFileName(file.Path);
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
    /// The file's path relative to the directory of the opened file (for example <c>shared/base.json</c>), or just the file
    /// name for the opened file. Used in messages.
    /// </summary>
    public string DisplayPath { get; }
}
