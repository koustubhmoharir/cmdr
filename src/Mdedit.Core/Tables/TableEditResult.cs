namespace Mdedit.Core.Tables;

/// <summary>
/// The outcome of a table operation: one replacement of the source text and the cell the caret goes to, or a refusal with the
/// message to show. A refusal changes nothing; its other fields are zero.
/// </summary>
/// <param name="Start">The offset of the replaced text in the source.</param>
/// <param name="Length">The number of characters replaced.</param>
/// <param name="Text">The text that replaces them.</param>
/// <param name="Row">The row of the cell the caret goes to, 0 for the header.</param>
/// <param name="Column">The column of that cell.</param>
/// <param name="Refusal">The message of a refused operation, or null when it applies.</param>
public sealed record TableEditResult(int Start, int Length, string Text, int Row, int Column, string? Refusal = null)
{
    /// <summary>Whether the operation was refused.</summary>
    public bool IsRefused => Refusal is not null;

    /// <summary>A refused operation with its message.</summary>
    /// <param name="message">The message to show.</param>
    internal static TableEditResult Refuse(string message) => new(0, 0, string.Empty, 0, 0, message);
}
