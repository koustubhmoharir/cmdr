namespace Logvu.Core.Parsing;

/// <summary>A named value of an event: a named capture of a text header, or a top-level member of a JSON object.</summary>
/// <param name="Name">The field name as written by the scheme or producer.</param>
/// <param name="Value">The value. JSON strings are unquoted; other JSON values are their raw JSON text.</param>
public sealed record LogField(string Name, string Value);
