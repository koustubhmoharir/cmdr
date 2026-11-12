using Cmdr.Core.Config;

namespace Cmdr.Core.Processes;

/// <summary>
/// A fully resolved launch request: every relative path is resolved and every collection is a private copy,
/// so later edits to the configuration do not affect a process that is already launched.
/// </summary>
/// <param name="Program">Executable to launch.</param>
/// <param name="Args">Arguments, passed without quoting.</param>
/// <param name="WorkingDirectory">Absolute working directory.</param>
/// <param name="EnvLevels">
/// Environment overlays, outermost first: each enclosing group with an <c>env</c>, then the command's own.
/// See <see cref="EnvExpansion.ApplyLevels"/>.
/// </param>
/// <param name="ConfigDir">Absolute config directory, the value of <c>${configDir}</c>.</param>
/// <param name="StopMethod">How the process is asked to stop.</param>
/// <param name="StopInput">Text written to stdin for the <see cref="StopMethod.Input"/> stop method.</param>
/// <param name="Leftovers">What happens to processes left running after the leader exits on its own.</param>
/// <param name="Stdin">Where the standard input comes from. <see cref="StdinMode.Console"/> is honoured on Windows only.</param>
/// <param name="Ready">Readiness patterns of the command, or null when it declares none. Each run matches its own output against them.</param>
public sealed record LaunchSpec(
    string Program,
    IReadOnlyList<string> Args,
    string WorkingDirectory,
    IReadOnlyList<EnvLevel> EnvLevels,
    string ConfigDir,
    StopMethod StopMethod,
    string? StopInput,
    LeftoverMode Leftovers = LeftoverMode.Track,
    StdinMode Stdin = StdinMode.Pipe,
    ReadySpec? Ready = null)
{
    /// <summary>
    /// Builds a launch request from a command and the groups that contain it (outermost first). The working directory and
    /// each environment level are resolved against <paramref name="configDir"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="ancestors"/> or <paramref name="spec"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="configDir"/> is null or empty.</exception>
    public static LaunchSpec From(IReadOnlyList<GroupSpec> ancestors, CommandSpec spec, string configDir)
    {
        ArgumentNullException.ThrowIfNull(ancestors);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        var levels = new List<EnvLevel>();
        string cwd = Path.GetFullPath(configDir);
        foreach (GroupSpec group in ancestors)
        {
            cwd = ConfigPaths.ResolveDir(group.Cwd, cwd);
            if (group.Env is not null)
            {
                levels.Add(new EnvLevel(new Dictionary<string, string?>(group.Env), cwd));
            }
        }

        string workingDirectory = ConfigPaths.ResolveDir(spec.Cwd, cwd);
        levels.Add(new EnvLevel(new Dictionary<string, string?>(spec.Env), workingDirectory));

        return new LaunchSpec(
            spec.Program,
            spec.Args.ToArray(),
            workingDirectory,
            levels,
            Path.GetFullPath(configDir),
            spec.Stop.Method,
            spec.Stop.Input,
            spec.Leftovers,
            spec.Stdin,
            spec.Ready?.Clone());
    }
}
