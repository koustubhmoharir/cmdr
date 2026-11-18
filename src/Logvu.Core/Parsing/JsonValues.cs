using System.Text.Json;

namespace Logvu.Core.Parsing;

/// <summary>JSON value helpers shared by the JSON schemes.</summary>
internal static class JsonValues
{
    /// <summary>A JSON string as its string value; any other value as its raw JSON text.</summary>
    public static string AsText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    /// <summary>Finds a member that is present and not JSON <c>null</c>.</summary>
    public static bool TryGetPresent(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
        {
            return true;
        }

        value = default;
        return false;
    }
}
