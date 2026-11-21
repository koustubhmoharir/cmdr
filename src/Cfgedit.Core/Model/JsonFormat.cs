using Cfgedit.Core.Json;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Model;

/// <summary>A JSON-family format: strict JSON, JSONC or JSON5, by its dialect.</summary>
public sealed class JsonFormat : IConfigFormat
{
    /// <summary>Creates the format for a dialect.</summary>
    public JsonFormat(JsonDialect dialect)
    {
        Dialect = dialect;
        Name = dialect switch
        {
            JsonDialect.Json => "json",
            JsonDialect.Jsonc => "jsonc",
            JsonDialect.Json5 => "json5",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Not a JSON dialect."),
        };
    }

    /// <summary>The dialect the text is parsed in.</summary>
    public JsonDialect Dialect { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public ConfigDocument Parse(SourceText source)
    {
        ArgumentNullException.ThrowIfNull(source);
        JsonDocument syntax = JsonDocument.Parse(source, Dialect);
        return new ConfigDocument(this, source, JsonModelBuilder.BuildTree(syntax), syntax);
    }

    /// <inheritdoc/>
    /// <exception cref="NotImplementedException">Always, until the edit milestone.</exception>
    public IReadOnlyList<TextEdit> Plan(ConfigDocument document, EditOp op)
    {
        throw new NotImplementedException("Edit planning is not implemented yet.");
    }

    /// <inheritdoc/>
    public LogicalNode ToLogical(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Syntax is not JsonDocument syntax)
        {
            throw new ArgumentException("The document was not parsed as JSON.", nameof(document));
        }

        return JsonModelBuilder.BuildLogical(syntax);
    }
}
