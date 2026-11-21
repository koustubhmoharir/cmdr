namespace Shared.Processes;

/// <summary>The outcome of one environment script run: the variables it set, or the reason it failed.</summary>
public sealed record EnvScriptResult
{
    private EnvScriptResult()
    {
    }

    /// <summary>True when the script exited with code 0 and its output was a valid object of variables.</summary>
    public bool Succeeded => Values is not null;

    /// <summary>The variables the script set, in output order. A null value removes the variable. Null when the run failed.</summary>
    public IReadOnlyDictionary<string, string?>? Values { get; private init; }

    /// <summary>
    /// One line saying why the run failed, or null when it succeeded. It never contains a value from the script's output; a
    /// variable name may appear in it.
    /// </summary>
    public string? Error { get; private init; }

    /// <summary>The last characters of the script's standard error, trimmed; empty when there is none.</summary>
    public string StdErr { get; private init; } = "";

    /// <summary>The script's exit code, or null when it did not exit on its own (it could not start, timed out, or was stopped for its output size).</summary>
    public int? ExitCode { get; private init; }

    /// <summary>Time from the start of the run to its end.</summary>
    public TimeSpan Elapsed { get; private init; }

    /// <summary>A successful run that set <paramref name="values"/>.</summary>
    internal static EnvScriptResult Success(IReadOnlyDictionary<string, string?> values, string stdErr, int? exitCode, TimeSpan elapsed) =>
        new()
        {
            Values = values,
            StdErr = stdErr,
            ExitCode = exitCode,
            Elapsed = elapsed,
        };

    /// <summary>A failed run with the one-line <paramref name="error"/>.</summary>
    public static EnvScriptResult Failure(string error, string stdErr, int? exitCode, TimeSpan elapsed) =>
        new()
        {
            Error = error,
            StdErr = stdErr,
            ExitCode = exitCode,
            Elapsed = elapsed,
        };
}
