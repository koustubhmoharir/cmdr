using System.Globalization;
using System.Text.Json;
using Logvu.Core.Parsing;

namespace Logvu.Core.Filter;

/// <summary>Resolves the dotted paths of field predicates in a JSON event.</summary>
internal static class JsonFieldPath
{
    /// <summary>
    /// Finds the value at a dotted path and gives its string form, the same text the detail shows for a scalar: a JSON string unquoted,
    /// and any other value as its raw JSON text. A path has segments separated by dots. A segment is a member name followed by zero or
    /// more array indices, as in <c>stack[0]</c> or <c>matrix[1][2]</c>. A segment may also be only indices, as in <c>[0]</c>. Member
    /// names are matched exactly, and a name cannot contain a dot.
    /// </summary>
    /// <param name="root">The event's JSON value, normally an object.</param>
    /// <param name="path">The dotted path. It must not be empty.</param>
    /// <param name="text">Receives the string form when the path resolves.</param>
    /// <returns><c>true</c> when the path resolves to a value; <c>false</c> when a member is missing, an index is out of range, or the path is malformed.</returns>
    public static bool TryGetText(JsonElement root, string path, out string text)
    {
        text = string.Empty;
        if (path.Length == 0)
        {
            return false;
        }

        JsonElement current = root;
        foreach (string segment in path.Split('.'))
        {
            if (!TryStep(ref current, segment))
            {
                return false;
            }
        }

        text = JsonValues.AsText(current);
        return true;
    }

    /// <summary>Moves to the member and then the indices of one path segment.</summary>
    private static bool TryStep(ref JsonElement current, string segment)
    {
        int bracket = segment.IndexOf('[');
        string name = bracket < 0 ? segment : segment[..bracket];
        if (name.Length > 0)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out JsonElement member))
            {
                return false;
            }

            current = member;
        }
        else if (bracket < 0)
        {
            return false;
        }

        int position = bracket < 0 ? segment.Length : bracket;
        while (position < segment.Length)
        {
            if (segment[position] != '[')
            {
                return false;
            }

            int close = segment.IndexOf(']', position);
            if (close < 0 || !int.TryParse(segment.AsSpan(position + 1, close - position - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                return false;
            }

            if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
            {
                return false;
            }

            current = current[index];
            position = close + 1;
        }

        return true;
    }
}
