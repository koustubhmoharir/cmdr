namespace Cmdr.Core.Config;

/// <summary>Applies a configured environment overlay to a process environment.</summary>
public static class EnvironmentOverlay
{
    /// <summary>
    /// For each overlay entry: a non-null value sets (overwrites) the variable; a null value
    /// removes it if present. Key comparison is left to <paramref name="target"/>
    /// (e.g. ProcessStartInfo.Environment handles Windows case-insensitivity).
    /// </summary>
    public static void Apply(IDictionary<string, string?> target, IReadOnlyDictionary<string, string?> overlay)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(overlay);

        foreach (KeyValuePair<string, string?> entry in overlay)
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
    }
}
