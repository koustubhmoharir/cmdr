namespace Shared.Processes;

/// <summary>
/// One environment overlay of the launch chain: the <c>env</c> of a group (or of a command) and the effective working
/// directory of the item that defines it, which is what <c>${cwd}</c> means in its values.
/// </summary>
/// <param name="Env">Values; a null value removes the variable. When <see cref="ImportFrom"/> is set, the keys are the imported names and the values are null.</param>
/// <param name="Cwd">The effective working directory of the item that defines <paramref name="Env"/>.</param>
public sealed record EnvLevel(IReadOnlyDictionary<string, string?> Env, string Cwd)
{
    /// <summary>
    /// Makes this an import level: the keys of <see cref="Env"/> are names to take from the environment that the levels here produce
    /// (the top-level environment of a base file). Those levels are applied to a copy of the environment as it is when this level is
    /// reached; then each name present in the copy is set to its value, and each name absent from it is removed. The values of
    /// <see cref="Env"/> are not expanded. <see cref="Cwd"/> is the base file's directory, which is also the <c>${configDir}</c> of
    /// the imported levels. Null for an ordinary level.
    /// </summary>
    public IReadOnlyList<EnvLevel>? ImportFrom { get; init; }

    /// <summary>
    /// Makes this a literal level: its values are set as they are, with no placeholder expanded (a null removes the variable).
    /// Used for the values an environment script produced, which are never expanded again. Ignored for an import level.
    /// </summary>
    public bool Literal { get; init; }
}

/// <summary>Applies the environment overlay levels of the launch chain. Values are expanded by <see cref="Placeholders.Expand"/>. Pure; no process state.</summary>
public static class EnvExpansion
{
    /// <summary>
    /// Applies the levels to <paramref name="target"/>, outermost first. Within a level every value is expanded against the
    /// environment as it was before that level, so <c>${env:PATH}</c> refers to the value from the outer level. A null value removes
    /// the variable. A literal level (<see cref="EnvLevel.Literal"/>) sets its values without expanding them. Key comparison is left
    /// to <paramref name="target"/> (Windows environments are case-insensitive).
    /// </summary>
    /// <param name="inputs">
    /// The values of the input placeholders in the values, or null to keep them as written (for display). A launch passes its inputs.
    /// </param>
    /// <exception cref="InvalidOperationException">An input placeholder in a value has no usable value (see <see cref="Placeholders.Expand"/>).</exception>
    public static void ApplyLevels(
        IDictionary<string, string?> target,
        IReadOnlyList<EnvLevel> levels,
        string configDir,
        char pathSeparator,
        IReadOnlyDictionary<string, string>? inputs)
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

            if (level.ImportFrom is not null)
            {
                // The imported levels see the environment as it is here, and their values are not expanded by this level. They are a
                // base file's top-level levels, so their ${configDir} is that file's directory, which is this level's Cwd.
                ApplyLevels(snapshot, level.ImportFrom, level.Cwd, pathSeparator, inputs);
                foreach (string name in level.Env.Keys)
                {
                    if (snapshot.TryGetValue(name, out string? value) && value is not null)
                    {
                        target[name] = value;
                    }
                    else
                    {
                        target.Remove(name);
                    }
                }

                continue;
            }

            if (level.Literal)
            {
                foreach (KeyValuePair<string, string?> entry in level.Env)
                {
                    if (entry.Value is null)
                    {
                        target.Remove(entry.Key);
                    }
                    else
                    {
                        target[entry.Key] = entry.Value;
                    }
                }

                continue;
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
                    target[entry.Key] = Placeholders.Expand(entry.Value, lookup, inputs, level.Cwd, configDir, pathSeparator);
                }
            }
        }
    }
}
