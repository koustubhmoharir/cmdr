namespace Cmdr.Tui;

/// <summary>The result of parsing the environment field: the overlay, or the errors that prevent it. Errors are empty when valid.</summary>
internal sealed record EnvParseResult(IReadOnlyDictionary<string, string?> Values, IReadOnlyList<string> Errors);

/// <summary>
/// The environment field of the command dialog: one <c>KEY=VALUE</c> per line, or <c>!KEY</c> to remove an inherited
/// variable (a null value in the config). Blank lines are ignored. Pure.
/// </summary>
internal static class EnvText
{
    /// <summary>Writes <c>KEY=VALUE</c> lines in dictionary order, and <c>!KEY</c> for null values.</summary>
    /// <remarks>
    /// A value containing a line break cannot be written in this form, and the text may then parse differently; use
    /// <see cref="CanEdit"/> to check whether a dictionary survives the round trip.
    /// </remarks>
    public static string Format(IReadOnlyDictionary<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var lines = new List<string>(env.Count);
        foreach (KeyValuePair<string, string?> entry in env)
        {
            lines.Add(entry.Value is null ? "!" + entry.Key : entry.Key + "=" + entry.Value);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// True when <see cref="Parse"/> of <see cref="Format"/> has no errors and gives back exactly <paramref name="env"/>: the
    /// same keys and the same values, including removals (null). False for values the line form cannot hold, such as a value
    /// with a line break, a final <c>\r</c>, or a line break that starts a new variable.
    /// </summary>
    public static bool CanEdit(IReadOnlyDictionary<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(env);

        EnvParseResult result = Parse(Format(env));
        if (result.Errors.Count > 0 || result.Values.Count != env.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, string?> entry in env)
        {
            if (!result.Values.TryGetValue(entry.Key, out string? value) || value != entry.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Parses the lines. Errors name the line number; duplicate keys are errors.</summary>
    public static EnvParseResult Parse(string text)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var errors = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return new EnvParseResult(values, errors);
        }

        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].EndsWith('\r') ? lines[i][..^1] : lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int lineNumber = i + 1;
            string key;
            string? value;
            if (line.StartsWith('!'))
            {
                key = line[1..].Trim();
                value = null;
            }
            else
            {
                int equals = line.IndexOf('=');
                if (equals < 0)
                {
                    errors.Add($"Line {lineNumber}: expected KEY=VALUE or !KEY.");
                    continue;
                }

                key = line[..equals].Trim();
                value = line[(equals + 1)..];
            }

            if (key.Length == 0)
            {
                errors.Add($"Line {lineNumber}: the variable name is empty.");
            }
            else if (values.ContainsKey(key))
            {
                errors.Add($"Line {lineNumber}: '{key}' is set more than once.");
            }
            else
            {
                values[key] = value;
            }
        }

        return new EnvParseResult(values, errors);
    }
}
