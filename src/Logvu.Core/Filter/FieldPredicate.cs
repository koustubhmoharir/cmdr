using Logvu.Core.Parsing;

namespace Logvu.Core.Filter;

/// <summary>
/// A field-value predicate: an event matches when its field at <paramref name="Path"/> has a value that <paramref name="Operator"/>
/// relates to <paramref name="Value"/>. For a text event the path is a field name. For a JSON event it is a dotted path into the object, with
/// array elements as <c>name[3]</c>.
/// </summary>
/// <param name="Path">The field name or the dotted JSON path. It is not empty.</param>
/// <param name="Operator">How the field's value is compared with <paramref name="Value"/>.</param>
/// <param name="Value">
/// The text to compare with, ignoring case. A JSON string is compared unquoted; other JSON values as their raw JSON text. An empty value
/// is a text: <see cref="FieldOperator.Equals"/> then matches an empty value, and the other operators match every value.
/// </param>
public sealed record FieldPredicate(string Path, FieldOperator Operator, string Value)
{
    /// <summary>Tells whether a value of the field is related to <see cref="Value"/> by <see cref="Operator"/>, ignoring case.</summary>
    /// <param name="value">The field's value, as <see cref="EventFields"/> gives it.</param>
    /// <returns><c>true</c> when the value matches; otherwise <c>false</c>.</returns>
    public bool Matches(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Operator switch
        {
            FieldOperator.Equals => string.Equals(value, Value, StringComparison.OrdinalIgnoreCase),
            FieldOperator.Prefix => value.StartsWith(Value, StringComparison.OrdinalIgnoreCase),
            FieldOperator.Suffix => value.EndsWith(Value, StringComparison.OrdinalIgnoreCase),
            FieldOperator.Contains => value.Contains(Value, StringComparison.OrdinalIgnoreCase),
            _ => throw new InvalidOperationException($"Unknown field operator {Operator}."),
        };
    }
}
