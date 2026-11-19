using Logvu.Core.Parsing;

namespace Logvu.Core.Reading;

/// <summary>Chooses the scheme of a file from the first lines of its content.</summary>
public static class SchemeSniffer
{
    /// <summary>The most bytes of the first line read in full for detection.</summary>
    private const int FirstLineCap = 1024 * 1024;

    /// <summary>
    /// Sniffs the scheme of a stream. The first non-blank lines (up to <see cref="SchemeDetector.SampleSize"/>) are read
    /// and passed to <see cref="SchemeDetector.Detect"/>. A first line longer than the text cap is read in full, up to
    /// 1 MiB, so that a long JSON object still parses.
    /// </summary>
    /// <param name="stream">A seekable stream. It is read, never disposed.</param>
    /// <param name="candidates">The schemes to choose from, in preference order.</param>
    /// <param name="live">
    /// Whether the file is still being written. Then an unterminated last line is not sampled: it may be incomplete.
    /// </param>
    /// <returns>The chosen scheme, or <c>null</c> when none applies.</returns>
    public static ILogScheme? Sniff(Stream stream, IReadOnlyList<ILogScheme> candidates, bool live)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(candidates);

        var reader = new LineReader(stream);
        long dataEnd = EventFramer.DataEnd(stream, live);
        var sample = new List<string>(SchemeDetector.SampleSize);
        foreach (RawLine line in reader.ReadForward(LineReader.ContentStart(stream), dataEnd))
        {
            // A truncated line is kept even when its prefix is blank: its content runs past the cap.
            if (!line.Truncated && string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            string text = line.Text;
            if (sample.Count == 0 && line.Truncated)
            {
                // EventText seeks the stream, which is safe: LineReader sets the position before every block read.
                var extent = new EventExtent(line.Start, line.End, line.End, Provisional: false, Orphan: false);
                text = EventText.Read(stream, extent, FirstLineCap).Text;
            }

            sample.Add(text);
            if (sample.Count == SchemeDetector.SampleSize)
            {
                break;
            }
        }

        return SchemeDetector.Detect(sample, candidates);
    }
}
