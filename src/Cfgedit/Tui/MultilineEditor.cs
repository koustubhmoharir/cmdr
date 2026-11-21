using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

// TextView is marked obsolete in Terminal.Gui 2.5.0; cmdr's dialogs use it for their multi-line fields (see CommandDialog).
#pragma warning disable CS0618

namespace Cfgedit.Tui;

/// <summary>
/// The multi-line editor of a value: a text area over almost the whole screen, with Cancel and OK (OK is the default). Ctrl+Enter
/// and F2 commit as OK does, and Esc cancels. Commit runs the apply function; when it returns an error, the error is shown above the
/// buttons and the dialog stays open with the text as typed. Enter adds a line, as the text area needs.
/// </summary>
internal sealed class MultilineEditor : Dialog<bool>
{
    private readonly IApplication _app;
    private readonly TextView _area;
    private readonly Label _error;
    private readonly Func<string, string?> _apply;

    private MultilineEditor(IApplication app, string title, string text, Func<string, string?> apply)
    {
        _app = app;
        _apply = apply;
        Title = title;

        // Sized from the screen at every layout, so the dialog follows the terminal when it is resized. The height is the whole
        // screen: the frame and the button area take five rows, and the text area and the error label take the rest.
        Width = Dim.Func(_ => Math.Max(1, _app.Screen.Width - 2));
        Height = Dim.Func(_ => Math.Max(1, _app.Screen.Height));

        _area = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Multiline = true,
            TabKeyAddsTab = false,
            Text = text,
        };
        _error = new Label { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1, Text = string.Empty };
        Add(_area, _error);

        var cancel = new Button { Text = "_Cancel" };
        var ok = new Button { Text = "_OK" };
        cancel.Accepting += (_, e) =>
        {
            e.Handled = true;
            Cancel();
        };
        ok.Accepting += (_, e) =>
        {
            e.Handled = true;
            Commit();
        };
        AddButton(cancel);
        AddButton(ok);

        // Terminal.Gui makes the last button added the default; the OK button is the one Enter-style commits mean.
        ok.IsDefault = true;
        cancel.IsDefault = false;
    }

    /// <summary>The error shown above the buttons, or empty when there is none. For the tests.</summary>
    internal string ErrorText => _error.Text;

    /// <summary>The text area, for the tests.</summary>
    internal TextView Area => _area;

    /// <summary>
    /// Shows the editor and returns when it closes. Returns true when the text was committed (or the apply found nothing to change), and
    /// false when it was cancelled.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="title">The title, such as "Edit string $.name".</param>
    /// <param name="text">The text the area starts with.</param>
    /// <param name="apply">Applies the committed text. Returns the error to show, or null when the text was applied.</param>
    public static bool Ask(IApplication app, string title, string text, Func<string, string?> apply)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(apply);
        using var dialog = new MultilineEditor(app, title, text, apply);
        app.Run(dialog);
        return dialog.Result == true;
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            Cancel();
            return true;
        }

        if (key == Key.Enter.WithCtrl || key == Key.F2)
        {
            Commit();
            return true;
        }

        return base.OnKeyDown(key);
    }

    private void Commit()
    {
        string? error = _apply(EditRules.TextOf(_area));
        if (error is null)
        {
            Result = true;
            _app.RequestStop(this);
            return;
        }

        _error.Text = error;
    }

    private void Cancel()
    {
        Result = false;
        _app.RequestStop(this);
    }
}
