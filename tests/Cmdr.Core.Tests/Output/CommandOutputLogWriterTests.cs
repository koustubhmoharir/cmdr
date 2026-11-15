using System.Diagnostics;
using System.Globalization;
using System.Text;
using Cmdr.Core.Output;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Output.OutputTestHelpers;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;

namespace Cmdr.Core.Tests.Output;

/// <summary>
/// Review 02-05 and 04-09: log I/O happens on a background writer, so a stalled log blocks nobody, and the replay cursor only
/// advances after a successful flush (Linux only: uses a FIFO and /dev/full).
/// </summary>
public class CommandOutputLogWriterTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(2);

    private static string StartMarker(DateTimeOffset startedAt) =>
        "=== started " + startedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " ===";

    /// <summary>Runs <paramref name="action"/> on a new thread; true if it returned within <paramref name="timeout"/>.</summary>
    private static bool CompletesWithin(Action action, TimeSpan timeout)
    {
        var thread = new Thread(() => action()) { IsBackground = true };
        thread.Start();
        return thread.Join(timeout);
    }

    private static string Lines(params string[] lines) => string.Concat(lines.Select(l => l + Environment.NewLine));

    [Fact]
    public void StalledLogBlocksNoWriterAndIsDisabledWhenItsQueueFills()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        using var fifo = StalledFifo.Create(dir.File("stalled.log"));
        var output = new CommandOutput(CommandOutput.LogQueueCapacity * 2);
        try
        {
            fifo.EnableLogOn(output);
            output.BeginRun(Start);

            // Far more than the pipe holds, so the writer blocks; then more lines than the queue takes. How many lines the
            // blocked writer took in its batch depends on when its thread ran, so write until logging is disabled (bounded).
            string line = new string('x', 200) + "\n";
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < CommandOutput.LogQueueCapacity * 3 && output.IsLogging; i++)
            {
                output.WriteStdOut(line);
            }

            output.WriteSystem("still responsive");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"writes took {watch.Elapsed}");

            Assert.False(output.IsLogging);
            Assert.Contains("log disabled: log writer stalled", BufferTexts(output.Buffer));
            Assert.False(output.WaitForLogWrites(TimeSpan.FromMilliseconds(200)));

            // Once the sink drains, the abandoned writer finishes and closes the file.
            fifo.Drain();
            Assert.True(output.WaitForLogWrites(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            output.Dispose();
        }
    }

    [Fact]
    public void ForceKillAndSnapshotsDoNotWaitForAStalledLog()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        using var fifo = StalledFifo.Create(dir.File("stalled.log"));
        var p = new ManagedProcess(1000);
        try
        {
            p.Start(Sh("head -c 300000 /dev/zero | tr '\\0' 'x' | fold -w 100; echo FILLED; sleep 30", dir.Path));
            fifo.EnableLogOn(p.Output);
            WaitForText(p, "FILLED");
            Assert.True(p.Output.IsLogging);
            Assert.False(p.Output.WaitForLogWrites(TimeSpan.FromMilliseconds(200)), "the log should be stalled");

            // Dedicated threads, not the thread pool: the test measures whether the calls block, not how soon the pool runs them.
            Assert.True(CompletesWithin(p.ForceKill, Prompt), "ForceKill waited for the stalled log");
            Assert.True(CompletesWithin(() => _ = p.Snapshot, Prompt), "Snapshot waited for the stalled log");
            WaitForExited(p);
            Assert.True(p.Snapshot.StopRequested);

            var lines = BufferTexts(p.Output.Buffer).ToList();
            int killLine = lines.IndexOf("force kill");
            int exitLine = lines.FindIndex(l => l.StartsWith("=== exited", StringComparison.Ordinal));
            Assert.True(killLine >= 0 && killLine < exitLine, "force kill line must precede the exit marker");
        }
        finally
        {
            fifo.Drain();
            Cleanup(p);
        }
    }

    [Fact]
    public void WriterWaitingBehindABlockedWriterReplaysTheLinesRetainedWhenLoggingWasEnabled()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        using var fifo = StalledFifo.Create(dir.File("blocked.log"));
        var output = new CommandOutput(100_000);
        try
        {
            output.BeginRun(Start);
            fifo.EnableLogOn(output);

            // More than the pipe holds (but less than the queue limit): the first writer blocks until the FIFO is drained.
            output.WriteStdOut(string.Concat(Enumerable.Repeat(new string('x', 200) + "\n", 2000)));

            output.DisableLog();
            output.WriteStdOut("REPLAY-ME\n"); // written while logging is off
            output.EnableLog(dir.File("blocked.log")); // waits for the first writer to finish
            Assert.True(output.IsLogging);

            output.BeginRun(Start.AddMinutes(1)); // clears the buffer: the waiting writer must not need it any more
            output.WriteStdOut("second-run\n");
            output.DisableLog();

            string text = fifo.ReadAll();
            Assert.Equal(1, CountOf(text, "REPLAY-ME\n"));
            Assert.Equal(1, CountOf(text, "second-run\n"));
            Assert.True(text.IndexOf("REPLAY-ME", StringComparison.Ordinal) < text.IndexOf("second-run", StringComparison.Ordinal));
        }
        finally
        {
            output.Dispose();
        }
    }

    [Fact]
    public void TogglingBehindABlockedWriterKeepsOneWaitingWriterAndLosesNoLine()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        using var fifo = StalledFifo.Create(dir.File("blocked.log"));
        var output = new CommandOutput(100_000);
        try
        {
            output.BeginRun(Start);
            fifo.EnableLogOn(output);
            output.WriteStdOut(string.Concat(Enumerable.Repeat(new string('x', 200) + "\n", 2000)));

            for (int cycle = 0; cycle < 20; cycle++)
            {
                output.DisableLog();
                output.WriteStdOut($"off-{cycle}\n");
                output.EnableLog(dir.File("blocked.log"));
                output.WriteStdOut($"on-{cycle}\n");
                Assert.True(output.UnfinishedLogWriterCount <= 2, $"{output.UnfinishedLogWriterCount} writers after cycle {cycle}");
            }

            output.DisableLog();
            string text = fifo.ReadAll();
            for (int cycle = 0; cycle < 20; cycle++)
            {
                Assert.Equal(1, CountOf(text, $"off-{cycle}\n"));
                Assert.Equal(1, CountOf(text, $"on-{cycle}\n"));
            }
        }
        finally
        {
            output.Dispose();
        }
    }

    [Fact]
    public void ReEnablingBehindAWriterAbandonedAsStalledIsRefused()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        using var fifo = StalledFifo.Create(dir.File("stalled.log"));
        var output = new CommandOutput(CommandOutput.LogQueueCapacity * 2);
        try
        {
            output.BeginRun(Start);
            fifo.EnableLogOn(output);
            string chunk = string.Concat(Enumerable.Repeat(new string('x', 200) + "\n", 500)); // 500 lines per write
            for (int i = 0; i < CommandOutput.LogQueueCapacity * 3 / 500 && output.IsLogging; i++)
            {
                output.WriteStdOut(chunk);
            }

            Assert.False(output.IsLogging); // abandoned as stalled, still blocked in its write

            output.EnableLog(dir.File("stalled.log"));

            Assert.False(output.IsLogging);
            Assert.Equal(1, output.UnfinishedLogWriterCount);
            Assert.Contains("log not enabled: an earlier write to this file is still blocked", BufferTexts(output.Buffer));
        }
        finally
        {
            fifo.Drain();
            output.Dispose();
        }
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void FailedFlushDoesNotCountLinesAsLoggedSoReEnablingReplaysThem()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/dev/full")) return;

        using var dir = new TempDir();
        string path = dir.File("full.log");
        File.CreateSymbolicLink(path, "/dev/full");
        var output = new CommandOutput(100);
        try
        {
            output.BeginRun(Start);
            output.WriteStdOut("a\n");
            output.EnableLog(path);
            output.WriteStdOut("b\n");

            WaitUntil(() => !output.IsLogging, "the failed flush to disable logging");
            Assert.Contains(BufferTexts(output.Buffer), t => t.StartsWith("log disabled: ", StringComparison.Ordinal));
            Assert.True(output.WaitForLogWrites(TimeSpan.FromSeconds(10)));

            string notice = BufferTexts(output.Buffer).Single(t => t.StartsWith("log disabled: ", StringComparison.Ordinal));

            // The same configured path, now a normal file: everything that never reached disk is written, including the
            // notice, which records the failure in the file.
            File.Delete(path);
            output.EnableLog(path);
            output.WriteStdOut("c\n");
            output.DisableLog();
            Assert.True(output.WaitForLogWrites(TimeSpan.FromSeconds(10)));

            // The notice is wherever the failure happened relative to "b": the writer may fail before or after "b" is written.
            // Either way the file now holds every line once, in the buffer's order.
            string[] expected = BufferTexts(output.Buffer);
            Assert.Equal(new[] { StartMarker(Start), "a", "b", "c", notice }.Order(), expected.Order());
            Assert.Equal(Lines(expected), File.ReadAllText(path));
        }
        finally
        {
            output.Dispose();
        }
    }

    [Fact]
    public void ReEnablingTheSameFileWaitsForTheEarlierWriterAndWritesEachLineOnce()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string path = dir.File("toggle.log");
        var output = new CommandOutput(100_000);
        try
        {
            output.BeginRun(Start);
            var expected = new List<string> { StartMarker(Start) };
            for (int round = 0; round < 20; round++)
            {
                output.EnableLog(path);
                for (int i = 0; i < 200; i++)
                {
                    string text = $"r{round}-{i}";
                    output.WriteStdOut(text + "\n");
                    expected.Add(text);
                }

                output.DisableLog();
            }

            Assert.True(output.WaitForLogWrites(TimeSpan.FromSeconds(10)));
            Assert.Equal(Lines(expected.ToArray()), File.ReadAllText(path));
        }
        finally
        {
            output.Dispose();
        }
    }

    /// <summary>A FIFO whose reader is open but does not read until <see cref="Drain"/>, so writes to it block once it is full.</summary>
    private sealed class StalledFifo : IDisposable
    {
        private readonly string _path;
        private readonly MemoryStream _captured = new();
        private Task<FileStream>? _reader;
        private Task? _drain;

        private StalledFifo(string path) => _path = path;

        public static StalledFifo Create(string path)
        {
            using Process mkfifo = Process.Start("mkfifo", new[] { path })!;
            mkfifo.WaitForExit();
            Assert.Equal(0, mkfifo.ExitCode);
            return new StalledFifo(path);
        }

        /// <summary>Opens the reading end (which waits for a writer) and enables the log, which opens the writing end.</summary>
        public void EnableLogOn(CommandOutput output)
        {
            _reader = Task.Run(() => new FileStream(_path, FileMode.Open, FileAccess.Read));
            output.EnableLog(_path);
            Assert.True(_reader.Wait(TimeSpan.FromSeconds(5)), "FIFO reader did not open");
            Assert.True(output.IsLogging);
        }

        /// <summary>Reads everything until the writer closes.</summary>
        public void Drain()
        {
            if (_reader is null || _drain is not null)
            {
                return;
            }

            FileStream reader = _reader.Result;
            _drain = Task.Run(() =>
            {
                reader.CopyTo(_captured);
                reader.Dispose();
            });
        }

        /// <summary>Drains, waits until every writer has closed the FIFO, and returns what was written.</summary>
        public string ReadAll()
        {
            Drain();
            Assert.True(_drain!.Wait(TimeSpan.FromSeconds(10)), "the FIFO writers did not close");
            return Encoding.UTF8.GetString(_captured.ToArray());
        }

        public void Dispose()
        {
            Drain();
            _drain?.Wait(TimeSpan.FromSeconds(10));
        }
    }
}
