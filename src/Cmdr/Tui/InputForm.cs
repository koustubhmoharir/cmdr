using Cmdr.Core.Config;

namespace Cmdr.Tui;

/// <summary>Validation and conversion of the input dialog's values. Pure.</summary>
internal static class InputForm
{
    /// <summary>
    /// The errors that prevent the values from being used: one per required input that is empty. Whitespace counts as a
    /// value. <paramref name="values"/> are in the same order as <paramref name="requests"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The lists have different lengths.</exception>
    public static IReadOnlyList<string> Validate(IReadOnlyList<InputRequest> requests, IReadOnlyList<string> values)
    {
        CheckLengths(requests, values);

        var errors = new List<string>();
        for (int i = 0; i < requests.Count; i++)
        {
            if (!requests[i].AllowEmpty && values[i].Length == 0)
            {
                errors.Add($"Input '{requests[i].Name}' must not be empty.");
            }
        }

        return errors;
    }

    /// <summary>The values by input name, for <see cref="Cmdr.Core.Session.Session.Activate"/> and the restart. Pure.</summary>
    /// <exception cref="ArgumentException">The lists have different lengths.</exception>
    public static IReadOnlyDictionary<string, string> Values(IReadOnlyList<InputRequest> requests, IReadOnlyList<string> values)
    {
        CheckLengths(requests, values);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < requests.Count; i++)
        {
            result[requests[i].Name] = values[i];
        }

        return result;
    }

    private static void CheckLengths(IReadOnlyList<InputRequest> requests, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(values);
        if (requests.Count != values.Count)
        {
            throw new ArgumentException("One value is needed for each input.", nameof(values));
        }
    }
}
