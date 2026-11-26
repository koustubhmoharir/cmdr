namespace Mdedit.Tui;

/// <summary>The result of a quit or switch flow.</summary>
/// <param name="Outcome">How the flow ended.</param>
/// <param name="Error">The error when the save failed, otherwise null.</param>
internal sealed record CloseResult(CloseOutcome Outcome, string? Error);
