using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>
/// Reads an item by its shape: an object with <c>items</c> is a group, one with <c>program</c> is a command.
/// Writes by runtime type through the source-generated context (no reflection).
/// </summary>
internal sealed class ItemSpecJsonConverter : JsonConverter<ItemSpec>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">The object is neither or both of a group and a command, or is not an object.</exception>
    public override ItemSpec? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement element = document.RootElement;

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("An item must be a JSON object.");
        }

        bool isGroup = element.TryGetProperty("items", out _);
        bool isCommand = element.TryGetProperty("program", out _);

        if (isGroup && isCommand)
        {
            throw new JsonException("An item cannot have both 'items' (a group) and 'program' (a command).");
        }

        if (isGroup)
        {
            return element.Deserialize(ConfigJsonContext.Default.GroupSpec);
        }

        if (isCommand)
        {
            return element.Deserialize(ConfigJsonContext.Default.CommandSpec);
        }

        if (element.TryGetProperty("commands", out _))
        {
            throw new JsonException("Use 'items' instead of 'commands' in a group.");
        }

        throw new JsonException("An item must have 'program' (a command) or 'items' (a group).");
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">The runtime type is neither a group nor a command.</exception>
    public override void Write(Utf8JsonWriter writer, ItemSpec value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case GroupSpec group:
                JsonSerializer.Serialize(writer, group, ConfigJsonContext.Default.GroupSpec);
                break;
            case CommandSpec command:
                JsonSerializer.Serialize(writer, command, ConfigJsonContext.Default.CommandSpec);
                break;
            default:
                throw new NotSupportedException($"Unsupported item type '{value.GetType().Name}'.");
        }
    }
}
