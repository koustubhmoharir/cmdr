using System.Text.Json.Serialization;
using Shared.Processes;

namespace Cmdr.Core.Config;

/// <summary>Stop settings for a command.</summary>
public class StopSpec
{
    /// <summary>Stop method; defaults to Ctrl+C.</summary>
    public StopMethod Method { get; set; } = StopMethod.CtrlC;

    /// <summary>Text written to stdin when <see cref="Method"/> is <see cref="StopMethod.Input"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Input { get; set; }
}
