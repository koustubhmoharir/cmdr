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

    /// <summary>
    /// Review 04-01: a file's second writer waits behind its blocked first writer. Logging to another file meanwhile must not move
    /// the first file's progress, or the line that never reached the file is lost when its waiting writer starts.
    /// </summary>
    [Fact]
    public void WaitingWriterOfAFileWritesTheLineItMissedWhileAnotherFileWasLogged()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string blockedPath = dir.File("blocked-a.log");
        using var fifo = StalledFifo.Create(blockedPath);
        string otherPath = dir.File("regular-b.log");
        var output = new CommandOutput(100_000);
        try
        {
            output.BeginRun(Start);
            fifo.EnableLogOn(output); // A's first writer: blocks once the pipe is full
            output.WriteStdOut(string.Concat(Enumerable.Repeat(new string('x', 200) + "\n", 2000)));
            output.DisableLog();

            output.WriteStdOut("MISSED-A\n"); // not given to A: logging is off
            output.EnableLog(blockedPath);     // A's second writer: waits behind the blocked first one
            output.DisableLog();

            // B is logged while A's writers wait. Each file keeps its own progress, so A's progress stays where A's writers left it.
            output.EnableLog(otherPath);
            output.WriteStdOut("B-LIVE\n");
            WaitUntil(() => File.Exists(otherPath) && File.ReadAllText(otherPath).Contains("B-LIVE\n", StringComparison.Ordinal),
                "B to flush B-LIVE");
            output.DisableLog();

            // A again: reuses the waiting writer. A was never given B-LIVE, so under the per-file rule it gets B-LIVE too, after
            // MISSED-A. Nothing A already has is written again.
            output.EnableLog(blockedPath);
            Assert.True(output.IsLogging);
            output.DisableLog();

            string text = fifo.ReadAll(); // lets A's first writer finish; its waiting writer then starts and finishes
            Assert.True(output.WaitForLogWrites(TimeSpan.FromSeconds(10)));

            Assert.Equal(1, CountOf(text, "MISSED-A\n"));
            Assert.Equal(1, CountOf(text, "B-LIVE\n"));
            Assert.Equal(1, CountOf(text, StartMarker(Start) + "\n"));
            Assert.Equal(2000, CountOf(text, new string('x', 200) + "\n"));
            Assert.True(text.IndexOf("MISSED-A", StringComparison.Ordinal) < text.IndexOf("B-LIVE", StringComparison.Ordinal));
        }
        finally
        {
            output.Dispose();
        }
    }

    /// <summary>
    /// Review 04-02: EnableLog opens its file outside the lock. Concurrent calls for one file, while its first writer is blocked,
    /// may all pass the checks before any of them has a writer; the file must still have at most one running and one waiting writer.
    /// </summary>
    /// <remarks>A barrier in the open hook holds every call until all three have passed the first checks, so all open together.</remarks>
    [Fact]
    public void ConcurrentEnableLogOfOneFileKeepsOneRunningAndOneWaitingWriter()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string path = dir.File("race.log");
        using var fifo = StalledFifo.Create(path);
        var output = new CommandOutput(100_000);
        var callers = new List<Thread>();

        // Not disposed: a caller may still be waiting on them when the finally block runs after a failed assertion.
        var go = new ManualResetEventSlim(false);
        var allChecked = new Barrier(3);
        try
        {
            output.BeginRun(Start);
            fifo.EnableLogOn(output);
            output.WriteStdOut(string.Concat(Enumerable.Repeat(new string('x', 200) + "\n", 2000))); // the first writer blocks
            output.DisableLog();
            output.BeforeLogOpenForTesting = () => allChecked.SignalAndWait(TimeSpan.FromSeconds(10));

            for (int i = 0; i < 3; i++)
            {
                var caller = new Thread(() =>
                {
                    go.Wait();
                    output.EnableLog(path);
                }) { IsBackground = true };
                callers.Add(caller);
                caller.Start();
            }

            go.Set();
            foreach (Thread caller in callers)
            {
                Assert.True(caller.Join(TimeSpan.FromSeconds(10)), "EnableLog did not return");
            }

            Assert.True(output.UnfinishedLogWriterCount <= 2, $"{output.UnfinishedLogWriterCount} writers for one file");

            // A writer that replaced another in the file's single waiting slot would leave that one untracked with its file open,
            // so the open handles are counted too: the test's reading end, the running writer and the waiting one.
            int handles = OpenHandlesTo(path);
            Assert.True(handles <= 3, $"{handles} open handles to the log file");

            output.WriteStdOut("after-race\n");
            output.DisableLog();
            string text = fifo.ReadAll();
            Assert.True(output.WaitForLogWrites(TimeSpan.FromSeconds(10)));
            Assert.Equal(1, CountOf(text, "after-race\n"));
        }
        finally
        {
            foreach (Thread caller in callers)
            {
                caller.Join(TimeSpan.FromSeconds(10));
            }

            output.Dispose();
        }
    }

    /// <summary>
    /// Review 04-03: lines replayed when logging starts must not raise the queue capacity for live lines. With the writer gated,
    /// more live lines than the capacity stall it, however many lines were replayed.
    /// </summary>
    [Fact]
    public void ReplayedLinesDoNotRaiseTheLiveQueueCapacity()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        using var fifo = StalledFifo.Create(dir.File("replay.log"));
        var output = new CommandOutput(CommandOutput.LogQueueCapacity * 2);
        try
        {
            // 12,000 lines (more than the queue takes) are retained before logging starts, so they are all replayed.
            List<string> retained = Enumerable.Range(0, 12_000).Select(i => $"line-{i:D6}").ToList();
            output.BeginRun(Start);
            output.WriteStdOut(string.Concat(retained.Select(l => l + "\n")));

            string replay = Lines(new[] { StartMarker(Start) }.Concat(retained).ToArray());
            fifo.EnableLogOn(output);

            // The sink takes exactly the replay and then nothing; the writer is then idle.
            fifo.ReadExactly(Encoding.UTF8.GetByteCount(replay));
            Assert.True(output.WaitForLogWrites(TimeSpan.FromSeconds(10)), "the replay did not finish");
            Assert.True(output.IsLogging);

            // Gate the writer: 200 KB is more than the pipe holds, so the write blocks until the sink is read again.
            output.WriteStdOut(string.Concat(Enumerable.Repeat(new string('x', 200) + "\n", 1000)));
            Assert.False(output.WaitForLogWrites(TimeSpan.FromMilliseconds(200)));

            // One more live line than the queue takes. If the replayed lines still counted, the limit would be 22,000 and this would fit.
            output.WriteStdOut(string.Concat(Enumerable.Repeat("live\n", CommandOutput.LogQueueCapacity + 1)));

            Assert.False(output.IsLogging);
            Assert.Contains("log disabled: log writer stalled", BufferTexts(output.Buffer));

            // The abandoned writer finishes once the sink is read; the replay is what the file starts with.
            string text = fifo.ReadAll();
            Assert.True(output.WaitForLogWrites(TimeSpan.FromSeconds(10)));
            Assert.StartsWith(replay, text, StringComparison.Ordinal);
        }
        finally
        {
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

        /// <summary>Reads exactly <paramref name="count"/> bytes and no more, so the sink can be held at a known point.</summary>
        public void ReadExactly(int count)
        {
            FileStream reader = _reader!.Result;
            var buffer = new byte[count];
            Task read = Task.Run(() => reader.ReadExactly(buffer));
            Assert.True(read.Wait(TimeSpan.FromSeconds(10)), "the FIFO did not deliver the expected bytes");
            _captured.Write(buffer);
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

    /// <summary>The number of this process's open file descriptors that point at <paramref name="path"/> (Linux).</summary>
    private static int OpenHandlesTo(string path)
    {
        int count = 0;
        foreach (string fd in Directory.EnumerateFileSystemEntries("/proc/self/fd"))
        {
            try
            {
                if (new FileInfo(fd).LinkTarget == path)
                {
                    count++;
                }
            }
            catch (IOException)
            {
                // Closed while enumerating.
            }
        }

        return count;
    }
}
