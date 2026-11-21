using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Shared.Processes;

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

    /// <summary>
    /// Environment overlay of the whole file, applied before the groups (see <see cref="EnvLevel"/>). Its <c>${cwd}</c> is the folder
    /// of the file. Null means none; null values remove a variable. Omitted from the configuration file when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string?>? Env { get; set; }

    /// <summary>The file's environment script (see <see cref="EnvScriptSpec"/>). Null means none. Omitted from the configuration file when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EnvScriptSpec? EnvScript { get; set; }

    /// <summary>
    /// Names of variables to take from the top-level environment of the base file (see <see cref="Base"/>). Null means none. Only
    /// allowed when <see cref="Base"/> is set. Omitted from the configuration file when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? BaseEnv { get; set; }

    /// <summary>Names of environment variables that are secret, as written in the configuration. Null means none. Omitted from the configuration file when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Secret { get; set; }

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

        ValidateEnvNoNul(Env, "Top level", errors);
        ValidateEnvPlaceholders(Env, "Top level", errors);

        if (EnvScript is not null)
        {
            ValidateEnvScript(EnvScript, "envScript", errors);
        }

        if (BaseEnv is not null)
        {
            if (string.IsNullOrWhiteSpace(Base))
            {
                errors.Add("baseEnv needs base.");
            }

            ValidateEnvNames(BaseEnv, "baseEnv", errors);
        }

        ValidateEnvNames(Secret, "secret", errors);

        for (int g = 0; g < Groups.Count; g++)
        {
            GroupSpec group = Groups[g];
            string label = TopLevelLabel(group, g);

            if (string.IsNullOrWhiteSpace(group.Name))
            {
                errors.Add($"{label}: name must not be blank.");
            }

            ValidateGroupStrings(group, label, errors);
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

                    for (int a = 0; a < cmd.Args.Count; a++)
                    {
                        foreach (string error in Placeholders.Errors(cmd.Args[a]))
                        {
                            errors.Add($"{label}: argument {a + 1}: {error}");
                        }
                    }

                    ValidateCommandStrings(cmd, label, errors);
                    ValidateEnvNames(cmd.Secret, $"{label}: secret", errors);

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
                        ValidateGroupStrings(child, label, errors);
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

    /// <summary>
    /// Checks the strings a command hands to the launch for NUL characters: a NUL would silently truncate the Windows command
    /// line and cannot be passed on Linux either. Covers the program, each argument, cwd, each env name and value, the stop input
    /// and the log path. Arguments are numbered from 1.
    /// </summary>
    private static void ValidateCommandStrings(CommandSpec cmd, string label, List<string> errors)
    {
        ValidateNoNul(cmd.Program, $"{label}: program", errors);
        for (int a = 0; a < cmd.Args.Count; a++)
        {
            ValidateNoNul(cmd.Args[a], $"{label}: argument {a + 1}", errors);
        }

        ValidateNoNul(cmd.Cwd, $"{label}: cwd", errors);
        ValidateEnvNoNul(cmd.Env, label, errors);
        ValidateEnvPlaceholders(cmd.Env, label, errors);
        ValidateNoNul(cmd.Stop.Input, $"{label}: stop.input", errors);
        ValidateNoNul(cmd.Log.Path, $"{label}: log.path", errors);
    }

    /// <summary>Checks a group's launch strings (cwd and env), which its commands inherit. See <see cref="ValidateCommandStrings"/>.</summary>
    private static void ValidateGroupStrings(GroupSpec group, string label, List<string> errors)
    {
        ValidateNoNul(group.Cwd, $"{label}: cwd", errors);
        ValidateEnvNoNul(group.Env, label, errors);
        ValidateEnvPlaceholders(group.Env, label, errors);

        if (group.EnvScript is not null)
        {
            ValidateEnvScript(group.EnvScript, $"{label}: envScript", errors);
        }

        ValidateEnvNames(group.Secret, $"{label}: secret", errors);
    }

    /// <summary>
    /// Checks an environment script: the program must not be blank, the program, each argument and the cwd must not contain NUL,
    /// each argument must be a valid placeholder text (see <see cref="Placeholders.Errors"/>), and the timeout must be greater than 0
    /// when set. Arguments are numbered from 1.
    /// </summary>
    private static void ValidateEnvScript(EnvScriptSpec script, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(script.Program))
        {
            errors.Add($"{label}: program must not be blank.");
        }

        ValidateNoNul(script.Program, $"{label}: program", errors);
        List<string> args = script.Args ?? new List<string>();
        for (int a = 0; a < args.Count; a++)
        {
            string? arg = args[a];
            if (arg is null)
            {
                errors.Add($"{label}: argument {a + 1} must be a string, not null.");
                continue;
            }

            ValidateNoNul(arg, $"{label}: argument {a + 1}", errors);
            foreach (string error in Placeholders.Errors(arg))
            {
                errors.Add($"{label}: argument {a + 1}: {error}");
            }
        }

        ValidateNoNul(script.Cwd, $"{label}: cwd", errors);

        if (script.TimeoutSeconds is { } timeout && timeout <= 0)
        {
            errors.Add($"{label}: timeoutSeconds must be greater than 0 (got {timeout}).");
        }
    }

    /// <summary>
    /// The errors of a list of environment variable names (the <c>baseEnv</c> or <c>secret</c> rule, see the configuration check), each
    /// labelled with <paramref name="label"/>. Empty when the names are valid. For the editing dialogs, which check names before they
    /// build a configuration.
    /// </summary>
    public static IReadOnlyList<string> EnvNameErrors(IReadOnlyList<string>? names, string label)
    {
        var errors = new List<string>();
        ValidateEnvNames(names, label, errors);
        return errors;
    }

    /// <summary>
    /// Checks a list of environment variable names (<c>baseEnv</c> or <c>secret</c>): each must be a valid name (see
    /// <see cref="Placeholders.IsEnvName"/>), and no name may occur twice. Duplicates are exact (case-sensitive), as on Linux.
    /// </summary>
    private static void ValidateEnvNames(IReadOnlyList<string>? names, string label, List<string> errors)
    {
        if (names is null)
        {
            return;
        }

        var positions = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var order = new List<string>();
        for (int i = 0; i < names.Count; i++)
        {
            string? name = names[i];
            if (name is null || !Placeholders.IsEnvName(name))
            {
                errors.Add($"{label}: name '{(name ?? string.Empty).Replace('\0', '?')}' is not a valid environment variable name.");
                continue;
            }

            if (!positions.TryGetValue(name, out List<int>? list))
            {
                list = new List<int>();
                positions[name] = list;
                order.Add(name);
            }

            list.Add(i + 1);
        }

        foreach (string name in order.Where(n => positions[n].Count > 1))
        {
            errors.Add($"{label}: duplicate name '{name}' ({FormatPositions(positions[name])}).");
        }
    }

    /// <summary>
    /// Checks the placeholders of each env value: malformed ones (see <see cref="Placeholders.Errors"/>). Null values (which remove a
    /// variable) are not checked.
    /// </summary>
    private static void ValidateEnvPlaceholders(Dictionary<string, string?>? env, string label, List<string> errors)
    {
        if (env is null)
        {
            return;
        }

        foreach (KeyValuePair<string, string?> entry in env)
        {
            if (entry.Value is null)
            {
                continue;
            }

            string name = entry.Key.Replace('\0', '?');
            foreach (string error in Placeholders.Errors(entry.Value))
            {
                errors.Add($"{label}: env '{name}': {error}");
            }
        }
    }

    private static void ValidateEnvNoNul(Dictionary<string, string?>? env, string label, List<string> errors)
    {
        if (env is null)
        {
            return;
        }

        foreach (KeyValuePair<string, string?> entry in env)
        {
            ValidateNoNul(entry.Key, $"{label}: env name '{entry.Key.Replace('\0', '?')}'", errors);
            ValidateNoNul(entry.Value, $"{label}: env '{entry.Key.Replace('\0', '?')}' value", errors);
        }
    }

    /// <summary>Adds <c>{subject} contains a NUL character.</c> when <paramref name="value"/> contains one.</summary>
    private static void ValidateNoNul(string? value, string subject, List<string> errors)
    {
        if (value is not null && value.Contains('\0'))
        {
            errors.Add($"{subject} contains a NUL character.");
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
