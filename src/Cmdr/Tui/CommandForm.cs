using System.Text.RegularExpressions;
using Cmdr.Core.Config;
using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>
/// The text and choices of the command dialog, as the user left them. <paramref name="KeptArgs"/> and
/// <paramref name="KeptEnv"/> are the original values to keep when the args or env field is read-only; the text of that
/// field is then not parsed.
/// </summary>
internal sealed record CommandFormValues(
    string Name,
    string Description,
    string Program,
    string Args,
    string Cwd,
    string Env,
    StopMethod Method,
    string StopInput,
    bool LogEnabled,
    string LogPath,
    bool DetachLeftovers,
    StdinMode Stdin,
    string ReadyPattern = "",
    string BusyPattern = "",
    IReadOnlyList<string>? KeptArgs = null,
    IReadOnlyDictionary<string, string?>? KeptEnv = null);

/// <summary>Validation and conversion of the command dialog's values. Pure.</summary>
internal static class CommandForm
{
    /// <summary>
    /// The errors that prevent the values from becoming a command. <paramref name="namesInTargetGroup"/> are the names of the
    /// other commands in the group the command would be in (the edited command itself is left out by the caller).
    /// </summary>
    public static IReadOnlyList<string> Validate(CommandFormValues values, IEnumerable<string> namesInTargetGroup)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(namesInTargetGroup);

        var errors = new List<string>();
        string name = values.Name.Trim();
        if (name.Length == 0)
        {
            errors.Add("Name must not be blank.");
        }
        else if (namesInTargetGroup.Any(other => string.Equals(other.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Another command in this group is already named '{name}'.");
        }

        if (values.Program.Trim().Length == 0)
        {
            errors.Add("Program must not be blank.");
        }

        // A kept environment is the original value of a read-only field: its text is not parsed.
        if (values.KeptEnv is null)
        {
            foreach (string error in EnvText.Parse(values.Env).Errors)
            {
                errors.Add("Env: " + error);
            }
        }

        errors.AddRange(ValidateReady(values.ReadyPattern, values.BusyPattern));

        // The stop input is only used by the Input method; other values are not checked.
        if (values.Method == StopMethod.Input)
        {
            EscapeParseResult stopInput = EscapeText.Parse(values.StopInput);
            if (stopInput.Error is not null)
            {
                errors.Add(stopInput.Error);
            }
            else if (stopInput.Value.Length == 0)
            {
                errors.Add("Stop input must not be empty when the stop method is Input.");
            }

            if (values.Stdin == StdinMode.Console)
            {
                errors.Add("The Input stop method needs Input 'Pipe' (there is nothing to write the stop input to).");
            }
        }

        return errors;
    }

    /// <summary>
    /// Builds the command spec. Call only when <see cref="Validate"/> returned no errors; the parse errors are not repeated here.
    /// Blank optional fields become null. Stop input is kept only for the Input method.
    /// </summary>
    public static CommandSpec Build(CommandFormValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        string? description = string.IsNullOrWhiteSpace(values.Description) ? null : values.Description;
        string? cwd = string.IsNullOrWhiteSpace(values.Cwd) ? null : values.Cwd.Trim();
        string? logPath = string.IsNullOrWhiteSpace(values.LogPath) ? null : values.LogPath.Trim();

        return new CommandSpec
        {
            Name = values.Name.Trim(),
            Description = description,
            Program = values.Program.Trim(),
            Args = values.KeptArgs is { } keptArgs ? keptArgs.ToList() : ArgsText.Parse(values.Args).ToList(),
            Cwd = cwd,
            Env = values.KeptEnv is { } keptEnv
                ? new Dictionary<string, string?>(keptEnv)
                : new Dictionary<string, string?>(EnvText.Parse(values.Env).Values),
            Stop = new StopSpec
            {
                Method = values.Method,
                Input = values.Method == StopMethod.Input ? EscapeText.Parse(values.StopInput).Value : null,
            },
            Log = new LogSpec { Enabled = values.LogEnabled, Path = logPath },
            Leftovers = values.DetachLeftovers ? LeftoverMode.Detach : LeftoverMode.Track,
            Stdin = values.Stdin,
            Ready = BuildReady(values.ReadyPattern, values.BusyPattern),
        };
    }

    /// <summary>
    /// The errors of the readiness patterns: a busy pattern needs a ready pattern, and each non-blank pattern must be a valid
    /// regular expression (the same rule as the configuration check).
    /// </summary>
    private static IEnumerable<string> ValidateReady(string readyPattern, string busyPattern)
    {
        bool hasReady = !string.IsNullOrWhiteSpace(readyPattern);
        bool hasBusy = !string.IsNullOrWhiteSpace(busyPattern);

        if (!hasReady)
        {
            if (hasBusy)
            {
                yield return "Busy pattern needs a ready pattern.";
            }

            yield break;
        }

        if (!IsValidRegex(readyPattern))
        {
            yield return $"Ready pattern is not a valid regular expression: '{readyPattern}'.";
        }

        if (hasBusy && !IsValidRegex(busyPattern))
        {
            yield return $"Busy pattern is not a valid regular expression: '{busyPattern}'.";
        }
    }

    /// <summary>The readiness settings: null when there is no ready pattern; the busy pattern only when it is not blank.</summary>
    private static ReadySpec? BuildReady(string readyPattern, string busyPattern)
    {
        if (string.IsNullOrWhiteSpace(readyPattern))
        {
            return null;
        }

        return new ReadySpec
        {
            Pattern = readyPattern,
            Busy = string.IsNullOrWhiteSpace(busyPattern) ? null : busyPattern,
        };
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
}

/// <summary>
/// The text and choices of the group dialog, as the user left them. <paramref name="KeptEnv"/> is the original environment when
/// the environment field is read-only; then <paramref name="Env"/> is not parsed.
/// </summary>
internal sealed record GroupFormValues(
    string Name,
    string? Description,
    GroupMode Mode,
    string Cwd,
    string Env,
    bool Expanded,
    IReadOnlyDictionary<string, string?>? KeptEnv = null);

/// <summary>Validation and conversion of the group dialog's values. Pure.</summary>
internal static class GroupForm
{
    /// <summary>
    /// The names of the siblings a group or command would share a namespace with: the items of its target parent (groups and
    /// commands together), without the item being edited.
    /// </summary>
    public static IReadOnlyList<string> SiblingNames(IEnumerable<ItemEntry> siblings, ItemEntry? editing)
    {
        ArgumentNullException.ThrowIfNull(siblings);

        return siblings
            .Where(item => !ReferenceEquals(item, editing))
            .Select(item => item.Name)
            .ToList();
    }

    /// <summary>The errors of a group name: it must not be blank and must be unique (case-insensitive) among its siblings.</summary>
    /// <param name="name">The name typed.</param>
    /// <param name="siblingNames">The names of the other groups and commands under the chosen parent (see <see cref="SiblingNames"/>).</param>
    public static IReadOnlyList<string> Validate(string name, IEnumerable<string> siblingNames)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(siblingNames);

        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return new[] { "Name must not be blank." };
        }

        if (siblingNames.Any(other => string.Equals(other.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return new[] { $"Another group or command here is already named '{trimmed}'." };
        }

        return Array.Empty<string>();
    }

    /// <summary>The errors of all the values: the name (see <see cref="Validate(string, IEnumerable{string})"/>) and the environment.</summary>
    public static IReadOnlyList<string> Validate(GroupFormValues values, IEnumerable<string> siblingNames)
    {
        ArgumentNullException.ThrowIfNull(values);

        var errors = new List<string>(Validate(values.Name, siblingNames));
        if (values.KeptEnv is null)
        {
            foreach (string error in EnvText.Parse(values.Env).Errors)
            {
                errors.Add("Env: " + error);
            }
        }

        return errors;
    }

    /// <summary>
    /// Builds the group settings. Call only when <see cref="Validate(GroupFormValues, IEnumerable{string})"/> returned no errors.
    /// Blank description, working directory and an empty environment become null.
    /// </summary>
    public static GroupSettings Build(GroupFormValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        string? description = string.IsNullOrWhiteSpace(values.Description) ? null : values.Description;
        string? cwd = string.IsNullOrWhiteSpace(values.Cwd) ? null : values.Cwd.Trim();
        IReadOnlyDictionary<string, string?> env = values.KeptEnv ?? EnvText.Parse(values.Env).Values;

        return new GroupSettings(
            values.Name.Trim(),
            description,
            values.Mode,
            cwd,
            env.Count == 0 ? null : new Dictionary<string, string?>(env),
            values.Expanded);
    }
}
