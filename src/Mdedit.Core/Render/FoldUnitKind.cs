namespace Mdedit.Core.Render;

/// <summary>The kinds of fold unit. See <see cref="FoldUnit"/>.</summary>
public enum FoldUnitKind
{
    /// <summary>A heading that is not in a container, with the blocks under it up to the next heading of the same or a higher level.</summary>
    Section,

    /// <summary>A fenced or indented code block, folded to its header line.</summary>
    Code,
}
