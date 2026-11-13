using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cmdr.Core.Config;

/// <summary>Reads exactly one string enum token, rejecting comma-separated enum combinations.</summary>
internal abstract class StrictEnumJsonConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private readonly JsonConverter<T> _inner = (JsonConverter<T>)new JsonStringEnumConverter<T>(allowIntegerValues: false)
        .CreateConverter(typeof(T), new JsonSerializerOptions());

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.GetString()!.Contains(','))
        {
            throw new JsonException($"Expected a single {typeof(T).Name} string value.");
        }

        return _inner.Read(ref reader, typeToConvert, options);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        _inner.Write(writer, value, options);
}
