namespace Cmdr.Tui;

/// <summary>The order in which Tab and Shift+Tab move the focus between the panes. Pure.</summary>
internal static class FocusCycle
{
    /// <summary>
    /// The index of the pane that gets the focus after the pane at <paramref name="current"/> (-1 when none of the panes has it),
    /// going forward, or backward with <paramref name="backward"/>. Wraps at both ends.
    /// </summary>
    public static int Next(int current, int count, bool backward)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0 || current >= count)
        {
            return 0;
        }

        return (current + (backward ? count - 1 : 1)) % count;
    }
}
