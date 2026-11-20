using Cmdr.Core.Config;

namespace Cmdr.Core.Processes;

/// <summary>
/// A fully resolved launch request: every relative path is resolved, and the constructor (and a <c>with</c> expression) takes
/// a private copy of every collection and of <see cref="Ready"/>, so later edits to the configuration, or to the lists the
/// caller passed, do not affect a process that is already launched.
/// </summary>
/// <param name="Program">Executable to launch.</param>
/// <param name="Args">Arguments, passed without quoting.</param>
/// <param name="WorkingDirectory">Absolute working directory.</param>
/// <param name="EnvLevels">
/// Environment overlays, outermost first: the file's levels, each enclosing group with an <c>env</c>, then the command's own.
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
    /// <summary>Arguments, passed without quoting. A private copy.</summary>
    public IReadOnlyList<string> Args { get; init => field = CopyArgs(value); } = CopyArgs(Args);

    /// <summary>Environment overlays, outermost first. A private copy, down to each level's dictionary.</summary>
    public IReadOnlyList<EnvLevel> EnvLevels { get; init => field = CopyLevels(value); } = CopyLevels(EnvLevels);

    /// <summary>Readiness patterns, or null. A private copy: <see cref="ReadySpec"/> is mutable.</summary>
    public ReadySpec? Ready { get; init => field = value?.Clone(); } = Ready?.Clone();

    /// <summary>
    /// The values of the input placeholders, which the launch expands in the arguments and in the env values (see
    /// <see cref="ExpandArgs"/> and <see cref="EnvExpansion.ApplyLevels"/>). A private copy; empty when none were given. Values
    /// the user typed are not part of <see cref="object.ToString"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Inputs { get; init => field = CopyInputs(value); } = CopyInputs(new Dictionary<string, string>());

    /// <summary>
    /// Builds a launch request from a command and the groups that contain it (outermost first). The working directory and
    /// each environment level are resolved against <paramref name="configDir"/>. The environment levels of the command's file
    /// (<paramref name="fileLevels"/>, see <c>ConfigSource.EnvLevels</c>) come first, then those of the groups, then the command's own.
    /// </summary>
    /// <param name="fileLevels">The file level of the environment chain: the file's <c>baseEnv</c> import and its top-level <c>env</c>. Empty for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ancestors"/>, <paramref name="spec"/> or <paramref name="fileLevels"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="configDir"/> is null or empty.</exception>
    public static LaunchSpec From(IReadOnlyList<GroupSpec> ancestors, CommandSpec spec, string configDir, IReadOnlyList<EnvLevel> fileLevels)
    {
        ArgumentNullException.ThrowIfNull(ancestors);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(fileLevels);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        var levels = new List<EnvLevel>(fileLevels);
        string cwd = Path.GetFullPath(configDir);
        foreach (GroupSpec group in ancestors)
        {
            cwd = ConfigPaths.ResolveDir(group.Cwd, cwd);
            if (group.Env is not null)
            {
                levels.Add(new EnvLevel(group.Env, cwd));
            }
        }

        string workingDirectory = ConfigPaths.ResolveDir(spec.Cwd, cwd);
        levels.Add(new EnvLevel(spec.Env, workingDirectory));

        return new LaunchSpec(
            spec.Program,
            spec.Args,
            workingDirectory,
            levels,
            Path.GetFullPath(configDir),
            spec.Stop.Method,
            spec.Stop.Input,
            spec.Leftovers,
            spec.Stdin,
            spec.Ready);
    }

    /// <summary>
    /// The arguments with every placeholder expanded, in one pass per argument: <c>${env:NAME}</c> from <paramref name="env"/>,
    /// <c>${cwd}</c> as <see cref="WorkingDirectory"/>, <c>${configDir}</c> as <see cref="ConfigDir"/>, <c>${pathsep}</c> as
    /// <paramref name="pathSeparator"/>, <c>$${</c> as <c>${</c>. An input placeholder is replaced by its value in
    /// <paramref name="inputs"/>, or kept as written when that is null (for display).
    /// </summary>
    /// <param name="env">The final environment of the launch, the value of each variable (null when undefined).</param>
    /// <param name="pathSeparator">The value of <c>${pathsep}</c>.</param>
    /// <param name="inputs">The input values, or null to keep input placeholders as written.</param>
    /// <exception cref="InvalidOperationException">An input has no usable value (see <see cref="Placeholders.Expand"/>).</exception>
    public string[] ExpandArgs(Func<string, string?> env, char pathSeparator, IReadOnlyDictionary<string, string>? inputs)
    {
        ArgumentNullException.ThrowIfNull(env);

        return Args.Select(arg => Placeholders.Expand(arg, env, inputs, WorkingDirectory, ConfigDir, pathSeparator)).ToArray();
    }

    private static IReadOnlyDictionary<string, string> CopyInputs(IReadOnlyDictionary<string, string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return inputs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static string[] CopyArgs(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return [.. args];
    }

    private static EnvLevel[] CopyLevels(IReadOnlyList<EnvLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var copy = new EnvLevel[levels.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            EnvLevel level = levels[i] ?? throw new ArgumentException("An environment level is null.", nameof(levels));
            copy[i] = new EnvLevel(new Dictionary<string, string?>(level.Env), level.Cwd)
            {
                ImportFrom = level.ImportFrom is null ? null : CopyLevels(level.ImportFrom),
            };
        }

        return copy;
    }
}
