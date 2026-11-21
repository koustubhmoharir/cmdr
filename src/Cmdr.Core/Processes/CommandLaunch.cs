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
    /// Builds a launch request from a command and the groups that contain it (outermost first). The working directory and
    /// each environment level are resolved against <paramref name="configDir"/>. The environment levels of the command's file
    /// (<paramref name="fileLevels"/>, see <c>ConfigSource.EnvLevels</c>) come first, then those of the groups, then the command's own.
    /// </summary>
    /// <param name="fileLevels">The file level of the environment chain: the file's <c>baseEnv</c> import and its top-level <c>env</c>. Empty for none.</param>
    /// <param name="groupScriptValues">
    /// The values of each ancestor group's environment script, aligned with <paramref name="ancestors"/>; null for a group whose script
    /// produced none. A group with values gets a literal level before its <c>env</c> level. Null when no group has values.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="ancestors"/>, <paramref name="spec"/> or <paramref name="fileLevels"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="configDir"/> is null or empty, or <paramref name="groupScriptValues"/> does not have one entry per ancestor.</exception>
    public static LaunchSpec From(
        IReadOnlyList<GroupSpec> ancestors,
        CommandSpec spec,
        string configDir,
        IReadOnlyList<EnvLevel> fileLevels,
        IReadOnlyList<IReadOnlyDictionary<string, string?>?>? groupScriptValues = null)
    {
        ArgumentNullException.ThrowIfNull(ancestors);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(fileLevels);
        ArgumentException.ThrowIfNullOrEmpty(configDir);
        if (groupScriptValues is not null && groupScriptValues.Count != ancestors.Count)
        {
            throw new ArgumentException("There must be one script value entry per ancestor group.", nameof(groupScriptValues));
        }

        var levels = new List<EnvLevel>(fileLevels);
        levels.AddRange(GroupLevels(ancestors, groupScriptValues, Path.GetFullPath(configDir), out string cwd));

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
    /// Builds the launch request of an environment script (<see cref="EnvScriptSpec"/>). The script's working directory is its
    /// <c>cwd</c> resolved against <paramref name="levelCwd"/>, or <paramref name="levelCwd"/> when it has none, as for a command.
    /// <c>${cwd}</c> in the script's arguments is that working directory. The script stops with Ctrl+C, reads a stdin pipe, and
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
    public static LaunchSpec ForEnvScript(
        EnvScriptSpec script,
        IReadOnlyList<EnvLevel> levels,
        string levelCwd,
        string configDir,
        IReadOnlyDictionary<string, string> inputs)
    {
        ArgumentNullException.ThrowIfNull(script);

        return LaunchSpec.ForScript(script.Program, script.Args, script.Cwd, levels, levelCwd, configDir, inputs);
    }

    /// <summary>
    /// The environment levels of a chain of groups, outermost first, from <paramref name="startCwd"/>: for each group with values in
    /// <paramref name="scriptValues"/> a literal level (its environment script's output), then one level for its <c>env</c> when it has
    /// one. Each level's directory is that group's effective working directory. <paramref name="cwd"/> receives the effective working
    /// directory of the innermost group (<paramref name="startCwd"/> when there are none).
    /// </summary>
    internal static List<EnvLevel> GroupLevels(
        IReadOnlyList<GroupSpec> groups,
        IReadOnlyList<IReadOnlyDictionary<string, string?>?>? scriptValues,
        string startCwd,
        out string cwd)
    {
        var levels = new List<EnvLevel>();
        cwd = startCwd;
        for (int i = 0; i < groups.Count; i++)
        {
            GroupSpec group = groups[i];
            cwd = ConfigPaths.ResolveDir(group.Cwd, cwd);
            if (scriptValues?[i] is { } values)
            {
                levels.Add(new EnvLevel(values, cwd) { Literal = true });
            }

            if (group.Env is not null)
            {
                levels.Add(new EnvLevel(group.Env, cwd));
            }
        }

        return levels;
    }
}
