using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>
/// A program whose output sets environment variables for the commands it applies to (the <c>envScript</c> setting of a file or
/// group). The settings are read and validated here; the script is not run yet.
/// </summary>
public class EnvScriptSpec
{
    /// <summary>The timeout used when <see cref="TimeoutSeconds"/> is not set.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>Executable to run. Required.</summary>
    public string Program { get; set; } = "";

    /// <summary>Arguments, passed as an argument list (no quoting).</summary>
    public List<string> Args { get; set; } = new();

    /// <summary>Working directory; relative paths resolve against the directory of the file that defines the script. Null means that directory.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; set; }

    /// <summary>Seconds the script may run before it is stopped. Null means <see cref="DefaultTimeoutSeconds"/>; must be greater than 0 when set.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TimeoutSeconds { get; set; }

    /// <summary>When the script runs again. Defaults to <see cref="EnvScriptRerun.Once"/>; the default is not written to the configuration file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public EnvScriptRerun Rerun { get; set; } = EnvScriptRerun.Once;

    /// <summary>Returns a deep copy: the arguments are copied.</summary>
    public EnvScriptSpec Clone() => new()
    {
        Program = Program,
        Args = Args is null ? new List<string>() : new List<string>(Args),
        Cwd = Cwd,
        TimeoutSeconds = TimeoutSeconds,
        Rerun = Rerun,
    };
}
