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
/// Add or edit a group. OK validates the name (errors are shown and the dialog stays open), then calls the commit
/// function; a message from commit also keeps it open. Esc or Cancel leaves the session unchanged.
/// </summary>
internal sealed class GroupDialog : Dialog<bool>
{
    private readonly IApplication _app;
    private readonly Session _session;
    private readonly GroupEntry? _editing;
    private readonly Func<string, string?, GroupMode, string?> _commit;

    private readonly TextField _name;
    private readonly TextView _description;
    private readonly OptionSelector<GroupMode> _mode;

    /// <param name="app">The application, for the message boxes.</param>
    /// <param name="session">Supplies the existing group names.</param>
    /// <param name="title">The dialog title, e.g. "Add group".</param>
    /// <param name="editing">The group being edited (excluded from the name check), or null when adding.</param>
    /// <param name="name">Initial name.</param>
    /// <param name="description">Initial description.</param>
    /// <param name="mode">Initial mode.</param>
    /// <param name="commit">Applies the group to the session. Returns an error message, or null on success.</param>
    public GroupDialog(
        IApplication app,
        Session session,
        string title,
        GroupEntry? editing,
        string name,
        string? description,
        GroupMode mode,
        Func<string, string?, GroupMode, string?> commit)
    {
        _app = app;
        _session = session;
        _editing = editing;
        _commit = commit;

        Title = title;
        Width = 62;
        Height = 13;

        _name = new TextField { X = 12, Y = 0, Width = 46, Text = name };
        _description = new TextView
        {
            X = 0,
            Y = 2,
            Width = 58,
            Height = 3,
            Multiline = true,
            TabKeyAddsTab = false,
            Text = description ?? string.Empty,
        };
        _mode = new OptionSelector<GroupMode>
        {
            X = 12,
            Y = 6,
            Width = 40,
            Height = 1,
            Orientation = Terminal.Gui.ViewBase.Orientation.Horizontal,
            Value = mode,
        };

        Add(
            new Label { Text = "Name", X = 0, Y = 0 }, _name,
            new Label { Text = "Description", X = 0, Y = 1 }, _description,
            new Label { Text = "Mode", X = 0, Y = 6 }, _mode);

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

    private bool TryCommit()
    {
        string name = _name.Text;
        IEnumerable<string> others = _session.Groups
            .Where(g => !ReferenceEquals(g, _editing))
            .Select(g => g.Name);
        IReadOnlyList<string> errors = GroupForm.Validate(name, others);
        if (errors.Count > 0)
        {
            MessageBox.ErrorQuery(_app, "Group", string.Join("\n", errors), "_OK");
            return false;
        }

        string? description = string.IsNullOrWhiteSpace(_description.Text) ? null : _description.Text;
        string? error = _commit(name.Trim(), description, _mode.Value ?? GroupMode.Parallel);
        if (error is not null)
        {
            MessageBox.ErrorQuery(_app, "Group", error, "_OK");
            return false;
        }

        Result = true;
        return true;
    }
}
