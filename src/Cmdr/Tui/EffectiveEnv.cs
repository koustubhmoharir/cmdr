using System.Collections;
using Shared.Processes;

namespace Cmdr.Tui;

/// <summary>
/// The environment variables that a launch sets, with the values it gives them. Pure, except <see cref="Process"/>, which reads
/// the process environment.
/// </summary>
internal static class EffectiveEnv
{
    /// <summary>
    /// The keys that any level mentions, in first-mention order, each with its final value: the value expanded, or null when the
    /// variable is removed. The levels are applied to a copy of <paramref name="environment"/> exactly as the launch applies them
    /// (see <see cref="Applied"/>). Input placeholders are kept as written. Empty when no level mentions a key.
    /// </summary>
    /// <param name="levels">The environment overlays of the launch, outermost first.</param>
    /// <param name="configDir">The config directory, the value of <c>${configDir}</c>.</param>
    /// <param name="environment">The environment the values are applied to.</param>
    /// <param name="windows">The platform: keys are compared case-insensitively on Windows.</param>
    /// <param name="pathSeparator">The value of <c>${pathsep}</c>.</param>
    public static IReadOnlyList<KeyValuePair<string, string?>> Of(
        IReadOnlyList<EnvLevel> levels,
        string configDir,
        IReadOnlyDictionary<string, string?> environment,
        bool windows,
        char pathSeparator)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentException.ThrowIfNullOrEmpty(configDir);
        ArgumentNullException.ThrowIfNull(environment);

        StringComparer keys = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        var mentioned = new List<string>();
        var seen = new HashSet<string>(keys);
        foreach (EnvLevel level in levels)
        {
            foreach (string key in level.Env.Keys)
            {
                if (seen.Add(key))
                {
                    mentioned.Add(key);
                }
            }
        }

        if (mentioned.Count == 0)
        {
            return Array.Empty<KeyValuePair<string, string?>>();
        }

        IReadOnlyDictionary<string, string?> target = Applied(levels, configDir, environment, windows, pathSeparator);

        var result = new List<KeyValuePair<string, string?>>(mentioned.Count);
        foreach (string key in mentioned)
        {
            result.Add(new KeyValuePair<string, string?>(key, target.TryGetValue(key, out string? value) ? value : null));
        }

        return result;
    }

    /// <summary>
    /// The whole environment of a launch: <paramref name="environment"/> with the levels applied, as the launch applies them,
    /// keyed case-insensitively on Windows. Input placeholders are kept as written. This is what <c>${env:NAME}</c> in the
    /// arguments of a copied command refers to.
    /// </summary>
    /// <param name="levels">The environment overlays of the launch, outermost first.</param>
    /// <param name="configDir">The config directory, the value of <c>${configDir}</c>.</param>
    /// <param name="environment">The environment the values are applied to.</param>
    /// <param name="windows">The platform: keys are compared case-insensitively on Windows.</param>
    /// <param name="pathSeparator">The value of <c>${pathsep}</c>.</param>
    public static IReadOnlyDictionary<string, string?> Applied(
        IReadOnlyList<EnvLevel> levels,
        string configDir,
        IReadOnlyDictionary<string, string?> environment,
        bool windows,
        char pathSeparator)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentException.ThrowIfNullOrEmpty(configDir);
        ArgumentNullException.ThrowIfNull(environment);

        var target = new Dictionary<string, string?>(windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (KeyValuePair<string, string?> entry in environment)
        {
            target[entry.Key] = entry.Value;
        }

        EnvExpansion.ApplyLevels(target, levels, configDir, pathSeparator, inputs: null);
        return target;
    }

    /// <summary>The current process environment.</summary>
    public static IReadOnlyDictionary<string, string?> Process()
    {
        var environment = new Dictionary<string, string?>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = (string?)entry.Value;
        }

        return environment;
    }
}
