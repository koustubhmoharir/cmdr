namespace Mdedit.Core.Tests.Editing;

/// <summary>
/// The end stop of a non-empty table cell, as edits see it: typing at it appends to the cell without growing a link that ends the cell, and
/// Backspace at it removes the cell's last character.
/// </summary>
public sealed class CellEndStopEditTests
{
    // The cell "[go](u)" ends at 5, after the "o" and before the "]": the stop is there and the link's destination is after it.
    private const string Link = "| [go](u) | x |\n|---|---|\n";

    [Fact]
    public void Typing_at_the_end_of_a_cell_that_ends_in_a_link_appends_after_the_link()
    {
        Assert.Equal(new EditCase.Outcome("| [go](u)x | x |\n|---|---|\n", 10), EditCase.Type(Link, 5, "x"));
    }

    [Fact]
    public void Backspace_at_the_end_of_a_cell_that_ends_in_a_link_removes_the_last_character_of_the_link_text()
    {
        Assert.Equal(new EditCase.Outcome("| [g](u) | x |\n|---|---|\n", 4), EditCase.Backspace(Link, 5));
    }
}
