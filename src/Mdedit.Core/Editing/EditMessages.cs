namespace Mdedit.Core.Editing;

/// <summary>The messages of the refused rendered-view edits.</summary>
internal static class EditMessages
{
    /// <summary>An edit that would change the structure or the visible text, or that the verification could not confirm.</summary>
    public const string CantEdit = "Can't edit this here; Ctrl+T edits the source";

    /// <summary>The caret is not on text the view can edit (a synthetic position, or a block the engine does not handle).</summary>
    public const string NoPlace = "Can't type here";

    /// <summary>A selection that starts and ends in different table cells.</summary>
    public const string CellSelection = "A selection can't cover more than one table cell; Ctrl+T edits the source";

    /// <summary>Pasted text with a line break into a table cell, which holds one line.</summary>
    public const string CellOneLine = "A table cell takes one line of text; Ctrl+T edits the source";

    /// <summary>A selection that ends or starts inside a code block, HTML or raw block, or table without covering it wholly.</summary>
    public const string WholeOnly = "A code block or table can only be deleted whole; Ctrl+T edits the source";

    /// <summary>A typed line break, which is Enter, or a line break typed in some other way.</summary>
    public const string LineBreak = "A line break isn't supported here; Ctrl+T edits the source";

    /// <summary>A join of two blocks that would change their structure (their containers, or a code block, table or HTML block).</summary>
    public const string CantJoin = "Can't join these blocks; Ctrl+T edits the source";

    /// <summary>A replacement with nothing selected and nothing typed.</summary>
    public const string NothingSelected = "Nothing is selected";
}
