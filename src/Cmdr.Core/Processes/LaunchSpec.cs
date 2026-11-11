using Cmdr.Core.Config;

namespace Cmdr.Core.Processes;

/// <summary>
/// A fully resolved launch request: every relative path is resolved and every collection is a private copy,
/// so later edits to the configuration do not affect a process that is already launched.
/// </summary>
/// <param name="Program">Executable to launch.</param>
/// <param name="Args">Arguments, passed without quoting.</param>
/// <param name="WorkingDirectory">Absolute working directory.</param>
/// <param name="Env">Environment overlay; a null value removes the inherited variable.</param>
/// <param name="StopMethod">How the process is asked to stop.</param>
/// <param name="StopInput">Text written to stdin for the <see cref="StopMethod.Input"/> stop method.</param>
public sealed record LaunchSpec(
    string Program,
    IReadOnlyList<string> Args,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?> Env,
    StopMethod StopMethod,
    string? StopInput)
{
    /// <summary>Builds a launch request from a command definition, resolving the working directory against <paramref name="configDir"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="configDir"/> is null or empty.</exception>
    public static LaunchSpec From(CommandSpec spec, string configDir)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        return new LaunchSpec(
            spec.Program,
            spec.Args.ToArray(),
            ConfigPaths.ResolveCwd(spec, configDir),
            new Dictionary<string, string?>(spec.Env),
            spec.Stop.Method,
            spec.Stop.Input);
    }
}
