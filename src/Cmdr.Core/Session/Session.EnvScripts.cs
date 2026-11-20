using System.Collections;
using System.Globalization;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;

namespace Cmdr.Core.Session;

/// <summary>
/// Environment scripts: which scripts a set of commands needs (their owners, in dependency order), running them with the environment
/// their position gives them, and the cache of their results (see <see cref="EnvScriptCache"/>).
/// </summary>
/// <remarks>
/// A command needs the scripts of the files whose levels reach its environment, and of the groups that contain it. A file's levels reach
/// the command when the file is its own file, or is the base of a file whose <c>baseEnv</c> imports from it (recursively). Owners run base
/// file first, then groups by depth, so every script sees the values of the scripts that its environment includes.
/// </remarks>
public sealed partial class Session
{
    /// <summary>Between the names of a group path in a label. The same separator as the UI's group paths.</summary>
    private const string GroupPathSeparator = " › ";

    /// <summary>How many lines of a failed script's standard error are written to each affected command.</summary>
    private const int StdErrTailLines = 5;

    private readonly EnvScriptCache _envCache = new();

    /// <summary>Runs the environment scripts. Internal so that tests can replace it.</summary>
    internal EnvScriptRunner EnvRunner { get; set; } = new EnvScriptRunner();

    /// <summary>True when any of the commands needs an environment script to run first (see <see cref="PrepareEnvAsync"/>).</summary>
    public bool NeedsEnvScripts(IReadOnlyList<CommandEntry> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        lock (_sync)
        {
            return OwnersOf(commands).Count > 0;
        }
    }

    /// <summary>
    /// Runs the environment scripts that the commands need, one at a time in dependency order, each in the environment its position gives
    /// it, and returns the values they produced. A script whose cached result can be reused is not run again (see <see cref="EnvScriptCache"/>).
    /// Stops at the first script that fails: its label, error and stderr are returned, and each command that needs it gets the failure
    /// written to its output. Nothing is written when cancelled.
    /// </summary>
    /// <param name="commands">The commands about to start.</param>
    /// <param name="inputs">The values of the input placeholders the scripts' arguments and environment use. Null means no values.</param>
    /// <param name="cancellationToken">Cancels the scripts and the task. The task is then cancelled, and the scripts are killed.</param>
    /// <returns>The preparation, or a cancelled task. Completes at once when no script is needed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commands"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// (Faulted task.) An input that a script's arguments or environment use has no value, or an empty one where it is not allowed.
    /// </exception>
    public Task<EnvPreparation> PrepareEnvAsync(
        IReadOnlyList<CommandEntry> commands,
        IReadOnlyDictionary<string, string>? inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<EnvPreparation>(cancellationToken);
        }

        IReadOnlyDictionary<string, string> frozen = Freeze(inputs);
        List<ScriptOwner> owners;
        var affected = new Dictionary<object, List<CommandEntry>>(ReferenceEqualityComparer.Instance);
        lock (_sync)
        {
            owners = OwnersOf(commands);
            foreach (ScriptOwner owner in owners)
            {
                affected[owner.Reference] = new List<CommandEntry>();
            }

            foreach (CommandEntry command in commands)
            {
                foreach (ScriptOwner owner in OwnersOf(new[] { command }))
                {
                    if (!affected[owner.Reference].Contains(command))
                    {
                        affected[owner.Reference].Add(command);
                    }
                }
            }
        }

        if (owners.Count == 0)
        {
            return Task.FromResult(new EnvPreparation(ScriptValues.Empty, null, null, "", Array.Empty<CommandEntry>()));
        }

        return RunOwnersAsync(owners, affected, frozen, cancellationToken);
    }

    /// <summary>
    /// Forgets the completed results of the scripts that the item needs, so that their next preparation runs them again. For a group: its
    /// file's scripts and its own and its ancestors' scripts. For a command: as <see cref="PrepareEnvAsync"/> would run them.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public void RefreshEnvScripts(ItemEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);

        List<object> references;
        lock (_sync)
        {
            references = OwnersOf(new[] { item }).Select(owner => owner.Reference).ToList();
        }

        foreach (object reference in references)
        {
            _envCache.Invalidate(reference);
        }
    }

    /// <summary>
    /// The last run of an environment script, successful or not, and when it finished, or null when it has not run. <paramref name="owner"/>
    /// is the <see cref="ConfigSource"/> or <see cref="GroupEntry"/> that owns the script. Cancelled runs are not recorded.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
    public (DateTimeOffset FinishedAt, EnvScriptResult Result)? LastEnvScriptRun(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return _envCache.LastRun(owner);
    }

    /// <summary>
    /// The levels <paramref name="owner"/>'s script runs in, given the values produced so far, and the directory its <c>cwd</c> is relative
    /// to. A file's script sees its base's levels only; a group's script sees the file's levels and the groups above it, not its own <c>env</c>.
    /// </summary>
    private static (IReadOnlyList<EnvLevel> Levels, string LevelCwd) LevelsBefore(ScriptOwner owner, ScriptValues values)
    {
        if (owner.Chain.Count == 0)
        {
            // A file's script sees only its base's levels: the file's own top-level env applies after it.
            return (owner.File.ImportLevelsWith(values), owner.File.Dir);
        }

        int last = owner.Chain.Count - 1;
        var levels = new List<EnvLevel>(owner.File.EnvLevelsWith(values));
        var outerValues = owner.Chain.Take(last)
            .Select(group => values.TryGet(group, out IReadOnlyDictionary<string, string?>? produced) ? produced : null)
            .ToList();
        levels.AddRange(LaunchSpec.GroupLevels(owner.ChainSpecs.Take(last).ToList(), outerValues, owner.File.Dir, out _));
        LaunchSpec.GroupLevels(owner.ChainSpecs, null, owner.File.Dir, out string levelCwd);
        return (levels, levelCwd);
    }

    /// <summary>Runs the owners in order, stopping at the first failure. Each owner's result feeds the environment of the next.</summary>
    private async Task<EnvPreparation> RunOwnersAsync(
        List<ScriptOwner> owners,
        Dictionary<object, List<CommandEntry>> affected,
        IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken)
    {
        ScriptValues values = ScriptValues.Empty;
        foreach (ScriptOwner owner in owners)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (IReadOnlyList<EnvLevel> levels, string levelCwd) = LevelsBefore(owner, values);
            Placeholders.CheckInputs(owner.Script.Args.Concat(LevelTexts(levels)), inputs);
            LaunchSpec launch = LaunchSpec.ForEnvScript(owner.Script, levels, levelCwd, owner.File.Dir, inputs);
            string fingerprint = EnvScriptFingerprint(owner.Script, launch, levels, inputs);
            TimeSpan timeout = TimeSpan.FromSeconds(owner.Script.TimeoutSeconds ?? EnvScriptSpec.DefaultTimeoutSeconds);

            EnvScriptResult result = await _envCache.GetAsync(
                owner.Reference,
                fingerprint,
                owner.Script.Rerun,
                token => EnvRunner.RunAsync(launch, timeout, token),
                cancellationToken);

            if (!result.Succeeded)
            {
                return ReportFailure(owner, result, values, affected);
            }

            values = values.With(owner.Reference, result.Values!);
        }

        return new EnvPreparation(values, null, null, "", Array.Empty<CommandEntry>());
    }

    /// <summary>Writes the failure to each command that needs the script, and returns it as the preparation's result.</summary>
    private static EnvPreparation ReportFailure(
        ScriptOwner owner,
        EnvScriptResult result,
        ScriptValues values,
        Dictionary<object, List<CommandEntry>> affected)
    {
        string error = result.Error ?? "the script failed";
        string[] stdErrTail = result.StdErr.Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0)
            .TakeLast(StdErrTailLines)
            .ToArray();
        string message = string.Join("\n", new[] { $"env script of {owner.Label} failed: {error}" }.Concat(stdErrTail));

        IReadOnlyList<CommandEntry> commands = affected[owner.Reference].AsReadOnly();
        foreach (CommandEntry command in commands)
        {
            command.Process.Output.WriteSystem(message);
        }

        return new EnvPreparation(values, owner.Label, error, result.StdErr, commands);
    }

    /// <summary>
    /// The fingerprint of a run: the script's program, its arguments as written, its working directory, timeout and rerun, the environment
    /// it runs in (this process's, with the levels before it and the inputs, sorted by name), and the values of the inputs its arguments use.
    /// Two runs with equal fingerprints would see the same thing.
    /// </summary>
    private static string EnvScriptFingerprint(
        EnvScriptSpec script,
        LaunchSpec launch,
        IReadOnlyList<EnvLevel> levels,
        IReadOnlyDictionary<string, string> inputs)
    {
        var environment = new Dictionary<string, string?>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = (string?)entry.Value;
        }

        EnvExpansion.ApplyLevels(environment, levels, launch.ConfigDir, Path.PathSeparator, inputs);

        var fields = new List<string?>
        {
            script.Program,
            script.Args.Count.ToString(CultureInfo.InvariantCulture),
        };
        fields.AddRange(script.Args);
        fields.Add(launch.WorkingDirectory);
        fields.Add((script.TimeoutSeconds ?? EnvScriptSpec.DefaultTimeoutSeconds).ToString(CultureInfo.InvariantCulture));
        fields.Add(script.Rerun.ToString());
        fields.Add(environment.Count.ToString(CultureInfo.InvariantCulture));
        foreach (string name in environment.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            fields.Add(name);
            fields.Add(environment[name]);
        }

        List<string> used = Placeholders.Requests(script.Args).Select(request => request.Name).OrderBy(name => name, StringComparer.Ordinal).ToList();
        fields.Add(used.Count.ToString(CultureInfo.InvariantCulture));
        foreach (string name in used)
        {
            fields.Add(name);
            fields.Add(inputs.TryGetValue(name, out string? value) ? value : null);
        }

        return EnvScriptCache.Fingerprint(fields);
    }

    /// <summary>
    /// The environment-script owners of the items, each once, in dependency order: the file scripts base-most first, then the group scripts
    /// by depth (ties in the order given). An item is a command (its file and the groups above it) or a group (its file, and it and its
    /// ancestors). Caller holds <c>_sync</c> when the tree may change.
    /// </summary>
    private static List<ScriptOwner> OwnersOf(IEnumerable<ItemEntry> items)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var fileOwners = new List<ScriptOwner>();
        var groupOwners = new List<ScriptOwner>();
        foreach (ItemEntry item in items)
        {
            foreach (ConfigSource file in FileOwnersOf(item.Source))
            {
                if (seen.Add(file))
                {
                    fileOwners.Add(FileOwner(file));
                }
            }

            GroupEntry? leaf = item as GroupEntry ?? (item as CommandEntry)?.Group;
            if (leaf is null)
            {
                continue;
            }

            List<GroupEntry> chain = ChainTo(leaf);
            for (int i = 0; i < chain.Count; i++)
            {
                if (chain[i].EnvScript is not null && seen.Add(chain[i]))
                {
                    groupOwners.Add(GroupOwner(chain.GetRange(0, i + 1)));
                }
            }
        }

        return fileOwners.OrderBy(owner => BaseDepth(owner.File))
            .Concat(groupOwners.OrderBy(owner => owner.Chain.Count))
            .ToList();
    }

    /// <summary>
    /// The files whose scripts reach a command of <paramref name="file"/>, base-most first: the file itself when it has a script, after
    /// the files its <c>baseEnv</c> imports from (recursively) when it has one.
    /// </summary>
    private static List<ConfigSource> FileOwnersOf(ConfigSource file)
    {
        var owners = new List<ConfigSource>();
        if (file.BaseSource is { } baseSource && file.BaseEnv is { Count: > 0 })
        {
            owners.AddRange(FileOwnersOf(baseSource));
        }

        if (file.EnvScript is not null)
        {
            owners.Add(file);
        }

        return owners;
    }

    /// <summary>The number of files a file imports, directly or not: 0 for a base file that imports none. Orders base files first.</summary>
    private static int BaseDepth(ConfigSource file)
    {
        int depth = 0;
        for (ConfigSource? baseSource = file.BaseSource; baseSource is not null; baseSource = baseSource.BaseSource)
        {
            depth++;
        }

        return depth;
    }

    /// <summary>The groups from the outermost down to <paramref name="leaf"/>, inclusive.</summary>
    private static List<GroupEntry> ChainTo(GroupEntry leaf)
    {
        var chain = new List<GroupEntry>();
        for (GroupEntry? group = leaf; group is not null; group = group.Parent)
        {
            chain.Insert(0, group);
        }

        return chain;
    }

    private static ScriptOwner FileOwner(ConfigSource file) =>
        new(file, file, Array.Empty<GroupEntry>(), Array.Empty<GroupSpec>(), file.EnvScript!, $"file '{file.DisplayPath}'");

    /// <summary>The script of the last group of <paramref name="chain"/>; the chain is its outermost-first path, so its levels see the groups above it.</summary>
    private static ScriptOwner GroupOwner(List<GroupEntry> chain)
    {
        GroupEntry group = chain[^1];
        string path = string.Join(GroupPathSeparator, chain.Select(entry => entry.Name));
        return new ScriptOwner(
            group,
            group.Source,
            chain,
            chain.Select(entry => entry.ToSpec()).ToList(),
            group.EnvScript!.Clone(),
            $"group '{path}'");
    }

    /// <summary>
    /// The file and group values one command's launch is built from (see <see cref="LaunchSpec.From"/>): the file's levels with
    /// <paramref name="values"/>, and for each group above the command (outermost first) its values, or null when it has none. Caller holds
    /// <c>_sync</c>, since it reads the tree.
    /// </summary>
    internal static (IReadOnlyList<EnvLevel> FileLevels, IReadOnlyList<IReadOnlyDictionary<string, string?>?> GroupValues) EnvironmentFor(
        CommandEntry entry,
        ScriptValues values)
    {
        var groupValues = new List<IReadOnlyDictionary<string, string?>?>();
        foreach (GroupEntry group in ChainTo(entry.Group))
        {
            groupValues.Add(values.TryGet(group, out IReadOnlyDictionary<string, string?>? produced) ? produced : null);
        }

        return (entry.Source.EnvLevelsWith(values), groupValues);
    }

    /// <summary>The argument texts of the environment-script owners of a command, which its inputs are asked for.</summary>
    private static IEnumerable<string> OwnerArgs(CommandEntry command) =>
        OwnersOf(new[] { command }).SelectMany(owner => owner.Script.Args);

    /// <summary>
    /// An environment script owner as it was when the preparation started: its reference (the owner compared by reference), its file,
    /// the groups from the outermost down to it (empty for a file), their specs, its script and the label used in messages.
    /// </summary>
    private sealed record ScriptOwner(
        object Reference,
        ConfigSource File,
        IReadOnlyList<GroupEntry> Chain,
        IReadOnlyList<GroupSpec> ChainSpecs,
        EnvScriptSpec Script,
        string Label);
}
