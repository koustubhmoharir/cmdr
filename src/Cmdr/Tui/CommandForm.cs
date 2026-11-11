using Cmdr.Core.Config;

namespace Cmdr.Tui;

/// <summary>The text and choices of the command dialog, as the user left them.</summary>
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
    string LogPath);

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

        EnvParseResult env = EnvText.Parse(values.Env);
        foreach (string error in env.Errors)
        {
            errors.Add("Env: " + error);
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
            Args = ArgsText.Parse(values.Args).ToList(),
            Cwd = cwd,
            Env = new Dictionary<string, string?>(EnvText.Parse(values.Env).Values),
            Stop = new StopSpec
            {
                Method = values.Method,
                Input = values.Method == StopMethod.Input ? EscapeText.Parse(values.StopInput).Value : null,
            },
            Log = new LogSpec { Enabled = values.LogEnabled, Path = logPath },
        };
    }
}

/// <summary>Validation of the group dialog's name. Pure.</summary>
internal static class GroupForm
{
    /// <summary>The errors of a group name: it must not be blank and must be unique (case-insensitive) among the other groups.</summary>
    public static IReadOnlyList<string> Validate(string name, IEnumerable<string> otherGroupNames)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(otherGroupNames);

        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return new[] { "Name must not be blank." };
        }

        if (otherGroupNames.Any(other => string.Equals(other.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return new[] { $"Another group is already named '{trimmed}'." };
        }

        return Array.Empty<string>();
    }
}
