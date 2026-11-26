namespace Mdedit.Core.Render;

/// <summary>
/// One container around a leaf block, in <see cref="RenderBlock.Containers"/> (outermost first). The layout builds the
/// prefix of each line from these: a quote bar per quote or alert, the title of an alert and the marker of a list item.
/// <see cref="IsFirstLeaf"/> says whether the leaf is the first leaf of this container, which is where the marker and the
/// title go; the other leaves get the same width of blank prefix instead.
/// </summary>
/// <remarks>
/// Use the factory methods, which set only the fields their kind uses. The fields of the other kinds keep their defaults.
/// </remarks>
public readonly record struct Container
{
    /// <summary>What kind of container this is.</summary>
    public ContainerKind Kind { get; init; }

    /// <summary>Whether the leaf is the first leaf of this container, so that the layout draws its marker or title on it.</summary>
    public bool IsFirstLeaf { get; init; }

    /// <summary>The kind of an alert. Meaningful for <see cref="ContainerKind.Alert"/> only.</summary>
    public AlertKind Alert { get; init; }

    /// <summary>
    /// The number of bullet items this item is nested in, 0 for an item of an outermost bullet list. Only bullet items
    /// count, so an ordered list between two bullet lists does not change the depth. Meaningful for
    /// <see cref="ContainerKind.BulletItem"/> only.
    /// </summary>
    public int BulletDepth { get; init; }

    /// <summary>
    /// The offset of the item's marker, relative to the start of the block (so usually negative), for the source-mapped
    /// digits of an ordered item. Meaningful for <see cref="ContainerKind.OrderedItem"/> only.
    /// </summary>
    public int MarkerOffset { get; init; }

    /// <summary>
    /// The marker of an ordered item as written in the source: its digits and delimiter, such as <c>3.</c> or <c>3)</c>.
    /// Meaningful for <see cref="ContainerKind.OrderedItem"/> only; null for the other kinds.
    /// </summary>
    public string? Marker { get; init; }

    /// <summary>A block quote.</summary>
    /// <param name="isFirstLeaf">Whether the leaf is the first leaf of the quote.</param>
    public static Container Quote(bool isFirstLeaf) => new() { Kind = ContainerKind.Quote, IsFirstLeaf = isFirstLeaf };

    /// <summary>An alert of the given kind.</summary>
    /// <param name="alert">The kind of the alert.</param>
    /// <param name="isFirstLeaf">Whether the leaf is the first leaf of the alert, which gets the title.</param>
    public static Container ForAlert(AlertKind alert, bool isFirstLeaf) =>
        new() { Kind = ContainerKind.Alert, Alert = alert, IsFirstLeaf = isFirstLeaf };

    /// <summary>An item of a bullet list.</summary>
    /// <param name="bulletDepth">The number of bullet items the item is nested in.</param>
    /// <param name="isFirstLeaf">Whether the leaf is the first leaf of the item, which gets the bullet.</param>
    public static Container Bullet(int bulletDepth, bool isFirstLeaf) =>
        new() { Kind = ContainerKind.BulletItem, BulletDepth = bulletDepth, IsFirstLeaf = isFirstLeaf };

    /// <summary>An item of an ordered list.</summary>
    /// <param name="markerOffset">The offset of the marker's first digit, relative to the start of the block.</param>
    /// <param name="marker">The marker as written, such as <c>3.</c>.</param>
    /// <param name="isFirstLeaf">Whether the leaf is the first leaf of the item, which gets the marker.</param>
    public static Container Ordered(int markerOffset, string marker, bool isFirstLeaf) =>
        new() { Kind = ContainerKind.OrderedItem, MarkerOffset = markerOffset, Marker = marker, IsFirstLeaf = isFirstLeaf };
}
