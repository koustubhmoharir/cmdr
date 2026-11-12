using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>One managed process definition: an item with a <c>program</c>.</summary>
public class CommandSpec : ItemSpec
{
    /// <summary>Executable to launch.</summary>
    public string Program { get; set; } = "";

    /// <summary>Arguments, passed as an argument list (no quoting).</summary>
    public List<string> Args { get; set; } = new();

    /// <summary>Working directory; relative paths resolve against the parent group's effective working directory.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; set; }

    /// <summary>
    /// Environment overlay. A null value removes the inherited variable.
    /// </summary>
    public Dictionary<string, string?> Env { get; set; } = new();

    /// <summary>How the process is asked to stop.</summary>
    public StopSpec Stop { get; set; } = new();

    /// <summary>Log file settings.</summary>
    public LogSpec Log { get; set; } = new();

    /// <summary>
    /// What happens to processes left running after the leader exits on its own. Defaults to <see cref="LeftoverMode.Track"/>;
    /// the default is not written to the configuration file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public LeftoverMode Leftovers { get; set; } = LeftoverMode.Track;

    /// <summary>
    /// Where the standard input comes from. Defaults to <see cref="StdinMode.Pipe"/>; the default is not written to the
    /// configuration file. <see cref="StdinMode.Console"/> is honoured on Windows only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public StdinMode Stdin { get; set; } = StdinMode.Pipe;

    /// <summary>Returns a deep copy: lists, dictionaries and sub-objects are copied.</summary>
    public override CommandSpec Clone() => new()
    {
        Name = Name,
        Description = Description,
        Program = Program,
        Args = new List<string>(Args),
        Cwd = Cwd,
        Env = new Dictionary<string, string?>(Env),
        Stop = new StopSpec { Method = Stop.Method, Input = Stop.Input },
        Log = new LogSpec { Enabled = Log.Enabled, Path = Log.Path },
        Leftovers = Leftovers,
        Stdin = Stdin,
    };
}
