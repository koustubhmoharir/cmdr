using Cmdr.Core.Config;
using Cmdr.Core.Output;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Tests.Processes;

/// <summary>Shared helpers for process supervision tests.</summary>
internal static class ProcessTestHelpers
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Builds a launch request running <c>sh -c script</c>.</summary>
    public static LaunchSpec Sh(
        string script,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? env = null,
        StopMethod stopMethod = StopMethod.CtrlC,
        string? stopInput = null) =>
        new("sh", new[] { "-c", script }, workingDirectory, env ?? new Dictionary<string, string?>(), stopMethod, stopInput);

    /// <summary>Polls <paramref name="condition"/> every 20 ms; fails with the current state if it does not hold in time.</summary>
    public static void WaitUntil(Func<bool> condition, string description, ManagedProcess? process = null, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                string context = process is null
                    ? string.Empty
                    : $"\nSnapshot: {process.Snapshot}\nOutput:\n{Text(process)}";
                Assert.Fail($"Timed out after {(timeout ?? DefaultTimeout).TotalSeconds}s waiting for: {description}.{context}");
            }

            Thread.Sleep(20);
        }
    }

    /// <summary>Waits until the leader has exited and the exit marker has been written.</summary>
    public static void WaitForExited(ManagedProcess process, TimeSpan? timeout = null) =>
        WaitUntil(
            () => process.Snapshot.State == ProcessState.Exited && Text(process).Contains("=== exited with code", StringComparison.Ordinal),
            "process to exit",
            process,
            timeout);

    /// <summary>All retained lines (completed and partial) as plain text joined with newlines.</summary>
    public static string Text(ManagedProcess process) => string.Join("\n", Lines(process).Select(l => l.PlainText));

    /// <summary>All retained lines, including partial lines.</summary>
    public static IReadOnlyList<OutputLine> Lines(ManagedProcess process) =>
        process.Output.Buffer.GetLines(0, int.MaxValue);

    /// <summary>Kills any remaining tree, waits briefly for leftovers to clear, and disposes the supervisor. Safe in finally blocks.</summary>
    public static void Cleanup(ManagedProcess process)
    {
        try
        {
            process.ForceKill();
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (process.Snapshot.LeftoverCount > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }
        }
        finally
        {
            process.Dispose();
        }
    }
}
