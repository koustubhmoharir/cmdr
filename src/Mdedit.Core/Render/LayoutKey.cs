using System.Collections.Immutable;

namespace Mdedit.Core.Render;

/// <summary>
/// The identity of a block's layout in a <see cref="LayoutCache"/>: its source text, kind, heading level, language, container
/// chain, tightness, first-in-item flag and the width. The container chain is compared element by element, because
/// <see cref="ImmutableArray{T}"/> equality is by reference.
/// </summary>
internal sealed class LayoutKey : IEquatable<LayoutKey>
{
    private readonly string _text;
    private readonly BlockKind _kind;
    private readonly int _headingLevel;
    private readonly string? _language;
    private readonly ImmutableArray<Container> _containers;
    private readonly bool _tight;
    private readonly bool _isFirstInItem;
    private readonly int _width;
    private readonly string _references;

    /// <summary>Makes the key of a block at a width.</summary>
    /// <param name="text">The source text of the block, <c>document.Text[Start..End]</c>.</param>
    /// <param name="block">The block.</param>
    /// <param name="width">The screen width.</param>
    /// <param name="references">The document's <see cref="RenderDocument.ReferenceKey"/>, which decides how its links resolve.</param>
    public LayoutKey(string text, RenderBlock block, int width, string references)
    {
        _text = text;
        _references = references;
        _kind = block.Kind;
        _headingLevel = block.HeadingLevel;
        _language = block.Language;
        _containers = block.Containers;
        _tight = block.Tight;
        _isFirstInItem = block.IsFirstInItem;
        _width = width;
    }

    /// <summary>Whether the two keys lay out the same block the same way.</summary>
    public bool Equals(LayoutKey? other)
    {
        if (other is null
            || _width != other._width
            || _kind != other._kind
            || _headingLevel != other._headingLevel
            || _tight != other._tight
            || _isFirstInItem != other._isFirstInItem
            || !string.Equals(_text, other._text, StringComparison.Ordinal)
            || !string.Equals(_language, other._language, StringComparison.Ordinal)
            || !string.Equals(_references, other._references, StringComparison.Ordinal)
            || _containers.Length != other._containers.Length)
        {
            return false;
        }

        for (int index = 0; index < _containers.Length; index++)
        {
            if (!_containers[index].Equals(other._containers[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="obj"/> is a key equal to this one.</summary>
    public override bool Equals(object? obj) => Equals(obj as LayoutKey);

    /// <summary>A hash over the same fields that <see cref="Equals(LayoutKey?)"/> compares.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_text, StringComparer.Ordinal);
        hash.Add(_kind);
        hash.Add(_headingLevel);
        hash.Add(_language, StringComparer.Ordinal);
        hash.Add(_tight);
        hash.Add(_isFirstInItem);
        hash.Add(_width);
        hash.Add(_references, StringComparer.Ordinal);
        foreach (Container entry in _containers)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }
}
