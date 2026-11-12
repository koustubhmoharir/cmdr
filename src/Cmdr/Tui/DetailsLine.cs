namespace Cmdr.Tui;

/// <summary>The roles of the pieces of text in the details strip. The colors are chosen by <see cref="DetailsView"/>.</summary>
internal enum DetailsStyle
{
    Normal,
    Prompt,
    Command,
    Label,
    Path,
    EnvName,
    EnvValue,
    Unset,
    Faint,
}

/// <summary>A run of text that has one style.</summary>
internal readonly record struct DetailsSpan(string Text, DetailsStyle Style);

/// <summary>A line of the details strip: its spans in order. Its plain text is the spans' texts joined.</summary>
internal sealed record DetailsLine(IReadOnlyList<DetailsSpan> Spans)
{
    /// <summary>The text of the line without styles.</summary>
    public string Text => string.Concat(Spans.Select(span => span.Text));
}

/// <summary>
/// A display row of wrapped text: the index of the logical line it is from, the UTF-16 range <c>[Start, End)</c> of that
/// line's cleaned text it shows, and its spans. The row's text is exactly that range of the logical text.
/// </summary>
internal sealed record DetailsRow(int LogicalLine, int Start, int End, DetailsLine Line);
