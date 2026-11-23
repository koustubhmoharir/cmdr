using Cmdr.Core.Config;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using static Cmdr.Tui.DialogControls;

namespace Cmdr.Tui;

/// <summary>
/// Sets, changes or removes an environment script (the <c>envScript</c> of a group or of the file). OK validates the fields
/// (errors are shown and the dialog stays open), then keeps the script; Remove removes it; Esc or Cancel leaves it as it was.
/// </summary>
/// <remarks>
/// <see cref="Result"/> is true when OK or Remove was chosen, and then <see cref="Spec"/> is the script, or null when it was
/// removed. Result is null (false here) when the dialog was cancelled. The fields fit an 80x24 terminal above the button area.
/// A value that cannot be written in its single-line or line form is read-only and kept exactly (see <see cref="FieldDraft"/>
/// and <see cref="ArgsText"/>).
/// </remarks>
internal sealed class EnvScriptDialog : Dialog<bool>
{
    private const int DialogWidth = 70;
    private const int DialogHeight = 16;
    private const int FieldX = 12;
    private const int FieldWidth = 56;

    private readonly IApplication _app;
    private readonly TextField _program;
    private readonly TextArea _args;
    private readonly TextField _cwd;
    private readonly TextField _timeout;
    private readonly OptionSelector<EnvScriptRerun> _rerun;

    // The originals of the single-line fields (see FieldDraft), and of the args when that field is read-only.
    private readonly FieldDraft _programDraft;
    private readonly FieldDraft _cwdDraft;
    private readonly IReadOnlyList<string>? _keptArgs;

    /// <param name="app">The application, for the message boxes.</param>
    /// <param name="title">The dialog title, e.g. "Environment script".</param>
    /// <param name="initial">The script as it is now, or null when there is none.</param>
    public EnvScriptDialog(IApplication app, string title, EnvScriptSpec? initial)
    {
        _app = app;
        Title = title;
        Width = DialogWidth;
        Height = DialogHeight;

        EnvScriptSpec start = initial ?? new EnvScriptSpec();
        bool argsEditable = ArgsText.CanEdit(start.Args);
        _keptArgs = argsEditable ? null : start.Args.ToList();
        _programDraft = new FieldDraft(start.Program);
        _cwdDraft = new FieldDraft(start.Cwd);

        _program = Field(FieldX, 0, FieldWidth, _programDraft);
        _args = Area(0, 2, DialogWidth - 4, 3, ArgsText.Format(start.Args));
        _args.ReadOnly = !argsEditable;
        _cwd = Field(FieldX, 5, FieldWidth, _cwdDraft);
        _timeout = Field(FieldX, 6, 10, EnvScriptForm.TimeoutText(start.TimeoutSeconds));
        _rerun = new OptionSelector<EnvScriptRerun>
        {
            X = FieldX,
            Y = 7,
            Width = 40,
            Height = 1,
            Orientation = Terminal.Gui.ViewBase.Orientation.Horizontal,
            Value = start.Rerun,
        };

        bool anyReadOnlyLine = !_programDraft.Editable || !_cwdDraft.Editable || !argsEditable;
        Add(
            Caption("Program", 0, 0), _program,
            Caption(argsEditable ? "Arguments, one per line" : "Arguments: read-only, edit the JSON", 0, 1), _args,
            Caption("Working dir", 0, 5), _cwd,
            Caption("Timeout", 0, 6), _timeout,
            Caption($"Seconds. Empty means the default ({EnvScriptSpec.DefaultTimeoutSeconds}).", FieldX + 12, 6),
            Caption("Rerun", 0, 7), _rerun,
            new Label
            {
                X = 0,
                Y = 9,
                Width = DialogWidth - 4,
                Text = anyReadOnlyLine ? "Read-only: a value cannot be written here. Edit it in the JSON." : string.Empty,
            });

        // OK and Enter in a text field both reach OnAccepting. Remove and Cancel end the dialog themselves.
        var ok = new Button { Text = "_OK" };
        var remove = new Button { Text = "_Remove" };
        var cancel = new Button { Text = "_Cancel" };
        remove.Accepting += (_, e) =>
        {
            e.Handled = true;
            Spec = null;
            Result = true;
            _app.RequestStop(this);
        };
        cancel.Accepting += (_, e) =>
        {
            e.Handled = true;
            _app.RequestStop(this);
        };
        AddButton(ok);
        AddButton(remove);
        AddButton(cancel);

        // Terminal.Gui makes the last button added the default; Enter must mean OK.
        ok.IsDefault = true;
        remove.IsDefault = false;
        cancel.IsDefault = false;
    }

    /// <summary>The script to keep after OK, or null after Remove. Meaningful only when <see cref="Result"/> is true.</summary>
    public EnvScriptSpec? Spec { get; private set; }

    /// <summary>OK, or Enter in a field: validate and keep the script, and close only when that succeeds.</summary>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        args.Handled = true;
        if (TryCommit())
        {
            _app.RequestStop(this);
        }

        return true;
    }

    /// <summary>Validates and keeps the script. Returns true when the dialog should close.</summary>
    private bool TryCommit()
    {
        EnvScriptFormValues values = new(
            Program: _program.Text,
            Args: _args.Text,
            Cwd: _cwd.Text,
            Timeout: _timeout.Text,
            Rerun: _rerun.Value ?? EnvScriptRerun.Once,
            KeptProgram: _programDraft.KeptIfUnchanged(_program.Text),
            KeptCwd: _cwdDraft.KeptIfUnchanged(_cwd.Text),
            KeptArgs: _keptArgs);

        IReadOnlyList<string> errors = EnvScriptForm.Validate(values);
        if (errors.Count > 0)
        {
            MessageBox.ErrorQuery(_app, "Environment script", string.Join("\n", errors), "_OK");
            return false;
        }

        Spec = EnvScriptForm.Build(values);
        Result = true;
        return true;
    }
}
