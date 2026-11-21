using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cfgedit.Tui;

/// <summary>
/// A list of choices. Enter picks the highlighted item and Esc cancels, so the dialog has no buttons: its rows are the items and the two
/// borders and one more row, which lets all seven JSON types show at 40 by 12. The list starts on the given item.
/// </summary>
internal sealed class ChoiceDialog : Dialog<bool>
{
    /// <summary>The width limit: the dialog is no wider than this, and no wider than the screen.</summary>
    internal const int MaxWidth = 40;

    // The rows the dialog takes beyond its items: the two borders, and the row Terminal.Gui keeps for the button area even with no buttons.
    private const int ChromeRows = 3;

    private readonly IApplication _app;
    private readonly ListView _list;
    private int? _chosen;

    private ChoiceDialog(IApplication app, string title, IReadOnlyList<string> items, int initial)
    {
        _app = app;
        Title = title;

        // Sized from the screen at every layout.
        Width = Dim.Func(_ => Math.Max(1, Math.Min(MaxWidth, _app.Screen.Width)));
        Height = Dim.Func(_ => Math.Max(1, Math.Min(items.Count + ChromeRows, _app.Screen.Height)));

        _list = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _list.Source = new ListWrapper<string>(new ObservableCollection<string>(items));
        _list.SelectedItem = Math.Clamp(initial, 0, items.Count - 1);
        _list.Accepting += (_, e) =>
        {
            e.Handled = true;
            Choose();
        };
        Add(_list);
        _list.SetFocus();
    }

    /// <summary>The list, for the tests.</summary>
    internal ListView List => _list;

    /// <summary>
    /// Shows the dialog and returns the index of the chosen item, or null when it was cancelled.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="title">The title.</param>
    /// <param name="items">The choices, in order. Must not be empty.</param>
    /// <param name="initial">The index the list starts on.</param>
    public static int? Ask(IApplication app, string title, IReadOnlyList<string> items, int initial)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one choice is needed.", nameof(items));
        }

        using var dialog = new ChoiceDialog(app, title, items, initial);
        app.Run(dialog);
        return dialog._chosen;
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

    private void Choose()
    {
        if (_list.SelectedItem is not int index)
        {
            return;
        }

        _chosen = index;
        Result = true;
        _app.RequestStop(this);
    }

    private void Cancel()
    {
        _chosen = null;
        Result = false;
        _app.RequestStop(this);
    }
}
