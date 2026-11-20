using System.Diagnostics.CodeAnalysis;

namespace Cmdr.Core.Session;

/// <summary>
/// The variables that environment scripts produced, by script owner: a <see cref="ConfigSource"/> (a file's <c>envScript</c>) or a
/// <see cref="GroupEntry"/> (a group's <c>envScript</c>). Owners are compared by reference. Immutable: <see cref="With"/> returns a
/// new instance, so a launch or a preparation keeps the values it started with.
/// </summary>
public sealed class ScriptValues
{
    private readonly Dictionary<object, IReadOnlyDictionary<string, string?>> _values;

    private ScriptValues(Dictionary<object, IReadOnlyDictionary<string, string?>> values)
    {
        _values = values;
    }

    /// <summary>No values: no script has produced any.</summary>
    public static ScriptValues Empty { get; } = new(new Dictionary<object, IReadOnlyDictionary<string, string?>>(ReferenceEqualityComparer.Instance));

    /// <summary>
    /// The variables the script of <paramref name="owner"/> produced, when it has produced them. A null value removes the variable.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is null.</exception>
    public bool TryGet(object owner, [MaybeNullWhen(false)] out IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return _values.TryGetValue(owner, out values);
    }

    /// <summary>A new instance with the values of <paramref name="owner"/> set to a copy of <paramref name="values"/>, replacing any before.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal ScriptValues With(object owner, IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(values);

        var copy = new Dictionary<object, IReadOnlyDictionary<string, string?>>(_values, ReferenceEqualityComparer.Instance)
        {
            [owner] = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        };
        return new ScriptValues(copy);
    }
}
