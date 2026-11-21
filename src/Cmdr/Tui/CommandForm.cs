using System.Text.RegularExpressions;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Shared.Processes;

namespace Cmdr.Tui;

/// <summary>
/// The text and choices of the command dialog, as the user left them. A <c>Kept</c> value is the original of a field that is
/// read-only or was not edited: it is saved exactly, and the text of that field is not used (see <see cref="FieldDraft"/>).
/// <paramref name="KeptArgs"/> and <paramref name="KeptEnv"/> are the same for the args and env fields, whose text is not parsed.
/// <paramref name="SecretText"/> is the secret names typed (see <see cref="NameListText"/>); <paramref name="KeptSecret"/> is the
/// original names when that field is read-only, and then the text is not parsed.
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
    string SecretText = "",
    IReadOnlyList<string>? KeptArgs = null,
    IReadOnlyDictionary<string, string?>? KeptEnv = null,
    KeptValue? KeptName = null,
    KeptValue? KeptProgram = null,
    KeptValue? KeptCwd = null,
    KeptValue? KeptLogPath = null,
    KeptValue? KeptReadyPattern = null,
    KeptValue? KeptBusyPattern = null,
    IReadOnlyList<string>? KeptSecret = null);

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
        string name = SavedText.Required(values.Name, values.KeptName).Trim();
        if (name.Length == 0)
        {
            errors.Add("Name must not be blank.");
        }
        else if (namesInTargetGroup.Any(other => string.Equals(other.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Another command in this group is already named '{name}'.");
        }

        if (SavedText.Required(values.Program, values.KeptProgram).Trim().Length == 0)
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

        errors.AddRange(ValidateReady(
            SavedText.Pattern(values.ReadyPattern, values.KeptReadyPattern),
            SavedText.Pattern(values.BusyPattern, values.KeptBusyPattern)));

        // Kept secret names are the original of a read-only field: they are not parsed.
        if (values.KeptSecret is null)
        {
            errors.AddRange(NameListText.Errors(values.SecretText, "Secret"));
        }

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
    /// Edited text is trimmed and blank optional fields become null; kept originals are saved exactly. Stop input is kept only
    /// for the Input method.
    /// </summary>
    public static CommandSpec Build(CommandFormValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        string? description = string.IsNullOrWhiteSpace(values.Description) ? null : values.Description;

        return new CommandSpec
        {
            Name = SavedText.Required(values.Name, values.KeptName),
            Description = description,
            Program = SavedText.Required(values.Program, values.KeptProgram),
            Args = values.KeptArgs is { } keptArgs ? keptArgs.ToList() : ArgsText.Parse(values.Args).ToList(),
            Cwd = SavedText.Optional(values.Cwd, values.KeptCwd),
            Env = values.KeptEnv is { } keptEnv
                ? new Dictionary<string, string?>(keptEnv)
                : new Dictionary<string, string?>(EnvText.Parse(values.Env).Values),
            Stop = new StopSpec
            {
                Method = values.Method,
                Input = values.Method == StopMethod.Input ? EscapeText.Parse(values.StopInput).Value : null,
            },
            Log = new LogSpec
            {
                Enabled = values.LogEnabled,
                Path = SavedText.Optional(values.LogPath, values.KeptLogPath),
            },
            Leftovers = values.DetachLeftovers ? LeftoverMode.Detach : LeftoverMode.Track,
            Stdin = values.Stdin,
            Secret = NameListText.NamesOrNull(values.KeptSecret ?? NameListText.Parse(values.SecretText)),
            Ready = BuildReady(
                SavedText.Pattern(values.ReadyPattern, values.KeptReadyPattern),
                SavedText.Pattern(values.BusyPattern, values.KeptBusyPattern)),
        };
    }

    /// <summary>
    /// The errors of the readiness patterns: a busy pattern needs a ready pattern, and each non-blank pattern must be a valid
    /// regular expression (the same rule as the configuration check).
    /// </summary>
    private static IEnumerable<string> ValidateReady(string? readyPattern, string? busyPattern)
    {
        if (string.IsNullOrWhiteSpace(readyPattern))
        {
            if (!string.IsNullOrWhiteSpace(busyPattern))
            {
                yield return "Busy pattern needs a ready pattern.";
            }

            yield break;
        }

        if (!IsValidRegex(readyPattern))
        {
            yield return $"Ready pattern is not a valid regular expression: '{readyPattern}'.";
        }

        if (!string.IsNullOrWhiteSpace(busyPattern) && !IsValidRegex(busyPattern))
        {
            yield return $"Busy pattern is not a valid regular expression: '{busyPattern}'.";
        }
    }

    /// <summary>The readiness settings: null when there is no ready pattern. The busy pattern is as given (null when there is none).</summary>
    private static ReadySpec? BuildReady(string? readyPattern, string? busyPattern)
    {
        if (string.IsNullOrWhiteSpace(readyPattern))
        {
            return null;
        }

        return new ReadySpec { Pattern = readyPattern, Busy = busyPattern };
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
/// the environment field is read-only; then <paramref name="Env"/> is not parsed. <paramref name="KeptName"/> and
/// <paramref name="KeptCwd"/> are the originals of the name and working dir fields when they were not edited (see
/// <see cref="FieldDraft"/>). <paramref name="EnvScript"/> is the environment script as the env script dialog left it (null for
/// none). <paramref name="SecretText"/> is the secret names typed; <paramref name="KeptSecret"/> is the original names when that
/// field is read-only, and then the text is not parsed.
/// </summary>
internal sealed record GroupFormValues(
    string Name,
    string? Description,
    GroupMode Mode,
    string Cwd,
    string Env,
    bool Expanded,
    IReadOnlyDictionary<string, string?>? KeptEnv = null,
    KeptValue? KeptName = null,
    KeptValue? KeptCwd = null,
    EnvScriptSpec? EnvScript = null,
    string SecretText = "",
    IReadOnlyList<string>? KeptSecret = null);

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

        var errors = new List<string>(Validate(SavedText.Required(values.Name, values.KeptName), siblingNames));
        if (values.KeptEnv is null)
        {
            foreach (string error in EnvText.Parse(values.Env).Errors)
            {
                errors.Add("Env: " + error);
            }
        }

        // Kept secret names are the original of a read-only field: they are not parsed.
        if (values.KeptSecret is null)
        {
            errors.AddRange(NameListText.Errors(values.SecretText, "Secret"));
        }

        return errors;
    }

    /// <summary>
    /// Builds the group settings. Call only when <see cref="Validate(GroupFormValues, IEnumerable{string})"/> returned no errors.
    /// Edited text is trimmed; a blank description, working directory or an empty environment becomes null. Kept originals are
    /// saved exactly. The environment script and the secret names are as the dialog holds them (empty secret names become null).
    /// </summary>
    public static GroupSettings Build(GroupFormValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        string? description = string.IsNullOrWhiteSpace(values.Description) ? null : values.Description;
        IReadOnlyDictionary<string, string?> env = values.KeptEnv ?? EnvText.Parse(values.Env).Values;

        return new GroupSettings(
            SavedText.Required(values.Name, values.KeptName),
            description,
            values.Mode,
            SavedText.Optional(values.Cwd, values.KeptCwd),
            env.Count == 0 ? null : new Dictionary<string, string?>(env),
            values.Expanded,
            values.EnvScript,
            NameListText.NamesOrNull(values.KeptSecret ?? NameListText.Parse(values.SecretText)));
    }
}
