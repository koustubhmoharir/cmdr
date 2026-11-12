using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>Root of the cmdr configuration file.</summary>
public class CmdrConfig
{
    /// <summary>Maximum number of output lines kept per command in memory.</summary>
    public int BufferLines { get; set; } = 10000;

    /// <summary>
    /// Directory for log files; relative paths resolve against the config directory. Null means <c>logs</c>
    /// (see <see cref="ConfigPaths.ResolveLogsDir"/>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogsDir { get; set; }

    /// <summary>Top-level groups managed by cmdr, in display order.</summary>
    public List<GroupSpec> Groups { get; set; } = new();

    /// <summary>
    /// Returns human-readable validation errors. An empty list means the configuration is valid.
    /// Loading does not validate; callers (the UI) decide when to check.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (BufferLines <= 0)
        {
            errors.Add($"bufferLines must be greater than 0 (got {BufferLines}).");
        }

        for (int g = 0; g < Groups.Count; g++)
        {
            GroupSpec group = Groups[g];
            bool blank = string.IsNullOrWhiteSpace(group.Name);
            string label = blank ? $"Group #{g + 1}" : $"Group '{group.Name}'";

            if (blank)
            {
                errors.Add($"{label}: name must not be blank.");
            }

            ValidateItems(group, label, errors);
        }

        foreach (KeyValuePair<string, List<int>> dup in FindDuplicates(Groups.Select(g => g.Name)))
        {
            errors.Add($"Duplicate group name '{dup.Key}' (groups {FormatPositions(dup.Value)}).");
        }

        return errors;
    }

    /// <summary>
    /// Validates the items of <paramref name="parent"/> and, recursively, of its nested groups. <paramref name="parentLabel"/>
    /// identifies the parent in messages, e.g. <c>Group 'go-apps' › 'nav'</c>.
    /// </summary>
    private static void ValidateItems(GroupSpec parent, string parentLabel, List<string> errors)
    {
        for (int i = 0; i < parent.Items.Count; i++)
        {
            ItemSpec item = parent.Items[i];
            string label = $"{parentLabel} › {DescribeItem(item, i)}";

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                errors.Add($"{label}: name must not be blank.");
            }

            switch (item)
            {
                case CommandSpec cmd:
                    if (string.IsNullOrWhiteSpace(cmd.Program))
                    {
                        errors.Add($"{label}: program must not be blank.");
                    }

                    if (cmd.Stop.Method == StopMethod.Input && string.IsNullOrEmpty(cmd.Stop.Input))
                    {
                        errors.Add($"{label}: stop.input must not be empty when stop.method is 'input'.");
                    }

                    break;

                case GroupSpec child:
                    ValidateItems(child, label, errors);
                    break;
            }
        }

        // Commands and groups share one namespace among siblings.
        foreach (KeyValuePair<string, List<int>> dup in FindDuplicates(parent.Items.Select(i => i.Name)))
        {
            errors.Add($"{parentLabel}: duplicate name '{dup.Key}' (items {FormatPositions(dup.Value)}).");
        }
    }

    /// <summary>
    /// Finds names that occur more than once (case-insensitive), keyed by the first spelling seen,
    /// with 1-based positions. Blank names are reported elsewhere and are ignored here.
    /// </summary>
    private static Dictionary<string, List<int>> FindDuplicates(IEnumerable<string> names)
    {
        var byName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        int position = 0;
        foreach (string name in names)
        {
            position++;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!byName.TryGetValue(name, out List<int>? positions))
            {
                positions = new List<int>();
                byName[name] = positions;
            }

            positions.Add(position);
        }

        return byName.Where(e => e.Value.Count > 1).ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatPositions(List<int> positions) =>
        string.Join(", ", positions.Select(p => $"#{p}"));

    /// <summary>Describes a nested item by its name, or by its 1-based position when the name is blank.</summary>
    private static string DescribeItem(ItemSpec item, int index) => (item, string.IsNullOrWhiteSpace(item.Name)) switch
    {
        (GroupSpec, true) => $"#{index + 1}",
        (GroupSpec, false) => $"'{item.Name}'",
        (_, true) => $"command #{index + 1}",
        (_, false) => $"command '{item.Name}'",
    };
}
