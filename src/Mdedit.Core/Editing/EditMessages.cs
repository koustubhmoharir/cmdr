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

    /// <summary>Backspace with nothing before the caret in its line.</summary>
    public const string BackspaceAtStart = "Backspace at a block's start isn't supported yet; Ctrl+T edits the source";

    /// <summary>Delete with nothing after the caret in its line.</summary>
    public const string DeleteAtEnd = "Delete at a block's end isn't supported yet; Ctrl+T edits the source";

    /// <summary>A selection that starts and ends in different blocks.</summary>
    public const string AcrossBlocks = "Deleting across blocks isn't supported yet; Ctrl+T edits the source";

    /// <summary>A selection that spans a hard line break inside one block.</summary>
    public const string AcrossLines = "Deleting across a line break isn't supported yet; Ctrl+T edits the source";

    /// <summary>A typed line break, which is Enter.</summary>
    public const string LineBreak = "A line break isn't supported yet; Ctrl+T edits the source";

    /// <summary>A removal that takes all the text of a block, which would remove the block itself: its merging is M6b's.</summary>
    public const string BlockEmptied = "Deleting a block's last character isn't supported yet; Ctrl+T edits the source";

    /// <summary>A replacement with nothing selected and nothing typed.</summary>
    public const string NothingSelected = "Nothing is selected";
}
