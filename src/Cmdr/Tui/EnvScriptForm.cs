using System.Globalization;
using Cmdr.Core.Config;

namespace Cmdr.Tui;

/// <summary>
/// The text and choices of the environment script dialog, as the user left them. <paramref name="KeptProgram"/>,
/// <paramref name="KeptCwd"/> and <paramref name="KeptArgs"/> are the originals of read-only or unedited fields: they are saved
/// exactly (see <see cref="FieldDraft"/> and <see cref="ArgsText"/>). <paramref name="Timeout"/> is the typed seconds; blank means the
/// default.
/// </summary>
internal sealed record EnvScriptFormValues(
    string Program,
    string Args,
    string Cwd,
    string Timeout,
    EnvScriptRerun Rerun,
    KeptValue? KeptProgram = null,
    KeptValue? KeptCwd = null,
    IReadOnlyList<string>? KeptArgs = null);

/// <summary>Validation and conversion of the environment script dialog's values, and its one-line summary. Pure.</summary>
internal static class EnvScriptForm
{
    /// <summary>The errors that prevent the values from becoming a script: the program must not be blank, and the timeout must be empty or a whole number above 0.</summary>
    public static IReadOnlyList<string> Validate(EnvScriptFormValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var errors = new List<string>();
        if (SavedText.Required(values.Program, values.KeptProgram).Trim().Length == 0)
        {
            errors.Add("Program must not be blank.");
        }

        if (!TryParseTimeout(values.Timeout, out _))
        {
            errors.Add(
                $"Timeout must be a whole number of seconds above 0, or empty for the default ({EnvScriptSpec.DefaultTimeoutSeconds} seconds).");
        }

        return errors;
    }

    /// <summary>
    /// Builds the script. Call only when <see cref="Validate"/> returned no errors. Edited text is trimmed and a blank working dir
    /// becomes null; a blank timeout becomes null (the default).
    /// </summary>
    public static EnvScriptSpec Build(EnvScriptFormValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        TryParseTimeout(values.Timeout, out int? seconds);
        return new EnvScriptSpec
        {
            Program = SavedText.Required(values.Program, values.KeptProgram),
            Args = values.KeptArgs is { } keptArgs ? keptArgs.ToList() : ArgsText.Parse(values.Args).ToList(),
            Cwd = SavedText.Optional(values.Cwd, values.KeptCwd),
            TimeoutSeconds = seconds,
            Rerun = values.Rerun,
        };
    }

    /// <summary>
    /// The timeout text: blank is no timeout (null, the default), otherwise a whole number above 0. False for anything else.
    /// </summary>
    public static bool TryParseTimeout(string text, out int? seconds)
    {
        ArgumentNullException.ThrowIfNull(text);

        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            seconds = null;
            return true;
        }

        if (int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0)
        {
            seconds = value;
            return true;
        }

        seconds = null;
        return false;
    }

    /// <summary>The timeout as the field shows it: the seconds, or empty for the default.</summary>
    public static string TimeoutText(int? seconds) => seconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// The one-line summary of a script for a button row: "none", or the program and its arguments joined by spaces. Longer text is cut
    /// to <paramref name="width"/> characters, the last one an ellipsis. Line breaks show as spaces.
    /// </summary>
    public static string Summary(EnvScriptSpec? spec, int width)
    {
        if (spec is null)
        {
            return "none";
        }

        string text = string.Join(" ", new[] { spec.Program }.Concat(spec.Args)).ReplaceLineEndings(" ").Trim();
        return text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";
    }
}
