using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>
/// Reads an item by its shape: an object with <c>items</c> is a group, one with <c>program</c> is a command.
/// Writes by runtime type through the source-generated context (no reflection).
/// </summary>
internal sealed class ItemSpecJsonConverter : JsonConverter<ItemSpec>
{
    private static readonly AsyncLocal<long> SourceOffset = new();

    /// <inheritdoc />
    /// <exception cref="JsonException">The object is neither or both of a group and a command, or is not an object.</exception>
    public override ItemSpec? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("An item must be a JSON object.");
        }

        // Inspect a copy to select the concrete type, then deserialize from the original reader.
        // Nested serializers report item-local coordinates; the origin is tracked and translated by ConfigStore.
        Utf8JsonReader scan = reader;
        bool isGroup = false;
        bool isCommand = false;
        bool hasLegacyCommands = false;
        while (scan.Read() && scan.TokenType != JsonTokenType.EndObject)
        {
            if (scan.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string? property = scan.GetString();
            if (!scan.Read())
            {
                break;
            }

            isGroup |= property == "items";
            isCommand |= property == "program";
            hasLegacyCommands |= property == "commands";
            scan.Skip();
        }

        if (isGroup && isCommand)
        {
            throw new JsonException("An item cannot have both 'items' (a group) and 'program' (a command).");
        }

        if (isGroup)
        {
            return DeserializeGroup(ref reader);
        }

        if (isCommand)
        {
            return DeserializeCommand(ref reader);
        }

        if (hasLegacyCommands)
        {
            throw new JsonException("Use 'items' instead of 'commands' in a group.");
        }

        throw new JsonException("An item must have 'program' (a command) or 'items' (a group).");
    }

    private static GroupSpec? DeserializeGroup(ref Utf8JsonReader reader)
    {
        long previousOffset = SourceOffset.Value;
        long itemOffset = previousOffset + reader.TokenStartIndex;
        SourceOffset.Value = itemOffset;
        try
        {
            return JsonSerializer.Deserialize(ref reader, ConfigJsonContext.Default.GroupSpec);
        }
        catch (ItemSourceJsonException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new ItemSourceJsonException(ex, itemOffset);
        }
        finally
        {
            SourceOffset.Value = previousOffset;
        }
    }

    private static CommandSpec? DeserializeCommand(ref Utf8JsonReader reader)
    {
        long previousOffset = SourceOffset.Value;
        long itemOffset = previousOffset + reader.TokenStartIndex;
        SourceOffset.Value = itemOffset;
        try
        {
            return JsonSerializer.Deserialize(ref reader, ConfigJsonContext.Default.CommandSpec);
        }
        catch (ItemSourceJsonException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new ItemSourceJsonException(ex, itemOffset);
        }
        finally
        {
            SourceOffset.Value = previousOffset;
        }
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

internal sealed class ItemSourceJsonException : JsonException
{
    public ItemSourceJsonException(JsonException innerException, long sourceByteOffset)
        : base(innerException.Message, innerException.Path, innerException.LineNumber, innerException.BytePositionInLine, innerException)
    {
        SourceByteOffset = sourceByteOffset;
    }

    public long SourceByteOffset { get; }
}
