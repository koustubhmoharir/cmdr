using Cmdr.Core.Config;

namespace Cmdr.Core.Session;

/// <summary>
/// The outcome of <see cref="Session.PrepareEnvAsync"/>: the values the environment scripts of the commands produced, or the first
/// script that failed. Failure is a result, not an exception; cancellation is an exception of the task.
/// </summary>
public sealed class EnvPreparation
{
    internal EnvPreparation(
        ScriptValues values,
        string? failedOwnerLabel,
        string? error,
        string stdErr,
        IReadOnlyList<CommandEntry> affected)
    {
        Values = values;
        FailedOwnerLabel = failedOwnerLabel;
        Error = error;
        StdErr = stdErr;
        Affected = affected;
    }

    /// <summary>True when every script succeeded (or none was needed).</summary>
    public bool Succeeded => FailedOwnerLabel is null;

    /// <summary>
    /// The values of every script, when <see cref="Succeeded"/>. When a script failed, the values of the scripts that ran before it, which
    /// are not complete for the commands (the failed script's own values are missing).
    /// </summary>
    public ScriptValues Values { get; }

    /// <summary>What failed, named as the UI names it: <c>file 'cmdr.json'</c> or <c>group 'a › b'</c>. Null when <see cref="Succeeded"/>.</summary>
    public string? FailedOwnerLabel { get; }

    /// <summary>The one-line reason the script failed, from its run (it never contains a value from the script's output). Null when succeeded.</summary>
    public string? Error { get; }

    /// <summary>The last characters of the failed script's standard error, trimmed; empty when there is none or when succeeded.</summary>
    public string StdErr { get; }

    /// <summary>The commands that need the failed script, in the order they were given. Empty when succeeded.</summary>
    public IReadOnlyList<CommandEntry> Affected { get; }
}
