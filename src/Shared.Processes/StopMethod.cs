using System.Text.Json.Serialization;

namespace Shared.Processes;

/// <summary>How a running command is asked to stop.</summary>
[JsonConverter(typeof(StopMethodJsonConverter))]
public enum StopMethod
{
    /// <summary>Send Ctrl+C (SIGINT on Linux). Serialized as "ctrlC".</summary>
    [JsonStringEnumMemberName("ctrlC")]
    CtrlC,

    /// <summary>Write the stop input (the <c>Input</c> of a command's stop settings) to stdin. Serialized as "input".</summary>
    [JsonStringEnumMemberName("input")]
    Input,
}

/// <summary>String-only enum converter: rejects integer values such as <c>3</c>.</summary>
public sealed class StopMethodJsonConverter : StrictEnumJsonConverter<StopMethod> { }
