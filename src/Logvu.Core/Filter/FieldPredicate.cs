namespace Logvu.Core.Filter;

/// <summary>
/// A field-value predicate: an event matches when its field at <paramref name="Path"/> has the string form <paramref name="Value"/>.
/// For a text event the path is a field name. For a JSON event it is a dotted path into the object, with array elements as <c>name[3]</c>.
/// </summary>
/// <param name="Path">The field name or the dotted JSON path. It is not empty.</param>
/// <param name="Value">The value to compare with, exactly and ordinally. A JSON string is compared unquoted; other JSON values as their raw JSON text.</param>
public sealed record FieldPredicate(string Path, string Value);
