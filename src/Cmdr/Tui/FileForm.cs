using Cmdr.Core.Config;
using Cmdr.Core.Session;

namespace Cmdr.Tui;

/// <summary>
/// The text and choices of the file dialog, as the user left them. A <c>Kept</c> value is the original of a read-only field: it is saved
/// exactly and its text is not parsed. <paramref name="HasBase"/> is false when the file has no <c>base</c>, which makes the base env
/// field read-only (its names need a base file).
/// </summary>
internal sealed record FileFormValues(
    string Env,
    IReadOnlyDictionary<string, string?>? KeptEnv,
    bool HasBase,
    string BaseEnv,
    IReadOnlyList<string>? KeptBaseEnv,
    string Secret,
    IReadOnlyList<string>? KeptSecret,
    EnvScriptSpec? EnvScript);

/// <summary>Validation and conversion of the file dialog's values. Pure.</summary>
internal static class FileForm
{
    /// <summary>
    /// The errors of the values: the env lines, the base env and the secret names (the rules of the configuration check), and base env
    /// names without a base file. Kept values are not checked.
    /// </summary>
    public static IReadOnlyList<string> Validate(FileFormValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var errors = new List<string>();
        if (values.KeptEnv is null)
        {
            foreach (string error in EnvText.Parse(values.Env).Errors)
            {
                errors.Add("Env: " + error);
            }
        }

        if (values.KeptBaseEnv is null)
        {
            if (!values.HasBase && NameListText.Parse(values.BaseEnv).Count > 0)
            {
                errors.Add("Base env needs a base file: this file has no base.");
            }

            errors.AddRange(NameListText.Errors(values.BaseEnv, "Base env"));
        }

        if (values.KeptSecret is null)
        {
            errors.AddRange(NameListText.Errors(values.Secret, "Secret"));
        }

        return errors;
    }

    /// <summary>
    /// Builds the file's settings. Call only when <see cref="Validate"/> returned no errors. Empty settings become null (not written);
    /// kept values are saved exactly.
    /// </summary>
    public static FileSettings Build(FileFormValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        IReadOnlyDictionary<string, string?> env = values.KeptEnv ?? EnvText.Parse(values.Env).Values;
        return new FileSettings(
            env.Count == 0 ? null : new Dictionary<string, string?>(env),
            values.EnvScript,
            NameListText.NamesOrNull(values.KeptBaseEnv ?? NameListText.Parse(values.BaseEnv)),
            NameListText.NamesOrNull(values.KeptSecret ?? NameListText.Parse(values.Secret)));
    }
}
