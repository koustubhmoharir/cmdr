using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>
/// Readiness patterns for a command: how cmdr tells that a long-running command is starting, busy, or ready.
/// Both patterns are .NET regular expressions matched against each completed output line's plain text
/// (stdout and stderr; no ANSI sequences; not partial lines; not cmdr's own system lines).
/// </summary>
public class ReadySpec
{
    /// <summary>
    /// Regular expression that marks the command as ready. Required. A line matching it makes the command ready.
    /// </summary>
    public string Pattern { get; set; } = "";

    /// <summary>
    /// Optional regular expression that marks the command as busy again (for example a rebuild). A line matching it,
    /// and not <see cref="Pattern"/>, makes the command starting. Null means no busy pattern.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Busy { get; set; }

    /// <summary>Returns a copy.</summary>
    public ReadySpec Clone() => new() { Pattern = Pattern, Busy = Busy };
}
