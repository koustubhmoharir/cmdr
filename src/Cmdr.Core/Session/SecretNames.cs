using Shared.Processes;

namespace Cmdr.Core.Session;

/// <summary>
/// The secret environment variable names of a chain of environment levels: the names declared <c>secret</c>, extended by the names whose
/// value references a secret through <c>${env:NAME}</c>. Pure.
/// </summary>
public static class SecretNames
{
    /// <summary>The comparer of environment names on this platform: case-insensitive on Windows, ordinal elsewhere.</summary>
    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// The names that are secret after the levels are applied, outermost first. <paramref name="secret"/> are the declared names. A key of
    /// a level becomes secret when one of its non-null values references a name that is secret at that point (see
    /// <see cref="Placeholders.EnvNames"/>); it stays secret after later levels that set it again. A reference is judged against the names
    /// secret before the level, as the value is expanded against the environment before the level (see <see cref="EnvExpansion.ApplyLevels"/>).
    /// A literal level (an environment script's output, never expanded) adds nothing.
    /// </summary>
    /// <param name="levels">The levels of the chain, outermost first.</param>
    /// <param name="secret">The declared secret names that start the walk.</param>
    /// <param name="importedSecret">
    /// The secret names of the base file that the import levels (<see cref="EnvLevel.ImportFrom"/>) import from: a key of an import level
    /// is secret when it is in this set. Pass the base's full set (see <see cref="Session.SecretNamesFor(ConfigSource)"/>), which already
    /// includes the names that the base derives. Empty when the chain has no import level.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlySet<string> Extend(
        IReadOnlyList<EnvLevel> levels,
        IReadOnlySet<string> secret,
        IReadOnlySet<string> importedSecret)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(importedSecret);

        var names = new HashSet<string>(secret, Comparer);
        foreach (EnvLevel level in levels)
        {
            if (level.ImportFrom is not null)
            {
                foreach (string name in level.Env.Keys)
                {
                    if (importedSecret.Contains(name))
                    {
                        names.Add(name);
                    }
                }

                continue;
            }

            if (level.Literal)
            {
                continue;
            }

            var derived = new List<string>();
            foreach (KeyValuePair<string, string?> entry in level.Env)
            {
                if (entry.Value is not null && Placeholders.EnvNames(entry.Value).Any(name => names.Contains(name)))
                {
                    derived.Add(entry.Key);
                }
            }

            names.UnionWith(derived);
        }

        return names;
    }
}
