namespace Mdedit.Core.Render;

/// <summary>The kinds of container a leaf block can sit in. See <see cref="Container"/>.</summary>
public enum ContainerKind
{
    /// <summary>A block quote, <c>&gt; text</c>.</summary>
    Quote,

    /// <summary>An alert, a block quote that starts with <c>[!NOTE]</c> or one of the other alert kinds.</summary>
    Alert,

    /// <summary>An item of a bullet list.</summary>
    BulletItem,

    /// <summary>An item of an ordered list, <c>1.</c> or <c>1)</c>.</summary>
    OrderedItem,
}
