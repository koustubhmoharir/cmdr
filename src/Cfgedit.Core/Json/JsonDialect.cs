namespace Cfgedit.Core.Json;

/// <summary>
/// The JSON-family dialects. <see cref="Json"/> is RFC 8259. <see cref="Jsonc"/> adds comments and one trailing comma in an
/// object or array. <see cref="Json5"/> follows the JSON5 1.0.0 specification.
/// </summary>
public enum JsonDialect
{
    /// <summary>Strict JSON (RFC 8259).</summary>
    Json,

    /// <summary>JSON with comments and trailing commas.</summary>
    Jsonc,

    /// <summary>JSON5 (https://spec.json5.org).</summary>
    Json5,
}
