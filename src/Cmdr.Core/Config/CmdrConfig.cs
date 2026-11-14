using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Cmdr.Core.Config;

/// <summary>Root of the cmdr configuration file.</summary>
public class CmdrConfig
{
    /// <summary>
    /// Maximum number of nested group levels. A top-level group is level 1, a group inside it level 2, and so on; level
    /// <see cref="MaxGroupDepth"/> is the deepest allowed. Enforced when loading, validating and editing.
    /// </summary>
    public const int MaxGroupDepth = 16;

    /// <summary>
    /// Path of a base file this file imports. The base's groups and settings are loaded as well but not merged with this file's.
    /// A relative path resolves against the directory of this file. Null means no base (see <see cref="ConfigStore.LoadChain"/>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Base { get; set; }

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

        if (Base is not null && string.IsNullOrWhiteSpace(Base))
        {
            errors.Add("base must not be blank.");
        }

        if (BufferLines <= 0)
        {
            errors.Add($"bufferLines must be greater than 0 (got {BufferLines}).");
        }

        for (int g = 0; g < Groups.Count; g++)
        {
            GroupSpec group = Groups[g];
            string label = TopLevelLabel(group, g);

            if (string.IsNullOrWhiteSpace(group.Name))
            {
                errors.Add($"{label}: name must not be blank.");
            }

            ValidateItems(group, label, level: 1, errors);
        }

        foreach (KeyValuePair<string, List<int>> dup in FindDuplicates(Groups.Select(g => g.Name)))
        {
            errors.Add($"Duplicate group name '{dup.Key}' (groups {FormatPositions(dup.Value)}).");
        }

        return errors;
    }

    /// <summary>
    /// Validates the items of <paramref name="parent"/> and, recursively, of its nested groups. <paramref name="parentLabel"/>
    /// identifies the parent in messages, e.g. <c>Group 'go-apps' › 'nav'</c>. <paramref name="level"/> is the nesting level of
    /// <paramref name="parent"/> (1 for a top-level group). A nested group deeper than <see cref="MaxGroupDepth"/> is reported
    /// once and not validated further.
    /// </summary>
    private static void ValidateItems(GroupSpec parent, string parentLabel, int level, List<string> errors)
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

                    if (cmd.Stdin == StdinMode.Console && cmd.Stop.Method == StopMethod.Input)
                    {
                        errors.Add($"{label}: stop.method 'input' needs stdin 'pipe' (there is nothing to write the stop input to).");
                    }

                    foreach (string error in InputPlaceholders.Errors(cmd.Args))
                    {
                        errors.Add($"{label}: {error}");
                    }

                    if (cmd.Ready is not null)
                    {
                        ValidateReady(cmd.Ready, label, errors);
                    }

                    break;

                case GroupSpec child:
                    if (level + 1 > MaxGroupDepth)
                    {
                        errors.Add($"{label}: groups can be nested at most {MaxGroupDepth} levels deep.");
                    }
                    else
                    {
                        ValidateItems(child, label, level + 1, errors);
                    }

                    break;
            }
        }

        // Commands and groups share one namespace among siblings.
        foreach (KeyValuePair<string, List<int>> dup in FindDuplicates(parent.Items.Select(i => i.Name)))
        {
            errors.Add($"{parentLabel}: duplicate name '{dup.Key}' (items {FormatPositions(dup.Value)}).");
        }
    }

    /// <summary>Checks the readiness patterns of a command: each must be a valid, non-blank regular expression.</summary>
    private static void ValidateReady(ReadySpec ready, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(ready.Pattern))
        {
            errors.Add($"{label}: ready.pattern must not be blank.");
        }
        else if (!IsValidRegex(ready.Pattern))
        {
            errors.Add($"{label}: ready.pattern is not a valid regular expression: '{ready.Pattern}'.");
        }

        if (ready.Busy is not null)
        {
            if (string.IsNullOrWhiteSpace(ready.Busy))
            {
                errors.Add($"{label}: ready.busy must not be blank (omit it instead).");
            }
            else if (!IsValidRegex(ready.Busy))
            {
                errors.Add($"{label}: ready.busy is not a valid regular expression: '{ready.Busy}'.");
            }
        }
    }

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
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

    /// <summary>
    /// Labels a top-level group in messages: <c>Group 'name'</c>, or <c>Group #n</c> when the name is blank, where n is the
    /// 1-based position of the group in its file.
    /// </summary>
    internal static string TopLevelLabel(GroupSpec group, int index) =>
        string.IsNullOrWhiteSpace(group.Name) ? $"Group #{index + 1}" : $"Group '{group.Name}'";

    /// <summary>Describes a nested item by its name, or by its 1-based position when the name is blank.</summary>
    internal static string DescribeItem(ItemSpec item, int index) => (item, string.IsNullOrWhiteSpace(item.Name)) switch
    {
        (GroupSpec, true) => $"#{index + 1}",
        (GroupSpec, false) => $"'{item.Name}'",
        (_, true) => $"command #{index + 1}",
        (_, false) => $"command '{item.Name}'",
    };
}
