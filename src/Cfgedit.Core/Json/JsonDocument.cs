using Shared.Core;

namespace Cfgedit.Core.Json;

/// <summary>
/// A parsed JSON-family document: the concrete syntax tree of a source text. The tree keeps every comment as an entry, and
/// every comma and colon by offset, so that the text between nodes is whatever the source holds there.
/// </summary>
public sealed class JsonDocument
{
    /// <summary>The deepest nesting of objects and arrays that is accepted.</summary>
    public const int MaxDepth = 256;

    internal JsonDocument(
        SourceText source, JsonDialect dialect, IReadOnlyList<JsonComment> leading, JsonValue root, IReadOnlyList<JsonComment> trailing)
    {
        Source = source;
        Dialect = dialect;
        Leading = leading;
        Root = root;
        Trailing = trailing;
    }

    /// <summary>The source text the tree was parsed from.</summary>
    public SourceText Source { get; }

    /// <summary>The dialect the text was parsed in.</summary>
    public JsonDialect Dialect { get; }

    /// <summary>The comments before the root value.</summary>
    public IReadOnlyList<JsonComment> Leading { get; }

    /// <summary>The root value.</summary>
    public JsonValue Root { get; }

    /// <summary>The comments after the root value.</summary>
    public IReadOnlyList<JsonComment> Trailing { get; }

    /// <summary>
    /// Parses a source text in a dialect. Duplicate keys are accepted.
    /// </summary>
    /// <exception cref="CfgeditException">The text is not a valid document in the dialect. Line and column say where.</exception>
    public static JsonDocument Parse(SourceText source, JsonDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(source);
        return JsonParser.Parse(source, dialect);
    }
}
