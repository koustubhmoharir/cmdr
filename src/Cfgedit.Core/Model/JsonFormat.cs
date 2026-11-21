using Cfgedit.Core.Json;
using Cfgedit.Core.Text;

namespace Cfgedit.Core.Model;

/// <summary>A JSON-family format: strict JSON, JSONC or JSON5, by its dialect.</summary>
public sealed class JsonFormat : IConfigFormat
{
    /// <summary>Creates the format for a dialect.</summary>
    public JsonFormat(JsonDialect dialect) : this(dialect, null)
    {
    }

    /// <summary>
    /// Creates the format with a hook that sees, and may change, the edits planned for each operation. The tests use it to
    /// check that <see cref="ConfigDocument.Apply"/> catches a planner that is wrong.
    /// </summary>
    internal JsonFormat(JsonDialect dialect, Func<EditOp, IReadOnlyList<TextEdit>, IReadOnlyList<TextEdit>>? planHook)
    {
        _planHook = planHook;
        Dialect = dialect;
        Name = dialect switch
        {
            JsonDialect.Json => "json",
            JsonDialect.Jsonc => "jsonc",
            JsonDialect.Json5 => "json5",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, "Not a JSON dialect."),
        };
    }

    private readonly Func<EditOp, IReadOnlyList<TextEdit>, IReadOnlyList<TextEdit>>? _planHook;

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
    /// <exception cref="CfgeditException">The operation is invalid.</exception>
    public IReadOnlyList<TextEdit> Plan(ConfigDocument document, EditOp op)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(op);
        if (document.Syntax is not JsonDocument)
        {
            throw new ArgumentException("The document was not parsed as JSON.", nameof(document));
        }

        IReadOnlyList<TextEdit> edits = new JsonEditPlanner(document, Dialect).Plan(op);
        return _planHook is null ? edits : _planHook(op, edits);
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
