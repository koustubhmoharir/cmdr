using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>How the commands of a group are started.</summary>
[JsonConverter(typeof(GroupModeJsonConverter))]
public enum GroupMode
{
    /// <summary>Commands start independently. Serialized as "parallel".</summary>
    [JsonStringEnumMemberName("parallel")]
    Parallel,

    /// <summary>Commands run one after another in file order; a failure cancels the rest. Serialized as "sequential".</summary>
    [JsonStringEnumMemberName("sequential")]
    Sequential,
}

/// <summary>A named, ordered collection of commands.</summary>
public class GroupSpec
{
    /// <summary>Display name; must be non-blank and unique across groups (case-insensitive).</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional description shown in the UI.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>How the commands are started; defaults to parallel.</summary>
    public GroupMode Mode { get; set; } = GroupMode.Parallel;

    /// <summary>Commands in this group, in display order.</summary>
    public List<CommandSpec> Commands { get; set; } = new();

    /// <summary>Returns a deep copy: the command list and each command are copied.</summary>
    public GroupSpec Clone() => new()
    {
        Name = Name,
        Description = Description,
        Mode = Mode,
        Commands = Commands.Select(c => c.Clone()).ToList(),
    };
}

/// <summary>String-only enum converter: rejects integer values such as <c>1</c>.</summary>
internal sealed class GroupModeJsonConverter() : JsonStringEnumConverter<GroupMode>(namingPolicy: null, allowIntegerValues: false);
