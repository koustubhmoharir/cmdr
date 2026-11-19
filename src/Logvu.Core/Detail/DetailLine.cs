namespace Logvu.Core.Detail;

/// <summary>The kind of a detail line. The view uses it for layout and for the raw-view toggle.</summary>
public enum LineKind
{
    /// <summary>A named field of a text event.</summary>
    Field,

    /// <summary>A line of a message, or of a JSON event's summary (message and error).</summary>
    Message,

    /// <summary>A line of a JSON tree, or of a JSON string value.</summary>
    Json,

    /// <summary>A line of the raw event text.</summary>
    Raw,

    /// <summary>A marker of a folded run of framework frames.</summary>
    Fold,

    /// <summary>A notice: an unavailable event, an unparsed record, or a truncated event.</summary>
    Notice,
}

/// <summary>One logical line of a detail document. The view wraps it to the width.</summary>
/// <param name="Text">The line's text, including any indentation, as displayed.</param>
/// <param name="Spans">
/// The styled spans of <paramref name="Text"/>, ordered by start, non-overlapping and inside the text.
/// </param>
/// <param name="Kind">The kind of line.</param>
/// <param name="Indent">
/// The column where the value starts. Continuation rows of a wrapped line are indented to this column.
/// </param>
/// <param name="NodeId">
/// The id of the expandable JSON node this line is, or the id of the fold this line marks; <c>null</c> otherwise.
/// </param>
/// <param name="Scope">
/// The highlight scope of the text on this line: <c>message</c>, <c>raw</c>, a field name, or a JSON dotted path. Highlight rules
/// match the text from <paramref name="Indent"/> on. <c>null</c> when the line carries no scoped text.
/// </param>
/// <param name="Value">
/// The exact value of a single-valued field line — a text-event field, or a JSON scalar (strings unquoted, other scalars as
/// their raw JSON text) — in the form <c>EventFilter</c> compares field predicates with, so "filter to this value" never has to
/// parse display text. <c>null</c> for every other line, including the lines of multi-line strings.
/// </param>
public sealed record DetailLine(
    string Text,
    IReadOnlyList<StyledSpan> Spans,
    LineKind Kind,
    int Indent,
    string? NodeId,
    string? Scope,
    string? Value = null);
