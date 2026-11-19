namespace Logvu.Core.Filter;

/// <summary>
/// How a field predicate compares a field's value with its value. Every operator compares ignoring case. The keyword of each operator, as the
/// filter editor writes it, is its lowercase name; <see cref="FieldOperators"/> maps between the two.
/// </summary>
public enum FieldOperator
{
    /// <summary>The field's value is the predicate's value.</summary>
    Equals,

    /// <summary>The field's value begins with the predicate's value.</summary>
    Prefix,

    /// <summary>The field's value ends with the predicate's value.</summary>
    Suffix,

    /// <summary>The field's value contains the predicate's value.</summary>
    Contains,
}
