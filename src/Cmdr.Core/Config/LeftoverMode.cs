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
    /// Stop supervising them once the leader exits: they keep running untracked. Applies after the leader exits on its
    /// own and after a graceful stop (Ctrl+C or input). Not applied when the run was force-killed, since a force kill
    /// means "kill everything". Serialized as "detach".
    /// </summary>
    [JsonStringEnumMemberName("detach")]
    Detach,
}

/// <summary>String-only enum converter: rejects integer values such as <c>1</c>.</summary>
internal sealed class LeftoverModeJsonConverter : StrictEnumJsonConverter<LeftoverMode> { }
