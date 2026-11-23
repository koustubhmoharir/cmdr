using System.Collections.ObjectModel;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using static Cmdr.Tui.DialogControls;

namespace Cmdr.Tui;

/// <summary>
/// Add or edit a group. OK validates the fields (errors are shown and the dialog stays open), then calls the commit
/// function with the chosen parent (null for top level); a message from commit also keeps it open. Esc or Cancel leaves the
/// session unchanged.
/// </summary>
/// <remarks>
/// Two columns of fields so that the dialog fits an 80x24 terminal. <see cref="Result"/> is true when the group was committed.
/// When the initial environment cannot be written in its line format (see <see cref="EnvText.CanEdit"/>) the field is
/// read-only, and accepting the dialog keeps the environment exactly: edit it in the JSON. The name and working dir fields work
/// the same way as the single-line fields of the command dialog (see <see cref="FieldDraft"/>). The secret names are a
/// comma-separated field (see <see cref="NameListText"/>), read-only and kept exactly when they cannot be written that way. The
/// environment script is set in its own dialog (see <see cref="EnvScriptDialog"/>), with a one-line summary beside its button.
/// </remarks>
internal sealed class GroupDialog : Dialog<bool>
{
    private const int DialogWidth = 70;
    private const int DialogHeight = 18;
    private const int LeftWidth = 32;
    private const int RightX = 36;
    private const int RightWidth = 32;
    private const int FieldX = 12;
    private const int FieldWidth = 56;

    private readonly IApplication _app;
    private readonly Session _session;
    private readonly GroupEntry? _editing;
    private readonly IReadOnlyList<GroupOption> _parentOptions;
    private readonly Func<GroupEntry?, GroupSettings, string?> _commit;

    private readonly DropDownList _parent;
    private readonly TextField _name;
    private readonly OptionSelector<GroupMode> _mode;
    private readonly TextField _cwd;
    private readonly TextArea _description;
    private readonly TextArea _env;
    private readonly CheckBox _expanded;
    private readonly Label _envScriptSummary;
    private readonly TextField _secret;
    private readonly Label _hint;

    // The originals of the name and working dir fields (see FieldDraft).
    private readonly FieldDraft _nameDraft;
    private readonly FieldDraft _cwdDraft;

    // The original environment when its field is read-only; null when the field is edited as text.
    private readonly IReadOnlyDictionary<string, string?>? _keptEnv;

    // The original secret names when their field is read-only; null when the field is edited as text.
    private readonly IReadOnlyList<string>? _keptSecret;

    // The environment script as the dialog holds it: the initial one, or the one the env script dialog kept (null for none).
    private EnvScriptSpec? _envScript;

    /// <param name="app">The application, for the message boxes.</param>
    /// <param name="session">Supplies the groups for the parent choices and the sibling names. Base groups are not offered.</param>
    /// <param name="title">The dialog title, e.g. "Add group".</param>
    /// <param name="editing">The group being edited (excluded from the name check and from the parent choices with its descendants), or null when adding.</param>
    /// <param name="initialParent">The parent selected at the start; null for top level.</param>
    /// <param name="initial">The values shown at the start. A new group uses an empty name and no cwd, env, script or secret.</param>
    /// <param name="commit">Applies the group to the session under the chosen parent. Returns an error message, or null on success.</param>
    public GroupDialog(
        IApplication app,
        Session session,
        string title,
        GroupEntry? editing,
        GroupEntry? initialParent,
        GroupSettings initial,
        Func<GroupEntry?, GroupSettings, string?> commit)
    {
        _app = app;
        _session = session;
        _editing = editing;
        _commit = commit;
        // Base groups are read-only, so they are not offered as a parent.
        _parentOptions = GroupOptions.ParentChoices(GroupTree.Editable(session.AllGroups), editing);

        Title = title;
        Width = DialogWidth;
        Height = DialogHeight;

        int initialIndex = Math.Max(0, _parentOptions.ToList().FindIndex(option => ReferenceEquals(option.Group, initialParent)));
        _parent = new DropDownList
        {
            X = FieldX,
            Y = 0,
            Width = FieldWidth,
            Source = new ListWrapper<string>(new ObservableCollection<string>(GroupOptions.Labels(_parentOptions))),
            Value = _parentOptions[initialIndex].Label,
        };

        _nameDraft = new FieldDraft(initial.Name);
        _cwdDraft = new FieldDraft(initial.Cwd);
        _name = Field(FieldX, 1, FieldWidth, _nameDraft);
        _mode = new OptionSelector<GroupMode>
        {
            X = FieldX,
            Y = 2,
            Width = 40,
            Height = 1,
            Orientation = Terminal.Gui.ViewBase.Orientation.Horizontal,
            Value = initial.Mode,
        };
        _cwd = Field(FieldX, 3, FieldWidth, _cwdDraft);
        _description = Area(0, 5, LeftWidth, 4, initial.Description ?? string.Empty);
        IReadOnlyDictionary<string, string?> initialEnv = initial.Env ?? new Dictionary<string, string?>();
        bool envEditable = EnvText.CanEdit(initialEnv);
        _keptEnv = envEditable ? null : new Dictionary<string, string?>(initialEnv);
        _env = Area(RightX, 5, RightWidth, 4, EnvText.Format(initialEnv));
        _env.ReadOnly = !envEditable;
        _expanded = new CheckBox
        {
            X = 0,
            Y = 9,
            Text = "Expanded on load",
            Value = initial.Expanded ? CheckState.Checked : CheckState.UnChecked,
        };

        _envScript = initial.EnvScript?.Clone();
        var editEnvScript = new Button { Text = "Env script…", X = RightX, Y = 9 };
        editEnvScript.Accepting += (_, e) =>
        {
            e.Handled = true;
            EditEnvScript();
        };
        _envScriptSummary = new Label
        {
            X = RightX,
            Y = 10,
            Width = RightWidth,
            Text = EnvScriptForm.Summary(_envScript, RightWidth),
        };

        bool secretEditable = NameListText.CanEdit(initial.Secret);
        _keptSecret = secretEditable ? null : initial.Secret?.ToList();
        _secret = Field(FieldX, 11, FieldWidth, NameListText.Format(initial.Secret));
        _secret.ReadOnly = !secretEditable;

        bool anyReadOnlyLine = !_nameDraft.Editable || !_cwdDraft.Editable || !secretEditable;
        _hint = new Label
        {
            X = 0,
            Y = 12,
            Width = DialogWidth - 4,
            Text = anyReadOnlyLine ? "Read-only: a value cannot be written here. Edit it in the JSON." : "Secret names are separated by commas.",
        };

        Add(
            Caption("Parent", 0, 0), _parent,
            Caption("Name", 0, 1), _name,
            Caption("Mode", 0, 2), _mode,
            Caption("Working dir", 0, 3), _cwd,
            Caption("Description", 0, 4), _description,
            Caption(envEditable ? "Env (KEY=VALUE, !KEY removes)" : "Env: read-only, edit the JSON", RightX, 4), _env,
            _expanded,
            editEnvScript, _envScriptSummary,
            Caption("Secret", 0, 11), _secret,
            _hint);

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

    /// <summary>The parent choice in the dropdown, by its label. Top level when the choice cannot be found.</summary>
    private GroupOption SelectedParent()
    {
        int index = GroupOptions.IndexOfLabel(_parentOptions, _parent.Value);
        return index >= 0 ? _parentOptions[index] : _parentOptions[0];
    }

    /// <summary>Opens the environment script dialog. Its result replaces the script of the group, when OK or Remove was chosen.</summary>
    private void EditEnvScript()
    {
        using var dialog = new EnvScriptDialog(_app, "Environment script", _envScript);
        _app.Run(dialog);
        if (dialog.Result == true)
        {
            _envScript = dialog.Spec;
            _envScriptSummary.Text = EnvScriptForm.Summary(_envScript, RightWidth);
        }
    }

    private bool TryCommit()
    {
        GroupOption parent = SelectedParent();
        GroupFormValues values = new(
            _name.Text,
            _description.Text,
            _mode.Value ?? GroupMode.Parallel,
            _cwd.Text,
            _env.Text,
            _expanded.Value == CheckState.Checked,
            _keptEnv,
            KeptName: _nameDraft.KeptIfUnchanged(_name.Text),
            KeptCwd: _cwdDraft.KeptIfUnchanged(_cwd.Text),
            EnvScript: _envScript,
            SecretText: _secret.Text,
            KeptSecret: _keptSecret);

        // Names are unique among the items of the parent the group would be in: groups and commands together. At top level
        // that is the groups of the opened file only; base groups are not changed by this group.
        IReadOnlyList<ItemEntry> siblings = parent.Group?.Items ?? GroupTree.Editable(_session.Groups);
        IReadOnlyList<string> errors = GroupForm.Validate(values, GroupForm.SiblingNames(siblings, _editing));
        if (errors.Count > 0)
        {
            MessageBox.ErrorQuery(_app, "Group", string.Join("\n", errors), "_OK");
            return false;
        }

        string? error = _commit(parent.Group, GroupForm.Build(values));
        if (error is not null)
        {
            MessageBox.ErrorQuery(_app, "Group", error, "_OK");
            return false;
        }

        Result = true;
        return true;
    }
}
