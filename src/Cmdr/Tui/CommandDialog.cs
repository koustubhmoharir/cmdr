using System.Collections.ObjectModel;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Shared.Processes;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using static Cmdr.Tui.DialogControls;

namespace Cmdr.Tui;

/// <summary>
/// Add or edit a command. OK validates the fields (errors are shown and the dialog stays open), then calls the commit
/// function; a message from commit also keeps the dialog open. Esc or Cancel leaves the session unchanged.
/// </summary>
/// <remarks>
/// Layout is two columns of fields so that the dialog fits an 80x24 terminal. <see cref="Result"/> is true when the
/// command was committed.
/// A field whose initial values cannot be written in its line format (see <see cref="ArgsText.CanEdit"/> and
/// <see cref="EnvText.CanEdit"/>) is read-only, and accepting the dialog keeps those values exactly: edit them in the JSON.
/// The single-line fields work the same way (see <see cref="FieldDraft"/>): a value with a line break is read-only, and a
/// value that is not edited is saved exactly; only edited text is trimmed. The secret names are a comma-separated field (see
/// <see cref="NameListText"/>), read-only and kept exactly when they cannot be written that way.
/// </remarks>
internal sealed class CommandDialog : Dialog<bool>
{
    private const int DialogWidth = 78;
    private const int DialogHeight = 21;
    private const int LeftWidth = 37;
    private const int RightX = 40;
    private const int RightWidth = 36;

    private readonly IApplication _app;
    private readonly Session _session;
    private readonly CommandEntry? _editing;
    private readonly GroupEntry _initialGroup;
    private readonly IReadOnlyList<GroupOption> _groupOptions;
    private readonly Func<GroupEntry, CommandSpec, string?> _commit;

    private readonly DropDownList _group;
    private readonly TextField _name;
    private readonly TextArea _description;
    private readonly TextField _program;
    private readonly TextArea _args;
    private readonly TextField _cwd;
    private readonly TextArea _env;
    private readonly OptionSelector<StopMethod> _method;
    private readonly TextField _stopInput;
    private readonly CheckBox _logEnabled;
    private readonly TextField _logPath;
    private readonly Label _logHint;
    private readonly CheckBox _detachLeftovers;
    private readonly OptionSelector<StdinMode> _stdin;
    private readonly TextField _readyPattern;
    private readonly TextField _busyPattern;
    private readonly TextField _secret;
    private readonly Label _multilineHint;

    // The originals of the single-line fields (see FieldDraft).
    private readonly FieldDraft _nameDraft;
    private readonly FieldDraft _programDraft;
    private readonly FieldDraft _cwdDraft;
    private readonly FieldDraft _logPathDraft;
    private readonly FieldDraft _readyDraft;
    private readonly FieldDraft _busyDraft;

    // The original args, env or secret names when their field is read-only; null when the field is edited as text.
    private readonly IReadOnlyList<string>? _keptArgs;
    private readonly IReadOnlyDictionary<string, string?>? _keptEnv;
    private readonly IReadOnlyList<string>? _keptSecret;

    /// <param name="app">The application, for the message boxes.</param>
    /// <param name="session">Supplies the groups and the logs directory.</param>
    /// <param name="title">The dialog title, e.g. "Add command".</param>
    /// <param name="initialGroup">The group selected at the start.</param>
    /// <param name="initial">The values shown at the start. A new command uses <c>new CommandSpec()</c>.</param>
    /// <param name="editing">The command being edited (excluded from the name check), or null when adding.</param>
    /// <param name="commit">Applies the command to the session. Returns an error message, or null on success.</param>
    public CommandDialog(
        IApplication app,
        Session session,
        string title,
        GroupEntry initialGroup,
        CommandSpec initial,
        CommandEntry? editing,
        Func<GroupEntry, CommandSpec, string?> commit)
    {
        _app = app;
        _session = session;
        _editing = editing;
        _commit = commit;
        _initialGroup = initialGroup;
        // Commands can only go into groups of the opened file; base groups are read-only.
        _groupOptions = GroupOptions.TargetChoices(GroupTree.Editable(session.AllGroups));

        bool argsEditable = ArgsText.CanEdit(initial.Args);
        bool envEditable = EnvText.CanEdit(initial.Env);
        _keptArgs = argsEditable ? null : initial.Args.ToList();
        _keptEnv = envEditable ? null : new Dictionary<string, string?>(initial.Env);
        bool secretEditable = NameListText.CanEdit(initial.Secret);
        _keptSecret = secretEditable ? null : initial.Secret?.ToList();

        _nameDraft = new FieldDraft(initial.Name);
        _programDraft = new FieldDraft(initial.Program);
        _cwdDraft = new FieldDraft(initial.Cwd);
        _logPathDraft = new FieldDraft(initial.Log.Path);
        _readyDraft = new FieldDraft(initial.Ready?.Pattern);
        _busyDraft = new FieldDraft(initial.Ready?.Busy);

        Title = title;
        Width = DialogWidth;
        Height = DialogHeight;

        // Left column: where the command runs. Every group is listed by its path, depth-first.
        int initialIndex = Math.Max(0, _groupOptions.ToList().FindIndex(option => ReferenceEquals(option.Group, initialGroup)));
        _group = new DropDownList
        {
            X = 12,
            Y = 0,
            Width = LeftWidth - 12,
            Source = new ListWrapper<string>(new ObservableCollection<string>(GroupOptions.Labels(_groupOptions))),
            Value = _groupOptions[initialIndex].Label,
        };

        _name = Field(12, 1, LeftWidth - 12, _nameDraft);
        _program = Field(12, 2, LeftWidth - 12, _programDraft);
        _cwd = Field(12, 3, LeftWidth - 12, _cwdDraft);
        _description = Area(0, 5, LeftWidth, 3, initial.Description ?? string.Empty);
        _env = Area(0, 9, LeftWidth, 3, EnvText.Format(initial.Env));
        _env.ReadOnly = !envEditable;

        // Right column: how it runs and stops, and its log.
        _args = Area(RightX, 1, RightWidth, 4, ArgsText.Format(initial.Args));
        _args.ReadOnly = !argsEditable;
        _method = new OptionSelector<StopMethod>
        {
            X = RightX + 5,
            Y = 5,
            Width = RightWidth - 5,
            Height = 1,
            Orientation = Terminal.Gui.ViewBase.Orientation.Horizontal,
            Value = initial.Stop.Method,
        };
        _stopInput = Field(RightX + 11, 6, RightWidth - 11, EscapeText.Format(initial.Stop.Input));
        _stopInput.Enabled = initial.Stop.Method == StopMethod.Input;
        _logEnabled = new CheckBox
        {
            X = RightX,
            Y = 7,
            Text = "Log to file",
            Value = initial.Log.Enabled ? CheckState.Checked : CheckState.UnChecked,
        };
        _logPath = Field(RightX + 10, 8, RightWidth - 10, _logPathDraft);
        _logHint = new Label { X = RightX, Y = 9, Width = RightWidth, Height = 2 };
        _detachLeftovers = new CheckBox
        {
            X = RightX,
            Y = 11,
            Text = "Detach leftovers on exit",
            Value = initial.Leftovers == LeftoverMode.Detach ? CheckState.Checked : CheckState.UnChecked,
        };
        _stdin = new OptionSelector<StdinMode>
        {
            X = RightX + 6,
            Y = 12,
            Width = RightWidth - 6,
            Height = 1,
            Orientation = Terminal.Gui.ViewBase.Orientation.Horizontal,
            Value = initial.Stdin,
        };

        // Left column, below the environment: the readiness patterns, one line each (see CommandForm for the rules), then the
        // secret names, a comma-separated line (see NameListText).
        _readyPattern = Field(12, 12, LeftWidth - 12, _readyDraft);
        _busyPattern = Field(12, 13, LeftWidth - 12, _busyDraft);
        _secret = Field(12, 14, LeftWidth - 12, NameListText.Format(initial.Secret));
        _secret.ReadOnly = !secretEditable;
        bool anyReadOnlyLine = !secretEditable || new[] { _nameDraft, _programDraft, _cwdDraft, _logPathDraft, _readyDraft, _busyDraft }
            .Any(draft => !draft.Editable);

        _multilineHint = new Label
        {
            X = 0,
            Y = 15,
            Width = DialogWidth - 4,
            Text = anyReadOnlyLine ? "Read-only: a value cannot be written here. Edit it in the JSON." : "Secret names are separated by commas.",
        };

        View[] root =
        {
            Caption("Group", 0, 0), _group,
            Caption("Name", 0, 1), _name,
            Caption("Program", 0, 2), _program,
            Caption("Working dir", 0, 3), _cwd,
            Caption("Description", 0, 4), _description,
            Caption("Environment", 0, 8),
            Caption(envEditable ? "KEY=VALUE, !KEY removes" : "read-only, edit the JSON", 13, 8), _env,
            Caption(argsEditable ? "Arguments, one per line" : "Arguments: read-only, edit the JSON", RightX, 0), _args,
            Caption("Stop", RightX, 5), _method,
            Caption("Stop input", RightX, 6), _stopInput,
            _logEnabled,
            Caption("Log path", RightX, 8), _logPath,
            _logHint,
            _detachLeftovers,
            Caption("Input", RightX, 12), _stdin,
            Caption("Ready regex", 0, 12), _readyPattern,
            Caption("Busy regex", 0, 13), _busyPattern,
            Caption("Secret", 0, 14), _secret,
            _multilineHint,
        };
        Add(root);

        _method.ValueChanged += (_, _) => _stopInput.Enabled = _method.Value == StopMethod.Input;
        _name.ValueChanged += (_, _) => UpdateLogHint();
        _group.ValueChanged += (_, _) => UpdateLogHint();
        UpdateLogHint();

        // OK and Enter in a text field both reach OnAccepting. Cancel ends the dialog without committing.
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

    /// <summary>The group chosen in the dropdown, by its label (which identifies its option). The initial group when the choice cannot be found.</summary>
    private GroupEntry SelectedGroup
    {
        get
        {
            int index = GroupOptions.IndexOfLabel(_groupOptions, _group.Value);
            return index >= 0 ? _groupOptions[index].Group ?? _initialGroup : _initialGroup;
        }
    }

    private void UpdateLogHint()
    {
        // The group and all its ancestors, outermost first: the default log folder has one level per group.
        string path = ConfigPaths.ResolveLogPath(
            GroupTree.SpecChain(SelectedGroup),
            new CommandSpec { Name = _name.Text },
            _session.LogsDir);
        _logHint.Text = "Default: " + Path.GetRelativePath(_session.ConfigDir, path);
    }

    private CommandFormValues ReadValues() => new(
        Name: _name.Text,
        Description: _description.Text,
        Program: _program.Text,
        Args: _args.Text,
        Cwd: _cwd.Text,
        Env: _env.Text,
        Method: _method.Value ?? StopMethod.CtrlC,
        StopInput: _stopInput.Text,
        LogEnabled: _logEnabled.Value == CheckState.Checked,
        LogPath: _logPath.Text,
        DetachLeftovers: _detachLeftovers.Value == CheckState.Checked,
        Stdin: _stdin.Value ?? StdinMode.Pipe,
        ReadyPattern: _readyPattern.Text,
        BusyPattern: _busyPattern.Text,
        SecretText: _secret.Text,
        KeptArgs: _keptArgs,
        KeptEnv: _keptEnv,
        KeptName: _nameDraft.KeptIfUnchanged(_name.Text),
        KeptProgram: _programDraft.KeptIfUnchanged(_program.Text),
        KeptCwd: _cwdDraft.KeptIfUnchanged(_cwd.Text),
        KeptLogPath: _logPathDraft.KeptIfUnchanged(_logPath.Text),
        KeptReadyPattern: _readyDraft.KeptIfUnchanged(_readyPattern.Text),
        KeptBusyPattern: _busyDraft.KeptIfUnchanged(_busyPattern.Text),
        KeptSecret: _keptSecret);

    /// <summary>OK, or Enter in a field: validate and commit, and close only when that succeeds.</summary>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        args.Handled = true;
        if (TryCommit())
        {
            _app.RequestStop(this);
        }

        return true;
    }

    /// <summary>Validates and commits. Returns true when the dialog should close.</summary>
    private bool TryCommit()
    {
        GroupEntry target = SelectedGroup;
        CommandFormValues values = ReadValues();

        // Names are unique among the items of the target group: its commands and its nested groups together.
        IReadOnlyList<string> names = GroupForm.SiblingNames(target.Items, _editing);
        IReadOnlyList<string> errors = CommandForm.Validate(values, names);
        if (errors.Count > 0)
        {
            ShowError(string.Join("\n", errors));
            return false;
        }

        string? error = _commit(target, CommandForm.Build(values));
        if (error is not null)
        {
            ShowError(error);
            return false;
        }

        Result = true;
        return true;
    }

    private void ShowError(string message)
    {
        MessageBox.ErrorQuery(_app, "Command", message, "_OK");
    }
}
