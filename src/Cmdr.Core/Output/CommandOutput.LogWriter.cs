using System.Text;

namespace Cmdr.Core.Output;

public sealed partial class CommandOutput
{
    /// <summary>
    /// Writes one log session's lines to its file on a dedicated background thread, so no caller waits for log I/O. The writes
    /// are synchronous and block while the file system does (a stalled network share or pipe can block indefinitely), so they
    /// must not run on the thread pool, whose threads the output pumps and process callbacks need. State is guarded by
    /// the owning <see cref="CommandOutput"/>'s lock; members ending in <c>Locked</c> must be called with it held.
    /// </summary>
    /// <remarks>
    /// Writers of the same file take turns: a writer starts only after the file's running writer has finished (see
    /// <see cref="LogDestination.Running"/>), and seeks to the end of the file before each batch, so their lines never
    /// interleave or overwrite each other.
    /// </remarks>
    private sealed class LogWriter
    {
        private readonly CommandOutput _owner;
        private readonly FileStream _stream;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _wake = new(0, 1);
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private List<(long Number, string Text)> _pending = new();

        // Lines in _pending that came live (not replayed). Only these count against LogQueueCapacity.
        private int _pendingLive;
        private bool _closing;
        private bool _abandoned;
        private bool _sleeping;
        private bool _busy;
        private TaskCompletionSource? _idle;

        public LogWriter(CommandOutput owner, LogDestination destination, string path, FileStream stream, long firstLiveLine)
        {
            _owner = owner;
            Destination = destination;
            _stream = stream;
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Path = path;
            FirstLiveLine = firstLiveLine;
            NextQueuedLine = firstLiveLine;
        }

        /// <summary>Full path of the file.</summary>
        public string Path { get; }

        /// <summary>The state of the file this writer writes, which holds its flush progress and its turn.</summary>
        public LogDestination Destination { get; }

        /// <summary>Number of the first line given to this writer as it completed; earlier lines are replayed on start.</summary>
        public long FirstLiveLine { get; }

        /// <summary>Whether the writing thread has started.</summary>
        public bool Started { get; private set; }

        /// <summary>Whether the writer was abandoned after a failure or a stall; it may still be blocked in a write.</summary>
        public bool Abandoned => _abandoned;

        /// <summary>Whether the writer was closed and accepts no more lines.</summary>
        public bool Closing => _closing;

        /// <summary>One past the number of the last line given to the writer (live or replayed).</summary>
        public long NextQueuedLine { get; private set; }

        /// <summary>
        /// For a writer that waits behind an earlier writer of its file: the lines to replay, captured when logging was enabled
        /// (so a new run or eviction meanwhile cannot lose them), with the number of the first. Null otherwise.
        /// </summary>
        public (long First, IReadOnlyList<OutputLine> Lines)? DeferredReplay { get; set; }

        /// <summary>Completes when the writer has stopped and closed its file.</summary>
        public Task Completion => _completion.Task;

        /// <summary>Queues lines. Returns false, queueing nothing, when the queue is full: the writer is not keeping up.</summary>
        public bool EnqueueLocked(IReadOnlyList<OutputLine> lines, long firstNumber)
        {
            if (_closing)
            {
                return true;
            }

            if (_pendingLive + lines.Count > LogQueueCapacity)
            {
                return false;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                _pending.Add((firstNumber + i, lines[i].PlainText));
            }

            _pendingLive += lines.Count;
            NextQueuedLine = Math.Max(NextQueuedLine, firstNumber + lines.Count);
            WakeLocked();
            return true;
        }

        /// <summary>
        /// Accepts lines again after <see cref="CloseLocked"/>, for a writer that has not started yet: re-enabling its file
        /// reuses it instead of opening another. Returns false if it cannot be reused.
        /// </summary>
        public bool ReopenLocked()
        {
            if (Started || _abandoned)
            {
                return false;
            }

            _closing = false;
            return true;
        }

        /// <summary>
        /// Queues replayed lines ahead of the live ones. They do not count against the queue capacity, and nothing is added to
        /// that capacity for them: once they are written, the writer is held to the same limit as before.
        /// </summary>
        public void PrependLocked(IReadOnlyList<OutputLine> lines, long firstNumber)
        {
            _pending.InsertRange(0, lines.Select((line, i) => (firstNumber + i, line.PlainText)));
        }

        public void StartLocked()
        {
            Started = true;
            var thread = new Thread(Run) { IsBackground = true, Name = "cmdr log writer" };
            thread.Start();
        }

        /// <summary>Accepts no more lines; the writer finishes the queued ones, then closes the file.</summary>
        public void CloseLocked()
        {
            _closing = true;
            WakeLocked();
        }

        /// <summary>Drops the queued lines and stops the writer after any write in progress.</summary>
        public void AbandonLocked()
        {
            _abandoned = true;
            _closing = true;
            _pending.Clear();
            _pendingLive = 0;
            WakeLocked();
        }

        /// <summary>Completes when the queue is empty and the last batch has been flushed, or the writer has stopped.</summary>
        public Task WhenIdleLocked()
        {
            if (_completion.Task.IsCompleted || (Started && !_busy && _pending.Count == 0))
            {
                return Task.CompletedTask;
            }

            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _idle.Task;
        }

        private void WakeLocked()
        {
            if (_sleeping)
            {
                _sleeping = false;
                _wake.Release();
            }
        }

        private void Run()
        {
            string? failure = null;
            try
            {
                while (true)
                {
                    List<(long Number, string Text)>? batch = null;
                    lock (_owner._sync)
                    {
                        if (_abandoned)
                        {
                            break;
                        }

                        if (_pending.Count == 0)
                        {
                            SetIdleLocked();
                            if (_closing)
                            {
                                break;
                            }

                            _sleeping = true;
                        }
                        else
                        {
                            batch = _pending;
                            _pending = new List<(long Number, string Text)>();
                            _pendingLive = 0;
                            _busy = true;
                        }
                    }

                    if (batch is null)
                    {
                        _wake.Wait();
                        continue;
                    }

                    // Each batch ends with a flush, so nothing is left in the writer's buffer when seeking. A pipe or device
                    // cannot seek and does not need to.
                    if (_stream.CanSeek)
                    {
                        _stream.Seek(0, SeekOrigin.End);
                    }

                    foreach ((long _, string text) in batch)
                    {
                        _writer.Write(text);
                        _writer.Write(Environment.NewLine);
                    }

                    _writer.Flush();

                    lock (_owner._sync)
                    {
                        _owner.CommitLoggedLocked(this, batch[^1].Number + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                // Any failure disables the log: an exception escaping this thread would end the process.
                failure = ex.Message;
            }
            finally
            {
                Close();
                lock (_owner._sync)
                {
                    SetIdleLocked();
                    if (failure is not null)
                    {
                        _owner.DisableLogAfterFailureLocked(this, failure);
                    }

                    _owner.OnLogWriterFinishedLocked(this);
                }

                _completion.TrySetResult();
            }
        }

        private void SetIdleLocked()
        {
            _busy = false;
            _idle?.TrySetResult();
            _idle = null;
        }

        /// <summary>Closes the file quietly. After a failure the writer may still hold unflushed text; it is abandoned.</summary>
        private void Close()
        {
            try
            {
                _writer.Dispose();
            }
            catch (Exception ex) when (IsIoFailure(ex))
            {
                _stream.Dispose();
            }
        }
    }

    /// <summary>
    /// The log state of one file path: its flush progress and its writers. One is kept per path used, for the life of the owning
    /// <see cref="CommandOutput"/> (a small object each), so a file keeps its progress while logging goes to other files.
    /// Guarded by the owner's lock.
    /// </summary>
    private sealed class LogDestination
    {
        /// <summary>
        /// Lines numbered below this have been flushed to this file. Advanced by its writer after each successful flush only.
        /// </summary>
        public long NextUnloggedLine { get; set; }

        /// <summary>The writer that has started on this file and not finished, or null.</summary>
        public LogWriter? Running { get; set; }

        /// <summary>The writer waiting for <see cref="Running"/> to finish, or null. Never more than one.</summary>
        public LogWriter? Waiting { get; set; }

        /// <summary>Whether a writer was abandoned and may still be blocked in a write, so that the file takes no new log.</summary>
        public bool IsBlocked => Running?.Abandoned == true || Waiting?.Abandoned == true;
    }
}
