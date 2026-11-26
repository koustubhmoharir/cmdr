namespace Mdedit.Core.Render;

/// <summary>
/// How the cells of a table column are aligned, from the delimiter row: <c>:--</c> left, <c>:-:</c> centred, <c>--:</c> right.
/// A delimiter with no colon at either end is left aligned.
/// </summary>
public enum ColumnAlignment
{
    /// <summary>The text starts at the left edge of the column.</summary>
    Left,

    /// <summary>The text is centred; when the free space is odd, the extra column goes on the right.</summary>
    Center,

    /// <summary>The text ends at the right edge of the column.</summary>
    Right,
}
