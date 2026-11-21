using System.Text.Json.Serialization;
using Shared.Processes;

namespace Cmdr.Core.Config;

/// <summary>How the items of a group are started.</summary>
[JsonConverter(typeof(GroupModeJsonConverter))]
public enum GroupMode
{
    /// <summary>Items start independently. Serialized as "parallel".</summary>
    [JsonStringEnumMemberName("parallel")]
    Parallel,

    /// <summary>Items run one after another in file order; a failure cancels the rest. Serialized as "sequential".</summary>
    [JsonStringEnumMemberName("sequential")]
    Sequential,
}

/// <summary>A named, ordered collection of items (commands and nested groups).</summary>
public class GroupSpec : ItemSpec
{
    /// <summary>How the items are started; defaults to parallel.</summary>
    public GroupMode Mode { get; set; } = GroupMode.Parallel;

    /// <summary>Working directory for the items; relative paths resolve against the parent's working directory.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; set; }

    /// <summary>Environment overlay inherited by descendants. Null means none; null values remove a variable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string?>? Env { get; set; }

    /// <summary>
    /// The environment script of the group (see <see cref="EnvScriptSpec"/>). Null means none. Omitted from the configuration file when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EnvScriptSpec? EnvScript { get; set; }

    /// <summary>Names of environment variables that are secret, as written in the configuration. Null means none. Omitted from the configuration file when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Secret { get; set; }

    /// <summary>
    /// Whether the group starts expanded when the configuration is loaded. Null (omitted) and true mean expanded; only false is
    /// written. It does not follow the collapse state of the list while the program runs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Expanded { get; set; }

    /// <summary>Commands and nested groups, in display order.</summary>
    public List<ItemSpec> Items { get; set; } = new();

    /// <summary>Returns a deep copy: the items, environment and each nested item are copied.</summary>
    public override GroupSpec Clone() => new()
    {
        Name = Name,
        Description = Description,
        Mode = Mode,
        Cwd = Cwd,
        Env = Env is null ? null : new Dictionary<string, string?>(Env),
        EnvScript = EnvScript?.Clone(),
        Secret = Secret is null ? null : new List<string>(Secret),
        Expanded = Expanded,
        Items = Items.Select(i => i.Clone()).ToList(),
    };
}

/// <summary>String-only enum converter: rejects integer values such as <c>1</c>.</summary>
internal sealed class GroupModeJsonConverter : StrictEnumJsonConverter<GroupMode> { }
