using Logvu.Core.Filter;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

// TextView is marked obsolete in Terminal.Gui 2.5.0 in favour of an editor package that is not in use here. It is still the multi-line
// text control of this version, and the field predicates need one (see CommandDialog, which uses it the same way).
#pragma warning disable CS0618

namespace Logvu.Tui;

/// <summary>
/// The result of the filter editor: the filter to apply, or <c>null</c> when the editor was left empty, which means no filter. A cancelled
/// editor gives no result at all.
/// </summary>
/// <param name="Filter">The filter, or <c>null</c> for no filter.</param>
internal sealed record FilterEdit(EventFilter? Filter);

/// <summary>
/// The filter editor: the minimum level, the source and file name, the text with its options, the time interval and the field predicates,
/// all ANDed. It is pre-filled from the active filter. <b>Apply</b> validates the fields and gives the filter; an invalid field shows a
/// message that names it, and the editor stays open. <b>Clear</b> empties the fields and stays open, so the user may still cancel. <b>Cancel</b>
/// and Esc end without a result.
/// </summary>
/// <remarks>
/// The buttons are <b>Clear</b>, <b>Cancel</b> and <b>Apply</b>. Terminal.Gui makes the last button the default, and Enter must mean Apply, the
/// action the user started the editor for.
/// </remarks>
internal sealed class FilterDialog : Dialog<FilterEdit>
{
    // The level row is about 60 columns wide, so the editor takes the full width of an 80-column terminal.
    private const int DialogWidth = 80;
    private const int LabelWidth = 17;
    private const int FieldWidth = 56;
    private const int PredicateRows = 4;

    // The rows of the content, from the top: a row per field, the text options on their own row, and two hint rows. The dialog's
    // button area is below them, so the content height is the number of rows the form needs.
    private const int ContentRows = 13;
    private const int ButtonAreaRows = 6;

    private readonly IApplication _app;
    private readonly TimeZoneInfo _zone;
    private readonly Func<DateTimeOffset> _clock;
    private readonly OptionSelector _level;
    private readonly TextField _source;
    private readonly TextField _file;
    private readonly TextField _text;
    private readonly CheckBox _caseSensitive;
    private readonly CheckBox _regex;
    private readonly CheckBox _wholeWord;
    private readonly TextField _from;
    private readonly TextField _to;
    private readonly TextView _predicates;

    /// <summary>Creates the editor, pre-filled from the active filter. Its fields are shown when it runs.</summary>
    /// <param name="app">The application, for the message boxes and the screen size.</param>
    /// <param name="active">The active filter, or <c>null</c> for an empty editor.</param>
    /// <param name="zone">The zone the times are typed in and shown in.</param>
    /// <param name="clock">The current instant, for a time of today. The system clock by default.</param>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="active"/> and <paramref name="clock"/> is <c>null</c>.</exception>
    internal FilterDialog(IApplication app, EventFilter? active, TimeZoneInfo zone, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(zone);
        _app = app;
        _zone = zone;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Title = $"Filter ({zone.Id})";
        Width = Math.Min(DialogWidth, app.Screen.Width);
        Height = Math.Min(ContentRows + ButtonAreaRows, app.Screen.Height);

        FilterInput input = FilterForm.InputOf(active, zone);

        // Row 0: the minimum level, one row of options changed with the arrow keys. Typing does not change it, and it is one Tab stop.
        _level = new OptionSelector
        {
            X = LabelWidth,
            Y = 0,
            Orientation = Terminal.Gui.ViewBase.Orientation.Horizontal,
            Labels = FilterForm.LevelChoices.Select(choice => choice.Label).ToList(),
            Value = FilterForm.LevelIndex(input.MinLevel),
        };

        // The options are reached with the arrow keys, so the selector is the only Tab stop of the row. Moving the focus to an option
        // chooses it: the arrow keys change the level, and no other key does.
        CheckBox[] options = _level.SubViews.OfType<CheckBox>().ToArray();
        for (int index = 0; index < options.Length; index++)
        {
            int chosen = index;
            options[index].TabStop = TabBehavior.NoStop;
            options[index].HasFocusChanged += (_, _) =>
            {
                if (options[chosen].HasFocus)
                {
                    _level.Value = chosen;
                }
            };
        }

        _source = Field(LabelWidth, 1, input.Source);
        _file = Field(LabelWidth, 2, input.File);
        _text = Field(LabelWidth, 3, input.Text);

        // Row 4: the three text options, each a check box.
        _caseSensitive = Check("Case-sensitive", LabelWidth, 4, input.CaseSensitive);
        _regex = Check("Regex", LabelWidth + 21, 4, input.Regex);
        _wholeWord = Check("Whole word", LabelWidth + 33, 4, input.WholeWord);

        _from = Field(LabelWidth, 5, input.From);
        _to = Field(LabelWidth, 6, input.To);

        // Rows 7 to 10: the predicates, one per line.
        _predicates = new TextView
        {
            X = LabelWidth,
            Y = 7,
            Width = FieldWidth,
            Height = PredicateRows,
            Multiline = true,
            TabKeyAddsTab = false,
            Text = input.Predicates,
        };

        // The controls are added in the order of the form, top to bottom and left to right: that is the Tab order.
        View[] root =
        {
            Caption("Minimum level", 0, 0), _level,
            Caption(FilterForm.FieldLabels.Source, 0, 1), _source,
            Caption(FilterForm.FieldLabels.File, 0, 2), _file,
            Caption(FilterForm.FieldLabels.Text, 0, 3), _text,
            _caseSensitive, _regex, _wholeWord,
            Caption(FilterForm.FieldLabels.From, 0, 5), _from,
            Caption(FilterForm.FieldLabels.To, 0, 6), _to,
            Caption(FilterForm.FieldLabels.Predicates, 0, 7), _predicates,
            Caption("Times: yyyy-MM-dd HH:mm[:ss], or HH:mm[:ss] for today.", 0, 11),
            Caption("Fields: one path = value per line; blank lines are ignored.", 0, 12),
        };
        Add(root);

        // Focusing the selector chooses the option it focuses, so the pre-filled level is set after the focus has moved.
        _level.SetFocus();
        _level.Value = FilterForm.LevelIndex(input.MinLevel);

        // Buttons in the order Clear, Cancel, Apply. Clear and Cancel handle their own accept, so it does not reach OnAccepting, which is Apply.
        var clear = new Button { Text = "_Clear" };
        var cancel = new Button { Text = "_Cancel" };
        var apply = new Button { Text = "_Apply" };
        clear.Accepting += (_, e) =>
        {
            e.Handled = true;
            Clear();
        };
        cancel.Accepting += (_, e) =>
        {
            e.Handled = true;
            _app.RequestStop(this);
        };
        AddButton(clear);
        AddButton(cancel);
        AddButton(apply);

        // Terminal.Gui makes the last button added the default; Enter must mean Apply.
        apply.IsDefault = true;
        clear.IsDefault = false;
        cancel.IsDefault = false;
    }

    /// <summary>
    /// Shows the editor pre-filled from the active filter, and returns what was applied: a result with the filter (or <c>null</c> for no
    /// filter), or <c>null</c> when the editor was cancelled.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="active">The active filter, or <c>null</c>.</param>
    /// <param name="zone">The zone the times are in.</param>
    public static FilterEdit? Ask(IApplication app, EventFilter? active, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(app);
        using var dialog = new FilterDialog(app, active, zone);
        app.Run(dialog);
        return dialog.Result;
    }

    /// <summary>The input as the fields hold it now.</summary>
    internal FilterInput Input() => new(
        FilterForm.LevelAt(_level.Value),
        _source.Text,
        _file.Text,
        _from.Text,
        _to.Text,
        _text.Text,
        _caseSensitive.Value == CheckState.Checked,
        _wholeWord.Value == CheckState.Checked,
        _regex.Value == CheckState.Checked,
        _predicates.Text);

    /// <summary>
    /// Validates the fields and builds the edit. Returns <c>true</c> with the edit when they are valid; otherwise <c>false</c> with the message
    /// that names the field that is wrong. Nothing is changed by a call.
    /// </summary>
    /// <param name="edit">The edit when valid, otherwise <c>null</c>.</param>
    /// <param name="error">The message when invalid, otherwise <c>null</c>.</param>
    internal bool TryRead(out FilterEdit? edit, out string? error)
    {
        try
        {
            FilterSpec spec = FilterForm.Build(Input(), _zone, _clock());
            var filter = new EventFilter(spec);
            edit = new FilterEdit(filter.IsEmpty ? null : filter);
            error = null;
            return true;
        }
        catch (FilterInputException ex)
        {
            error = $"{ex.Field}: {ex.Message}";
        }
        catch (ArgumentException ex)
        {
            error = $"Filter: {ex.Message}";
        }

        edit = null;
        return false;
    }

    /// <summary>Empties every field: the level is any, the text options are off, and the times and predicates are blank.</summary>
    internal void Clear()
    {
        _level.Value = FilterForm.LevelIndex(null);
        _source.Text = string.Empty;
        _file.Text = string.Empty;
        _text.Text = string.Empty;
        _caseSensitive.Value = CheckState.UnChecked;
        _regex.Value = CheckState.UnChecked;
        _wholeWord.Value = CheckState.UnChecked;
        _from.Text = string.Empty;
        _to.Text = string.Empty;
        _predicates.Text = string.Empty;
    }

    /// <summary>Apply, or Enter in a field: validate, and close with the edit only when the fields are valid.</summary>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        args.Handled = true;
        if (TryRead(out FilterEdit? edit, out string? error))
        {
            Result = edit;
            _app.RequestStop(this);
        }
        else
        {
            MessageBox.ErrorQuery(_app, "Filter", error!, "_OK");
        }

        return true;
    }

    private static Label Caption(string text, int x, int y) => new() { Text = text, X = x, Y = y };

    private static TextField Field(int x, int y, string text) => new()
    {
        X = x,
        Y = y,
        Width = FieldWidth,
        Text = text,
    };

    private static CheckBox Check(string text, int x, int y, bool value) => new()
    {
        X = x,
        Y = y,
        Text = text,
        Value = value ? CheckState.Checked : CheckState.UnChecked,
    };
}
