using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Logvu.Tui;

/// <summary>
/// The modal window shown while a config's <c>envScript</c> runs for the picker. Esc or Cancel cancels the script and the text says
/// so; the window stays until the script has ended, so that it is dead before the picker goes on. Its owner closes it with
/// <see cref="Finish"/>, on the UI thread, when the script's task has completed.
/// </summary>
internal sealed class EnvScriptWaitDialog : Dialog<bool>
{
    private readonly IApplication _app;
    private readonly CancellationTokenSource _cancel;
    private readonly Label _text;

    /// <summary>Creates the window for the script of a config file.</summary>
    /// <param name="app">The running application. The window stops it.</param>
    /// <param name="fileName">The name of the config file, shown in the text.</param>
    /// <param name="cancel">Cancelled by Esc and Cancel; the script stops when its resolution sees it.</param>
    public EnvScriptWaitDialog(IApplication app, string fileName, CancellationTokenSource cancel)
    {
        _app = app;
        _cancel = cancel;

        Title = "Open";
        // 60 columns and 7 rows fit an 80x24 terminal. The text has two rows, above the button row: a long file name wraps onto the
        // second row rather than being cut off.
        Width = 60;
        Height = 7;

        _text = new Label { X = 1, Y = 0, Width = Dim.Fill(1), Height = 2 };
        _text.TextFormatter.WordWrap = true;
        Add(_text);
        _text.Text = $"Running envScript of {fileName}…";

        var cancelButton = new Button { Text = "_Cancel", IsDefault = true };
        cancelButton.Accepting += (_, e) =>
        {
            // Stays open: Cancelling is shown until the script has ended and Finish closes the window.
            e.Handled = true;
            RequestCancel();
        };
        AddButton(cancelButton);
    }

    /// <summary>True once <see cref="Finish"/> has closed the window.</summary>
    public bool Finished { get; private set; }

    /// <summary>The text shown while the script runs.</summary>
    internal string ShownText => _text.Text;

    /// <summary>The button that cancels the script.</summary>
    internal Button CancelButton => (Button)Buttons.Single();

    /// <summary>
    /// Esc cancels as Cancel does. It is handled here, so that the dialog's own Esc binding does not close the window before the
    /// script has ended.
    /// </summary>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            RequestCancel();
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <summary>Cancels the script and says so. A second cancel does nothing. Call on the UI thread.</summary>
    public void RequestCancel()
    {
        if (Finished || _cancel.IsCancellationRequested)
        {
            return;
        }

        _cancel.Cancel();
        _text.Text = "Cancelling…";
    }

    /// <summary>Closes the window: the script has ended. Call on the UI thread.</summary>
    public void Finish()
    {
        if (Finished)
        {
            return;
        }

        Finished = true;
        Result = true;
        _app.RequestStop(this);
    }
}
