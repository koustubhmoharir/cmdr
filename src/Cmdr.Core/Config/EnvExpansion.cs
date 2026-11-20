namespace Cmdr.Core.Config;

/// <summary>
/// One environment overlay of the launch chain: the <c>env</c> of a group (or of a command) and the effective working
/// directory of the item that defines it, which is what <c>${cwd}</c> means in its values.
/// </summary>
/// <param name="Env">Values; a null value removes the variable.</param>
/// <param name="Cwd">The effective working directory of the item that defines <paramref name="Env"/>.</param>
public sealed record EnvLevel(IReadOnlyDictionary<string, string?> Env, string Cwd);

/// <summary>Applies the environment overlay levels of the launch chain. Values are expanded by <see cref="Placeholders.Expand"/>. Pure; no process state.</summary>
public static class EnvExpansion
{
    /// <summary>
    /// Applies the levels to <paramref name="target"/>, outermost first. Within a level every value is expanded against the
    /// environment as it was before that level, so <c>${env:PATH}</c> refers to the value from the outer level. A null value removes
    /// the variable. Key comparison is left to <paramref name="target"/> (Windows environments are case-insensitive).
    /// </summary>
    public static void ApplyLevels(IDictionary<string, string?> target, IReadOnlyList<EnvLevel> levels, string configDir, char pathSeparator)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        foreach (EnvLevel level in levels)
        {
            var snapshot = new Dictionary<string, string?>(comparer);
            foreach (KeyValuePair<string, string?> entry in target)
            {
                snapshot[entry.Key] = entry.Value;
            }

            Func<string, string?> lookup = name => snapshot.TryGetValue(name, out string? value) ? value : null;

            foreach (KeyValuePair<string, string?> entry in level.Env)
            {
                if (entry.Value is null)
                {
                    target.Remove(entry.Key);
                }
                else
                {
                    target[entry.Key] = Placeholders.Expand(entry.Value, lookup, inputs: null, level.Cwd, configDir, pathSeparator);
                }
            }
        }
    }
}
