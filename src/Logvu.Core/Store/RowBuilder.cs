using System.Text;
using Logvu.Core.Config;
using Logvu.Core.Filter;
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

    /// <summary>The most characters of the first line kept as one of a row's <see cref="EventRow.Columns"/> values.</summary>
    public const int ColumnCap = 256;

    /// <summary>The shared column list of a row built with no columns configured.</summary>
    private static readonly string?[] NoColumns = Array.Empty<string?>();

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
            Preview(ev, context.MessageFields),
            ev.IsRaw,
            extent.Orphan,
            extent.Provisional)
        {
            Columns = ColumnValues(ev, context.Columns),
        };
        return (row, ev);
    }

    /// <summary>
    /// Builds the row of an event of a text scheme from its header line alone, for a filter to decide on before the event is parsed. The
    /// row has the same key, extent, time and level as <see cref="BuildFromText"/> gives, and an empty preview. Its
    /// <see cref="EventRow.Columns"/> are empty: a header row is only filtered on and is never displayed.
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
    /// The preview of an event: the first line of its message, then, for a JSON event, each configured message field that resolves in its
    /// JSON as <c>path=value</c>. The first pair is preceded by two spaces, or none when the message is empty; later pairs by one space. The
    /// value of a pair is the first line of its JSON text, as <see cref="JsonFieldPath.TryGetText"/> gives it. The whole is cut to
    /// <see cref="PreviewCap"/> characters.
    /// </summary>
    /// <param name="ev">The parsed event.</param>
    /// <param name="messageFields">The configured JSON paths, in configuration order.</param>
    internal static string Preview(LogEvent ev, IReadOnlyList<string> messageFields)
    {
        string head = FirstLine(ev.Message, PreviewCap);
        if (messageFields.Count == 0 || ev.IsRaw || ev.Json is not { } json)
        {
            return head;
        }

        var preview = new StringBuilder(head);
        bool any = false;
        foreach (string path in messageFields)
        {
            if (preview.Length >= PreviewCap)
            {
                break;
            }

            if (!JsonFieldPath.TryGetText(json, path, out string text))
            {
                continue;
            }

            string separator = any ? " " : head.Length > 0 ? "  " : string.Empty;
            preview.Append(separator).Append(path).Append('=').Append(FirstLine(text, PreviewCap));
            any = true;
        }

        return FirstLine(preview.ToString(), PreviewCap);
    }

    /// <summary>
    /// The values of the configured columns of an event, in configuration order. A raw event has no values, and a value the event does not
    /// have is <c>null</c>.
    /// </summary>
    /// <param name="ev">The parsed event.</param>
    /// <param name="columns">The configured columns, in configuration order.</param>
    /// <returns>One value per column, or the shared empty list when there are no columns.</returns>
    private static IReadOnlyList<string?> ColumnValues(LogEvent ev, IReadOnlyList<ColumnSpec> columns)
    {
        if (columns.Count == 0)
        {
            return NoColumns;
        }

        var values = new string?[columns.Count];
        if (ev.IsRaw)
        {
            return values;
        }

        for (int i = 0; i < columns.Count; i++)
        {
            values[i] = ColumnValue(ev, columns[i].Path);
        }

        return values;
    }

    /// <summary>
    /// The cell text of one column of an event, or <c>null</c> when the event has no value at the path. The value is found by
    /// <see cref="EventFields.TryGetValue"/>, the reader the field filter uses, so a column and a predicate on one path agree.
    /// </summary>
    private static string? ColumnValue(LogEvent ev, string path) =>
        EventFields.TryGetValue(ev, path, out string text) ? FirstLine(text, ColumnCap) : null;

    /// <summary>
    /// The first line of a value, cut to <paramref name="cap"/> characters. A cut never separates the two halves of a surrogate pair.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="cap">The most characters kept.</param>
    private static string FirstLine(string value, int cap)
    {
        int newline = value.IndexOf('\n');
        ReadOnlySpan<char> line = newline < 0 ? value.AsSpan() : value.AsSpan(0, newline);
        if (line.Length > cap)
        {
            int cut = cap;
            if (char.IsHighSurrogate(line[cut - 1]))
            {
                cut--;
            }

            line = line[..cut];
        }

        return line.ToString();
    }
}
