namespace Mdedit.Tui;

/// <summary>The result of a check for a change on disk.</summary>
/// <param name="Outcome">How the check ended.</param>
/// <param name="Error">The error when the check failed, otherwise null.</param>
internal sealed record ExternalResult(ExternalOutcome Outcome, string? Error);
