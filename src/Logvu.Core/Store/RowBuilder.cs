using Logvu.Core.Parsing;
using Logvu.Core.Reading;

namespace Logvu.Core.Store;

/// <summary>Turns an event's bytes into a row and its parsed event.</summary>
public static class RowBuilder
{
    /// <summary>The most bytes of an event read as text and parsed: 1 MiB. A longer event is parsed from its prefix.</summary>
    public const int TextCap = 1024 * 1024;

    /// <summary>The most characters of the first line kept as a row's <see cref="EventRow.Preview"/>.</summary>
    public const int PreviewCap = 512;

    /// <summary>
    /// Reads and parses one event, and builds its row. The text is read with <see cref="EventText.Read"/> and capped at
    /// <see cref="TextCap"/> bytes. An event that is cut and no longer parses, such as a JSON line longer than the cap, is a raw row
    /// with a parse error, as any malformed record is.
    /// </summary>
    /// <param name="stream">A seekable stream holding the event. It is read, never disposed.</param>
    /// <param name="scheme">The scheme of the event's member, which parses the text. Readable members always have one.</param>
    /// <param name="generation">The generation of the event's member, which the row's key carries.</param>
    /// <param name="extent">The event's bytes within <paramref name="stream"/>.</param>
    /// <param name="context">The parse settings.</param>
    /// <returns>The row, and the parsed event it was built from.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="scheme"/> is <c>null</c>: the member has no scheme.</exception>
    /// <exception cref="IOException">The bytes of the event cannot be read.</exception>
    public static (EventRow Row, LogEvent Event) Build(Stream stream, ILogScheme? scheme, int generation, EventExtent extent, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(context);
        if (scheme is null)
        {
            throw new InvalidOperationException($"Generation {generation} has no scheme.");
        }

        (string text, _) = EventText.Read(stream, extent, TextCap);
        LogEvent ev = scheme.Parse(text, context);
        var row = new EventRow(
            new RowKey(generation, extent.Start),
            extent,
            ev.Time,
            ev.TimeText,
            ev.Level,
            ev.LevelText,
            ev.Source,
            Preview(ev.Message),
            ev.IsRaw,
            extent.Orphan,
            extent.Provisional);
        return (row, ev);
    }

    /// <summary>
    /// The first line of a message, cut to <see cref="PreviewCap"/> characters. A cut never separates the two halves of a surrogate pair.
    /// </summary>
    internal static string Preview(string message)
    {
        int newline = message.IndexOf('\n');
        ReadOnlySpan<char> line = newline < 0 ? message.AsSpan() : message.AsSpan(0, newline);
        if (line.Length > PreviewCap)
        {
            int cut = PreviewCap;
            if (char.IsHighSurrogate(line[cut - 1]))
            {
                cut--;
            }

            line = line[..cut];
        }

        return line.ToString();
    }
}
