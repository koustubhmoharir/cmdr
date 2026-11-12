using System.Collections.ObjectModel;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

// See CommandDialog: TextView is the multi-line control of Terminal.Gui 2.5.0.
#pragma warning disable CS0618

namespace Cmdr.Tui;

/// <summary>
/// Add or edit a group. OK validates the fields (errors are shown and the dialog stays open), then calls the commit
/// function with the chosen parent (null for top level); a message from commit also keeps it open. Esc or Cancel leaves the
/// session unchanged.
/// </summary>
/// <remarks>
/// Two columns of fields so that the dialog fits an 80x24 terminal. <see cref="Result"/> is true when the group was committed.
/// </remarks>
internal sealed class GroupDialog : Dialog<bool>
{
    private const int DialogWidth = 70;
    private const int DialogHeight = 16;
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
    private readonly TextView _description;
    private readonly TextView _env;
    private readonly CheckBox _expanded;

    /// <param name="app">The application, for the message boxes.</param>
    /// <param name="session">Supplies the groups for the parent choices and the sibling names. Base groups are not offered.</param>
    /// <param name="title">The dialog title, e.g. "Add group".</param>
    /// <param name="editing">The group being edited (excluded from the name check and from the parent choices with its descendants), or null when adding.</param>
    /// <param name="initialParent">The parent selected at the start; null for top level.</param>
    /// <param name="initial">The values shown at the start. A new group uses an empty name and no cwd or env.</param>
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

        _name = Field(FieldX, 1, FieldWidth, initial.Name);
        _mode = new OptionSelector<GroupMode>
        {
            X = FieldX,
            Y = 2,
            Width = 40,
            Height = 1,
            Orientation = Terminal.Gui.ViewBase.Orientation.Horizontal,
            Value = initial.Mode,
        };
        _cwd = Field(FieldX, 3, FieldWidth, initial.Cwd ?? string.Empty);
        _description = Area(0, 5, LeftWidth, 4, initial.Description ?? string.Empty);
        _env = Area(RightX, 5, RightWidth, 4, EnvText.Format(initial.Env ?? new Dictionary<string, string?>()));
        _expanded = new CheckBox
        {
            X = 0,
            Y = 9,
            Text = "Expanded on load",
            Value = initial.Expanded ? CheckState.Checked : CheckState.UnChecked,
        };

        Add(
            Caption("Parent", 0, 0), _parent,
            Caption("Name", 0, 1), _name,
            Caption("Mode", 0, 2), _mode,
            Caption("Working dir", 0, 3), _cwd,
            Caption("Description", 0, 4), _description,
            Caption("Env (KEY=VALUE, !KEY removes)", RightX, 4), _env,
            _expanded);

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

    private bool TryCommit()
    {
        GroupOption parent = SelectedParent();
        GroupFormValues values = new(
            _name.Text,
            _description.Text,
            _mode.Value ?? GroupMode.Parallel,
            _cwd.Text,
            _env.Text,
            _expanded.Value == CheckState.Checked);

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
