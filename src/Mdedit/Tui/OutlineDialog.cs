using System.Collections.ObjectModel;
using Mdedit.Core.Render;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Mdedit.Tui;

/// <summary>
/// The outline: a filter field over the headings of the document. The field has the focus, and typing filters the list (see
/// <see cref="OutlineFilter"/>). Up, Down, PgUp, PgDn, Home and End move the list's selection while the field has the focus. Enter or a
/// double-click goes to the selected heading, and Esc cancels. The list starts on the section the caret is in.
/// </summary>
/// <remarks>
/// The dialog has no buttons, so its result is the chosen heading's start offset, or null when it was cancelled or the filter shows no
/// heading. Typing a filter selects the first row that it leaves.
/// </remarks>
internal sealed class OutlineDialog : Dialog<int?>
{
    /// <summary>The width limit: the dialog is no wider than this, and no wider than the screen.</summary>
    internal const int MaxWidth = 60;

    // The rows the dialog takes beyond its headings: the filter field, the two borders, and the row Terminal.Gui keeps for the button area.
    private const int ChromeRows = 4;

    private readonly IApplication _app;
    private readonly IReadOnlyList<OutlineEntry> _entries;
    private readonly TextField _filter;
    private readonly ListView _list;
    private IReadOnlyList<OutlineRow> _rows = [];

    // Set once the dialog has chosen or cancelled: a key that arrives while it is stopping does not change the result.
    private bool _closing;

    private OutlineDialog(IApplication app, IReadOnlyList<OutlineEntry> entries, int caret)
    {
        _app = app;
        _entries = entries;
        Title = "Outline";

        // Sized from the screen at every layout, and tall enough for every heading, so that a filter does not resize it.
        Width = Dim.Func(_ => Math.Max(1, Math.Min(MaxWidth, _app.Screen.Width)));
        Height = Dim.Func(_ => Math.Max(1, Math.Min(entries.Count + ChromeRows, _app.Screen.Height)));

        _filter = new OutlineField(OnFieldKey) { X = 0, Y = 0, Width = Dim.Fill() };
        _list = new ListView { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill() };
        _list.Accepting += (_, e) =>
        {
            e.Handled = true;
            Choose();
        };

        ShowRows(string.Empty, OutlineFilter.InitialIndex(entries, caret));
        _filter.ValueChanged += (_, _) => ShowRows(_filter.Text, 0);

        Add(_filter, _list);
        _filter.SetFocus();
    }

    /// <summary>The filter field, for the tests.</summary>
    internal TextField Filter => _filter;

    /// <summary>The list, for the tests.</summary>
    internal ListView List => _list;

    /// <summary>The rows the list shows, for the tests.</summary>
    internal IReadOnlyList<OutlineRow> Rows => _rows;

    /// <summary>
    /// Shows the outline and returns the start offset of the heading chosen, or null when it was cancelled.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="entries">The headings, in document order. Must not be empty.</param>
    /// <param name="caret">The caret's offset, which selects the section it is in at the start.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="entries"/> is empty.</exception>
    public static int? Ask(IApplication app, IReadOnlyList<OutlineEntry> entries, int caret)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            throw new ArgumentException("The outline needs at least one heading.", nameof(entries));
        }

        using var dialog = new OutlineDialog(app, entries, caret);
        app.Run(dialog);
        return dialog.Result;
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

    // Shows the rows of a filter, with the row at the index selected (none when the filter shows no heading).
    private void ShowRows(string filter, int selected)
    {
        _rows = OutlineFilter.Rows(_entries, filter);
        _list.Source = new ListWrapper<string>(new ObservableCollection<string>(_rows.Select(row => row.Label)));
        _list.SelectedItem = _rows.Count == 0 ? null : Math.Clamp(selected, 0, _rows.Count - 1);
    }

    // The keys the field passes to the list, and Enter and Esc, which the field would otherwise take or leave for the dialog. Returns true
    // for a key it handled.
    private bool OnFieldKey(Key key)
    {
        if (key == Key.Enter)
        {
            Choose();
            return true;
        }

        if (key == Key.Esc)
        {
            Cancel();
            return true;
        }

        if (IsListKey(key))
        {
            return _list.NewKeyDownEvent(key);
        }

        return false;
    }

    private static bool IsListKey(Key key) =>
        key == Key.CursorUp || key == Key.CursorDown || key == Key.PageUp || key == Key.PageDown || key == Key.Home || key == Key.End;

    // Ends the dialog with the selected heading. Nothing happens when no row is selected.
    private void Choose()
    {
        if (_closing || _list.SelectedItem is not int index || index >= _rows.Count)
        {
            return;
        }

        Result = _rows[index].Start;
        Stop();
    }

    private void Cancel()
    {
        if (_closing)
        {
            return;
        }

        Result = null;
        Stop();
    }

    private void Stop()
    {
        _closing = true;
        _app.RequestStop(this);
    }

    // The filter field, which hands the list's keys to the dialog before it takes them itself: a TextField would move its caret on Home
    // and End.
    private sealed class OutlineField(Func<Key, bool> onKey) : TextField
    {
        protected override bool OnKeyDown(Key key) => onKey(key) || base.OnKeyDown(key);
    }
}
