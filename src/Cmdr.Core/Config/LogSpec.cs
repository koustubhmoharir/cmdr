using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>Log file settings for a command.</summary>
public class LogSpec
{
    /// <summary>Whether output is written to a log file.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Log file path. Relative paths resolve against the logs directory.
    /// When null or empty, a default path inside the logs directory is used (see <see cref="ConfigPaths.ResolveLogPath"/>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }
}
