using Shared.Processes;

namespace Cmdr.Core.Session;

/// <summary>
/// Secret environment variables: the names that apply to a file, a group or a command, with the names their values derive from a secret
/// (see <see cref="SecretNames.Extend"/>). The UI masks these values and the copy leaves them out.
/// </summary>
/// <remarks>
/// The declared names of a file are its top-level <c>secret</c>; of a group, the <c>secret</c> of every group from the outermost down to
/// it, plus its file's; of a command, its own <c>secret</c> plus its group's. A base file's names reach the file that imports them only
/// through its <c>baseEnv</c>. The walk over the levels then adds every name whose value references a secret, in level order.
/// </remarks>
public sealed partial class Session
{
    /// <summary>
    /// The secret names that apply to a command: its file's and its groups' declared names and the names derived from them, with its own
    /// <c>secret</c> and <c>env</c> (see <see cref="SecretNames.Extend"/>).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
    public IReadOnlySet<string> SecretNamesFor(ItemEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_sync)
        {
            if (item is CommandEntry command)
            {
                return SecretsWalk(command.Source, ChainTo(command.Group), command.Spec.Env, command.Spec.Secret);
            }

            var group = item as GroupEntry ?? throw new ArgumentException("Unknown kind of item.", nameof(item));
            return SecretsWalk(group.Source, ChainTo(group), commandEnv: null, commandSecret: null);
        }
    }

    /// <summary>
    /// The secret names that apply to a file's own levels: its top-level <c>secret</c> and the names derived from them, including the names
    /// of its base that its <c>baseEnv</c> imports (see <see cref="SecretNames.Extend"/>).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public IReadOnlySet<string> SecretNamesFor(ConfigSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return FileSecretNames(source);
    }

    /// <summary>The names secret in a file's own levels (see <see cref="SecretNamesFor(ConfigSource)"/>).</summary>
    private static HashSet<string> FileSecretNames(ConfigSource file) =>
        SecretNames.Extend(file.EnvLevels, DeclaredNames(file.Secret), BaseSecretNames(file)).ToHashSet(SecretNames.Comparer);

    /// <summary>
    /// The names secret in the file a file imports from (empty when it imports none). An import level's keys are secret when they are in
    /// this set, which is the base's own full set: the names it declares and those it derives.
    /// </summary>
    private static IReadOnlySet<string> BaseSecretNames(ConfigSource file) =>
        file.BaseSource is { } baseSource ? FileSecretNames(baseSource) : new HashSet<string>(SecretNames.Comparer);

    /// <summary>
    /// The names secret in the levels of a group chain (or of a command in it: <paramref name="commandEnv"/> is its <c>env</c>), given the
    /// declared names of the file, of each group and of the command.
    /// </summary>
    private static IReadOnlySet<string> SecretsWalk(
        ConfigSource file,
        IReadOnlyList<GroupEntry> chain,
        IReadOnlyDictionary<string, string?>? commandEnv,
        IReadOnlyList<string>? commandSecret)
    {
        var declared = DeclaredNames(file.Secret);
        foreach (GroupEntry group in chain)
        {
            foreach (string name in group.Secret ?? Array.Empty<string>())
            {
                declared.Add(name);
            }
        }

        foreach (string name in commandSecret ?? Array.Empty<string>())
        {
            declared.Add(name);
        }

        var levels = new List<EnvLevel>(file.EnvLevels);
        foreach (GroupEntry group in chain)
        {
            if (group.Env is { } env)
            {
                levels.Add(new EnvLevel(env, group.Source.Dir));
            }
        }

        if (commandEnv is not null)
        {
            levels.Add(new EnvLevel(commandEnv, file.Dir));
        }

        return SecretNames.Extend(levels, declared, BaseSecretNames(file));
    }

    /// <summary>A new set of the given declared names, in the platform's comparison. Null gives an empty set.</summary>
    private static HashSet<string> DeclaredNames(IReadOnlyList<string>? names)
    {
        var set = new HashSet<string>(SecretNames.Comparer);
        foreach (string name in names ?? Array.Empty<string>())
        {
            set.Add(name);
        }

        return set;
    }
}
