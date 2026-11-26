using Mdedit.Core.Render;

namespace Mdedit.Core.Tables;

/// <summary>The text of a delimiter-row cell for an alignment.</summary>
internal static class Delimiters
{
    /// <summary>
    /// The delimiter cell content with <paramref name="dashes"/> dashes and the alignment's colons: <c>---</c> for none, <c>:--</c>
    /// left, <c>:-:</c> centred, <c>--:</c> right.
    /// </summary>
    /// <param name="alignment">The alignment, or null for none.</param>
    /// <param name="dashes">The number of dashes, at least 1.</param>
    public static string Text(ColumnAlignment? alignment, int dashes)
    {
        string bar = new('-', dashes);
        return alignment switch
        {
            ColumnAlignment.Left => ":" + bar,
            ColumnAlignment.Right => bar + ":",
            ColumnAlignment.Center => ":" + bar + ":",
            _ => bar,
        };
    }

    /// <summary>The number of colons an alignment's delimiter has.</summary>
    public static int Colons(ColumnAlignment? alignment) => alignment switch
    {
        ColumnAlignment.Center => 2,
        null => 0,
        _ => 1,
    };
}
