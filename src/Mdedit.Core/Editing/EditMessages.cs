namespace Mdedit.Core.Editing;

/// <summary>The messages of the refused rendered-view edits.</summary>
internal static class EditMessages
{
    /// <summary>An edit that would change the structure or the visible text, or that the verification could not confirm.</summary>
    public const string CantEdit = "Can't edit this here; Ctrl+T edits the source";

    /// <summary>The caret is not on text the view can edit (a synthetic position, or a block the engine does not handle).</summary>
    public const string NoPlace = "Can't type here";

    /// <summary>A table cell: typing is the table editor's until M6c.</summary>
    public const string Table = "F2 edits a table cell";

    /// <summary>A selection that starts and ends in different blocks.</summary>
    public const string AcrossBlocks = "Deleting across blocks isn't supported yet; Ctrl+T edits the source";

    /// <summary>A typed line break, which is Enter, or a pasted one (paste is M6c).</summary>
    public const string LineBreak = "A line break isn't supported yet; Ctrl+T edits the source";

    /// <summary>A join of two blocks that would change their structure (their containers, or a code block, table or HTML block).</summary>
    public const string CantJoin = "Can't join these blocks; Ctrl+T edits the source";

    /// <summary>A replacement with nothing selected and nothing typed.</summary>
    public const string NothingSelected = "Nothing is selected";
}
