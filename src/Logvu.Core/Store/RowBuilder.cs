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
    /// <returns>The row, the parsed event it was built from, and the text the event was parsed from.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="scheme"/> is <c>null</c>: the member has no scheme.</exception>
    /// <exception cref="IOException">The bytes of the event cannot be read.</exception>
    public static (EventRow Row, LogEvent Event, string Text) Build(Stream stream, ILogScheme? scheme, int generation, EventExtent extent, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(context);
        RequireScheme(scheme, generation);

        (string text, _) = EventText.Read(stream, extent, TextCap);
        (EventRow row, LogEvent ev) = BuildFromText(text, scheme, generation, extent, context);
        return (row, ev, text);
    }

    /// <summary>
    /// Parses the text of one event and builds its row, for callers that have read the text themselves (a detail read keeps the text it shows).
    /// </summary>
    /// <param name="text">The event's text, as <see cref="EventText.Read"/> gives it. It may be a prefix of the event when it was cut.</param>
    /// <param name="scheme">The scheme of the event's member, which parses the text.</param>
    /// <param name="generation">The generation of the event's member, which the row's key carries.</param>
    /// <param name="extent">The event's bytes within its file.</param>
    /// <param name="context">The parse settings.</param>
    /// <returns>The row, and the parsed event it was built from.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="scheme"/> is <c>null</c>: the member has no scheme.</exception>
    public static (EventRow Row, LogEvent Event) BuildFromText(string text, ILogScheme? scheme, int generation, EventExtent extent, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);
        RequireScheme(scheme, generation);

        LogEvent ev = scheme!.Parse(text, context);
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
    /// Builds the row of an event of a text scheme from its header line alone, for a filter to decide on before the event is parsed. The
    /// row has the same key, extent, time, level and source as <see cref="BuildFromText"/> gives, and an empty preview.
    /// </summary>
    /// <param name="text">The event's text, as <see cref="EventText.Read"/> gives it. Only its first line is parsed.</param>
    /// <param name="scheme">The scheme of the event's member.</param>
    /// <param name="generation">The generation of the event's member, which the row's key carries.</param>
    /// <param name="extent">The event's bytes within its file.</param>
    /// <param name="context">The parse settings.</param>
    /// <param name="withTime">Whether the row carries the header's time. Without it, the row's time and time text are <c>null</c>.</param>
    /// <returns>The header row, or <c>null</c> for a JSON scheme, whose header is the whole event.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="scheme"/> is <c>null</c>: the member has no scheme.</exception>
    public static EventRow? BuildHead(string text, ILogScheme? scheme, int generation, EventExtent extent, ParseContext context, bool withTime = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);
        RequireScheme(scheme, generation);

        if (scheme is not TextScheme textScheme)
        {
            return null;
        }

        LogEvent ev = textScheme.ParseHeader(text, context, withTime);
        return new EventRow(
            new RowKey(generation, extent.Start),
            extent,
            ev.Time,
            ev.TimeText,
            ev.Level,
            ev.LevelText,
            ev.Source,
            string.Empty,
            ev.IsRaw,
            extent.Orphan,
            extent.Provisional);
    }

    /// <summary>The scheme of a member, which every readable member has.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="scheme"/> is <c>null</c>.</exception>
    internal static ILogScheme SchemeOf(ILogScheme? scheme, int generation)
    {
        RequireScheme(scheme, generation);
        return scheme!;
    }

    private static void RequireScheme(ILogScheme? scheme, int generation)
    {
        if (scheme is null)
        {
            throw new InvalidOperationException($"Generation {generation} has no scheme.");
        }
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
