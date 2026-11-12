namespace Cmdr.Core.Config;

/// <summary>
/// One environment overlay of the launch chain: the <c>env</c> of a group (or of a command) and the effective working
/// directory of the item that defines it, which is what <c>${cwd}</c> means in its values.
/// </summary>
/// <param name="Env">Values; a null value removes the variable.</param>
/// <param name="Cwd">The effective working directory of the item that defines <paramref name="Env"/>.</param>
public sealed record EnvLevel(IReadOnlyDictionary<string, string?> Env, string Cwd);

/// <summary>Expands <c>${...}</c> references in environment values and applies overlay levels. Pure; no process state.</summary>
public static class EnvExpansion
{
    /// <summary>
    /// Expands one value. <c>${NAME}</c> becomes <paramref name="lookup"/>(NAME), or empty when undefined. The built-ins
    /// <c>${cwd}</c>, <c>${configDir}</c> and <c>${pathsep}</c> take precedence over variables of the same name.
    /// <c>$$</c> becomes <c>$</c>. A <c>$</c> not followed by <c>{</c> or <c>$</c> is literal, and so is an unterminated <c>${</c>.
    /// </summary>
    public static string Expand(string value, Func<string, string?> lookup, string cwd, string configDir, char pathSeparator)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(lookup);

        if (value.IndexOf('$') < 0)
        {
            return value;
        }

        var result = new System.Text.StringBuilder(value.Length);
        int i = 0;
        while (i < value.Length)
        {
            char c = value[i];
            if (c != '$' || i + 1 >= value.Length)
            {
                result.Append(c);
                i++;
                continue;
            }

            char next = value[i + 1];
            if (next == '$')
            {
                result.Append('$');
                i += 2;
                continue;
            }

            if (next != '{')
            {
                result.Append('$');
                i++;
                continue;
            }

            int close = value.IndexOf('}', i + 2);
            if (close < 0)
            {
                // Unterminated: keep the rest of the value as written.
                result.Append(value, i, value.Length - i);
                break;
            }

            string name = value.Substring(i + 2, close - (i + 2));
            result.Append(name switch
            {
                "cwd" => cwd,
                "configDir" => configDir,
                "pathsep" => pathSeparator.ToString(),
                _ => lookup(name) ?? "",
            });
            i = close + 1;
        }

        return result.ToString();
    }

    /// <summary>
    /// Applies the levels to <paramref name="target"/>, outermost first. Within a level every value is expanded against the
    /// environment as it was before that level, so <c>${PATH}</c> refers to the value from the outer level. A null value removes
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
                    target[entry.Key] = Expand(entry.Value, lookup, level.Cwd, configDir, pathSeparator);
                }
            }
        }
    }
}
