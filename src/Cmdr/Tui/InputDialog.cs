using Cmdr.Core.Config;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cmdr.Tui;

/// <summary>
/// Asks for the values of the input placeholders of the commands about to start. One field per input, in order, all
/// empty. OK validates (errors are shown and the dialog stays open) and returns the values by name. Esc or Cancel returns
/// null and nothing starts.
/// </summary>
internal sealed class InputDialog : Dialog<IReadOnlyDictionary<string, string>>
{
    private const int MinFieldWidth = 30;
    private const int MaxFieldWidth = 60;

    private readonly IApplication _app;
    private readonly IReadOnlyList<InputPlaceholders.InputRequest> _requests;
    private readonly TextField[] _fields;

    /// <param name="app">The application, for the message boxes and the screen size.</param>
    /// <param name="what">What is being started, shown in the title: a command name, a group path or "N commands".</param>
    /// <param name="requests">The inputs, in order. Must not be empty.</param>
    private InputDialog(IApplication app, string what, IReadOnlyList<InputPlaceholders.InputRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            throw new ArgumentException("At least one input is needed.", nameof(requests));
        }

        _app = app;
        _requests = requests;
        Title = "Inputs: " + what;

        // Labels are the names; optional inputs say so. The fields follow the longest label.
        List<string> labels = requests.Select(LabelOf).ToList();
        int labelWidth = labels.Max(label => label.Length) + 1;
        int fieldWidth = Math.Clamp(_app.Screen.Width - labelWidth - 6, MinFieldWidth, MaxFieldWidth);
        Width = Math.Min(labelWidth + fieldWidth + 4, _app.Screen.Width);

        // Two border rows and the button area (a blank row, the buttons and their shadow). Fields beyond the screen height are not shown.
        Height = Math.Min(requests.Count + 6, _app.Screen.Height);

        _fields = new TextField[requests.Count];
        var views = new List<View>();
        for (int i = 0; i < requests.Count; i++)
        {
            views.Add(new Label { Text = labels[i], X = 0, Y = i });
            _fields[i] = new TextField { X = labelWidth, Y = i, Width = fieldWidth };
            views.Add(_fields[i]);
        }

        Add(views.ToArray());

        // OK and Enter in a text field both reach OnAccepting. Cancel ends the dialog without a result.
        var ok = new Button { Text = "_OK" };
        var cancel = new Button { Text = "_Cancel" };
        cancel.Accepting += (_, e) =>
        {
            e.Handled = true;
            _app.RequestStop(this);
        };
        AddButton(ok);
        AddButton(cancel);

        // Terminal.Gui makes the last button added the default; Enter must mean OK.
        ok.IsDefault = true;
        cancel.IsDefault = false;
    }

    /// <summary>
    /// Shows the dialog and returns the values by input name, or null when it was cancelled.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="what">What is being started, shown in the title.</param>
    /// <param name="requests">The inputs, in order. Must not be empty.</param>
    public static IReadOnlyDictionary<string, string>? Ask(
        IApplication app,
        string what,
        IReadOnlyList<InputPlaceholders.InputRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(app);
        using var dialog = new InputDialog(app, what, requests);
        app.Run(dialog);
        return dialog.Result;
    }

    private static string LabelOf(InputPlaceholders.InputRequest request) =>
        request.AllowEmpty ? request.Name + " (optional)" : request.Name;

    /// <summary>OK, or Enter in a field: validate, and close only when the values are accepted.</summary>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        args.Handled = true;
        if (TryAccept())
        {
            _app.RequestStop(this);
        }

        return true;
    }

    /// <summary>Validates the fields. Returns true, with <see cref="Dialog{TResult}.Result"/> set, when they are accepted.</summary>
    private bool TryAccept()
    {
        IReadOnlyList<string> values = _fields.Select(field => field.Text).ToList();
        IReadOnlyList<string> errors = InputForm.Validate(_requests, values);
        if (errors.Count > 0)
        {
            MessageBox.ErrorQuery(_app, "Inputs", string.Join("\n", errors), "_OK");
            return false;
        }

        Result = InputForm.Values(_requests, values);
        return true;
    }
}
