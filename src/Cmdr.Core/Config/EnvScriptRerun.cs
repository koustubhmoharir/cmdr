using System.Text.Json.Serialization;
using Shared.Processes;

namespace Cmdr.Core.Config;

/// <summary>
/// When an environment script runs again. Serialized as the <c>rerun</c> setting of an <see cref="EnvScriptSpec"/>.
/// </summary>
[JsonConverter(typeof(EnvScriptRerunJsonConverter))]
public enum EnvScriptRerun
{
    /// <summary>The script runs once, the first time a command needs its environment. Serialized as "once".</summary>
    [JsonStringEnumMemberName("once")]
    Once,

    /// <summary>The script runs again at each start of a command. Serialized as "eachStart".</summary>
    [JsonStringEnumMemberName("eachStart")]
    EachStart,
}

/// <summary>String-only enum converter: rejects integer values such as <c>1</c>.</summary>
internal sealed class EnvScriptRerunJsonConverter : StrictEnumJsonConverter<EnvScriptRerun> { }
