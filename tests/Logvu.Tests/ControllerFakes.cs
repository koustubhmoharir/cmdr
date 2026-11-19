using System.Collections.Concurrent;
using Logvu.Core.Config;
using Logvu.Core.Detail;
using Logvu.Core.Store;
using Logvu.Tui;

namespace Logvu.Tests;

/// <summary>
/// A detail source that holds the reads it is asked for, so a test completes, fails or cancels them. A read that is not held goes to the
/// inner source.
/// </summary>
internal sealed class HeldDetailSource : IDetailSource
{
    private readonly IDetailSource _inner;

    /// <summary>Creates a source that holds reads, and forwards them to <paramref name="inner"/> when <see cref="Hold"/> is cleared.</summary>
    public HeldDetailSource(IDetailSource inner) => _inner = inner;

    /// <summary>Whether reads are held. True by default.</summary>
    public bool Hold { get; set; } = true;

    /// <summary>The held reads, in the order they were asked for.</summary>
    public List<HeldRead> Reads { get; } = [];

    /// <inheritdoc/>
    public Task<EventDetail> ReadDetailAsync(RowKey key, CancellationToken ct)
    {
        if (!Hold)
        {
            return _inner.ReadDetailAsync(key, ct);
        }

        var completion = new TaskCompletionSource<EventDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new HeldRead(key, ct, completion);
        Reads.Add(read);
        return completion.Task;
    }

    /// <summary>A held read: its key, its cancellation token, and the completion that finishes it.</summary>
    /// <param name="Key">The key the read was asked for.</param>
    /// <param name="Token">The read's cancellation token.</param>
    /// <param name="Completion">Finishes the read.</param>
    public sealed record HeldRead(RowKey Key, CancellationToken Token, TaskCompletionSource<EventDetail> Completion)
    {
        /// <summary>Finishes the read with a detail. A read that has finished already is unchanged.</summary>
        public void Complete(EventDetail detail) => Completion.TrySetResult(detail);
    }
}

/// <summary>
/// The UI-thread queue of a controller under test: continuations are queued, and the test runs them on its own thread, so the controller's
/// state changes on one thread as it does in the application.
/// </summary>
internal sealed class PostQueue
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private int _posted;

    /// <summary>The number of continuations posted so far, run or not.</summary>
    public int Posted => Volatile.Read(ref _posted);

    /// <summary>Queues a continuation. Called from any thread.</summary>
    public void Post(Action action)
    {
        _queue.Enqueue(action);
        Interlocked.Increment(ref _posted);
    }

    /// <summary>Runs the queued continuations, in order, on the calling thread, including those queued while they run.</summary>
    public void Pump()
    {
        while (_queue.TryDequeue(out Action? action))
        {
            action();
        }
    }

    /// <summary>Runs the queued continuations until the condition holds. Fails the test after ten seconds.</summary>
    public void PumpUntil(Func<bool> condition, string because = "the condition")
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            Pump();
            if (condition())
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for {because}.");
            }

            Thread.Sleep(5);
        }
    }
}

/// <summary>A temporary log set: a folder with a configuration and one log file of Serilog JSON events, one per second from 10:00 UTC.</summary>
internal sealed class LogSet : IDisposable
{
    private const string LogName = "app.log";

    /// <summary>
    /// Creates the folder, the configuration and <paramref name="events"/> events. The configuration's <c>minLevel</c> is
    /// <paramref name="minLevel"/>, which defaults to <c>any</c> so that the timeline starts unfiltered.
    /// </summary>
    public LogSet(int events, string minLevel = "any")
    {
        Folder = Path.Combine(Path.GetTempPath(), $"logvu-set-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Folder);
        string configPath = Path.Combine(Folder, "logvu.json");
        string members = DetailFixtures.BaseMembers.Replace("\"minLevel\": \"any\"", $"\"minLevel\": \"{minLevel}\"");
        File.WriteAllText(configPath, "{ \"format\": \"serilog-json\", " + members + " }");
        File.WriteAllLines(LogPath, Enumerable.Range(0, events).Select(Json));
        Config = LogvuConfig.Load(configPath);
    }

    /// <summary>The folder holding the configuration and the log.</summary>
    public string Folder { get; }

    /// <summary>The configuration of the set.</summary>
    public LogvuConfig Config { get; }

    private string LogPath => Path.Combine(Folder, LogName);

    /// <summary>Appends the event with the given index, as a writer of the log would.</summary>
    public void Append(int index) => File.AppendAllText(LogPath, Json(index) + "\n");

    /// <inheritdoc/>
    public void Dispose()
    {
        Directory.Delete(Folder, recursive: true);
    }

    private static string Json(int index) =>
        $"{{\"Timestamp\":\"{Time(index):O}\",\"Level\":\"Information\",\"RenderedMessage\":\"r{index}\"}}";

    private static DateTimeOffset Time(int index) => new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero).AddSeconds(index);
}
