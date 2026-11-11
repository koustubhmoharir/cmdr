using System.Globalization;
using System.Text;

namespace Cmdr.Core.Output;

/// <summary>
/// The output pipeline for one command: parses stdout and stderr, feeds the in-memory <see cref="OutputBuffer"/>,
/// and optionally writes completed lines to a plain-text log file.
/// </summary>
/// <remarks>
/// All methods are thread-safe. Stdout and stderr may be written from different threads; work is serialized
/// so buffer order and log order match.
/// </remarks>
public sealed class CommandOutput : IDisposable
{
    private readonly object _sync = new();
    private readonly OutputBuffer _buffer;
    private readonly List<OutputLine> _completed = new();

    private AnsiLineParser _stdOutParser = new(OutputSource.StdOut);
    private AnsiLineParser _stdErrParser = new(OutputSource.StdErr);
    private long? _runFirstLine;
    private StreamWriter? _log;
    private string? _logPath;
    private string? _lastLoggedPath;
    private long _nextUnloggedLine;

    /// <summary>Creates the pipeline with a buffer retaining up to <paramref name="bufferLines"/> completed lines.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bufferLines"/> is not positive.</exception>
    public CommandOutput(int bufferLines)
    {
        _buffer = new OutputBuffer(bufferLines);
    }

    /// <summary>The in-memory output buffer displayed by the UI.</summary>
    public OutputBuffer Buffer => _buffer;

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
                return _logPath;
            }
        }
    }

    /// <summary>
    /// Starts a new run: resets both parsers, discards any partial lines, and appends a
    /// <c>=== started yyyy-MM-dd HH:mm:ss ===</c> marker (local time).
    /// </summary>
    public void BeginRun(DateTimeOffset startedAt)
    {
        lock (_sync)
        {
            _stdOutParser = new AnsiLineParser(OutputSource.StdOut);
            _stdErrParser = new AnsiLineParser(OutputSource.StdErr);
            _buffer.SetPartial(OutputSource.StdOut, null);
            _buffer.SetPartial(OutputSource.StdErr, null);

            _runFirstLine = _buffer.NextLineNumber;
            string stamp = startedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            AppendSystemLocked($"=== started {stamp} ===");
            FlushLogLocked();
        }
    }

    /// <summary>Writes a chunk of standard output.</summary>
    public void WriteStdOut(ReadOnlySpan<char> chunk)
    {
        lock (_sync)
        {
            WriteChunkLocked(_stdOutParser, OutputSource.StdOut, chunk);
        }
    }

    /// <summary>Writes a chunk of standard error.</summary>
    public void WriteStdErr(ReadOnlySpan<char> chunk)
    {
        lock (_sync)
        {
            WriteChunkLocked(_stdErrParser, OutputSource.StdErr, chunk);
        }
    }

    /// <summary>Appends a system line (or several, if <paramref name="message"/> contains newlines).</summary>
    public void WriteSystem(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_sync)
        {
            AppendSystemLocked(message);
            FlushLogLocked();
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
            _completed.Clear();
            _stdOutParser.Flush(_completed);
            AppendLinesLocked(_completed);

            _completed.Clear();
            _stdErrParser.Flush(_completed);
            AppendLinesLocked(_completed);
            _completed.Clear();

            _buffer.SetPartial(OutputSource.StdOut, null);
            _buffer.SetPartial(OutputSource.StdErr, null);

            AppendSystemLocked($"=== {summary} ===");
            FlushLogLocked();
        }
    }

    /// <summary>
    /// Starts writing completed lines to <paramref name="path"/> (appending). If already logging to the same
    /// full path this does nothing; if logging elsewhere, the old file is closed first. The retained lines of
    /// the current run are written first, then new lines as they complete.
    /// On an open or write failure, logging is disabled and a <c>log disabled: ...</c> system line is added
    /// to the buffer. This method does not throw for I/O errors.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or invalid.</exception>
    public void EnableLog(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string fullPath = Path.GetFullPath(path);

        lock (_sync)
        {
            if (_log is not null && PathEquals(fullPath, _logPath))
            {
                return;
            }

            bool samePathAsLast = PathEquals(fullPath, _lastLoggedPath);
            CloseLogLocked();

            FileStream? stream = null;
            try
            {
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                _log = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                stream = null;
                _logPath = fullPath;
            }
            catch (Exception ex) when (IsIoFailure(ex))
            {
                stream?.Dispose();
                DisableLogAfterFailureLocked(ex.Message);
                return;
            }

            _lastLoggedPath = fullPath;
            if (!samePathAsLast)
            {
                _nextUnloggedLine = 0;
            }

            // Lines already written to this same file are not repeated. Otherwise the current run's
            // retained lines are written from its first line.
            if (_runFirstLine is long runFirst)
            {
                long from = Math.Max(runFirst, _nextUnloggedLine);
                long actual = Math.Max(from, _buffer.FirstLineNumber);
                AppendToLogLocked(_buffer.GetCompletedLinesFrom(actual), actual);
            }

            FlushLogLocked();
        }
    }

    /// <summary>Stops logging, flushing and closing the file. Does nothing if not logging.</summary>
    public void DisableLog()
    {
        lock (_sync)
        {
            CloseLogLocked();
        }
    }

    /// <summary>Closes the log file, if open.</summary>
    public void Dispose()
    {
        DisableLog();
    }

    private void WriteChunkLocked(AnsiLineParser parser, OutputSource source, ReadOnlySpan<char> chunk)
    {
        _completed.Clear();
        parser.Feed(chunk, _completed);
        AppendLinesLocked(_completed);
        _completed.Clear();

        _buffer.SetPartial(source, parser.Partial);
        FlushLogLocked();
    }

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

    /// <summary>Adds completed lines to the buffer, then to the log if logging.</summary>
    private void AppendLinesLocked(IReadOnlyList<OutputLine> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        long first = _buffer.NextLineNumber;
        _buffer.Append(lines);
        AppendToLogLocked(lines, first);
    }

    /// <summary>
    /// Writes lines to the log, whose first line has absolute number <paramref name="firstNumber"/>.
    /// Advances <see cref="_nextUnloggedLine"/> past each line written.
    /// </summary>
    private void AppendToLogLocked(IReadOnlyList<OutputLine> lines, long firstNumber)
    {
        if (_log is null)
        {
            return;
        }

        try
        {
            for (int i = 0; i < lines.Count; i++)
            {
                _log.Write(lines[i].PlainText);
                _log.Write(Environment.NewLine);
                _nextUnloggedLine = firstNumber + i + 1;
            }
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            DisableLogAfterFailureLocked(ex.Message);
        }
    }

    private void FlushLogLocked()
    {
        if (_log is null)
        {
            return;
        }

        try
        {
            _log.Flush();
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            DisableLogAfterFailureLocked(ex.Message);
        }
    }

    private void DisableLogAfterFailureLocked(string message)
    {
        CloseLogLocked();
        _buffer.Append(new[] { new OutputLine(OutputSource.System, new[] { new StyledSpan($"log disabled: {message}", SpanStyle.Default) }) });
    }

    /// <summary>Closes the log quietly. Errors while flushing a failed log are ignored.</summary>
    private void CloseLogLocked()
    {
        if (_log is null)
        {
            return;
        }

        try
        {
            _log.Dispose();
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            // The file is being abandoned; nothing more can be done.
        }

        _log = null;
        _logPath = null;
    }

    private static bool IsIoFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException;

    private static bool PathEquals(string a, string? b) =>
        b is not null && string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
