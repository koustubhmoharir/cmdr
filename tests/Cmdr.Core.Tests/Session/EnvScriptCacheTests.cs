using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Shared.Processes;

namespace Cmdr.Core.Tests.Session;

/// <summary>The environment script result cache, with fake run delegates (no processes).</summary>
public class EnvScriptCacheTests
{
    private const string Fp = "fp";

    private static EnvScriptResult Ok(string value) =>
        EnvScriptResult.Success(new Dictionary<string, string?> { ["V"] = value }, "", 0, TimeSpan.Zero);

    private static EnvScriptResult Failed(string error = "exit 1") =>
        EnvScriptResult.Failure(error, "boom", 1, TimeSpan.Zero);

    private static Func<CancellationToken, Task<EnvScriptResult>> Returns(EnvScriptResult result, Counter counter) =>
        _ =>
        {
            counter.Increment();
            return Task.FromResult(result);
        };

    /// <summary>A run that waits until its token is cancelled, recording the token it was given.</summary>
    private static Func<CancellationToken, Task<EnvScriptResult>> WaitsForCancel(Counter counter, CancellationToken[] seen) =>
        async ct =>
        {
            seen[0] = ct;
            counter.Increment();
            await Task.Delay(Timeout.Infinite, ct);
            return Ok("never");
        };

    [Fact]
    public async Task Once_SameFingerprint_ReusesTheSuccessfulResult()
    {
        using var cache = new EnvScriptCache();
        var counter = new Counter();
        object owner = new();

        EnvScriptResult first = await cache.GetAsync(owner, Fp, EnvScriptRerun.Once, Returns(Ok("1"), counter), CancellationToken.None);
        EnvScriptResult second = await cache.GetAsync(owner, Fp, EnvScriptRerun.Once, Returns(Ok("2"), counter), CancellationToken.None);

        Assert.Equal(1, counter.Value);
        Assert.Equal("1", first.Values!["V"]);
        Assert.Equal("1", second.Values!["V"]);
    }

    [Fact]
    public async Task Once_DifferentFingerprint_RunsAgain_AndTheNewResultIsReused()
    {
        using var cache = new EnvScriptCache();
        var counter = new Counter();
        object owner = new();

        await cache.GetAsync(owner, "a", EnvScriptRerun.Once, Returns(Ok("a"), counter), CancellationToken.None);
        EnvScriptResult b = await cache.GetAsync(owner, "b", EnvScriptRerun.Once, Returns(Ok("b"), counter), CancellationToken.None);
        EnvScriptResult again = await cache.GetAsync(owner, "b", EnvScriptRerun.Once, Returns(Ok("unused"), counter), CancellationToken.None);

        Assert.Equal(2, counter.Value);
        Assert.Equal("b", b.Values!["V"]);
        Assert.Equal("b", again.Values!["V"]);
    }

    [Fact]
    public async Task EachStart_IsNeverReused()
    {
        using var cache = new EnvScriptCache();
        var counter = new Counter();
        object owner = new();

        await cache.GetAsync(owner, Fp, EnvScriptRerun.EachStart, Returns(Ok("1"), counter), CancellationToken.None);
        EnvScriptResult second = await cache.GetAsync(owner, Fp, EnvScriptRerun.EachStart, Returns(Ok("2"), counter), CancellationToken.None);

        Assert.Equal(2, counter.Value);
        Assert.Equal("2", second.Values!["V"]);
    }

    [Fact]
    public async Task Failures_AreNotReused()
    {
        using var cache = new EnvScriptCache();
        var counter = new Counter();
        object owner = new();

        EnvScriptResult first = await cache.GetAsync(owner, Fp, EnvScriptRerun.Once, Returns(Failed(), counter), CancellationToken.None);
        EnvScriptResult second = await cache.GetAsync(owner, Fp, EnvScriptRerun.Once, Returns(Ok("2"), counter), CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.Equal(2, counter.Value);
        Assert.Equal("2", second.Values!["V"]);
    }

    [Fact]
    public async Task InFlight_ConcurrentRequests_ShareOneRun()
    {
        using var cache = new EnvScriptCache();
        var counter = new Counter();
        var gate = new TaskCompletionSource<EnvScriptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, Task<EnvScriptResult>> run = _ =>
        {
            counter.Increment();
            return gate.Task;
        };
        object owner = new();

        Task<EnvScriptResult> first = cache.GetAsync(owner, Fp, EnvScriptRerun.EachStart, run, CancellationToken.None);
        Task<EnvScriptResult> second = cache.GetAsync(owner, Fp, EnvScriptRerun.EachStart, run, CancellationToken.None);
        gate.SetResult(Ok("shared"));

        EnvScriptResult[] results = await Task.WhenAll(first, second);

        Assert.Equal(1, counter.Value);
        Assert.Equal("shared", results[0].Values!["V"]);
        Assert.Equal("shared", results[1].Values!["V"]);
    }

    [Fact]
    public async Task OneWaiterCancels_TheRunContinues_ForTheOther()
    {
        using var cache = new EnvScriptCache();
        var counter = new Counter();
        var gate = new TaskCompletionSource<EnvScriptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new CancellationToken[1];
        Func<CancellationToken, Task<EnvScriptResult>> run = ct =>
        {
            seen[0] = ct;
            counter.Increment();
            return gate.Task;
        };
        object owner = new();
        using var leave = new CancellationTokenSource();

        Task<EnvScriptResult> leaving = cache.GetAsync(owner, Fp, EnvScriptRerun.EachStart, run, leave.Token);
        Task<EnvScriptResult> staying = cache.GetAsync(owner, Fp, EnvScriptRerun.EachStart, run, CancellationToken.None);
        leave.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaving);
        Assert.False(seen[0].IsCancellationRequested);

        gate.SetResult(Ok("kept"));
        EnvScriptResult result = await staying;

        Assert.Equal("kept", result.Values!["V"]);
        Assert.Equal(1, counter.Value);
    }

    [Fact]
    public async Task LastWaiterCancels_TheRunTokenIsCancelled_AndANewRequestStartsAgain()
    {
        using var cache = new EnvScriptCache();
        var counter = new Counter();
        var seen = new CancellationToken[1];
        object owner = new();
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        Task<EnvScriptResult> a = cache.GetAsync(owner, Fp, EnvScriptRerun.Once, WaitsForCancel(counter, seen), first.Token);
        Task<EnvScriptResult> b = cache.GetAsync(owner, Fp, EnvScriptRerun.Once, WaitsForCancel(counter, seen), second.Token);
        first.Cancel();
        Assert.False(seen[0].IsCancellationRequested);
        second.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b);
        Assert.True(seen[0].IsCancellationRequested);

        using var third = new CancellationTokenSource();
        Task<EnvScriptResult> c = cache.GetAsync(owner, Fp, EnvScriptRerun.Once, Returns(Ok("again"), counter), third.Token);
        EnvScriptResult result = await c;

        Assert.Equal(2, counter.Value);
        Assert.Equal("again", result.Values!["V"]);
    }

    [Fact]
    public async Task AlreadyCancelledWaiter_IsCancelledWithoutStartingARun()
    {
        using var cache = new EnvScriptCache();
        var counter = new Counter();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Task<EnvScriptResult> task = cache.GetAsync(new object(), Fp, EnvScriptRerun.Once, Returns(Ok("x"), counter), cancelled.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(0, counter.Value);
    }

    [Fact]
    public async Task Invalidate_DropsTheCompletedResult()
    {
        using var cache = new EnvScriptCache();
        var counter = new Counter();
        object owner = new();

        await cache.GetAsync(owner, Fp, EnvScriptRerun.Once, Returns(Ok("1"), counter), CancellationToken.None);
        cache.Invalidate(owner);
        EnvScriptResult second = await cache.GetAsync(owner, Fp, EnvScriptRerun.Once, Returns(Ok("2"), counter), CancellationToken.None);

        Assert.Equal(2, counter.Value);
        Assert.Equal("2", second.Values!["V"]);
    }

    [Fact]
    public async Task Owners_AreComparedByReference()
    {
        using var cache = new EnvScriptCache();
        var counter = new Counter();

        await cache.GetAsync(new object(), Fp, EnvScriptRerun.Once, Returns(Ok("1"), counter), CancellationToken.None);
        await cache.GetAsync(new object(), Fp, EnvScriptRerun.Once, Returns(Ok("2"), counter), CancellationToken.None);

        Assert.Equal(2, counter.Value);
    }

    [Fact]
    public async Task LastRun_RecordsFailuresToo()
    {
        using var cache = new EnvScriptCache();
        var counter = new Counter();
        object owner = new();
        Assert.Null(cache.LastRun(owner));

        await cache.GetAsync(owner, Fp, EnvScriptRerun.Once, Returns(Ok("1"), counter), CancellationToken.None);
        await cache.GetAsync(owner, "other", EnvScriptRerun.Once, Returns(Failed("exit 3"), counter), CancellationToken.None);

        (DateTimeOffset finishedAt, EnvScriptResult result)? last = cache.LastRun(owner);
        Assert.NotNull(last);
        Assert.False(last.Value.result.Succeeded);
        Assert.Equal("exit 3", last.Value.result.Error);
        Assert.True(last.Value.finishedAt > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task ExceptionFromTheRun_BecomesAFailure()
    {
        using var cache = new EnvScriptCache();
        object owner = new();

        EnvScriptResult result = await cache.GetAsync(
            owner,
            Fp,
            EnvScriptRerun.Once,
            _ => throw new InvalidOperationException("no such program"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("no such program", result.Error);
    }

    [Fact]
    public async Task Dispose_CancelsInFlightRuns()
    {
        var cache = new EnvScriptCache();
        var counter = new Counter();
        var seen = new CancellationToken[1];

        Task<EnvScriptResult> waiting = cache.GetAsync(new object(), Fp, EnvScriptRerun.Once, WaitsForCancel(counter, seen), CancellationToken.None);
        Assert.Equal(1, counter.Value);

        cache.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.True(seen[0].IsCancellationRequested);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = cache.GetAsync(new object(), Fp, EnvScriptRerun.Once, Returns(Ok("x"), counter), CancellationToken.None);
        });
        cache.Dispose();
    }

    [Fact]
    public void Fingerprint_IsUnambiguous()
    {
        Assert.NotEqual(EnvScriptCache.Fingerprint(new string?[] { "ab", "c" }), EnvScriptCache.Fingerprint(new string?[] { "a", "bc" }));
        Assert.NotEqual(EnvScriptCache.Fingerprint(new string?[] { "1:a" }), EnvScriptCache.Fingerprint(new string?[] { "a" }));
        Assert.NotEqual(EnvScriptCache.Fingerprint(new string?[] { null }), EnvScriptCache.Fingerprint(new string?[] { "" }));
        Assert.Equal(EnvScriptCache.Fingerprint(new string?[] { "x", null }), EnvScriptCache.Fingerprint(new string?[] { "x", null }));
    }

    private sealed class Counter
    {
        private int _value;

        public int Value => Volatile.Read(ref _value);

        public void Increment() => Interlocked.Increment(ref _value);
    }
}
