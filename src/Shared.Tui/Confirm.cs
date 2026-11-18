using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Shared.Tui;

/// <summary>
/// Confirmation dialogs. The default button is the one the user initiated the action with: Terminal.Gui makes the last
/// button of a message box the default and focuses it, so the buttons are passed in reverse.
/// </summary>
public static class Confirm
{
    /// <summary>Asks a yes/no question with Yes as the default. Returns true only for Yes; Esc and No return false.</summary>
    public static bool Ask(IApplication app, string title, string message)
    {
        return Choose(app, title, message, "_Yes", "_No") == 0;
    }

    /// <summary>
    /// Shows one button per choice, <c>choices[0]</c> as the default. Returns the index into <paramref name="choices"/>
    /// of the button pressed, or null when the dialog was cancelled.
    /// </summary>
    public static int? Choose(IApplication app, string title, string message, params string[] choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Length == 0)
        {
            throw new ArgumentException("At least one choice is required.", nameof(choices));
        }

        string[] buttons = choices.AsEnumerable().Reverse().ToArray();
        int? button = MessageBox.Query(app, title, message, buttons);
        return ChoiceIndex(button, choices.Length);
    }

    /// <summary>
    /// Maps the index of a button of a message box shown with the choices in reverse order back to the index of the
    /// choice. Null (cancelled) stays null. Pure.
    /// </summary>
    public static int? ChoiceIndex(int? button, int count)
    {
        if (button is null)
        {
            return null;
        }

        if (button < 0 || button >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(button), button, "The button is not one of the choices.");
        }

        return count - 1 - button;
    }
}
