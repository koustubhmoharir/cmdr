namespace Cmdr.Tui;

/// <summary>The pane that has the focus, as far as the copy hint is concerned.</summary>
internal enum CopyPane
{
    /// <summary>No pane that copies with c.</summary>
    None,

    /// <summary>The command list.</summary>
    List,

    /// <summary>The info pane (DetailsView).</summary>
    Details,

    /// <summary>The output pane (OutputView).</summary>
    Output,
}

/// <summary>
/// The "c" part of the hint bar: names what c copies in the focused pane, or null when c copies nothing there. Mirrors the
/// copy rules of each view: the list copies the command line of a command row; the info pane copies its selection, else the
/// command line of a command row; the output pane copies its selection, else the cursor line when there is a line. Pure.
/// </summary>
internal static class CopyHint
{
    /// <summary>
    /// "c Copy command", "c Copy selection", "c Copy line", or null. <paramref name="hasSelection"/> is only read for the info
    /// and output panes; <paramref name="hasCommand"/> for the list and info panes; <paramref name="hasLine"/> for the output pane.
    /// </summary>
    public static string? Describe(CopyPane pane, bool hasSelection, bool hasCommand, bool hasLine)
    {
        switch (pane)
        {
            case CopyPane.List:
                return hasCommand ? "c Copy command" : null;
            case CopyPane.Details:
                if (hasSelection)
                {
                    return "c Copy selection";
                }

                return hasCommand ? "c Copy command" : null;
            case CopyPane.Output:
                if (hasSelection)
                {
                    return "c Copy selection";
                }

                return hasLine ? "c Copy line" : null;
            default:
                return null;
        }
    }
}
