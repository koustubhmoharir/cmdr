namespace Shared.Processes.Tests;

/// <summary>
/// Helpers for process tests in this project. A subset of the Cmdr.Core.Tests helper of the same name: the members that
/// need a managed process (which is in Cmdr.Core) stay there. <see cref="WaitUntil"/> here has no managed-process
/// diagnostics: its timeout message names only the condition.
/// </summary>
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
        Exec("sh", new[] { "-c", script }, workingDirectory, env, stopMethod, stopInput);

    /// <summary>
    /// Builds a launch request for a program. The optional <paramref name="env"/> is one overlay level defined in
    /// <paramref name="workingDirectory"/>, which is also the config directory.
    /// </summary>
    public static LaunchSpec Exec(
        string program,
        IReadOnlyList<string> args,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? env = null,
        StopMethod stopMethod = StopMethod.CtrlC,
        string? stopInput = null) =>
        new(
            program,
            args,
            workingDirectory,
            env is null ? Array.Empty<EnvLevel>() : new[] { new EnvLevel(env, workingDirectory) },
            workingDirectory,
            stopMethod,
            stopInput);

    /// <summary>Polls <paramref name="condition"/> every 20 ms; fails with the current state if it does not hold in time.</summary>
    public static void WaitUntil(Func<bool> condition, string description, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail($"Timed out after {(timeout ?? DefaultTimeout).TotalSeconds}s waiting for: {description}.");
            }

            Thread.Sleep(20);
        }
    }
}
