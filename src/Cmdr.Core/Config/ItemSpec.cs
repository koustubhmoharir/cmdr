using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>
/// An entry of a group's <c>items</c>: either a <see cref="CommandSpec"/> (has <c>program</c>) or a nested
/// <see cref="GroupSpec"/> (has <c>items</c>). Serialized through <see cref="ItemSpecJsonConverter"/>.
/// </summary>
[JsonConverter(typeof(ItemSpecJsonConverter))]
public abstract class ItemSpec
{
    /// <summary>Display name; must be non-blank and unique among its siblings (case-insensitive).</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional multi-line description shown in the UI.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>Returns a deep copy.</summary>
    public abstract ItemSpec Clone();
}
