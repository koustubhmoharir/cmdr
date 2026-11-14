using System.Globalization;
using Cmdr.Core.Output;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Output.OutputTestHelpers;

namespace Cmdr.Core.Tests.Output;

public class CommandOutputTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private readonly TempDir _temp = new();
    private readonly CommandOutput _output = new(100);

    public void Dispose()
    {
        _output.Dispose();
        _temp.Dispose();
    }

    private static string StartMarker(DateTimeOffset startedAt) =>
        "=== started " + startedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " ===";

    private static string Lines(params string[] lines) => string.Concat(lines.Select(l => l + Environment.NewLine));

    /// <summary>Reads a log file once the background writers of <paramref name="output"/> (default: the shared one) are idle.</summary>
    private string ReadLog(string path, CommandOutput? output = null)
    {
        Assert.True((output ?? _output).WaitForLogWrites(TimeSpan.FromSeconds(10)), "log writes did not finish");
        return File.ReadAllText(path);
    }

    [Fact]
    public void ConstructorRejectsNonPositiveBufferSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CommandOutput(0));
    }

    [Fact]
    public void LongPartialLineWrittenInTinyChunksAllocatesLinearly()
    {
        const int length = 32_768;
        _output.BeginRun(Start);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < length; i++)
        {
            _output.WriteStdOut("a");
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Copying the whole partial line on every chunk allocated about 1 GB here.
        Assert.True(allocated < 32 * 1024 * 1024, $"allocated {allocated} bytes");
        IReadOnlyList<OutputLine> lines = _output.Buffer.GetLines(0, _output.Buffer.Count);
        Assert.Equal(new string('a', length), lines[^1].PlainText);
    }

    [Fact]
    public void BeginRunAppendsStartedMarkerAsSystemLine()
    {
        _output.BeginRun(Start);

        OutputLine line = _output.Buffer.GetLines(0, 1)[0];
        Assert.Equal(StartMarker(Start), line.PlainText);
        Assert.Equal(OutputSource.System, line.Source);
        Assert.Equal(SpanStyle.Default, line.Spans[0].Style);
    }

    [Fact]
    public void StdOutAndStdErrLinesKeepTheirSource()
    {
        _output.BeginRun(Start);
        _output.WriteStdOut("out\n");
        _output.WriteStdErr("err\n");

        IReadOnlyList<OutputLine> lines = _output.Buffer.GetLines(0, _output.Buffer.Count);
        Assert.Equal(OutputSource.StdOut, lines[1].Source);
        Assert.Equal("out", lines[1].PlainText);
        Assert.Equal(OutputSource.StdErr, lines[2].Source);
        Assert.Equal("err", lines[2].PlainText);
    }

    [Fact]
    public void PartialLineIsVisibleThenCompletes()
    {
        _output.BeginRun(Start);
        _output.WriteStdOut("par");

        IReadOnlyList<OutputLine> lines = _output.Buffer.GetLines(0, _output.Buffer.Count);
        Assert.Equal(2, lines.Count);
        Assert.Equal("par", lines[1].PlainText);
        Assert.Equal(OutputSource.StdOut, lines[1].Source);

        _output.WriteStdOut("tial\n");
        Assert.Equal(new[] { StartMarker(Start), "partial" }, BufferTexts(_output.Buffer));
    }

    [Fact]
    public void StdErrPartialAppearsAfterStdOutPartial()
    {
        _output.BeginRun(Start);
        _output.WriteStdErr("e");
        _output.WriteStdOut("o");
        Assert.Equal(new[] { StartMarker(Start), "o", "e" }, BufferTexts(_output.Buffer));
    }

    [Fact]
    public void ChunksSplitMidLineAreJoined()
    {
        _output.BeginRun(Start);
        _output.WriteStdOut("\u001B[31mhel");
        _output.WriteStdOut("lo\u001B[0m\nworld\n");

        IReadOnlyList<OutputLine> lines = _output.Buffer.GetLines(0, _output.Buffer.Count);
        Assert.Equal("hello", lines[1].PlainText);
        Assert.Equal(AnsiColor.FromIndex(1), lines[1].Spans[0].Style.Foreground);
        Assert.Equal("world", lines[2].PlainText);
    }

    [Fact]
    public void WriteSystemSplitsNewlinesIntoLines()
    {
        _output.WriteSystem("first\nsecond\r\n");

        IReadOnlyList<OutputLine> lines = _output.Buffer.GetLines(0, _output.Buffer.Count);
        Assert.Equal(new[] { "first", "second" }, Texts(lines));
        Assert.All(lines, l => Assert.Equal(OutputSource.System, l.Source));
        Assert.Equal(SpanStyle.Default, lines[0].Spans[0].Style);
    }

    [Fact]
    public void EndRunFlushesPartialsAndAppendsSummary()
    {
        _output.BeginRun(Start);
        _output.WriteStdOut("no newline");
        _output.WriteStdErr("err tail");
        _output.EndRun("exit 0");

        Assert.Equal(
            new[] { StartMarker(Start), "no newline", "err tail", "=== exit 0 ===" },
            BufferTexts(_output.Buffer));
        Assert.Equal(OutputSource.System, _output.Buffer.GetLines(3, 1)[0].Source);
    }

    [Fact]
    public void EndRunClearsPartials()
    {
        _output.BeginRun(Start);
        _output.WriteStdOut("pending");
        _output.EndRun("done");

        // The flushed line is completed; no partial remains after the summary.
        Assert.Equal(3, _output.Buffer.Count);
        Assert.Equal(3, _output.Buffer.GetCompletedLinesFrom(0).Count);
    }

    [Fact]
    public void EndRunFlushesStdOutBeforeStdErr()
    {
        _output.BeginRun(Start);
        _output.WriteStdErr("e");
        _output.WriteStdOut("o");
        _output.EndRun("x");

        Assert.Equal(new[] { StartMarker(Start), "o", "e", "=== x ===" }, BufferTexts(_output.Buffer));
    }

    [Fact]
    public void SecondRunStartsWithNewMarkerAndFreshParser()
    {
        _output.BeginRun(Start);
        _output.WriteStdOut("\u001B[31mold");
        _output.EndRun("1");

        var second = Start.AddMinutes(1);
        _output.BeginRun(second);
        _output.WriteStdOut("new\n");

        Assert.Equal(new[] { StartMarker(second), "new" }, BufferTexts(_output.Buffer));
        Assert.Equal(SpanStyle.Default, _output.Buffer.GetLines(1, 1)[0].Spans[0].Style);
    }

    [Fact]
    public void SecondBeginRunClearsPreviousRunFromBuffer()
    {
        _output.BeginRun(Start);
        _output.WriteStdOut("old\n");
        _output.WriteStdErr("partial");
        _output.EndRun("1");

        var second = Start.AddMinutes(1);
        _output.BeginRun(second);

        Assert.Equal(new[] { StartMarker(second) }, BufferTexts(_output.Buffer));
        Assert.Equal(1, _output.Buffer.Count);
    }

    [Fact]
    public void LogKeepsBothRunsAfterSecondBeginRun()
    {
        string path = _temp.File("both-runs.log");
        _output.EnableLog(path);
        _output.BeginRun(Start);
        _output.WriteStdOut("old\n");
        _output.EndRun("1");

        var second = Start.AddMinutes(1);
        _output.BeginRun(second);
        _output.WriteStdOut("new\n");
        _output.DisableLog();

        Assert.Equal(
            Lines(StartMarker(Start), "old", "=== 1 ===", StartMarker(second), "new"),
            ReadLog(path));
    }

    [Fact]
    public void BufferTrimsOldLinesThroughCommandOutput()
    {
        var output = new CommandOutput(3);
        output.WriteStdOut("1\n2\n3\n4\n");
        Assert.Equal(new[] { "2", "3", "4" }, BufferTexts(output.Buffer));
        Assert.Equal(1, output.Buffer.FirstLineNumber);
    }

    [Fact]
    public void WriteSystemIsLoggedOnce()
    {
        string path = _temp.File("sys.log");
        _output.EnableLog(path);
        _output.WriteSystem("hello");
        _output.DisableLog();

        Assert.Equal(Lines("hello"), ReadLog(path));
    }

    [Fact]
    public void LogContainsPlainTextWithoutEscapeCodes()
    {
        string path = _temp.File("plain.log");
        _output.EnableLog(path);
        _output.BeginRun(Start);
        _output.WriteStdOut("\u001B[31mred\u001B[0m \u001B[1mbold\u001B[0m\n");
        _output.WriteStdErr("\u001B]0;title\u0007\u001B[2Kerr\tx\n");
        _output.EndRun("exit 1");
        _output.DisableLog();

        string text = ReadLog(path);
        Assert.DoesNotContain('\u001B', text);
        Assert.Equal(
            Lines(StartMarker(Start), "red bold", "err     x", "=== exit 1 ==="),
            text);
    }

    [Fact]
    public void LogUsesUtf8WithoutBom()
    {
        string path = _temp.File("utf8.log");
        _output.EnableLog(path);
        _output.WriteStdOut("héllo\n");
        _output.DisableLog();
        Assert.True(_output.WaitForLogWrites(TimeSpan.FromSeconds(10)));

        byte[] bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("héllo" + Environment.NewLine, System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void PartialLinesAreNotWrittenUntilCompleted()
    {
        string path = _temp.File("partial.log");
        _output.EnableLog(path);
        _output.BeginRun(Start);
        _output.WriteStdOut("abc");
        Assert.Equal(Lines(StartMarker(Start)), ReadLog(path));

        _output.WriteStdOut("def\n");
        Assert.Equal(Lines(StartMarker(Start), "abcdef"), ReadLog(path));
        _output.DisableLog();
    }

    [Fact]
    public void EnableLogMidRunWritesCurrentRunThenLiveLines()
    {
        string path = _temp.File("midrun.log");
        _output.BeginRun(Start);
        _output.WriteStdOut("a\nb\n");

        _output.EnableLog(path);
        Assert.Equal(Lines(StartMarker(Start), "a", "b"), ReadLog(path));

        _output.WriteStdOut("live\n");
        _output.DisableLog();
        Assert.Equal(Lines(StartMarker(Start), "a", "b", "live"), ReadLog(path));
    }

    [Fact]
    public void EnableLogExcludesPreviousRuns()
    {
        string path = _temp.File("runs.log");
        _output.BeginRun(Start);
        _output.WriteStdOut("old\n");
        _output.EndRun("first");

        var second = Start.AddHours(1);
        _output.BeginRun(second);
        _output.WriteStdOut("new\n");

        _output.EnableLog(path);
        _output.DisableLog();

        Assert.Equal(Lines(StartMarker(second), "new"), ReadLog(path));
    }

    [Fact]
    public void EnableLogBeforeAnyRunWritesNothingUntilOutput()
    {
        string path = _temp.File("norun.log");
        _output.WriteSystem("before");
        _output.EnableLog(path);
        Assert.Equal(string.Empty, ReadLog(path));

        _output.BeginRun(Start);
        _output.DisableLog();
        Assert.Equal(Lines(StartMarker(Start)), ReadLog(path));
    }

    [Fact]
    public void EnableLogCreatesParentDirectories()
    {
        string path = _temp.File(Path.Combine("nested", "dir", "out.log"));
        _output.EnableLog(path);

        Assert.True(_output.IsLogging);
        Assert.Equal(Path.GetFullPath(path), _output.LogPath);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void LogPathIsNullWhenNotLogging()
    {
        Assert.False(_output.IsLogging);
        Assert.Null(_output.LogPath);
    }

    [Fact]
    public void DisableLogStopsWritingAndClearsState()
    {
        string path = _temp.File("disable.log");
        _output.EnableLog(path);
        _output.WriteStdOut("one\n");
        _output.DisableLog();

        Assert.False(_output.IsLogging);
        Assert.Null(_output.LogPath);

        _output.WriteStdOut("two\n");
        Assert.Equal(Lines("one"), ReadLog(path));
        Assert.Equal(new[] { "one", "two" }, BufferTexts(_output.Buffer));
    }

    [Fact]
    public void DisableLogWhenNotLoggingIsNoOp()
    {
        _output.DisableLog();
        _output.DisableLog();
        Assert.False(_output.IsLogging);
    }

    [Fact]
    public void ToggleOffAndOnSamePathDoesNotDuplicateLines()
    {
        string path = _temp.File("toggle.log");
        _output.BeginRun(Start);
        _output.WriteStdOut("a\n");
        _output.EnableLog(path);
        _output.WriteStdOut("b\n");
        _output.DisableLog();

        _output.EnableLog(path);
        _output.WriteStdOut("c\n");
        _output.DisableLog();

        Assert.Equal(Lines(StartMarker(Start), "a", "b", "c"), ReadLog(path));
    }

    [Fact]
    public void LinesProducedWhileDisabledAreWrittenOnReenable()
    {
        string path = _temp.File("while-off.log");
        _output.BeginRun(Start);
        _output.EnableLog(path);
        _output.WriteStdOut("a\n");
        _output.DisableLog();

        _output.WriteStdOut("x\ny\n");
        _output.EnableLog(path);
        _output.WriteStdOut("z\n");
        _output.DisableLog();

        Assert.Equal(Lines(StartMarker(Start), "a", "x", "y", "z"), ReadLog(path));
    }

    [Fact]
    public void LinesEvictedWhileDisabledAreNotWrittenOnReenable()
    {
        var output = new CommandOutput(3);
        string path = _temp.File("evicted.log");
        output.BeginRun(Start);
        output.EnableLog(path);
        output.DisableLog();

        output.WriteStdOut("1\n2\n3\n4\n5\n");
        output.EnableLog(path);
        output.DisableLog();

        Assert.Equal(Lines(StartMarker(Start), "3", "4", "5"), ReadLog(path, output));
        output.Dispose();
    }

    [Fact]
    public void ReenableAfterNewRunWritesOnlyNewRunBacklog()
    {
        string path = _temp.File("new-run.log");
        _output.BeginRun(Start);
        _output.EnableLog(path);
        _output.WriteStdOut("old\n");
        _output.EndRun("1");
        _output.DisableLog();

        var second = Start.AddHours(1);
        _output.BeginRun(second);
        _output.EnableLog(path);
        _output.WriteStdOut("new\n");
        _output.DisableLog();

        Assert.Equal(
            Lines(StartMarker(Start), "old", "=== 1 ===", StartMarker(second), "new"),
            ReadLog(path));
    }

    [Fact]
    public void SwitchToDifferentPathWritesCurrentRunBacklogToIt()
    {
        string first = _temp.File("switch-first.log");
        string second = _temp.File("switch-second.log");
        _output.BeginRun(Start);
        _output.EnableLog(first);
        _output.WriteStdOut("a\n");
        _output.DisableLog();

        _output.WriteStdOut("b\n");
        _output.EnableLog(second);
        _output.WriteStdOut("c\n");
        _output.DisableLog();

        Assert.Equal(Lines(StartMarker(Start), "a"), ReadLog(first));
        Assert.Equal(Lines(StartMarker(Start), "a", "b", "c"), ReadLog(second));
    }

    [Fact]
    public void SwitchingPathWhileLoggingWritesBacklogToNewFileOnly()
    {
        string first = _temp.File("live-first.log");
        string second = _temp.File("live-second.log");
        _output.BeginRun(Start);
        _output.EnableLog(first);
        _output.WriteStdOut("a\n");

        _output.EnableLog(second);
        _output.WriteStdOut("b\n");
        _output.DisableLog();

        Assert.Equal(Lines(StartMarker(Start), "a"), ReadLog(first));
        Assert.Equal(Lines(StartMarker(Start), "a", "b"), ReadLog(second));
    }

    [Fact]
    public void EnableLogSameFileTwiceIsNoOp()
    {
        string path = _temp.File("same.log");
        _output.BeginRun(Start);
        _output.EnableLog(path);
        _output.WriteStdOut("x\n");
        _output.EnableLog(path);
        _output.WriteStdOut("y\n");
        _output.DisableLog();

        Assert.Equal(Lines(StartMarker(Start), "x", "y"), ReadLog(path));
    }

    [Fact]
    public void EnableLogToDifferentFileSwitchesFiles()
    {
        string first = _temp.File("first.log");
        string second = _temp.File("second.log");
        _output.BeginRun(Start);
        _output.EnableLog(first);
        _output.WriteStdOut("a\n");

        _output.EnableLog(second);
        Assert.Equal(Path.GetFullPath(second), _output.LogPath);
        _output.WriteStdOut("b\n");
        _output.DisableLog();

        Assert.Equal(Lines(StartMarker(Start), "a"), ReadLog(first));
        Assert.Equal(Lines(StartMarker(Start), "a", "b"), ReadLog(second));
    }

    [Fact]
    public void LogOpenFailureDisablesLoggingWithoutThrowing()
    {
        string dirPath = _temp.File("is-a-directory");
        Directory.CreateDirectory(dirPath);

        _output.BeginRun(Start);
        _output.EnableLog(dirPath);

        Assert.False(_output.IsLogging);
        Assert.Null(_output.LogPath);

        IReadOnlyList<OutputLine> lines = _output.Buffer.GetLines(0, _output.Buffer.Count);
        OutputLine last = lines[^1];
        Assert.Equal(OutputSource.System, last.Source);
        Assert.StartsWith("log disabled: ", last.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public void LogOpenFailureDoesNotThrowFromWrites()
    {
        string dirPath = _temp.File("another-directory");
        Directory.CreateDirectory(dirPath);
        _output.EnableLog(dirPath);

        _output.BeginRun(Start);
        _output.WriteStdOut("still works\n");
        _output.EndRun("ok");

        // BeginRun cleared the buffer, so the earlier "log disabled" notice is gone.
        Assert.Equal(new[] { StartMarker(Start), "still works", "=== ok ===" }, BufferTexts(_output.Buffer));
    }

    [Fact]
    public void LogOpenFailureDoesNotWriteNoticeToLog()
    {
        string dirPath = _temp.File("dir-no-notice");
        Directory.CreateDirectory(dirPath);
        string sibling = _temp.File("notice.log");

        _output.EnableLog(dirPath);
        _output.EnableLog(sibling);
        _output.WriteStdOut("hi\n");
        _output.DisableLog();

        Assert.Equal(Lines("hi"), ReadLog(sibling));
    }

    [Fact]
    public void DisposeClosesLog()
    {
        string path = _temp.File("dispose.log");
        var output = new CommandOutput(10);
        output.EnableLog(path);
        output.WriteStdOut("kept\n");
        output.Dispose();

        Assert.False(output.IsLogging);
        Assert.Equal(Lines("kept"), ReadLog(path, output));
    }

    [Fact]
    public void DisposeWithoutLogIsSafe()
    {
        var output = new CommandOutput(10);
        output.Dispose();
        output.Dispose();
    }

    [Fact]
    public void ProcessLinesCompleted_ReportsCompletedProcessLinesOnly()
    {
        var notified = new List<OutputLine>();
        _output.ProcessLinesCompleted += (_, lines) => notified.AddRange(lines);

        _output.BeginRun(Start);
        _output.WriteStdOut("a\nb");
        _output.WriteSystem("system line");
        _output.WriteStdErr("e\n");
        _output.EndRun("exit 0");

        // "b" is partial until the run ends, and system lines never reach the handler.
        Assert.Equal(new[] { "a", "e" }, Texts(notified));
        Assert.Equal(new[] { OutputSource.StdOut, OutputSource.StdErr }, notified.Select(l => l.Source));
    }

    [Fact]
    public void ProcessLinesCompleted_ReportsPlainTextWithoutAnsi()
    {
        var notified = new List<OutputLine>();
        _output.ProcessLinesCompleted += (_, lines) => notified.AddRange(lines);

        _output.WriteStdOut("\u001B[31mred\u001B[0m\n");

        Assert.Equal(new[] { "red" }, Texts(notified));
    }

    [Fact]
    public void ProcessLinesCompleted_RunsAfterTheLinesAreInTheBuffer()
    {
        int countSeen = -1;
        string? lastSeen = null;
        _output.ProcessLinesCompleted += (_, lines) =>
        {
            countSeen = _output.Buffer.Count;
            lastSeen = _output.Buffer.GetLines(_output.Buffer.Count - 1, 1)[0].PlainText;
        };

        _output.BeginRun(Start);
        _output.WriteStdOut("first\nsecond\n");

        Assert.Equal(3, countSeen);
        Assert.Equal("second", lastSeen);
    }

    [Fact]
    public void ProcessLinesCompleted_HandlerMayReadTheOutputWithoutDeadlock()
    {
        bool logging = true;
        _output.ProcessLinesCompleted += (_, _) => logging = _output.IsLogging;

        _output.WriteStdOut("line\n");

        Assert.False(logging);
    }

    [Fact]
    public void OutputOfAnEarlierRunIsDroppedAfterTheNextRunBegins()
    {
        string path = _temp.File("stale.log");
        _output.EnableLog(path);
        var notified = new List<(long Run, string Text)>();
        _output.ProcessLinesCompleted += (run, lines) => notified.AddRange(lines.Select(l => (run, l.PlainText)));

        long first = _output.BeginRun(Start);
        _output.WriteStdOut(first, "old\npar\u001B[3");
        _output.EndRun("exit 0");

        // Before the next run begins, the first run's output is still shown, after its end marker.
        _output.WriteStdErr(first, "late\n");

        var secondStart = Start.AddMinutes(1);
        long second = _output.BeginRun(secondStart);
        Assert.NotEqual(first, second);
        _output.WriteStdOut(second, "new");
        _output.WriteStdOut(first, "tial\nOLD_READY\n");
        _output.WriteStdErr(first, "OLD_READY\n");
        _output.WriteStdOut(second, "\n");
        _output.DisableLog();

        string[] expected = { StartMarker(secondStart), CommandOutput.StaleOutputNotice, "new" };
        Assert.Equal(expected, BufferTexts(_output.Buffer));
        Assert.Equal(new[] { (first, "old"), (first, "late"), (second, "new") }, notified);
        Assert.Equal(
            Lines(StartMarker(Start), "old", "par", "=== exit 0 ===", "late", StartMarker(secondStart), CommandOutput.StaleOutputNotice, "new"),
            ReadLog(path));
    }

    [Fact]
    public void StaleOutputNoticeIsWrittenOncePerRun()
    {
        long first = _output.BeginRun(Start);
        long second = _output.BeginRun(Start);
        _output.WriteStdOut(first, "a\n");
        _output.WriteStdOut(first, "b\n");
        Assert.Equal(new[] { StartMarker(Start), CommandOutput.StaleOutputNotice }, BufferTexts(_output.Buffer));

        _output.BeginRun(Start);
        _output.WriteStdOut(second, "c\n");
        _output.WriteStdOut(first, "d\n");
        Assert.Equal(new[] { StartMarker(Start), CommandOutput.StaleOutputNotice }, BufferTexts(_output.Buffer));
    }

    [Fact]
    public void ProcessOutputAfterDisposeIsDropped()
    {
        long run = _output.BeginRun(Start);
        _output.Dispose();
        _output.WriteStdOut(run, "after\n");
        _output.WriteStdErr("after\n");

        Assert.Equal(new[] { StartMarker(Start) }, BufferTexts(_output.Buffer));
    }
}
