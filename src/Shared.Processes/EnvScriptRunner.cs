using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Shared.Processes;

/// <summary>
/// Runs an environment script and turns its standard output into the environment variables it sets: a JSON object whose
/// values are strings or null. The outcome is an <see cref="EnvScriptResult"/>.
/// </summary>
/// <remarks>
/// The script is launched like a command, in its own process group or job. Its stdin is closed at once, its stdout and stderr
/// are read concurrently, and when the run ends (exit, timeout, cancellation or output size) every process of its tree is
/// killed, so nothing it started keeps running. Reading the streams after that is bounded, so a pipe held by a process that
/// escaped the tree cannot hang the caller.
/// </remarks>
public sealed class EnvScriptRunner
{
    /// <summary>The largest standard output accepted, in characters (1 MiB).</summary>
    internal const int MaxOutputChars = 1024 * 1024;

    /// <summary>The number of characters of standard error kept: the last ones (16 KiB).</summary>
    internal const int MaxStdErrChars = 16 * 1024;

    private const string OutputTooLargeMessage = "output is larger than 1 MiB";
    private const int ReadBufferChars = 4096;
    private static readonly TimeSpan StreamDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly IProcessPlatform _platform;

    /// <summary>Creates a runner over <paramref name="platform"/>, or over the platform of this operating system when it is null.</summary>
    public EnvScriptRunner(IProcessPlatform? platform = null)
    {
        _platform = platform ?? ProcessPlatform.Current;
    }

    /// <summary>
    /// Runs the script and parses its output. Returns once the script's tree has been killed and its streams have been read
    /// (or abandoned after a bounded wait). Failures are reported in the result, not thrown; see <see cref="EnvScriptResult"/>.
    /// </summary>
    /// <param name="launch">The script to run, from <see cref="LaunchSpec.ForScript"/>.</param>
    /// <param name="timeout">How long the script may run before it is killed. Must be positive.</param>
    /// <param name="cancellationToken">Stops the run: the tree is killed, and the returned task is cancelled.</param>
    /// <exception cref="ArgumentNullException"><paramref name="launch"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is not positive.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the run ended.</exception>
    public async Task<EnvScriptResult> RunAsync(LaunchSpec launch, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var clock = Stopwatch.StartNew();
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource<EndReason>(TaskCreationOptions.RunContinuationsAsynchronously);

        LaunchedTree launched;
        try
        {
            // The exit callback may run before Launch returns; both completion sources are ready for that.
            PreparedLaunch prepared = _platform.Prepare(launch);
            launched = _platform.Launch(prepared, leader =>
            {
                exited.TrySetResult(leader.ExitCode);
                ended.TrySetResult(EndReason.Exited);
            });
        }
        catch (Exception ex)
        {
            return EnvScriptResult.Failure($"could not start: {ex.Message}", string.Empty, null, clock.Elapsed);
        }

        ILaunchedProcess process = launched.Process;
        IProcessGroup group = launched.Group;
        bool killed = false;
        try
        {
            CloseStdin(process);

            var stdout = new StreamCapture(
                process.StandardOutput,
                MaxOutputChars,
                keepTail: false,
                onOverflow: () => ended.TrySetResult(EndReason.OutputTooLarge));
            var stderr = new StreamCapture(process.StandardError, MaxStdErrChars, keepTail: true, onOverflow: null);

            using var timer = new CancellationTokenSource(timeout);
            using CancellationTokenRegistration timedOut = timer.Token.Register(() => ended.TrySetResult(EndReason.TimedOut));
            using CancellationTokenRegistration cancelled = cancellationToken.Register(() => ended.TrySetResult(EndReason.Cancelled));

            EndReason reason = await ended.Task.ConfigureAwait(false);

            // Every process of the tree goes, also after a normal exit: a leftover would keep the pipes open.
            string? killError = null;
            try
            {
                group.Kill();
            }
            catch (Win32Exception ex)
            {
                killError = ex.Message;
            }

            killed = true;

            bool drained;
            try
            {
                await Task.WhenAll(exited.Task, stdout.Completed, stderr.Completed)
                    .WaitAsync(StreamDrainTimeout)
                    .ConfigureAwait(false);
                drained = true;
            }
            catch (TimeoutException)
            {
                drained = false;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            int? exitCode = reason == EndReason.Exited ? exited.Task.Result : null;
            string stdErrTail = stderr.Text().Trim();

            EnvScriptResult Fail(string error) => EnvScriptResult.Failure(error, stdErrTail, exitCode, clock.Elapsed);

            // Processes of the script may still run: the run is not trusted, whatever it printed.
            if (killError is not null)
            {
                return Fail($"could not stop its processes: {killError}");
            }

            if (reason == EndReason.TimedOut)
            {
                return Fail($"timed out after {timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s");
            }

            // Checked before the exit code: the kill that a too-large output causes makes the exit code meaningless.
            if (reason == EndReason.OutputTooLarge || stdout.Overflowed)
            {
                return Fail(OutputTooLargeMessage);
            }

            if (exitCode is int code && code != 0)
            {
                return Fail($"exited with code {code}");
            }

            if (!drained)
            {
                return Fail($"output did not end within {StreamDrainTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s");
            }

            Dictionary<string, string?>? values = ParseValues(stdout.Text(), out string? parseError);
            return values is null
                ? Fail(parseError!)
                : EnvScriptResult.Success(values, stdErrTail, exitCode, clock.Elapsed);
        }
        finally
        {
            if (!killed)
            {
                try
                {
                    group.Kill();
                }
                catch (Win32Exception)
                {
                    // The run is already ending with another exception; the group is released below.
                }
            }

            CloseReader(process.StandardOutput);
            CloseReader(process.StandardError);
            group.Dispose();
            process.Dispose();
        }
    }

    /// <summary>
    /// Parses the standard output: an optional UTF-8 byte order mark, then one JSON object (surrounding whitespace allowed) whose
    /// names are valid variable names, each name once, and whose values are strings or null. Returns null with a one-line
    /// <paramref name="error"/>; the error names a variable but never contains a value.
    /// </summary>
    private static Dictionary<string, string?>? ParseValues(string output, out string? error)
    {
        if (output.Length > 0 && output[0] == '﻿')
        {
            output = output[1..];
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            error = NotAnObject("empty output");
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(output);
        }
        catch (JsonException ex)
        {
            error = NotAnObject(ex.LineNumber is long line && ex.BytePositionInLine is long column
                ? $"invalid JSON at line {line + 1}, position {column + 1}"
                : "invalid JSON");
            return null;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = NotAnObject($"found {DescribeKind(root.ValueKind)}");
                return null;
            }

            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                string name = property.Name;
                if (!Placeholders.IsEnvName(name))
                {
                    error = $"invalid variable name '{OneLine(name)}'";
                    return null;
                }

                if (values.ContainsKey(name))
                {
                    error = $"duplicate variable name '{OneLine(name)}'";
                    return null;
                }

                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        values[name] = property.Value.GetString();
                        break;
                    case JsonValueKind.Null:
                        values[name] = null;
                        break;
                    default:
                        error = $"value of '{OneLine(name)}' is not a string or null";
                        return null;
                }
            }

            error = null;
            return values;
        }
    }

    private static string NotAnObject(string detail) => $"output is not a JSON object: {detail}";

    private static string DescribeKind(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        _ => "null",
    };

    /// <summary>A name as it may appear in a one-line message: control characters (NUL, line breaks) become '?'.</summary>
    private static string OneLine(string name)
    {
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]))
            {
                chars[i] = '?';
            }
        }

        return new string(chars);
    }

    /// <summary>Closes the script's stdin at once: it reads end of file, and nothing is ever written to it.</summary>
    private static void CloseStdin(ILaunchedProcess process)
    {
        try
        {
            process.StandardInput?.Dispose();
        }
        catch (IOException)
        {
            // The script has already closed its end of the pipe.
        }
    }

    /// <summary>Closes a redirected stream of the run. Its reader task, if still reading, ends with the closed pipe.</summary>
    private static void CloseReader(StreamReader reader)
    {
        try
        {
            reader.Dispose();
        }
        catch (IOException)
        {
            // The pipe is being abandoned.
        }
    }

    /// <summary>Why the wait for the script ended. The first reason wins.</summary>
    private enum EndReason
    {
        /// <summary>The leader exited on its own.</summary>
        Exited,

        /// <summary>The timeout passed while it ran.</summary>
        TimedOut,

        /// <summary>Its standard output grew past <see cref="MaxOutputChars"/>.</summary>
        OutputTooLarge,

        /// <summary>The caller cancelled the run.</summary>
        Cancelled,
    }

    /// <summary>
    /// Reads one redirected stream to its end on a background task. A stream that keeps its head stops keeping text once it is
    /// larger than the limit, and reports that once; a stream that keeps its tail keeps only its last <c>limit</c> characters.
    /// </summary>
    private sealed class StreamCapture
    {
        private readonly object _sync = new();
        private readonly StringBuilder _text = new();
        private readonly StreamReader _reader;
        private readonly int _limit;
        private readonly bool _keepTail;
        private readonly Action? _onOverflow;
        private bool _overflowed;

        public StreamCapture(StreamReader reader, int limit, bool keepTail, Action? onOverflow)
        {
            _reader = reader;
            _limit = limit;
            _keepTail = keepTail;
            _onOverflow = onOverflow;
            Completed = Task.Run(ReadToEndAsync);
        }

        /// <summary>Completes when the stream has reached its end or was closed.</summary>
        public Task Completed { get; }

        /// <summary>True once a head-keeping stream grew past its limit.</summary>
        public bool Overflowed
        {
            get
            {
                lock (_sync)
                {
                    return _overflowed;
                }
            }
        }

        /// <summary>The text read so far: the head (up to the limit), or the last <c>limit</c> characters of a tail-keeping stream.</summary>
        public string Text()
        {
            lock (_sync)
            {
                string text = _text.ToString();
                if (_keepTail && text.Length > _limit)
                {
                    text = text[^_limit..];
                    if (char.IsLowSurrogate(text[0]))
                    {
                        text = text[1..];
                    }
                }

                return text;
            }
        }

        private async Task ReadToEndAsync()
        {
            char[] buffer = new char[ReadBufferChars];
            try
            {
                while (true)
                {
                    int read = await _reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    Append(buffer.AsSpan(0, read));
                }
            }
            catch (IOException)
            {
                // The pipe broke, or it was closed once the run ended.
            }
            catch (ObjectDisposedException)
            {
                // The reader was closed while it was reading.
            }
        }

        private void Append(ReadOnlySpan<char> chunk)
        {
            if (_keepTail)
            {
                lock (_sync)
                {
                    _text.Append(chunk);
                    if (_text.Length > 2 * _limit)
                    {
                        // Trimmed in batches, so that a long stream costs amortised constant work per chunk.
                        _text.Remove(0, _text.Length - _limit);
                    }
                }

                return;
            }

            bool justOverflowed = false;
            lock (_sync)
            {
                if (!_overflowed)
                {
                    if (_text.Length + chunk.Length > _limit)
                    {
                        _overflowed = true;
                        _text.Clear();
                        justOverflowed = true;
                    }
                    else
                    {
                        _text.Append(chunk);
                    }
                }
            }

            // Still read to the end (and discarded) afterwards: the tree is killed, and the pipe must not block it before that.
            if (justOverflowed)
            {
                _onOverflow?.Invoke();
            }
        }
    }
}
