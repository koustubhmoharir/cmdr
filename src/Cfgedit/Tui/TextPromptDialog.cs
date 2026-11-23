using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cfgedit.Tui;

/// <summary>
/// A dialog with one labelled text field: the attribute name of an add, or the root element of a new XML file. OK passes the text to the
/// apply function; an error it returns is shown above the buttons and the dialog stays open with the text as typed. Cancel and Esc close
/// it. OK is the default.
/// </summary>
internal sealed class TextPromptDialog : Dialog<bool>
{
    /// <summary>The width limit: the dialog is no wider than this, and no wider than the screen.</summary>
    internal const int MaxWidth = 60;

    /// <summary>The height the dialog asks for: the field and the error row under it, above the buttons.</summary>
    internal const int WantedHeight = 8;

    private readonly IApplication _app;
    private readonly Func<string, string?> _apply;
    private readonly Label _error;
    private readonly TextField _field;

    private TextPromptDialog(IApplication app, string title, string label, string text, Func<string, string?> apply)
    {
        _app = app;
        _apply = apply;
        Title = title;

        // Sized from the screen at every layout, so the dialog follows the terminal when it is resized.
        Width = Dim.Func(_ => Math.Max(1, Math.Min(MaxWidth, _app.Screen.Width)));
        Height = Dim.Func(_ => Math.Max(1, Math.Min(WantedHeight, _app.Screen.Height)));

        var prompt = new Label { X = 0, Y = 0, Width = Math.Max(1, label.Length + 2), Height = 1, Text = label + ":" };
        _field = new TextField { X = Math.Max(1, label.Length + 2), Y = 0, Width = Dim.Fill(), Height = 1, Text = text };
        // Right under the field: the buttons and their shadow cover the bottom rows of the content area, so a label anchored
        // to its end is hidden.
        _error = new Label { X = 0, Y = 1, Width = Dim.Fill(), Height = 1, Text = string.Empty };
        Add(prompt, _field, _error);

        var cancel = new Button { Text = "_Cancel" };
        var ok = new Button { Text = "_OK" };
        cancel.Accepting += (_, e) =>
        {
            e.Handled = true;
            Cancel();
        };
        AddButton(cancel);
        AddButton(ok);

        // Terminal.Gui makes the last button added the default; Enter means OK.
        ok.IsDefault = true;
        cancel.IsDefault = false;
        _field.SetFocus();
    }

    /// <summary>The error shown above the buttons, or empty when there is none. For the tests.</summary>
    internal string ErrorText => _error.Text;

    /// <summary>The field, for the tests.</summary>
    internal TextField Field => _field;

    /// <summary>
    /// Shows the dialog and returns when it closes. Returns true when the apply function accepted the text, and false when the dialog
    /// was cancelled.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="title">The title.</param>
    /// <param name="label">The field's label, such as "Name".</param>
    /// <param name="text">The text the field starts with.</param>
    /// <param name="apply">Applies the text. Returns the error to show, or null when the text was applied.</param>
    public static bool Ask(IApplication app, string title, string label, string text, Func<string, string?> apply)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(apply);
        using var dialog = new TextPromptDialog(app, title, label, text, apply);
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

        return base.OnKeyDown(key);
    }

    /// <summary>OK, or Enter in the field: apply the text, and close only when it is accepted.</summary>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        args.Handled = true;
        string? error = _apply(_field.Text);
        if (error is null)
        {
            Result = true;
            _app.RequestStop(this);
        }
        else
        {
            _error.Text = error;
        }

        return true;
    }

    private void Cancel()
    {
        Result = false;
        _app.RequestStop(this);
    }
}
