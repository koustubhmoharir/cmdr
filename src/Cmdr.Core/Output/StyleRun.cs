namespace Cmdr.Core.Output;

/// <summary>
/// Where a style starts in a line's <see cref="OutputLine.PlainText"/>. The run extends to the next run's start, or to the end
/// of the text for the last run. Inside an <see cref="OutputLine"/> no run is empty and adjacent runs have different styles.
/// </summary>
/// <param name="Start">Index of the run's first UTF-16 unit in the plain text.</param>
/// <param name="Style">The style of the run.</param>
public readonly record struct StyleRun(int Start, SpanStyle Style);
