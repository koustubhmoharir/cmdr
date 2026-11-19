using Logvu.Core.Config;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;

namespace Logvu.Core.Store;

/// <summary>
/// Finds where a file's first event is and what time it has. The probe reads only as far as it needs: the scheme sample, then
/// the first event's header.
/// </summary>
public static class MemberProbe
{
    /// <summary>The most bytes of the first event read as text to find its time.</summary>
    public const int FirstEventCap = 1024 * 1024;

    /// <summary>The diagnostic of a file whose scheme could not be detected.</summary>
    public const string FormatNotRecognized = "format not recognized";

    /// <summary>The diagnostic of a file whose first event has no readable time.</summary>
    public const string NoReadableTime = "first event has no readable time";

    /// <summary>The diagnostic of a closed file with no event header.</summary>
    public const string NoEventHeader = "no event header found";

    /// <summary>
    /// Probes a stream. The scheme is <see cref="LogvuConfig.Format"/> when configured, otherwise sniffed from the content.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>No data (empty, a byte order mark only, or a live file whose only line is unterminated) is <see cref="MemberState.Pending"/>.</item>
    /// <item>
    /// With no scheme: a live file with fewer than <see cref="SchemeDetector.SampleSize"/> complete non-blank lines is pending, since
    /// detection may succeed as it grows. Otherwise the file is read with the raw-lines scheme and has <see cref="MemberState.UnknownTime"/>.
    /// </item>
    /// <item>
    /// With a scheme: the first non-orphan event is found from the content start. Its header's time gives <see cref="MemberState.Dated"/>;
    /// no time gives <see cref="MemberState.UnknownTime"/>. A live file with no event yet is pending; a closed one is unknown-time.
    /// </item>
    /// </list>
    /// A provisional first event still has its complete header line, so its time is usable.
    /// </remarks>
    /// <param name="stream">A seekable stream. It is read, never disposed.</param>
    /// <param name="config">The configuration: the format, the candidate schemes and the time zone.</param>
    /// <param name="live">Whether the file is still being written.</param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    public static ProbeResult Probe(Stream stream, LogvuConfig config, bool live)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(config);

        long length = stream.Length;
        var reader = new LineReader(stream);
        long contentStart = LineReader.ContentStart(stream);
        long dataEnd = EventFramer.DataEnd(stream, live);
        ILogScheme? scheme = config.Format ?? SchemeSniffer.Sniff(stream, config.Schemes, live);

        if (dataEnd <= contentStart)
        {
            return new ProbeResult(scheme, MemberState.Pending, contentStart, null, null, length);
        }

        if (scheme is null)
        {
            if (live && CountNonBlankLines(reader, contentStart, dataEnd, SchemeDetector.SampleSize) < SchemeDetector.SampleSize)
            {
                return new ProbeResult(null, MemberState.Pending, contentStart, null, null, length);
            }

            return new ProbeResult(RawLinesScheme.Instance, MemberState.UnknownTime, contentStart, null, FormatNotRecognized, length);
        }

        var context = new ParseContext(config.TimeZone);
        foreach (EventExtent extent in new EventFramer(stream, scheme, live).ReadForward(contentStart))
        {
            if (extent.Orphan)
            {
                continue;
            }

            string text = EventText.Read(stream, extent, FirstEventCap).Text;
            DateTimeOffset? time = scheme.Parse(text, context).Time;
            return time is null
                ? new ProbeResult(scheme, MemberState.UnknownTime, extent.Start, null, NoReadableTime, length)
                : new ProbeResult(scheme, MemberState.Dated, extent.Start, time, null, length);
        }

        return live
            ? new ProbeResult(scheme, MemberState.Pending, contentStart, null, null, length)
            : new ProbeResult(scheme, MemberState.UnknownTime, contentStart, null, NoEventHeader, length);
    }

    /// <summary>The number of non-blank lines in a range, counting no more than <paramref name="max"/>.</summary>
    private static int CountNonBlankLines(LineReader reader, long from, long limit, int max)
    {
        int count = 0;
        foreach (RawLine line in reader.ReadForward(from, limit))
        {
            // A truncated line is kept even when its prefix is blank: its content runs past the cap.
            if (line.Truncated || !string.IsNullOrWhiteSpace(line.Text))
            {
                count++;
                if (count == max)
                {
                    break;
                }
            }
        }

        return count;
    }
}
