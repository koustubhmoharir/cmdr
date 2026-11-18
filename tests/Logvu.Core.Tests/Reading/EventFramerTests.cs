using System.Text;
using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Tests.Parsing;

namespace Logvu.Core.Tests.Reading;

public sealed class EventFramerTests
{
    /// <summary>Malformed JSON lines in the JSON fixtures: their events are raw, and no other event is.</summary>
    private static readonly string[] KnownMalformed = ["Truncated {Thing", "\"msg\":\"truncated", "[\"2026-11-18T10:20:34"];

    private static readonly string[] Levels = ["INFO", "DEBUG", "WARN", "ERROR", "FATAL", "TRACE"];
    private static readonly string[] Threads = ["1", "12", "W3", "main"];
    private static readonly string[] Loggers = ["A.B", "Svc", "Sheet.Runner", "Engine.Export"];
    private static readonly string[] PlainLines =
        ["Retrying is not possible", "System.InvalidOperationException: boom", "done", "2026-11-18 is a date with no level"];

    /// <summary>Characters for random messages: ASCII, plus 2-byte and 3-byte UTF-8 characters.</summary>
    private const string MessagePool = "abcXYZ 0123-:[]()_.é€";

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Fixture(string fileName) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static EventExtent E(long start, long end, long headerEnd, bool provisional = false, bool orphan = false) =>
        new(start, end, headerEnd, provisional, orphan);

    private static void Append(MemoryStream stream, string text)
    {
        // Readers move the position, so every append goes to the end first.
        stream.Position = stream.Length;
        byte[] bytes = Bytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static EventExtent Only(IEnumerable<EventExtent> events)
    {
        EventExtent[] array = events.ToArray();
        Assert.Single(array);
        return array[0];
    }

    private static LogEvent ParseEvent(Stream stream, EventExtent extent, ILogScheme scheme)
    {
        string text = EventText.Read(stream, extent, EventText.DefaultCap).Text;
        return scheme.Parse(text, ParsingFixtures.Utc);
    }

    /// <summary>Frames the data both ways and asserts that the two directions agree. Returns the forward extents.</summary>
    private static EventExtent[] FrameBothWays(byte[] data, ILogScheme scheme, bool live, int blockSize = 64 * 1024, int textCap = LineReader.TextCap)
    {
        using var stream = new MemoryStream(data, writable: false);
        var framer = new EventFramer(stream, scheme, live, blockSize, textCap);
        EventExtent[] forward = framer.ReadForward(0).ToArray();
        EventExtent[] backward = framer.ReadBackward(stream.Length).Reverse().ToArray();
        Assert.Equal(forward, backward);
        return forward;
    }

    /// <summary>The fixture with LF or CRLF line ends, with or without a byte order mark.</summary>
    private static byte[] Variant(byte[] fixture, bool crlf, bool bom)
    {
        string text = Encoding.UTF8.GetString(fixture);
        if (crlf)
        {
            text = text.Replace("\n", "\r\n");
        }

        byte[] body = Bytes(text);
        if (!bom)
        {
            return body;
        }

        return [0xEF, 0xBB, 0xBF, .. body];
    }

    public static IEnumerable<object[]> FixtureCases()
    {
        (string File, string Scheme)[] fixtures =
        [
            ("log4net-core.log", "log4net-core"),
            ("log4net-web.log", "log4net-web"),
            ("log4net-scheduler.log", "log4net-scheduler"),
            ("log4net-date-first.log", "log4net-date-first"),
            ("serilog.json", "serilog-json"),
            ("slog.json", "slog-json"),
        ];
        foreach ((string file, string scheme) in fixtures)
        {
            foreach (bool crlf in new[] { false, true })
            {
                foreach (bool bom in new[] { false, true })
                {
                    foreach (int blockSize in new[] { 7, 64, 4096 })
                    {
                        foreach (bool live in new[] { false, true })
                        {
                            yield return [file, scheme, crlf, bom, blockSize, live];
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(FixtureCases))]
    public void Fixtures_frame_identically_in_both_directions_and_tile_the_data(
        string fileName, string schemeName, bool crlf, bool bom, int blockSize, bool live)
    {
        ILogScheme scheme = BuiltInSchemes.Find(schemeName)!;
        byte[] data = Variant(Fixture(fileName), crlf, bom);
        long contentStart = bom ? 3 : 0;

        using var stream = new MemoryStream(data, writable: false);
        var framer = new EventFramer(stream, scheme, live, blockSize);
        EventExtent[] forward = framer.ReadForward(0).ToArray();
        EventExtent[] backward = framer.ReadBackward(stream.Length).Reverse().ToArray();
        Assert.Equal(forward, backward);

        // The fixtures end with a line terminator, so the live data end is the length too.
        Assert.Equal(contentStart, forward[0].Start);
        for (int i = 0; i + 1 < forward.Length; i++)
        {
            Assert.Equal(forward[i].End, forward[i + 1].Start);
        }

        Assert.Equal(stream.Length, forward[^1].End);

        string[] lines = ParsingFixtures.Lines(fileName);
        int orphan = lines.TakeWhile(line => !scheme.IsHeader(line)).Any() ? 1 : 0;
        Assert.Equal(lines.Count(scheme.IsHeader) + orphan, forward.Length);
        Assert.All(forward, e => Assert.False(e.Orphan));

        for (int i = 0; i < forward.Length; i++)
        {
            bool last = i == forward.Length - 1;
            Assert.Equal(live && !scheme.IsJson && last, forward[i].Provisional);
        }

        foreach (EventExtent e in forward)
        {
            string text = EventText.Read(stream, e, EventText.DefaultCap).Text;
            LogEvent parsed = scheme.Parse(text, ParsingFixtures.Utc);
            bool known = KnownMalformed.Any(text.Contains);
            Assert.Equal(known, parsed.IsRaw);
        }
    }

    [Fact]
    public void Fixture_events_have_exact_header_ends()
    {
        // The first event of log4net-core is its first line, which ends after its LF.
        EventExtent[] events = FrameBothWays(Fixture("log4net-core.log"), BuiltInSchemes.Log4NetCore, live: false);
        string firstLine = ParsingFixtures.Lines("log4net-core.log")[0];
        Assert.Equal(E(0, events[1].Start, Encoding.UTF8.GetByteCount(firstLine) + 1), events[0]);
        Assert.Equal(events[1].Start, events[0].End);
    }

    // ---- Property tests against a reference model -----------------------------------------------------------

    private const int PropertyCases = 400;

    private sealed record Line(long Start, long End, bool Terminated, string Text);

    private sealed record Document(byte[] Bytes, IReadOnlyList<Line> Lines, long ContentStart, long TerminatedEnd);

    private static string Pick(Random rng, string[] values) => values[rng.Next(values.Length)];

    private static string Eol(Random rng) => rng.Next(2) == 0 ? "\n" : "\r\n";

    private static string Message(Random rng)
    {
        int length = rng.Next(0, 121);
        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = MessagePool[rng.Next(MessagePool.Length)];
        }

        return new string(chars);
    }

    private static string Date(Random rng) =>
        $"2026-11-18 {rng.Next(24):D2}:{rng.Next(60):D2}:{rng.Next(60):D2},{rng.Next(1000):D3}";

    private static string CoreHeader(Random rng)
    {
        string level = Pick(rng, Levels);
        string thread = Pick(rng, Threads);
        string logger = Pick(rng, Loggers);
        return $"{level,-5} {Date(rng)} [{thread}] {logger} - {Message(rng)}";
    }

    private static string WebHeader(Random rng)
    {
        string level = Pick(rng, Levels);
        string version = Pick(rng, ["5.2.1", "(null)"]);
        string user = Pick(rng, ["17", "(null)"]);
        string source = Pick(rng, ["10.0.0.5", "(null)"]);
        string thread = Pick(rng, Threads);
        string logger = Pick(rng, Loggers);
        return $"{level,-5} {Date(rng)} v.{version} u.{user} {source} 10.0.0.1 [{thread}] {logger} - {Message(rng)}";
    }

    private static string TextLine(Random rng)
    {
        int kind = rng.Next(100);
        if (kind < 25)
        {
            return CoreHeader(rng);
        }

        if (kind < 40)
        {
            return WebHeader(rng);
        }

        if (kind < 60)
        {
            return $"   at Sheet.Runner.Execute(Int32 id) in C:\\src\\Runner.cs:line {rng.Next(1, 999)}";
        }

        if (kind < 75)
        {
            return rng.Next(2) == 0 ? "" : "   ";
        }

        return Pick(rng, PlainLines);
    }

    private static string JsonLine(Random rng)
    {
        int kind = rng.Next(100);
        if (kind < 60)
        {
            return $"{{\"n\":{rng.Next(1000)},\"msg\":\"{Message(rng)}\"}}";
        }

        if (kind < 75)
        {
            return Pick(rng, ["{\"broken", "not json", "[1,2]"]);
        }

        return Pick(rng, ["", "  ", "\t"]);
    }

    /// <summary>Lays the lines out as bytes, recording each line's extent.</summary>
    private static Document Assemble((string Text, string Eol)[] parts, bool bom)
    {
        var buffer = new MemoryStream();
        if (bom)
        {
            buffer.Write(new byte[] { 0xEF, 0xBB, 0xBF });
        }

        long contentStart = bom ? 3 : 0;
        var lines = new List<Line>();
        foreach ((string text, string eol) in parts)
        {
            byte[] bytes = Bytes(text + eol);
            long start = buffer.Position;
            buffer.Write(bytes);
            lines.Add(new Line(start, buffer.Position, eol.Length > 0, text));
        }

        long terminatedEnd = lines.Where(line => line.Terminated).Select(line => line.End).DefaultIfEmpty(contentStart).Max();
        return new Document(buffer.ToArray(), lines, contentStart, terminatedEnd);
    }

    /// <summary>
    /// The reference model: groups the lines the document was built from. It shares no code with the framer beyond the
    /// scheme's own header test.
    /// </summary>
    private static EventExtent[] Model(Document document, ILogScheme scheme, bool live, long dataEnd)
    {
        var events = new List<EventExtent>();
        bool open = false;
        long start = 0;
        long headerEnd = 0;
        bool orphan = false;

        foreach (Line line in document.Lines.Where(line => line.End <= dataEnd))
        {
            bool header = scheme.IsHeader(line.Text);
            if (open && header)
            {
                events.Add(Event(start, line.Start, headerEnd, orphan));
                open = false;
            }

            if (!open)
            {
                open = true;
                start = line.Start;
                headerEnd = line.End;
                orphan = !header;
            }
        }

        if (open)
        {
            events.Add(Event(start, dataEnd, headerEnd, orphan));
        }

        return events.ToArray();

        EventExtent Event(long s, long e, long he, bool o) =>
            new(s, e, he, live && !scheme.IsJson && e == dataEnd, o);
    }

    /// <summary>Checks the framer against the model for one document: both directions, and FindEventStart for every line.</summary>
    private static void CheckDocument(Document document, ILogScheme scheme, bool live, int blockSize, int textCap)
    {
        long dataEnd = live ? document.TerminatedEnd : document.Bytes.Length;
        EventExtent[] expected = Model(document, scheme, live, dataEnd);

        using var stream = new MemoryStream(document.Bytes, writable: false);
        var framer = new EventFramer(stream, scheme, live, blockSize, textCap);
        EventExtent[] forward = framer.ReadForward(0).ToArray();
        EventExtent[] backward = framer.ReadBackward(stream.Length).Reverse().ToArray();
        Assert.Equal(expected, forward);
        Assert.Equal(expected, backward);

        foreach (Line line in document.Lines)
        {
            if (line.Start >= dataEnd)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => { framer.FindEventStart(line.Start); });
                continue;
            }

            EventExtent owner = expected.Single(e => e.Start <= line.Start && line.Start < e.End);
            Assert.Equal(owner.Start, framer.FindEventStart(line.Start));
        }
    }

    [Fact]
    public void Random_text_documents_frame_as_the_model_says()
    {
        var rng = new Random(20260118);
        for (int i = 0; i < PropertyCases / 2; i++)
        {
            ILogScheme scheme = i % 2 == 0 ? BuiltInSchemes.Log4NetCore : BuiltInSchemes.Log4NetWeb;
            int count = rng.Next(0, 41);
            var parts = new List<(string Text, string Eol)>();
            for (int line = 0; line < count; line++)
            {
                parts.Add((TextLine(rng), Eol(rng)));
            }

            if (rng.Next(4) == 0)
            {
                // An unterminated tail: a complete header, or a partial one.
                parts.Add((rng.Next(2) == 0 ? CoreHeader(rng) : "INFO  2026-11-18 10:2", ""));
            }

            Document document = Assemble(parts.ToArray(), bom: rng.Next(2) == 0);
            int blockSize = rng.Next(1, 34);
            int textCap = rng.Next(96, 129);
            bool live = rng.Next(2) == 0;
            try
            {
                CheckDocument(document, scheme, live, blockSize, textCap);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"text case {i} ({scheme.Name}, live {live}, block {blockSize}, cap {textCap})", ex);
            }
        }
    }

    [Fact]
    public void Random_json_documents_frame_as_the_model_says()
    {
        var rng = new Random(20260119);
        for (int i = 0; i < PropertyCases / 2; i++)
        {
            ILogScheme scheme = i % 2 == 0 ? BuiltInSchemes.SerilogJson : BuiltInSchemes.SlogJson;
            int count = rng.Next(0, 41);
            var parts = new List<(string Text, string Eol)>();
            for (int line = 0; line < count; line++)
            {
                parts.Add((JsonLine(rng), Eol(rng)));
            }

            if (rng.Next(4) == 0)
            {
                parts.Add((rng.Next(2) == 0 ? "{\"n\":7}" : "{\"n\":99,\"msg\":\"tail", ""));
            }

            Document document = Assemble(parts.ToArray(), bom: rng.Next(2) == 0);
            int blockSize = rng.Next(1, 34);
            int textCap = rng.Next(8, 65);
            bool live = rng.Next(2) == 0;
            try
            {
                CheckDocument(document, scheme, live, blockSize, textCap);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"json case {i} ({scheme.Name}, live {live}, block {blockSize}, cap {textCap})", ex);
            }
        }
    }

    // ---- Large events ---------------------------------------------------------------------------------------

    [Fact]
    public void Event_with_a_300_kb_stack_trace_between_two_normal_events()
    {
        string before = "INFO  2026-11-18 10:20:30,123 [1] A.B - before\n";
        string failure = "ERROR 2026-11-18 10:20:31,000 [1] A.B - big failure\n";
        string after = "WARN  2026-11-18 10:20:32,000 [1] A.B - after\n";
        const string Frame = "   at Sheet.Runner.Execute(Int32 id) in C:\\src\\Runner.cs:line 88\n";
        var trace = new StringBuilder();
        while (trace.Length < 300_000)
        {
            trace.Append(Frame);
        }

        string text = before + failure + trace + after;
        byte[] data = Bytes(text);

        EventExtent[] events = FrameBothWays(data, BuiltInSchemes.Log4NetCore, live: false, blockSize: 4096);

        Assert.Equal(3, events.Length);
        long firstEnd = before.Length;
        long secondEnd = firstEnd + failure.Length + trace.Length;
        Assert.Equal(E(0, firstEnd, firstEnd), events[0]);
        Assert.Equal(E(firstEnd, secondEnd, firstEnd + failure.Length), events[1]);
        Assert.Equal(E(secondEnd, data.Length, data.Length), events[2]);

        using var stream = new MemoryStream(data, writable: false);
        (string capped, bool truncated) = EventText.Read(stream, events[1], 1000);
        Assert.True(truncated);
        Assert.True(Encoding.UTF8.GetByteCount(capped) <= 1000);
        Assert.StartsWith("ERROR", capped);

        (string whole, bool wholeTruncated) = EventText.Read(stream, events[1], EventText.DefaultCap);
        Assert.False(wholeTruncated);
        Assert.Equal(failure.Length + trace.Length - 1, whole.Length);
    }

    // ---- Live rules -----------------------------------------------------------------------------------------

    [Fact]
    public void Live_text_event_is_provisional_and_grows_with_continuation_lines()
    {
        using var stream = new MemoryStream();
        Append(stream, "INFO  2026-11-18 10:20:30,123 [1] A.B - started\n   at frame\n");
        var framer = new EventFramer(stream, BuiltInSchemes.Log4NetCore, live: true);

        EventExtent first = Only(framer.ReadForward(0));
        Assert.Equal(E(0, stream.Length, first.HeaderEnd, provisional: true), first);

        Append(stream, "   at frame2\n");
        EventExtent grown = Only(framer.ReadForward(first.Start));
        Assert.True(grown.Provisional);
        Assert.Equal(first.Start, grown.Start);
        Assert.True(grown.End > first.End);
        Assert.Equal(stream.Length, grown.End);

        Append(stream, "WARN  2026-11-18 10:20:31,000 [1] A.B - next\n");
        EventExtent[] events = framer.ReadForward(0).ToArray();
        Assert.Equal(2, events.Length);
        Assert.False(events[0].Provisional);
        Assert.True(events[1].Provisional);
        Assert.Equal(grown.End, events[0].End);
        Assert.Equal(events, framer.ReadBackward(stream.Length).Reverse().ToArray());
    }

    [Fact]
    public void Live_text_file_with_unterminated_tail_excludes_it_and_closed_includes_it()
    {
        const string Ok = "INFO  2026-11-18 10:20:30,123 [1] A.B - ok\n";
        const string Tail = "INFO  2026-11-18 10:20:30,123 [1] A.B - partial";
        byte[] data = Bytes(Ok + Tail);

        using (var live = new MemoryStream(data, writable: false))
        {
            var framer = new EventFramer(live, BuiltInSchemes.Log4NetCore, live: true);
            EventExtent only = E(0, Ok.Length, Ok.Length, provisional: true);
            Assert.Equal([only], framer.ReadForward(0).ToArray());
            Assert.Equal([only], framer.ReadBackward(live.Length).ToArray());
            Assert.Empty(framer.ReadForward(Ok.Length));
            Assert.Throws<ArgumentOutOfRangeException>(() => { framer.FindEventStart(Ok.Length); });
        }

        using (var closed = new MemoryStream(data, writable: false))
        {
            var framer = new EventFramer(closed, BuiltInSchemes.Log4NetCore, live: false);
            EventExtent[] expected = [E(0, Ok.Length, Ok.Length), E(Ok.Length, data.Length, data.Length)];
            Assert.Equal(expected, framer.ReadForward(0).ToArray());
            Assert.Equal(expected.Reverse(), framer.ReadBackward(closed.Length).ToArray());
        }
    }

    [Fact]
    public void Live_file_whose_only_content_is_an_unterminated_line_has_no_events()
    {
        byte[] data = Bytes("INFO  2026-11-18 10:20:30,123 [1] A.B - partial");
        using var live = new MemoryStream(data, writable: false);
        var framer = new EventFramer(live, BuiltInSchemes.Log4NetCore, live: true);
        Assert.Empty(framer.ReadForward(0));
        Assert.Empty(framer.ReadBackward(live.Length));
    }

    [Fact]
    public void Live_json_holds_back_an_unterminated_last_line()
    {
        string complete = "{\"Timestamp\":\"2026-11-18T10:20:30+05:30\",\"Level\":\"Information\",\"MessageTemplate\":\"ok\"}\n";
        const string Partial = "{\"Timestamp\":\"2026-11-18T10:20:31+05:30\",\"Level\":\"Inf";
        byte[] data = Bytes(complete + Partial);

        using (var live = new MemoryStream(data, writable: false))
        {
            var framer = new EventFramer(live, BuiltInSchemes.SerilogJson, live: true);
            EventExtent only = E(0, complete.Length, complete.Length);
            Assert.Equal([only], framer.ReadForward(0).ToArray());
            Assert.Equal([only], framer.ReadBackward(live.Length).ToArray());
        }

        using (var closed = new MemoryStream(data, writable: false))
        {
            var framer = new EventFramer(closed, BuiltInSchemes.SerilogJson, live: false);
            EventExtent[] events = framer.ReadForward(0).ToArray();
            Assert.Equal(2, events.Length);
            Assert.All(events, e => Assert.False(e.Provisional));
            Assert.True(ParseEvent(closed, events[1], BuiltInSchemes.SerilogJson).IsRaw);
        }
    }

    [Fact]
    public void Closed_json_tail_without_terminator_is_an_event_and_parses_when_valid()
    {
        string complete = "{\"Timestamp\":\"2026-11-18T10:20:30+05:30\",\"Level\":\"Information\",\"MessageTemplate\":\"ok\"}\n";
        string valid = "{\"Timestamp\":\"2026-11-18T10:20:31+05:30\",\"Level\":\"Information\",\"MessageTemplate\":\"tail\"}";
        using var stream = new MemoryStream(Bytes(complete + valid), writable: false);
        var framer = new EventFramer(stream, BuiltInSchemes.SerilogJson, live: false);

        EventExtent[] events = framer.ReadForward(0).ToArray();

        Assert.Equal(2, events.Length);
        Assert.False(ParseEvent(stream, events[1], BuiltInSchemes.SerilogJson).IsRaw);
    }

    [Fact]
    public void Live_json_events_are_never_provisional()
    {
        string lines = "{\"n\":1}\n{\"n\":2}\n";
        using var stream = new MemoryStream(Bytes(lines), writable: false);
        var framer = new EventFramer(stream, BuiltInSchemes.SlogJson, live: true);
        EventExtent[] events = framer.ReadForward(0).ToArray();
        Assert.Equal([E(0, 8, 8), E(8, 16, 16)], events);
    }

    // ---- Orphans and empty data -----------------------------------------------------------------------------

    [Fact]
    public void File_starting_with_continuation_lines_has_an_orphan_first_event_in_both_directions()
    {
        string orphanText = "   at x\n";
        string first = "INFO  2026-11-18 10:20:30,123 [1] A.B - a\n";
        string second = "   at y\n";
        string third = "WARN  2026-11-18 10:20:31,000 [1] A.B - b\n";
        byte[] data = Bytes(orphanText + first + second + third);

        EventExtent[] events = FrameBothWays(data, BuiltInSchemes.Log4NetCore, live: false);

        int h1 = orphanText.Length;
        int h2 = h1 + first.Length + second.Length;
        Assert.Equal(
            [
                E(0, h1, orphanText.Length, orphan: true),
                E(h1, h1 + first.Length + second.Length, h1 + first.Length),
                E(h2, data.Length, data.Length),
            ],
            events);
    }

    [Fact]
    public void File_with_no_header_is_one_orphan_event()
    {
        byte[] data = Bytes("plain one\nplain two\n");
        Assert.Equal([E(0, data.Length, 10, orphan: true)], FrameBothWays(data, BuiltInSchemes.Log4NetCore, live: false));
        Assert.Equal([E(0, data.Length, 10, provisional: true, orphan: true)], FrameBothWays(data, BuiltInSchemes.Log4NetCore, live: true));
    }

    [Fact]
    public void Empty_and_bom_only_files_have_no_events()
    {
        byte[][] files = [[], [0xEF, 0xBB, 0xBF]];
        foreach (byte[] data in files)
        {
            foreach (bool live in new[] { false, true })
            {
                Assert.Empty(FrameBothWays(data, BuiltInSchemes.Log4NetCore, live));
                Assert.Empty(FrameBothWays(data, BuiltInSchemes.SerilogJson, live));
            }
        }
    }

    [Fact]
    public void Json_leading_blank_lines_are_an_orphan_and_later_blank_lines_attach_to_their_event()
    {
        string json = "{\"n\":1}\n";
        string leading = "\n \n";
        string trailing = "\n";
        byte[] data = Bytes(leading + json + trailing);

        EventExtent[] events = FrameBothWays(data, BuiltInSchemes.SlogJson, live: false);

        Assert.Equal(
            [
                E(0, leading.Length, 1, orphan: true),
                E(leading.Length, data.Length, leading.Length + json.Length),
            ],
            events);
    }

    // ---- Mid-file reads -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("log4net-core.log", "log4net-core", 7, false)]
    [InlineData("log4net-core.log", "log4net-core", 64, true)]
    [InlineData("serilog.json", "serilog-json", 7, false)]
    [InlineData("serilog.json", "serilog-json", 4096, true)]
    public void Backward_from_a_mid_file_event_end_and_forward_from_a_mid_file_start_give_the_subsequences(
        string fileName, string schemeName, int blockSize, bool live)
    {
        ILogScheme scheme = BuiltInSchemes.Find(schemeName)!;
        byte[] data = Variant(Fixture(fileName), crlf: true, bom: true);
        using var stream = new MemoryStream(data, writable: false);
        var framer = new EventFramer(stream, scheme, live, blockSize);
        EventExtent[] events = framer.ReadForward(0).ToArray();
        Assert.True(events.Length > 3);

        for (int k = 0; k < events.Length; k++)
        {
            Assert.Equal(events.Take(k + 1).Reverse(), framer.ReadBackward(events[k].End).ToArray());
            Assert.Equal(events.Skip(k), framer.ReadForward(events[k].Start).ToArray());
        }
    }

    [Fact]
    public void Backward_before_beyond_the_data_end_is_clamped_to_it()
    {
        byte[] data = Bytes("INFO  2026-11-18 10:20:30,123 [1] A.B - ok\npartial");
        using var stream = new MemoryStream(data, writable: false);
        var framer = new EventFramer(stream, BuiltInSchemes.Log4NetCore, live: true);

        EventExtent[] atLength = framer.ReadBackward(stream.Length).ToArray();
        Assert.Equal(atLength, framer.ReadBackward(long.MaxValue).ToArray());
        Assert.Single(atLength);
    }

    // ---- Argument checks ------------------------------------------------------------------------------------

    [Fact]
    public void Out_of_range_arguments_are_rejected()
    {
        using var stream = new MemoryStream(Bytes("INFO  2026-11-18 10:20:30,123 [1] A.B - ok\n"), writable: false);
        var framer = new EventFramer(stream, BuiltInSchemes.Log4NetCore, live: false);

        Assert.Throws<ArgumentOutOfRangeException>(() => framer.ReadForward(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => framer.ReadForward(stream.Length + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => framer.ReadBackward(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => framer.FindEventStart(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => framer.FindEventStart(stream.Length));
    }

    [Fact]
    public void Find_event_start_rejects_offsets_inside_the_byte_order_mark()
    {
        byte[] data = [0xEF, 0xBB, 0xBF, .. Bytes("INFO  2026-11-18 10:20:30,123 [1] A.B - ok\n")];
        using var stream = new MemoryStream(data, writable: false);
        var framer = new EventFramer(stream, BuiltInSchemes.Log4NetCore, live: false);

        Assert.Throws<ArgumentOutOfRangeException>(() => framer.FindEventStart(1));
        Assert.Equal(3, framer.FindEventStart(3));
    }

    [Fact]
    public void Find_event_start_walks_back_from_a_continuation_line_to_its_header()
    {
        byte[] data = Bytes("   at orphan\nINFO  2026-11-18 10:20:30,123 [1] A.B - a\n   at frame\n   at frame2\n");
        using var stream = new MemoryStream(data, writable: false);
        var framer = new EventFramer(stream, BuiltInSchemes.Log4NetCore, live: false);

        int header = "   at orphan\n".Length;
        int frame = header + "INFO  2026-11-18 10:20:30,123 [1] A.B - a\n".Length;
        Assert.Equal(0, framer.FindEventStart(0));
        Assert.Equal(header, framer.FindEventStart(header));
        Assert.Equal(header, framer.FindEventStart(frame));
        Assert.Equal(header, framer.FindEventStart(frame + "   at frame\n".Length));
    }
}
