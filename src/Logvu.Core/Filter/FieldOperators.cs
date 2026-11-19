namespace Logvu.Core.Filter;

/// <summary>
/// The keywords of the field operators, as the filter editor writes and reads them. The keyword of an operator is its lowercase name, and
/// a keyword is read ignoring case. This is the one place that maps the two, so a new operator is added here.
/// </summary>
public static class FieldOperators
{
    /// <summary>The keywords in the order the operators are declared, for the messages that list them.</summary>
    public static IReadOnlyList<string> Keywords { get; } =
        Enum.GetValues<FieldOperator>().Select(Keyword).ToArray();

    /// <summary>The keyword of an operator, as the editor writes it.</summary>
    /// <param name="op">The operator.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="op"/> is not an operator.</exception>
    public static string Keyword(FieldOperator op) => op switch
    {
        FieldOperator.Equals => "equals",
        FieldOperator.Prefix => "prefix",
        FieldOperator.Suffix => "suffix",
        FieldOperator.Contains => "contains",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Not a field operator."),
    };

    /// <summary>Reads an operator from its keyword, ignoring case.</summary>
    /// <param name="keyword">The keyword, such as <c>prefix</c> or <c>Contains</c>.</param>
    /// <param name="op">Receives the operator when the keyword is one.</param>
    /// <returns><c>true</c> when the keyword is one of the operators' keywords; otherwise <c>false</c>.</returns>
    public static bool TryParse(string keyword, out FieldOperator op)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        foreach (FieldOperator candidate in Enum.GetValues<FieldOperator>())
        {
            if (string.Equals(Keyword(candidate), keyword, StringComparison.OrdinalIgnoreCase))
            {
                op = candidate;
                return true;
            }
        }

        op = FieldOperator.Equals;
        return false;
    }
}
