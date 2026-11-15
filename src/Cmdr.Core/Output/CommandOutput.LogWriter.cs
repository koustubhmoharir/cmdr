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
    /// Writers of the same file take turns: a writer starts only after every earlier writer of that file has finished
    /// (see <see cref="HasEarlierWriterLocked"/>), and seeks to the end of the file before each batch, so their lines never
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
        private int _limit = LogQueueCapacity;
        private bool _closing;
        private bool _abandoned;
        private bool _sleeping;
        private bool _busy;
        private TaskCompletionSource? _idle;

        public LogWriter(CommandOutput owner, string path, FileStream stream, long firstLiveLine)
        {
            _owner = owner;
            _stream = stream;
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Path = path;
            FirstLiveLine = firstLiveLine;
        }

        /// <summary>Full path of the file.</summary>
        public string Path { get; }

        /// <summary>Number of the first line given to this writer as it completed; earlier lines are replayed on start.</summary>
        public long FirstLiveLine { get; }

        /// <summary>Whether the writing task has started.</summary>
        public bool Started { get; private set; }

        /// <summary>Completes when the writer has stopped and closed its file.</summary>
        public Task Completion => _completion.Task;

        /// <summary>Queues lines. Returns false, queueing nothing, when the queue is full: the writer is not keeping up.</summary>
        public bool EnqueueLocked(IReadOnlyList<OutputLine> lines, long firstNumber)
        {
            if (_closing)
            {
                return true;
            }

            if (_pending.Count + lines.Count > _limit)
            {
                return false;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                _pending.Add((firstNumber + i, lines[i].PlainText));
            }

            WakeLocked();
            return true;
        }

        /// <summary>Queues replayed lines ahead of the live ones. They do not count against the queue capacity.</summary>
        public void PrependLocked(IReadOnlyList<OutputLine> lines, long firstNumber)
        {
            _pending.InsertRange(0, lines.Select((line, i) => (firstNumber + i, line.PlainText)));
            _limit += lines.Count;
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
}
