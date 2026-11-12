using System.Text.RegularExpressions;
using Cmdr.Core.Config;
using Cmdr.Core.Output;

namespace Cmdr.Core.Processes;

/// <summary>
/// Matches the completed output lines of one run against the readiness patterns of its command.
/// Immutable apart from the compiled expressions, so it may be used from any thread.
/// </summary>
internal sealed class ReadinessMatcher
{
    /// <summary>A pattern that takes longer than this on one line counts as no match.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    private const RegexOptions Options = RegexOptions.CultureInvariant;

    private readonly Regex _ready;
    private readonly Regex? _busy;

    /// <summary>Compiles the patterns.</summary>
    /// <exception cref="ArgumentException">A pattern is not a valid regular expression.</exception>
    public ReadinessMatcher(ReadySpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _ready = new Regex(spec.Pattern, Options, MatchTimeout);
        _busy = string.IsNullOrEmpty(spec.Busy) ? null : new Regex(spec.Busy, Options, MatchTimeout);
    }

    /// <summary>
    /// Evaluates the lines in order. A line matching the ready pattern means <see cref="ProcessReadiness.Ready"/>; a line
    /// matching only the busy pattern means <see cref="ProcessReadiness.Starting"/>. Returns the verdict of the last decisive
    /// line, or null when no line is decisive (the phase then stays as it is).
    /// </summary>
    public ProcessReadiness? Evaluate(IReadOnlyList<OutputLine> lines)
    {
        ProcessReadiness? verdict = null;
        foreach (OutputLine line in lines)
        {
            if (IsMatch(_ready, line.PlainText))
            {
                verdict = ProcessReadiness.Ready;
            }
            else if (_busy is not null && IsMatch(_busy, line.PlainText))
            {
                verdict = ProcessReadiness.Starting;
            }
        }

        return verdict;
    }

    private static bool IsMatch(Regex regex, string text)
    {
        try
        {
            return regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
