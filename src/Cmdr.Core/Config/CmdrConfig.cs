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

    /// <summary>Command groups managed by cmdr, in display order.</summary>
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
            string groupLabel = DescribeGroup(group, g);

            if (string.IsNullOrWhiteSpace(group.Name))
            {
                errors.Add($"Group #{g + 1}: name must not be blank.");
            }

            for (int c = 0; c < group.Commands.Count; c++)
            {
                CommandSpec cmd = group.Commands[c];
                string label = $"{groupLabel}, {DescribeCommand(cmd, c)}";

                if (string.IsNullOrWhiteSpace(cmd.Name))
                {
                    errors.Add($"{label}: name must not be blank.");
                }

                if (string.IsNullOrWhiteSpace(cmd.Program))
                {
                    errors.Add($"{label}: program must not be blank.");
                }

                if (cmd.Stop.Method == StopMethod.Input && string.IsNullOrEmpty(cmd.Stop.Input))
                {
                    errors.Add($"{label}: stop.input must not be empty when stop.method is 'input'.");
                }
            }

            // Command names are unique within their group only.
            foreach (KeyValuePair<string, List<int>> dup in FindDuplicates(group.Commands.Select(c => c.Name)))
            {
                errors.Add($"{groupLabel}: duplicate command name '{dup.Key}' (commands {FormatPositions(dup.Value)}).");
            }
        }

        foreach (KeyValuePair<string, List<int>> dup in FindDuplicates(Groups.Select(g => g.Name)))
        {
            errors.Add($"Duplicate group name '{dup.Key}' (groups {FormatPositions(dup.Value)}).");
        }

        return errors;
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

    private static string DescribeGroup(GroupSpec group, int index) =>
        string.IsNullOrWhiteSpace(group.Name)
            ? $"Group #{index + 1}"
            : $"Group '{group.Name}'";

    private static string DescribeCommand(CommandSpec cmd, int index) =>
        string.IsNullOrWhiteSpace(cmd.Name)
            ? $"command #{index + 1}"
            : $"command '{cmd.Name}'";
}
