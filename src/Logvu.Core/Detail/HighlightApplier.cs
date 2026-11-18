using System.Text.RegularExpressions;
using Logvu.Core.Config;

namespace Logvu.Core.Detail;

/// <summary>Applies the ordered highlight rules to one line's scoped text.</summary>
internal static class HighlightApplier
{
    /// <summary>
    /// Returns the line with a highlight span for every match of a rule whose field is the line's scope. The rules are tried in order and a
    /// later match yields to an earlier one where they overlap. Highlight spans split the structural spans they overlap. A rule whose match
    /// times out contributes nothing to that line.
    /// </summary>
    /// <param name="line">The line.</param>
    /// <param name="rules">The highlight rules, in configuration order.</param>
    public static DetailLine Apply(DetailLine line, IReadOnlyList<HighlightRule> rules)
    {
        if (line.Scope is null || rules.Count == 0 || line.Indent >= line.Text.Length)
        {
            return line;
        }

        int start = line.Indent;
        string value = line.Text[start..];
        var claimed = new List<StyledSpan>();

        foreach (HighlightRule rule in rules)
        {
            if (!ScopeMatches(rule.Field, line.Scope))
            {
                continue;
            }

            var matches = new List<(int Start, int End)>();
            try
            {
                foreach (Match match in rule.Pattern.Matches(value))
                {
                    if (match.Length > 0)
                    {
                        matches.Add((match.Index + start, match.Index + match.Length + start));
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            foreach ((int matchStart, int matchEnd) in matches)
            {
                List<(int Start, int End)> cuts = claimed.Select(c => (c.Start, c.Start + c.Length)).ToList();
                foreach ((int pieceStart, int pieceEnd) in Subtract(matchStart, matchEnd, cuts))
                {
                    claimed.Add(new StyledSpan(pieceStart, pieceEnd - pieceStart, SpanStyle.Highlight, rule.Style));
                }
            }
        }

        if (claimed.Count == 0)
        {
            return line;
        }

        List<(int Start, int End)> claimedRanges = claimed.Select(c => (c.Start, c.Start + c.Length)).ToList();
        var result = new List<StyledSpan>();
        foreach (StyledSpan span in line.Spans)
        {
            foreach ((int pieceStart, int pieceEnd) in Subtract(span.Start, span.Start + span.Length, claimedRanges))
            {
                result.Add(span with { Start = pieceStart, Length = pieceEnd - pieceStart });
            }
        }

        result.AddRange(claimed);
        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return line with { Spans = result };
    }

    /// <summary>The parts of <c>[start, end)</c> that no cut covers, in order.</summary>
    private static List<(int Start, int End)> Subtract(int start, int end, IEnumerable<(int Start, int End)> cuts)
    {
        var pieces = new List<(int Start, int End)>();
        int cursor = start;
        foreach ((int cutStart, int cutEnd) in cuts.OrderBy(c => c.Start))
        {
            if (cursor >= end)
            {
                break;
            }

            if (cutEnd <= cursor || cutStart >= end)
            {
                continue;
            }

            if (cutStart > cursor)
            {
                pieces.Add((cursor, cutStart));
            }

            cursor = Math.Max(cursor, cutEnd);
        }

        if (cursor < end)
        {
            pieces.Add((cursor, end));
        }

        return pieces;
    }

    /// <summary>
    /// Whether a rule's field covers a line's scope: the same path, or an element of the array at that path (a rule on
    /// <c>err.stack</c> covers <c>err.stack[0]</c>, <c>err.stack[1]</c>, …), so that a rule on an array applies to its items.
    /// </summary>
    internal static bool ScopeMatches(string field, string scope) =>
        string.Equals(field, scope, StringComparison.Ordinal)
        || (scope.Length > field.Length + 2
            && scope.StartsWith(field, StringComparison.Ordinal)
            && scope[field.Length] == '['
            && scope.IndexOf(']', field.Length) == scope.Length - 1
            && scope.AsSpan(field.Length + 1, scope.Length - field.Length - 2).IndexOfAnyExceptInRange('0', '9') < 0);
}
