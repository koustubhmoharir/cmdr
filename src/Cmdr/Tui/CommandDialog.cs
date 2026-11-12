using System.Collections.ObjectModel;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

// TextView is marked obsolete in Terminal.Gui 2.5.0 in favour of an editor package that is not in use here. It is still
// the multi-line text control of this version, and the dialogs need one for description, args and env.
#pragma warning disable CS0618

namespace Cmdr.Tui;

/// <summary>
/// Add or edit a command. OK validates the fields (errors are shown and the dialog stays open), then calls the commit
/// function; a message from commit also keeps the dialog open. Esc or Cancel leaves the session unchanged.
/// </summary>
/// <remarks>
/// Layout is two columns of fields so that the dialog fits an 80x24 terminal. <see cref="Result"/> is true when the
/// command was committed.
/// </remarks>
internal sealed class CommandDialog : Dialog<bool>
{
    private const int DialogWidth = 78;
    private const int DialogHeight = 18;
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
    private readonly TextView _description;
    private readonly TextField _program;
    private readonly TextView _args;
    private readonly TextField _cwd;
    private readonly TextView _env;
    private readonly OptionSelector<StopMethod> _method;
    private readonly TextField _stopInput;
    private readonly CheckBox _logEnabled;
    private readonly TextField _logPath;
    private readonly Label _logHint;
    private readonly CheckBox _detachLeftovers;

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
        _groupOptions = GroupOptions.TargetChoices(session.AllGroups);

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

        _name = Field(12, 1, LeftWidth - 12, initial.Name);
        _program = Field(12, 2, LeftWidth - 12, initial.Program);
        _cwd = Field(12, 3, LeftWidth - 12, initial.Cwd ?? string.Empty);
        _description = Area(0, 5, LeftWidth, 3, initial.Description ?? string.Empty);
        _env = Area(0, 9, LeftWidth, 3, EnvText.Format(initial.Env));

        // Right column: how it runs and stops, and its log.
        _args = Area(RightX, 1, RightWidth, 4, ArgsText.Format(initial.Args));
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
        _logPath = Field(RightX + 10, 8, RightWidth - 10, initial.Log.Path ?? string.Empty);
        _logHint = new Label { X = RightX, Y = 9, Width = RightWidth, Height = 2 };
        _detachLeftovers = new CheckBox
        {
            X = RightX,
            Y = 11,
            Text = "Detach leftovers on exit",
            Value = initial.Leftovers == LeftoverMode.Detach ? CheckState.Checked : CheckState.UnChecked,
        };

        View[] root =
        {
            Caption("Group", 0, 0), _group,
            Caption("Name", 0, 1), _name,
            Caption("Program", 0, 2), _program,
            Caption("Working dir", 0, 3), _cwd,
            Caption("Description", 0, 4), _description,
            Caption("Environment", 0, 8), Caption("KEY=VALUE, !KEY removes", 13, 8), _env,
            Caption("Arguments, one per line", RightX, 0), _args,
            Caption("Stop", RightX, 5), _method,
            Caption("Stop input", RightX, 6), _stopInput,
            _logEnabled,
            Caption("Log path", RightX, 8), _logPath,
            _logHint,
            _detachLeftovers,
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

    private static Label Caption(string text, int x, int y) => new() { Text = text, X = x, Y = y };

    private static TextField Field(int x, int y, int width, string text) =>
        new() { X = x, Y = y, Width = width, Text = text };

    private static TextView Area(int x, int y, int width, int height, string text) => new()
    {
        X = x,
        Y = y,
        Width = width,
        Height = height,
        Multiline = true,
        TabKeyAddsTab = false,
        Text = text,
    };

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
        DetachLeftovers: _detachLeftovers.Value == CheckState.Checked);

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
