using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>How a running command is asked to stop.</summary>
[JsonConverter(typeof(StopMethodJsonConverter))]
public enum StopMethod
{
    /// <summary>Send Ctrl+C (SIGINT on Linux). Serialized as "ctrlC".</summary>
    [JsonStringEnumMemberName("ctrlC")]
    CtrlC,

    /// <summary>Write <see cref="StopSpec.Input"/> to stdin. Serialized as "input".</summary>
    [JsonStringEnumMemberName("input")]
    Input,
}

/// <summary>Stop settings for a command.</summary>
public class StopSpec
{
    /// <summary>Stop method; defaults to Ctrl+C.</summary>
    public StopMethod Method { get; set; } = StopMethod.CtrlC;

    /// <summary>Text written to stdin when <see cref="Method"/> is <see cref="StopMethod.Input"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Input { get; set; }
}

/// <summary>String-only enum converter: rejects integer values such as <c>3</c>.</summary>
internal sealed class StopMethodJsonConverter : StrictEnumJsonConverter<StopMethod> { }
