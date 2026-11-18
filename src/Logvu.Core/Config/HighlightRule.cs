using System.Text.RegularExpressions;

namespace Logvu.Core.Config;

/// <summary>A highlight rule: events whose field matches the pattern are shown in the style.</summary>
/// <param name="Field">
/// The field to match: <c>message</c>, <c>raw</c>, or a field name (a JSON dotted path for JSON events).
/// </param>
/// <param name="Pattern">The regular expression, with a 250 ms match timeout.</param>
/// <param name="Style">A built-in style name or a key of <see cref="LogvuConfig.Styles"/>.</param>
public sealed record HighlightRule(string Field, Regex Pattern, string Style);
