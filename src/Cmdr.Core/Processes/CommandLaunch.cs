using Cmdr.Core.Config;
using Shared.Processes;

namespace Cmdr.Core.Processes;

/// <summary>
/// Builds the launch requests of cmdr's commands and environment scripts from cmdr's configuration types. The launch
/// itself is <see cref="LaunchSpec"/> in Shared.Processes.
/// </summary>
public static class CommandLaunch
{
    /// <summary>
    /// Builds a launch request from a command and the groups that contain it (outermost first). The environment levels of the command's file
    /// (<paramref name="fileLevels"/>, see <c>ConfigSource.EnvLevels</c>) come first, then those of the groups, then the command's own. Each
    /// working directory is resolved in the environment that is in effect where it is defined (see <see cref="GroupLevels"/>): a group's cwd
    /// sees the file's levels and the groups above it, not its own, and the command's cwd sees the groups, not its own <c>env</c>.
    /// </summary>
    /// <param name="fileLevels">The file level of the environment chain: the file's <c>baseEnv</c> import and its top-level <c>env</c>. Empty for none.</param>
    /// <param name="groupScriptValues">
    /// The values of each ancestor group's environment script, aligned with <paramref name="ancestors"/>; null for a group whose script
    /// produced none. A group with values gets a literal level before its <c>env</c> level. Null when no group has values.
    /// </param>
    /// <param name="baseEnvironment">
    /// The environment that the levels are applied to, as the cwd sees it. Null means cmdr's process environment. Tests pass their own.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="ancestors"/>, <paramref name="spec"/> or <paramref name="fileLevels"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="configDir"/> is null or empty, or <paramref name="groupScriptValues"/> does not have one entry per ancestor.</exception>
    /// <exception cref="InvalidOperationException">
    /// A group's or the command's cwd has an <c>${env:NAME}</c> whose variable is not set in that environment (see <see cref="LaunchPaths.ResolveDir(string?, string, Func{string, string?}, string, char)"/>).
    /// </exception>
    public static LaunchSpec From(
        IReadOnlyList<GroupSpec> ancestors,
        CommandSpec spec,
        string configDir,
        IReadOnlyList<EnvLevel> fileLevels,
        IReadOnlyList<IReadOnlyDictionary<string, string?>?>? groupScriptValues = null,
        IReadOnlyDictionary<string, string?>? baseEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(ancestors);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(fileLevels);
        ArgumentException.ThrowIfNullOrEmpty(configDir);
        if (groupScriptValues is not null && groupScriptValues.Count != ancestors.Count)
        {
            throw new ArgumentException("There must be one script value entry per ancestor group.", nameof(groupScriptValues));
        }

        string configFull = Path.GetFullPath(configDir);
        Dictionary<string, string?> environment = EnvExpansion.NewEnvironment(baseEnvironment);
        EnvExpansion.ApplyLevels(environment, fileLevels, configFull, Path.PathSeparator, inputs: null);

        var levels = new List<EnvLevel>(fileLevels);
        levels.AddRange(GroupLevels(ancestors, groupScriptValues, configFull, environment, configFull, out string cwd));

        string workingDirectory = LaunchPaths.ResolveDir(spec.Cwd, cwd, EnvExpansion.Lookup(environment), configFull, Path.PathSeparator);
        levels.Add(new EnvLevel(spec.Env, workingDirectory));

        return new LaunchSpec(
            spec.Program,
            spec.Args,
            workingDirectory,
            levels,
            configFull,
            spec.Stop.Method,
            spec.Stop.Input,
            spec.Leftovers,
            spec.Stdin,
            spec.Ready);
    }

    /// <summary>
    /// Builds the launch request of an environment script (<see cref="EnvScriptSpec"/>). The script's working directory is its
    /// <c>cwd</c> resolved against <paramref name="levelCwd"/>, or <paramref name="levelCwd"/> when it has none, as for a command; its
    /// <c>${env:NAME}</c> sees the environment of <paramref name="levels"/>. <c>${cwd}</c> in the script's arguments is that working directory. The script stops with Ctrl+C, reads a stdin pipe, and
    /// leftover processes are tracked. The script itself is not in <paramref name="levels"/>: the caller passes the environment
    /// chain that applies to it (the levels up to, not including, the level that owns the script).
    /// </summary>
    /// <param name="script">The script to launch. Its arguments are copied; they are expanded when the process is launched.</param>
    /// <param name="levels">The environment overlays that apply to the script, outermost first. Copied.</param>
    /// <param name="levelCwd">The directory that the script's <c>cwd</c> is relative to: the effective working directory of the level that owns the script.</param>
    /// <param name="configDir">The config directory, the value of <c>${configDir}</c>.</param>
    /// <param name="inputs">The input placeholder values, for the script's arguments and the env values of <paramref name="levels"/>. Copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="script"/>, <paramref name="levels"/> or <paramref name="inputs"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="levelCwd"/> or <paramref name="configDir"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">The script's cwd has an <c>${env:NAME}</c> whose variable is not set in the environment of <paramref name="levels"/>.</exception>
    public static LaunchSpec ForEnvScript(
        EnvScriptSpec script,
        IReadOnlyList<EnvLevel> levels,
        string levelCwd,
        string configDir,
        IReadOnlyDictionary<string, string> inputs)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentException.ThrowIfNullOrEmpty(levelCwd);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        // The working directory may use the variables the script sees, so it is resolved here, in the environment of the levels (a cwd
        // has no inputs), and handed to the launch as an absolute path.
        string configFull = Path.GetFullPath(configDir);
        Dictionary<string, string?> environment = EnvExpansion.NewEnvironment();
        EnvExpansion.ApplyLevels(environment, levels, configFull, Path.PathSeparator, inputs: null);
        string workingDirectory = LaunchPaths.ResolveDir(script.Cwd, levelCwd, EnvExpansion.Lookup(environment), configFull, Path.PathSeparator);

        return LaunchSpec.ForScript(script.Program, script.Args, workingDirectory, levels, levelCwd, configDir, inputs);
    }

    /// <summary>
    /// The environment levels of a chain of groups, outermost first, from <paramref name="startCwd"/>, applied to
    /// <paramref name="environment"/> as they are made. Each group's working directory is resolved first, in the environment that holds
    /// the levels above it (and the file's levels, which the caller applied): a group's own levels are not in effect for its own
    /// directory. Then, for each group with values in <paramref name="scriptValues"/>, a literal level (its environment script's output),
    /// then one level for its <c>env</c> when it has one. Each level's directory is that group's effective working directory.
    /// </summary>
    /// <param name="groups">The groups, outermost first.</param>
    /// <param name="scriptValues">The values of each group's environment script, aligned with <paramref name="groups"/>; null for none.</param>
    /// <param name="startCwd">The directory the outermost group's cwd is relative to.</param>
    /// <param name="environment">
    /// The environment the levels are applied to, updated as they are applied. It holds the environment above the first group.
    /// </param>
    /// <param name="configDir">The config directory, the value of <c>${configDir}</c>.</param>
    /// <param name="cwd">
    /// Receives the effective working directory of the innermost group (<paramref name="startCwd"/> when there are none), which is
    /// resolved before that group's own levels are applied.
    /// </param>
    /// <exception cref="InvalidOperationException">A group's cwd has an <c>${env:NAME}</c> whose variable is not set (see <see cref="LaunchPaths.ResolveDir(string?, string, Func{string, string?}, string, char)"/>).</exception>
    internal static List<EnvLevel> GroupLevels(
        IReadOnlyList<GroupSpec> groups,
        IReadOnlyList<IReadOnlyDictionary<string, string?>?>? scriptValues,
        string startCwd,
        Dictionary<string, string?> environment,
        string configDir,
        out string cwd)
    {
        var levels = new List<EnvLevel>();
        cwd = startCwd;
        for (int i = 0; i < groups.Count; i++)
        {
            GroupSpec group = groups[i];
            cwd = LaunchPaths.ResolveDir(group.Cwd, cwd, EnvExpansion.Lookup(environment), configDir, Path.PathSeparator);

            var own = new List<EnvLevel>();
            if (scriptValues?[i] is { } values)
            {
                own.Add(new EnvLevel(values, cwd) { Literal = true });
            }

            if (group.Env is not null)
            {
                own.Add(new EnvLevel(group.Env, cwd));
            }

            levels.AddRange(own);
            EnvExpansion.ApplyLevels(environment, own, configDir, Path.PathSeparator, inputs: null);
        }

        return levels;
    }

    /// <summary>
    /// The effective working directory of the innermost of <paramref name="groups"/> (outermost first), resolved as a launch resolves it: in the
    /// environment of <paramref name="fileLevels"/> and of the groups above it, not of its own levels (see <see cref="From"/>).
    /// </summary>
    /// <param name="groups">The group and its ancestors, outermost first.</param>
    /// <param name="fileLevels">The file's levels (see <see cref="From"/>).</param>
    /// <param name="groupScriptValues">The script values of the groups, aligned with <paramref name="groups"/>; null for none.</param>
    /// <param name="configDir">The config directory.</param>
    /// <param name="baseEnvironment">The environment the levels are applied to. Null means cmdr's process environment.</param>
    /// <exception cref="InvalidOperationException">A group's cwd has an <c>${env:NAME}</c> whose variable is not set.</exception>
    public static string GroupDirectory(
        IReadOnlyList<GroupSpec> groups,
        IReadOnlyList<EnvLevel> fileLevels,
        IReadOnlyList<IReadOnlyDictionary<string, string?>?>? groupScriptValues,
        string configDir,
        IReadOnlyDictionary<string, string?>? baseEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(fileLevels);
        ArgumentException.ThrowIfNullOrEmpty(configDir);

        string configFull = Path.GetFullPath(configDir);
        Dictionary<string, string?> environment = EnvExpansion.NewEnvironment(baseEnvironment);
        EnvExpansion.ApplyLevels(environment, fileLevels, configFull, Path.PathSeparator, inputs: null);
        GroupLevels(groups, groupScriptValues, configFull, environment, configFull, out string cwd);
        return cwd;
    }
}
