using System.Collections.ObjectModel;

namespace Cfgedit.Core.Model;

/// <summary>
/// The path of a node from the document node: an immutable list of steps. A path identifies a node across reparses, so the
/// editor can keep its cursor and collapsed rows as paths. Two paths are equal when their steps are equal.
/// </summary>
/// <remarks>
/// <c>ToString</c> writes each step after a slash, for example <c>/Member("servers", 0)/Item(1)</c>. The document node's
/// path is <c>/</c>.
/// </remarks>
public sealed class NodePath : IEquatable<NodePath>
{
    private readonly ReadOnlyCollection<PathStep> _steps;

    /// <summary>The path of the document node: no steps.</summary>
    public static NodePath Empty { get; } = new([]);

    /// <summary>Creates a path from its steps, from the document node down.</summary>
    /// <param name="steps">The steps. None may be null.</param>
    public NodePath(IEnumerable<PathStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        PathStep[] array = steps.ToArray();
        foreach (PathStep step in array)
        {
            ArgumentNullException.ThrowIfNull(step, nameof(steps));
        }

        _steps = Array.AsReadOnly(array);
    }

    /// <summary>The steps, from the document node down.</summary>
    public IReadOnlyList<PathStep> Steps => _steps;

    /// <summary>The number of steps. Zero for the document node.</summary>
    public int Count => _steps.Count;

    /// <summary>Returns the path of a child of this node.</summary>
    public NodePath Append(PathStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return new NodePath(_steps.Append(step));
    }

    /// <summary>Returns the first <paramref name="count"/> steps of this path.</summary>
    public NodePath Prefix(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _steps.Count);
        return new NodePath(_steps.Take(count));
    }

    /// <inheritdoc/>
    public bool Equals(NodePath? other)
    {
        return other is not null && _steps.SequenceEqual(other._steps);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is NodePath other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (PathStep step in _steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        if (_steps.Count == 0)
        {
            return "/";
        }

        return string.Concat(_steps.Select(step => "/" + step));
    }

    /// <summary>Equality by value.</summary>
    public static bool operator ==(NodePath? left, NodePath? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    /// <summary>Inequality by value.</summary>
    public static bool operator !=(NodePath? left, NodePath? right)
    {
        return !(left == right);
    }
}
