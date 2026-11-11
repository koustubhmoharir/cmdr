namespace Cmdr.Core.Config;

/// <summary>Root of the cmdr configuration file.</summary>
public class CmdrConfig
{
    /// <summary>Maximum number of output lines kept per command in memory.</summary>
    public int BufferLines { get; set; } = 10000;

    /// <summary>Commands managed by cmdr, in display order.</summary>
    public List<CommandSpec> Commands { get; set; } = new();

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

        for (int i = 0; i < Commands.Count; i++)
        {
            CommandSpec cmd = Commands[i];
            string label = DescribeCommand(cmd, i);

            if (string.IsNullOrWhiteSpace(cmd.Name))
            {
                errors.Add($"Command #{i + 1}: name must not be blank.");
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

        // Duplicate names (case-insensitive). Blank names are reported above, not here.
        var byName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Commands.Count; i++)
        {
            string name = Commands[i].Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!byName.TryGetValue(name, out List<int>? indices))
            {
                indices = new List<int>();
                byName[name] = indices;
            }

            indices.Add(i + 1);
        }

        foreach (KeyValuePair<string, List<int>> entry in byName)
        {
            if (entry.Value.Count > 1)
            {
                string positions = string.Join(", ", entry.Value);
                errors.Add($"Duplicate command name '{entry.Key}' (commands #{positions}).");
            }
        }

        return errors;
    }

    private static string DescribeCommand(CommandSpec cmd, int index) =>
        string.IsNullOrWhiteSpace(cmd.Name)
            ? $"Command #{index + 1}"
            : $"Command '{cmd.Name}'";
}
