namespace Mdedit.Tui;

/// <summary>The result of a save flow.</summary>
/// <param name="Outcome">How the flow ended.</param>
/// <param name="Error">The error when the save or the reload failed, otherwise null.</param>
internal sealed record SaveResult(SaveOutcome Outcome, string? Error);
