using System.Text.Json;

namespace Logvu.Core.Parsing;

/// <summary>
/// Shared shape of the JSON schemes: each non-blank line is one JSON object. A line that does not parse, or is not an
/// object, is a raw event.
/// </summary>
public abstract class JsonScheme : ILogScheme
{
    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public bool IsJson => true;

    /// <inheritdoc/>
    public bool IsHeader(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return !string.IsNullOrWhiteSpace(line);
    }

    /// <inheritdoc/>
    public bool IsHeader(ReadOnlySpan<char> line) => !line.IsWhiteSpace();

    /// <inheritdoc/>
    public LogEvent Parse(string eventText, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(eventText);
        ArgumentNullException.ThrowIfNull(context);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(eventText);
        }
        catch (JsonException ex)
        {
            return LogEvent.Raw(eventText, ex.Message);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return LogEvent.Raw(eventText, "line is not a JSON object");
            }

            return Build(root, context);
        }
    }

    /// <summary>Builds the event from a JSON object. The element is valid only during this call.</summary>
    protected abstract LogEvent Build(JsonElement root, ParseContext context);

    /// <summary>Every top-level member, in order: strings unquoted, other values as raw JSON text.</summary>
    protected static IReadOnlyList<LogField> TopLevelFields(JsonElement root)
    {
        var fields = new List<LogField>();
        foreach (JsonProperty property in root.EnumerateObject())
        {
            fields.Add(new LogField(property.Name, JsonValues.AsText(property.Value)));
        }

        return fields;
    }
}

/// <summary>
/// Serilog <c>JsonFormatter</c> output: <c>Timestamp</c>, <c>Level</c>, <c>MessageTemplate</c>, optional
/// <c>RenderedMessage</c>, <c>Properties</c> and optional <c>Exception</c>.
/// </summary>
public sealed class SerilogJsonScheme : JsonScheme
{
    /// <inheritdoc/>
    public override string Name => "serilog-json";

    /// <inheritdoc/>
    /// <remarks>
    /// The message is <c>RenderedMessage</c> when present, otherwise <c>MessageTemplate</c> rendered from
    /// <c>Properties</c>. A missing level is <see cref="LogLevel.Info"/>, spelled "Information". Fields are the top-level
    /// members; <c>Properties</c> is one field, so its members, such as <c>SourceContext</c>, are reached through
    /// <see cref="LogEvent.Json"/>.
    /// </remarks>
    protected override LogEvent Build(JsonElement root, ParseContext context)
    {
        string? timeText = null;
        DateTimeOffset? time = null;
        if (JsonValues.TryGetPresent(root, "Timestamp", out JsonElement timestamp))
        {
            timeText = JsonValues.AsText(timestamp);
            if (timestamp.ValueKind == JsonValueKind.String)
            {
                time = TimeParsing.ParseOffsetAware(timeText, context);
            }
        }

        string levelText = "Information";
        if (JsonValues.TryGetPresent(root, "Level", out JsonElement level))
        {
            levelText = JsonValues.AsText(level);
        }

        JsonElement? properties = null;
        if (JsonValues.TryGetPresent(root, "Properties", out JsonElement props) && props.ValueKind == JsonValueKind.Object)
        {
            properties = props;
        }

        string message;
        bool messageRendered = false;
        if (JsonValues.TryGetPresent(root, "RenderedMessage", out JsonElement rendered))
        {
            message = JsonValues.AsText(rendered);
        }
        else if (JsonValues.TryGetPresent(root, "MessageTemplate", out JsonElement template))
        {
            if (template.ValueKind == JsonValueKind.String)
            {
                string templateText = template.GetString()!;
                message = SerilogTemplate.Render(templateText, properties);
                messageRendered = !string.Equals(message, templateText, StringComparison.Ordinal);
            }
            else
            {
                message = JsonValues.AsText(template);
            }
        }
        else
        {
            message = string.Empty;
        }

        return new LogEvent
        {
            Time = time,
            TimeText = timeText,
            Level = LogLevels.Normalize(levelText),
            LevelText = levelText,
            Message = message,
            MessageRendered = messageRendered,
            Fields = TopLevelFields(root),
            Json = root.Clone(),
        };
    }
}

/// <summary>
/// Go <c>log/slog</c> <c>JSONHandler</c> output: <c>time</c>, <c>level</c>, <c>msg</c>, an optional <c>logger</c>
/// attribute and any other attributes.
/// </summary>
public sealed class SlogJsonScheme : JsonScheme
{
    /// <inheritdoc/>
    public override string Name => "slog-json";

    /// <inheritdoc/>
    /// <remarks>Fields are the top-level members, so error values such as <c>cause</c> and <c>stack</c> stay raw.</remarks>
    protected override LogEvent Build(JsonElement root, ParseContext context)
    {
        string? timeText = null;
        DateTimeOffset? time = null;
        if (JsonValues.TryGetPresent(root, "time", out JsonElement timestamp))
        {
            timeText = JsonValues.AsText(timestamp);
            if (timestamp.ValueKind == JsonValueKind.String)
            {
                time = TimeParsing.ParseOffsetAware(timeText, context);
            }
        }

        string? levelText = null;
        if (JsonValues.TryGetPresent(root, "level", out JsonElement level))
        {
            levelText = JsonValues.AsText(level);
        }

        string message = JsonValues.TryGetPresent(root, "msg", out JsonElement msg) ? JsonValues.AsText(msg) : string.Empty;

        return new LogEvent
        {
            Time = time,
            TimeText = timeText,
            Level = LogLevels.Normalize(levelText),
            LevelText = levelText,
            Message = message,
            Fields = TopLevelFields(root),
            Json = root.Clone(),
        };
    }
}
