using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>
/// What happens to processes left running in a command's process group after its leader exits on its own.
/// </summary>
[JsonConverter(typeof(LeftoverModeJsonConverter))]
public enum LeftoverMode
{
    /// <summary>Keep tracking them: cmdr reports them, blocks a restart and offers a force kill. Serialized as "track".</summary>
    [JsonStringEnumMemberName("track")]
    Track,

    /// <summary>
    /// Stop supervising them once the leader exits on its own: they keep running untracked. Not applied when the run was
    /// stopped or force-killed. Serialized as "detach".
    /// </summary>
    [JsonStringEnumMemberName("detach")]
    Detach,
}

/// <summary>String-only enum converter: rejects integer values such as <c>1</c>.</summary>
internal sealed class LeftoverModeJsonConverter() : JsonStringEnumConverter<LeftoverMode>(namingPolicy: null, allowIntegerValues: false);
