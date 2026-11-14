using System.Text;

namespace Cmdr.Core.Processes;

/// <summary>
/// Builds the Unicode environment block that <c>CreateProcessW</c> takes, in the same form .NET's <c>Process</c>
/// builds internally.
/// </summary>
/// <remarks>
/// Pure and platform-independent, so it is testable on Linux. The caller must pass the returned block together with
/// the <c>CREATE_UNICODE_ENVIRONMENT</c> creation flag, because the block is UTF-16.
/// </remarks>
internal static class WindowsEnvironmentBlock
{
    /// <summary>
    /// Builds the block: the entries with a non-null value as <c>KEY=VALUE</c>, sorted by key with
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> (original spelling kept), each followed by a NUL, and the whole
    /// block ended by one more NUL. An empty environment yields <c>"\0\0"</c>.
    /// </summary>
    /// <param name="environment">Variables to pass to the child. Entries with a null value are omitted.</param>
    /// <exception cref="ArgumentException">
    /// A key is empty, contains <c>=</c> (a leading <c>=</c>, as in the hidden <c>=C:</c> variables, is allowed) or
    /// NUL; a value contains NUL; or two keys differ only in case. The message names the key.
    /// </exception>
    public static string Build(IDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var entries = new List<KeyValuePair<string, string>>(environment.Count);
        foreach (KeyValuePair<string, string?> pair in environment)
        {
            if (pair.Value is null)
            {
                continue;
            }
            ValidateKey(pair.Key);
            ValidateValue(pair.Key, pair.Value);
            entries.Add(new KeyValuePair<string, string>(pair.Key, pair.Value));
        }

        entries.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));

        for (int i = 1; i < entries.Count; i++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(entries[i - 1].Key, entries[i].Key))
            {
                throw new ArgumentException(
                    $"Environment variables '{entries[i - 1].Key}' and '{entries[i].Key}' differ only in case.",
                    nameof(environment));
            }
        }

        if (entries.Count == 0)
        {
            return "\0\0";
        }

        var sb = new StringBuilder();
        foreach (KeyValuePair<string, string> entry in entries)
        {
            sb.Append(entry.Key).Append('=').Append(entry.Value).Append('\0');
        }
        sb.Append('\0');
        return sb.ToString();
    }

    private static void ValidateKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length == 0)
        {
            throw new ArgumentException("An environment variable name cannot be empty.", nameof(key));
        }

        // A leading '=' is allowed: Windows uses names such as "=C:" for the per-drive current directory.
        if (key.IndexOf('=', 1) >= 0)
        {
            throw new ArgumentException($"Environment variable name '{key}' cannot contain '='.", nameof(key));
        }

        if (key.Contains('\0'))
        {
            throw new ArgumentException($"Environment variable name '{key}' cannot contain a NUL character.", nameof(key));
        }
    }

    private static void ValidateValue(string key, string value)
    {
        if (value.Contains('\0'))
        {
            throw new ArgumentException($"Value of environment variable '{key}' cannot contain a NUL character.", nameof(key));
        }
    }
}
