using System.Diagnostics;
using System.Globalization;
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

            // Far more than the pipe holds, so the writer blocks; then more lines than the queue takes.
            string line = new string('x', 200) + "\n";
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < CommandOutput.LogQueueCapacity + 2000; i++)
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
    public async Task ForceKillAndSnapshotsDoNotWaitForAStalledLog()
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

            Task kill = Task.Run(p.ForceKill);
            Assert.Same(kill, await Task.WhenAny(kill, Task.Delay(Prompt)));
            Task<ProcessSnapshot> snapshot = Task.Run(() => p.Snapshot);
            Assert.Same(snapshot, await Task.WhenAny(snapshot, Task.Delay(Prompt)));
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

            Assert.Equal(Lines(StartMarker(Start), "a", "b", notice, "c"), File.ReadAllText(path));
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
                reader.CopyTo(Stream.Null);
                reader.Dispose();
            });
        }

        public void Dispose()
        {
            Drain();
            _drain?.Wait(TimeSpan.FromSeconds(10));
        }
    }
}
