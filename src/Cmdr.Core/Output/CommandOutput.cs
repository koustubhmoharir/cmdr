using System.Globalization;
using Cmdr.Core.Config;
using Shared.Core;

namespace Cmdr.Core.Output;

/// <summary>
/// The output pipeline for one command: parses stdout and stderr, feeds the in-memory <see cref="OutputBuffer"/>,
/// and optionally writes completed lines to a plain-text log file.
/// </summary>
/// <remarks>
/// All methods are thread-safe. Stdout and stderr may be written from different threads; work is serialized
/// so buffer order and log order match. Log writes happen on a background writer, never on the caller's thread, so a stalled
/// log file does not block output or callers (see <see cref="EnableLog"/>).
/// </remarks>
public sealed partial class CommandOutput : IDisposable
{
    /// <summary>
    /// How many lines may wait for the log writer beyond those replayed when logging starts. When more are waiting, the
    /// writer is considered stalled and logging is disabled.
    /// </summary>
    internal const int LogQueueCapacity = 10_000;

    private readonly object _sync = new();
    private readonly object _notifySync = new();
    private readonly OutputBuffer _buffer;
    private readonly List<OutputLine> _completed = new();

    private AnsiLineParser _stdOutParser = new(OutputSource.StdOut);
    private AnsiLineParser _stdErrParser = new(OutputSource.StdErr);
    private long? _runFirstLine;
    private LogWriter? _log;

    // One per log path used, kept for the life of this object: each file keeps its own flush progress and writers.
    private readonly Dictionary<string, LogDestination> _destinations = new(PathComparer);
    private long _run;
    private bool _staleOutputNoticeWritten;
    private bool _disposed;

    /// <summary>The system line written the first time output from an earlier run is dropped during a run.</summary>
    public const string StaleOutputNotice = "discarding output from processes left over by the previous run";

    /// <summary>Creates the pipeline with a buffer retaining up to <paramref name="bufferLines"/> completed lines.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bufferLines"/> is not positive.</exception>
    public CommandOutput(int bufferLines)
    {
        _buffer = new OutputBuffer(bufferLines);
    }

    /// <summary>The in-memory output buffer displayed by the UI.</summary>
    public OutputBuffer Buffer => _buffer;

    /// <summary>
    /// Raised with the run and the lines that a call to <see cref="WriteStdOut(long, ReadOnlySpan{char})"/> or
    /// <see cref="WriteStdErr(long, ReadOnlySpan{char})"/> (or their current-run forms) completed: process output only, not
    /// system lines and not partial lines. Lines are already in <see cref="Buffer"/> when this is raised. The run is the value
    /// <see cref="BeginRun"/> returned; by the time a handler runs, a newer run may have begun.
    /// </summary>
    /// <remarks>
    /// Raised on the writing thread, after the internal lock is released, so a handler may call back into this object's
    /// read members. Notifications are serialized and follow buffer order, also across stdout and stderr. A handler
    /// must not write output itself (it would be notified re-entrantly) and must not block on another thread that writes output.
    /// </remarks>
    public event Action<long, IReadOnlyList<OutputLine>>? ProcessLinesCompleted;

    /// <summary>Whether output is currently being written to a log file.</summary>
    public bool IsLogging
    {
        get
        {
            lock (_sync)
            {
                return _log is not null;
            }
        }
    }

    /// <summary>Full path of the log file being written, or null when not logging.</summary>
    public string? LogPath
    {
        get
        {
            lock (_sync)
            {
                return _log?.Path;
            }
        }
    }

    /// <summary>
    /// Starts a new run: clears the in-memory buffer (output of previous runs; the log file is not affected),
    /// resets both parsers, discards any partial lines, and appends a
    /// <c>=== started yyyy-MM-dd HH:mm:ss ===</c> marker (local time). From now on, process output written for an earlier
    /// run is dropped.
    /// </summary>
    /// <returns>The identity of the new run, to pass to <see cref="WriteStdOut(long, ReadOnlySpan{char})"/> and
    /// <see cref="WriteStdErr(long, ReadOnlySpan{char})"/>.</returns>
    public long BeginRun(DateTimeOffset startedAt)
    {
        lock (_sync)
        {
            _run++;
            _staleOutputNoticeWritten = false;
            _stdOutParser = new AnsiLineParser(OutputSource.StdOut);
            _stdErrParser = new AnsiLineParser(OutputSource.StdErr);

            // Clear also drops partial lines. Every line already appended to the buffer has been queued for the log
            // with its text, so clearing loses nothing from the log.
            _buffer.Clear();

            _runFirstLine = _buffer.NextLineNumber;
            string stamp = startedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            AppendSystemLocked($"=== started {stamp} ===");
            return _run;
        }
    }

    /// <summary>Writes a chunk of standard output for the current run.</summary>
    public void WriteStdOut(ReadOnlySpan<char> chunk) => WriteProcessChunk(null, OutputSource.StdOut, chunk);

    /// <summary>
    /// Writes a chunk of standard output for <paramref name="run"/>. The chunk is dropped if a newer run has begun (the first
    /// such chunk in a run adds <see cref="StaleOutputNotice"/>) or this object is disposed.
    /// </summary>
    public void WriteStdOut(long run, ReadOnlySpan<char> chunk) => WriteProcessChunk(run, OutputSource.StdOut, chunk);

    /// <summary>Writes a chunk of standard error for the current run.</summary>
    public void WriteStdErr(ReadOnlySpan<char> chunk) => WriteProcessChunk(null, OutputSource.StdErr, chunk);

    /// <summary>Writes a chunk of standard error for <paramref name="run"/>; see <see cref="WriteStdOut(long, ReadOnlySpan{char})"/>.</summary>
    public void WriteStdErr(long run, ReadOnlySpan<char> chunk) => WriteProcessChunk(run, OutputSource.StdErr, chunk);

    /// <summary>Appends a system line (or several, if <paramref name="message"/> contains newlines).</summary>
    public void WriteSystem(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_sync)
        {
            AppendSystemLocked(message);
        }
    }

    /// <summary>
    /// Appends a system line for <paramref name="run"/> only while it is still the current run; otherwise the line is dropped
    /// (unlike <see cref="StaleOutputNotice"/>, no notice is added). Used by failures reported after a run may have ended.
    /// </summary>
    internal void WriteSystem(long run, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_sync)
        {
            if (IsCurrentRunLocked(run))
            {
                AppendSystemLocked(message);
            }
        }
    }

    /// <summary>
    /// Ends the run: flushes both parsers (stdout first), clears partial lines, and appends
    /// <c>=== {summary} ===</c>.
    /// </summary>
    public void EndRun(string summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        lock (_sync)
        {
            // Each stream's flushed line replaces its partial in one buffer step, so no reader sees both.
            _completed.Clear();
            _stdOutParser.Flush(_completed);
            AppendLinesLocked(_completed, OutputSource.StdOut, partial: null);

            _completed.Clear();
            _stdErrParser.Flush(_completed);
            AppendLinesLocked(_completed, OutputSource.StdErr, partial: null);
            _completed.Clear();

            AppendSystemLocked($"=== {summary} ===");
        }
    }

    /// <summary>
    /// Starts writing completed lines to <paramref name="path"/> (appending). If already logging to the same
    /// full path this does nothing; if logging elsewhere, the old log is closed first (its writer finishes the lines already
    /// given to it). The retained lines of the current run that have not reached this file yet are written first, then new
    /// lines as they complete.
    /// </summary>
    /// <remarks>
    /// <para>The file is opened on the calling thread, so an open failure is reported immediately. Writing happens on a
    /// background writer.</para>
    /// <para>On an open or write failure, logging is disabled and a <c>log disabled: ...</c> system line is added to the buffer
    /// (not to the log). If more than <see cref="LogQueueCapacity"/> lines wait for a writer that is not making progress, it is
    /// abandoned the same way with <c>log disabled: log writer stalled</c>.</para>
    /// <para>Each file keeps its own progress. Re-enabling a file after a failure, or after logging went to other files, writes
    /// the retained lines that never reached that file, each once. Lines of a batch whose write or flush failed count as not
    /// written, so some of them may appear twice in the file.</para>
    /// <para>Writers of one file take turns. A file has at most one running and one waiting writer: re-enabling a file whose
    /// writer is still waiting for its turn reuses that writer (giving it the retained lines written while logging was off),
    /// and re-enabling a file whose earlier writer was abandoned and is still blocked is refused with
    /// <c>log not enabled: an earlier write to this file is still blocked</c>. A waiting writer's replay is taken when logging
    /// is enabled, so a new run or eviction before its turn does not lose those lines.</para>
    /// <para>This method does not throw for I/O errors.</para>
    /// <para>On Linux, when cmdr runs as root, a folder or log file that this call creates is given to the user who ran sudo, or to the
    /// parent folder's owner without sudo. Existing items are not changed.</para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or invalid.</exception>
    public void EnableLog(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string fullPath = Path.GetFullPath(path);

        lock (_sync)
        {
            if (DecideEnableLocked(fullPath) != EnableStep.NeedsWriter)
            {
                return;
            }
        }

        // Opened outside the lock: opening can stall too (for example on a network drive), and output keeps flowing meanwhile.
        FileStream stream;
        try
        {
            BeforeLogOpenForTesting?.Invoke();
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                UnixOwnership.CreateDirectory(directory);
            }

            bool existed = File.Exists(fullPath);

            // Write sharing allows an earlier writer of the same file to finish while this one waits for its turn.
            stream = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            if (!existed && !string.IsNullOrEmpty(directory))
            {
                UnixOwnership.GiveNewFileAway(stream.SafeFileHandle, directory); // best effort, only when root
            }
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            lock (_sync)
            {
                CloseLogLocked();
                AppendNoticeLocked($"log disabled: {ex.Message}");
            }

            return;
        }

        // Opening can take a while, so the state may have changed: the same rules are applied again, and a stream that is no
        // longer needed is closed outside the lock, since closing a file can flush and block.
        FileStream? redundant = null;
        lock (_sync)
        {
            if (DecideEnableLocked(fullPath) == EnableStep.NeedsWriter)
            {
                StartNewWriterLocked(fullPath, stream);
            }
            else
            {
                redundant = stream;
            }
        }

        redundant?.Dispose();
    }

    /// <summary>For tests: called by <see cref="EnableLog"/> after its first checks, just before it opens the file outside the lock.</summary>
    internal Action? BeforeLogOpenForTesting { get; set; }

    /// <summary>What <see cref="EnableLog"/> does for one path, as decided by <see cref="DecideEnableLocked"/>.</summary>
    private enum EnableStep
    {
        /// <summary>Nothing more to do: disposed, already logging to the file, or the file's waiting writer took the new log.</summary>
        Done,

        /// <summary>Refused: an earlier writer of the file is abandoned and may still be blocked. A notice was added.</summary>
        Refused,

        /// <summary>A new writer is needed, on a stream opened for the file.</summary>
        NeedsWriter,
    }

    /// <summary>
    /// The rules of <see cref="EnableLog"/> for <paramref name="fullPath"/>, applied under the lock. EnableLog applies them before
    /// the file is opened and again after, so the two checks cannot diverge. Caller holds the lock.
    /// </summary>
    private EnableStep DecideEnableLocked(string fullPath)
    {
        if (_disposed || IsLoggingToLocked(fullPath))
        {
            return EnableStep.Done;
        }

        LogDestination destination = DestinationLocked(fullPath);

        // At most one running and one waiting writer per file, however often logging is toggled: a writer abandoned as stalled
        // may stay blocked in a write indefinitely, and every writer queued behind it would hold a file and its lines until then.
        if (destination.IsBlocked)
        {
            AppendNoticeLocked("log not enabled: an earlier write to this file is still blocked");
            return EnableStep.Refused;
        }

        if (destination.Waiting is { } waiting && waiting.ReopenLocked())
        {
            ReuseWaitingWriterLocked(waiting);
            return EnableStep.Done;
        }

        return EnableStep.NeedsWriter;
    }

    /// <summary>
    /// Makes <paramref name="fullPath"/> the current log, with a new writer on <paramref name="stream"/>. The writer starts at once
    /// if no writer of the file is running; otherwise it waits for its turn. Caller holds the lock and has checked the file with
    /// <see cref="DecideEnableLocked"/>, which leaves no waiting writer in that case.
    /// </summary>
    private void StartNewWriterLocked(string fullPath, FileStream stream)
    {
        CloseLogLocked();
        LogDestination destination = DestinationLocked(fullPath);
        var writer = new LogWriter(this, destination, fullPath, stream, _buffer.NextLineNumber);
        _log = writer;
        if (destination.Running is null)
        {
            destination.Running = writer;
            StartWriterLocked(writer);
        }
        else
        {
            // Captured now: by the time the writer starts, a new run or eviction may have dropped these lines.
            destination.Waiting = writer;
            writer.DeferredReplay = ReplayLocked(writer);
        }
    }

    /// <summary>
    /// Makes a writer that is still waiting for its turn the current log again, giving it the retained lines written while
    /// logging was off. Its queued lines and captured replay are kept. Caller holds the lock and has reopened it.
    /// </summary>
    private void ReuseWaitingWriterLocked(LogWriter writer)
    {
        CloseLogLocked();
        long from = Math.Max(writer.NextQueuedLine, _buffer.FirstLineNumber);
        IReadOnlyList<OutputLine> missed = _buffer.GetCompletedLinesFrom(from);
        _log = writer;
        AppendToLogLocked(missed, from);
    }

    /// <summary>Writers that have not finished yet (running, waiting for their turn, or blocked). For tests.</summary>
    internal int UnfinishedLogWriterCount
    {
        get
        {
            lock (_sync)
            {
                return UnfinishedWritersLocked().Count;
            }
        }
    }

    /// <summary>The running and the waiting writer of each file that has one. Caller holds the lock.</summary>
    private List<LogWriter> UnfinishedWritersLocked()
    {
        var writers = new List<LogWriter>();
        foreach (LogDestination destination in _destinations.Values)
        {
            if (destination.Running is { } running)
            {
                writers.Add(running);
            }

            if (destination.Waiting is { } waiting)
            {
                writers.Add(waiting);
            }
        }

        return writers;
    }

    /// <summary>The state of <paramref name="fullPath"/>, created on first use. Caller holds the lock.</summary>
    private LogDestination DestinationLocked(string fullPath)
    {
        if (!_destinations.TryGetValue(fullPath, out LogDestination? destination))
        {
            destination = new LogDestination();
            _destinations.Add(fullPath, destination);
        }

        return destination;
    }

    /// <summary>
    /// Stops logging. Does nothing if not logging. The writer finishes the lines already given to it in the background, then
    /// closes the file; see <see cref="WaitForLogWrites"/>.
    /// </summary>
    public void DisableLog()
    {
        lock (_sync)
        {
            CloseLogLocked();
        }
    }

    /// <summary>
    /// Waits until every line given to a log writer so far has been written and flushed, or its writer has failed or been
    /// abandoned, and closed writers have closed their files. Returns false if that did not happen within <paramref name="timeout"/>.
    /// </summary>
    public bool WaitForLogWrites(TimeSpan timeout)
    {
        Task[] pending;
        lock (_sync)
        {
            pending = UnfinishedWritersLocked().Select(w => ReferenceEquals(w, _log) ? w.WhenIdleLocked() : w.Completion).ToArray();
        }

        try
        {
            return Task.WaitAll(pending, timeout);
        }
        catch (AggregateException)
        {
            // Writer tasks do not fault; their failures are reported as notices.
            return true;
        }
    }

    /// <summary>
    /// Stops logging (the writer finishes in the background, as with <see cref="DisableLog"/>). Process output written
    /// afterwards is dropped.
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            CloseLogLocked();
        }
    }

    /// <summary>
    /// Appends a process output chunk, then notifies <see cref="ProcessLinesCompleted"/> with the lines it completed.
    /// </summary>
    /// <remarks>
    /// Lock order: <c>_notifySync</c>, then <c>_sync</c>. <c>_notifySync</c> is taken only here, so an owner of this output
    /// must never call these methods while holding its own lock. Callers that write process output must hold no lock.
    /// The handler runs outside <c>_sync</c>, so a handler may take an owner's lock as long as that owner does not write
    /// process output while holding it.
    /// </remarks>
    private void WriteProcessChunk(long? run, OutputSource source, ReadOnlySpan<char> chunk)
    {
        lock (_notifySync)
        {
            IReadOnlyList<OutputLine> completed;
            long current;
            lock (_sync)
            {
                // Checked under the same lock as BeginRun, so a chunk of an earlier run can never reach the new run's parser.
                if (_disposed)
                {
                    return;
                }

                if (run is long r && !IsCurrentRunLocked(r))
                {
                    if (!_staleOutputNoticeWritten && r < _run)
                    {
                        _staleOutputNoticeWritten = true;
                        AppendSystemLocked(StaleOutputNotice);
                    }

                    return;
                }

                current = _run;
                completed = WriteChunkLocked(source == OutputSource.StdOut ? _stdOutParser : _stdErrParser, source, chunk);
            }

            if (completed.Count > 0)
            {
                RaiseProcessLinesCompleted(current, completed);
            }
        }
    }

    /// <summary>
    /// Invokes each subscriber of <see cref="ProcessLinesCompleted"/> separately. An exception from one subscriber is reported as
    /// a <c>cmdr error:</c> system line of the current run and does not stop the others or reach the writing thread. Caller holds
    /// <c>_notifySync</c> and no <c>_sync</c>.
    /// </summary>
    private void RaiseProcessLinesCompleted(long run, IReadOnlyList<OutputLine> lines)
    {
        Action<long, IReadOnlyList<OutputLine>>? handlers = ProcessLinesCompleted;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<long, IReadOnlyList<OutputLine>> handler in handlers.GetInvocationList().Cast<Action<long, IReadOnlyList<OutputLine>>>())
        {
            try
            {
                handler(run, lines);
            }
            catch (Exception ex)
            {
                WriteSystem($"cmdr error: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Parses and appends one chunk. Returns the lines it completed (a copy, safe to use after the lock is released).</summary>
    private IReadOnlyList<OutputLine> WriteChunkLocked(AnsiLineParser parser, OutputSource source, ReadOnlySpan<char> chunk)
    {
        _completed.Clear();
        parser.Feed(chunk, _completed);
        IReadOnlyList<OutputLine> completed = _completed.Count == 0 ? Array.Empty<OutputLine>() : _completed.ToArray();
        _completed.Clear();

        // The completed lines and the stream's new partial enter the buffer in one update, so a reader never sees both the
        // completed line and the old partial.
        AppendLinesLocked(completed, source, parser.Partial);
        return completed;
    }

    /// <summary>True when <paramref name="run"/> is the run begun by the latest <see cref="BeginRun"/>.</summary>
    private bool IsCurrentRunLocked(long run) => run == _run;

    private void AppendSystemLocked(string message)
    {
        var lines = new List<OutputLine>();
        string[] segments = message.Split('\n');
        int count = segments.Length;

        // "a\n" is one line, not two: a trailing newline does not start an extra empty line.
        if (count > 1 && segments[count - 1].Length == 0)
        {
            count--;
        }

        for (int i = 0; i < count; i++)
        {
            string text = segments[i].TrimEnd('\r');
            StyledSpan[] spans = text.Length == 0
                ? Array.Empty<StyledSpan>()
                : new[] { new StyledSpan(text, SpanStyle.Default) };
            lines.Add(new OutputLine(OutputSource.System, spans));
        }

        AppendLinesLocked(lines);
    }

    /// <summary>
    /// Adds completed lines to the buffer, then to the log if logging. With <paramref name="source"/>, that stream's partial is
    /// set in the same buffer update (see <see cref="OutputBuffer.AppendAndSetPartial"/>).
    /// </summary>
    private void AppendLinesLocked(IReadOnlyList<OutputLine> lines, OutputSource? source = null, OutputLine? partial = null)
    {
        long first = _buffer.NextLineNumber;
        if (source is { } stream)
        {
            _buffer.AppendAndSetPartial(lines, stream, partial);
        }
        else
        {
            _buffer.Append(lines);
        }

        if (lines.Count > 0)
        {
            AppendToLogLocked(lines, first);
        }
    }

    /// <summary>Gives lines to the log writer, if logging; the first line has absolute number <paramref name="firstNumber"/>.</summary>
    private void AppendToLogLocked(IReadOnlyList<OutputLine> lines, long firstNumber)
    {
        if (_log is not null && !_log.EnqueueLocked(lines, firstNumber))
        {
            DisableLogAfterFailureLocked(_log, "log writer stalled");
        }
    }

    /// <summary>Records that <paramref name="writer"/> flushed every line numbered below <paramref name="next"/>.</summary>
    private void CommitLoggedLocked(LogWriter writer, long next)
    {
        LogDestination destination = writer.Destination;
        destination.NextUnloggedLine = Math.Max(destination.NextUnloggedLine, next);
    }

    /// <summary>
    /// Abandons <paramref name="writer"/> after a failure and, if it is the current log, disables logging with a notice.
    /// A writer that was already closed fails silently, as closing a failed file always has.
    /// </summary>
    private void DisableLogAfterFailureLocked(LogWriter writer, string message)
    {
        writer.AbandonLocked();
        if (ReferenceEquals(writer, _log))
        {
            _log = null;
            AppendNoticeLocked($"log disabled: {message}");
        }
    }

    /// <summary>Adds a system line to the buffer only, not to the log.</summary>
    private void AppendNoticeLocked(string message) =>
        _buffer.Append(new[] { new OutputLine(OutputSource.System, new[] { new StyledSpan(message, SpanStyle.Default) }) });

    /// <summary>Closes the current log, if any; its writer finishes the lines already given to it.</summary>
    private void CloseLogLocked()
    {
        _log?.CloseLocked();
        _log = null;
    }

    private bool IsLoggingToLocked(string fullPath) => _log is not null && PathEquals(fullPath, _log.Path);

    /// <summary>
    /// Starts a writer that has its turn on its file, first giving it the current run's retained lines that the file is missing
    /// (see <see cref="ReplayLocked"/>). A writer that had to wait uses the lines captured when logging was enabled, less those
    /// the file has been flushed since.
    /// </summary>
    private void StartWriterLocked(LogWriter writer)
    {
        (long First, IReadOnlyList<OutputLine> Lines)? replay = writer.DeferredReplay ?? ReplayLocked(writer);
        writer.DeferredReplay = null;
        if (replay is { } r)
        {
            long flushed = writer.Destination.NextUnloggedLine;
            int skip = (int)Math.Clamp(flushed - r.First, 0, r.Lines.Count);
            if (skip < r.Lines.Count)
            {
                writer.PrependLocked(r.Lines.Skip(skip).ToList(), r.First + skip);
            }
        }

        writer.StartLocked();
    }

    /// <summary>
    /// The current run's retained lines that the writer's file is missing: from the run's first line, or from the first line not
    /// yet flushed to this file, up to the first line it received live. Null when there are none.
    /// </summary>
    private (long First, IReadOnlyList<OutputLine> Lines)? ReplayLocked(LogWriter writer)
    {
        if (_runFirstLine is not long runFirst)
        {
            return null;
        }

        long flushed = writer.Destination.NextUnloggedLine;
        long from = Math.Max(Math.Max(runFirst, flushed), _buffer.FirstLineNumber);
        if (from >= writer.FirstLiveLine)
        {
            return null;
        }

        IReadOnlyList<OutputLine> retained = _buffer.GetCompletedLinesFrom(from);
        int count = (int)Math.Min(retained.Count, writer.FirstLiveLine - from);
        return count == 0 ? null : (from, retained.Take(count).ToList());
    }

    /// <summary>Frees the file for its next writer when <paramref name="writer"/> finishes, and starts that writer if it is waiting.</summary>
    private void OnLogWriterFinishedLocked(LogWriter writer)
    {
        LogDestination destination = writer.Destination;
        destination.Running = null;
        if (destination.Waiting is { } next)
        {
            destination.Waiting = null;
            destination.Running = next;
            StartWriterLocked(next);
        }
    }

    private static bool IsIoFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException;

    /// <summary>How log paths are compared: as Windows file names (case-insensitive) or exactly.</summary>
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool PathEquals(string a, string? b) => b is not null && PathComparer.Equals(a, b);
}
