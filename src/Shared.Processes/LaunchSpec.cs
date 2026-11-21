namespace Shared.Processes;

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
    /// Builds the launch request of a script: its program and arguments, run in <paramref name="cwd"/> resolved against
    /// <paramref name="levelCwd"/>, or in <paramref name="levelCwd"/> when <paramref name="cwd"/> is null or blank, as for a command.
    /// <c>${cwd}</c> in the script's arguments is that working directory. The script stops with Ctrl+C, reads a stdin pipe, and
    /// leftover processes are tracked. The script itself is not in <paramref name="levels"/>: the caller passes the environment
    /// chain that applies to it (the levels up to, not including, the level that owns the script).
    /// </summary>
    /// <param name="program">The executable of the script.</param>
    /// <param name="args">The arguments of the script, passed without quoting. Copied; they are expanded when the process is launched.</param>
    /// <param name="cwd">The working directory of the script, relative to <paramref name="levelCwd"/> when not absolute. Null means <paramref name="levelCwd"/>.</param>
    /// <param name="levels">The environment overlays that apply to the script, outermost first. Copied.</param>
    /// <param name="levelCwd">The directory that the script's <paramref name="cwd"/> is relative to: the effective working directory of the level that owns the script.</param>
    /// <param name="configDir">The config directory, the value of <c>${configDir}</c>.</param>
    /// <param name="inputs">The input placeholder values, for the script's arguments and the env values of <paramref name="levels"/>. Copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="args"/>, <paramref name="levels"/> or <paramref name="inputs"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="levelCwd"/> or <paramref name="configDir"/> is null or empty.</exception>
    public static LaunchSpec ForScript(
        string program,
        IReadOnlyList<string> args,
        string? cwd,
        IReadOnlyList<EnvLevel> levels,
        string levelCwd,
        string configDir,
        IReadOnlyDictionary<string, string> inputs)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentException.ThrowIfNullOrEmpty(levelCwd);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        string workingDirectory = LaunchPaths.ResolveDir(cwd, levelCwd);
        return new LaunchSpec(
            program,
            args,
            workingDirectory,
            levels,
            Path.GetFullPath(configDir),
            StopMethod.CtrlC,
            StopInput: null,
            Leftovers: LeftoverMode.Track,
            Stdin: StdinMode.Pipe,
            Ready: null)
        {
            Inputs = inputs,
        };
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
                Literal = level.Literal,
            };
        }

        return copy;
    }
}
