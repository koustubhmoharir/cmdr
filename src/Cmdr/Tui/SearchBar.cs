using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cmdr.Tui;

/// <summary>
/// The one-row search bar above the status line: the find field, the three option checkboxes, the status of the search and
/// the key hints at the right edge. The bar is modal while it has the focus: it takes every key except Ctrl+Q and Ctrl+S,
/// which pass on to the main window. It knows nothing of the panes; the controller reacts to its events and sets its status.
/// </summary>
internal sealed class SearchBar : View
{
    // The hints, longest first. The widest that leaves the field its minimum and the options is shown; the next and previous keys
    // are in every one, so they are always visible.
    private static readonly string[] HintTexts =
    [
        "F3 next · Shift+F3 prev · Enter to pane · Esc close",
        "F3 next · Shift+F3 prev · Esc close",
        "F3 next · Shift+F3 prev",
    ];

    private const int CaptionWidth = 6;
    private const int CaseWidth = 6;
    private const int WordWidth = 8;
    private const int RegexWidth = 6;
    private const int OptionSpacing = 1;
    private const int OptionsWidth = CaseWidth + OptionSpacing + WordWidth + OptionSpacing + RegexWidth;
    private const int Gap = 2;
    private const int MinFieldWidth = 8;

    private readonly SearchField _field;
    private readonly CheckBox _caseBox;
    private readonly CheckBox _wordBox;
    private readonly CheckBox _regexBox;
    private readonly Label _status;
    private readonly Label _hint;
    private bool _quiet;

    /// <summary>Raised when the user changes the text or an option. <see cref="SetQuery"/> does not raise it.</summary>
    public event Action<SearchQuery>? QueryChanged;

    /// <summary>Raised by Enter, Tab and Shift+Tab: the focus should go back to the pane.</summary>
    public event Action? FocusPaneRequested;

    /// <summary>Raised by Esc: the search should close.</summary>
    public event Action? Closed;

    /// <summary>Raised by F3 (false) and Shift+F3 (true): the next (or previous) match is wanted.</summary>
    public event Action<bool>? FindRequested;

    /// <summary>
    /// Creates the bar with its field, options, status and hint. The bar is focusable only so that its field can take the focus (a
    /// view cannot gain it under a parent that cannot take it); it is kept out of the Tab order, and the field is the one to focus.
    /// </summary>
    public SearchBar()
    {
        CanFocus = true;
        TabStop = TabBehavior.NoStop;
        _field = new SearchField(HandleFieldKey) { X = CaptionWidth, Y = 0, Width = MinFieldWidth, Height = 1 };
        _caseBox = Option("Aa", CaseWidth);
        _wordBox = Option("Word", WordWidth);
        _regexBox = Option(".*", RegexWidth);
        _status = new Label { X = 0, Y = 0, Width = 0, Height = 1, Text = string.Empty, CanFocus = false };
        _hint = new Label { X = 0, Y = 0, Width = 0, Height = 1, Text = HintTexts[0], CanFocus = false };
        var caption = new Label { X = 0, Y = 0, Width = CaptionWidth, Height = 1, Text = "Find: ", CanFocus = false };

        _field.TextChanged += (_, _) => RaiseQueryChanged();
        _caseBox.ValueChanged += (_, _) => RaiseQueryChanged();
        _wordBox.ValueChanged += (_, _) => RaiseQueryChanged();
        _regexBox.ValueChanged += (_, _) => RaiseQueryChanged();

        // The hint is added last so that it is drawn over the rest if the bar is too narrow for everything.
        Add(caption, _field, _caseBox, _wordBox, _regexBox, _status, _hint);
    }

    /// <summary>The query in the field and the state of the three options.</summary>
    public SearchQuery Query => new(
        _field.Text ?? string.Empty,
        _caseBox.Value == CheckState.Checked,
        _wordBox.Value == CheckState.Checked,
        _regexBox.Value == CheckState.Checked);

    /// <summary>The status text shown after the options. Empty when there is nothing to report.</summary>
    public string Status => _status.Text ?? string.Empty;

    /// <summary>True when the field has the focus, which is where the keys go while the bar is open.</summary>
    public bool HasFieldFocus => _field.HasFocus;

    /// <summary>Sets the text and the options shown, without raising <see cref="QueryChanged"/>.</summary>
    public void SetQuery(SearchQuery query)
    {
        _quiet = true;
        try
        {
            _field.Text = query.Text;
            _caseBox.Value = Check(query.CaseSensitive);
            _wordBox.Value = Check(query.WholeWord);
            _regexBox.Value = Check(query.Regex);
        }
        finally
        {
            _quiet = false;
        }
    }

    /// <summary>Focuses the field, and selects all its text when <paramref name="selectAll"/> is true.</summary>
    public void FocusField(bool selectAll)
    {
        _field.SetFocus();
        if (selectAll)
        {
            _field.SelectAll();
        }
    }

    /// <summary>Shows the status text. The bar redraws only when the text changes.</summary>
    public void SetStatus(string text)
    {
        if (text == Status)
        {
            return;
        }

        _status.Text = text;
        Relayout();
    }

    /// <inheritdoc/>
    protected override void OnViewportChanged(DrawEventArgs e)
    {
        base.OnViewportChanged(e);
        Relayout();
    }

    /// <summary>
    /// Places the parts for the bar's width. The hint is always fully visible at the right edge: the widest hint that leaves the field
    /// its minimum and the options is shown, and below that the shortest, which holds the next and previous keys. The field takes the
    /// room that is left after the options and the status, but never less than its minimum; the status is cut to what remains before
    /// the hint. When the bar is too narrow even for the shortest hint with the field and the options, the options are hidden.
    /// </summary>
    private void Relayout()
    {
        int width = Viewport.Width;
        int fixedWidth = CaptionWidth + MinFieldWidth + OptionSpacing + OptionsWidth + Gap;
        string hint = HintTexts.FirstOrDefault(text => fixedWidth + TextWidth.Columns(text) <= width) ?? HintTexts[^1];
        int hintWidth = TextWidth.Columns(hint);
        bool options = fixedWidth + hintWidth <= width;
        _caseBox.Visible = options;
        _wordBox.Visible = options;
        _regexBox.Visible = options;
        _hint.Text = hint;
        _hint.Width = Math.Min(width, hintWidth);
        int hintX = Math.Max(0, width - hintWidth);
        int left = Math.Max(0, hintX - Gap);
        int statusWidth = TextWidth.Columns(Status);

        int optionsWidth = options ? OptionSpacing + OptionsWidth : 0;
        int fieldWidth = Math.Max(MinFieldWidth, left - CaptionWidth - optionsWidth - Gap - statusWidth);
        fieldWidth = Math.Min(fieldWidth, Math.Max(0, left - CaptionWidth));
        int optionsX = CaptionWidth + fieldWidth + OptionSpacing;
        int wordX = optionsX + CaseWidth + OptionSpacing;
        int regexX = wordX + WordWidth + OptionSpacing;
        int statusX = CaptionWidth + fieldWidth + optionsWidth + Gap;

        _field.X = CaptionWidth;
        _field.Width = fieldWidth;
        _caseBox.X = optionsX;
        _wordBox.X = wordX;
        _regexBox.X = regexX;
        _status.X = statusX;
        _status.Width = Math.Max(0, left - statusX);
        _hint.X = hintX;
    }

    /// <summary>
    /// The keys the field must not consume: Enter, Tab and Shift+Tab, Esc, F3, Shift+F3 and Alt+C, Alt+W and Alt+R. Called before the
    /// field's own handling. Returns true when the key is handled.
    /// </summary>
    private bool HandleFieldKey(Key key)
    {
        if (key == Key.Enter || key == Key.Tab || key == Key.Tab.WithShift)
        {
            FocusPaneRequested?.Invoke();
            return true;
        }

        if (key == Key.Esc)
        {
            Closed?.Invoke();
            return true;
        }

        if (key == Key.F3)
        {
            FindRequested?.Invoke(false);
            return true;
        }

        if (key == Key.F3.WithShift)
        {
            FindRequested?.Invoke(true);
            return true;
        }

        if (key == Key.C.WithAlt)
        {
            Toggle(_caseBox);
            return true;
        }

        if (key == Key.W.WithAlt)
        {
            Toggle(_wordBox);
            return true;
        }

        if (key == Key.R.WithAlt)
        {
            Toggle(_regexBox);
            return true;
        }

        return false;
    }

    /// <remarks>
    /// Runs after the focused field has declined the key, so every key the field does not use ends here. Ctrl+Q and Ctrl+S are
    /// the only keys that pass on to the main window.
    /// </remarks>
    protected override bool OnKeyDown(Key key) => !(key == Key.Q.WithCtrl || key == Key.S.WithCtrl);

    private void RaiseQueryChanged()
    {
        if (!_quiet)
        {
            QueryChanged?.Invoke(Query);
        }
    }

    private static void Toggle(CheckBox box) => box.Value = Check(box.Value != CheckState.Checked);

    private static CheckState Check(bool on) => on ? CheckState.Checked : CheckState.UnChecked;

    /// <summary>A checkbox that does not take the focus; the focus stays in the field.</summary>
    private static CheckBox Option(string text, int width) => new()
    {
        X = 0,
        Y = 0,
        Width = width,
        Height = 1,
        Text = text,
        CanFocus = false,
    };

    /// <summary>
    /// The find field. It hands each key to the bar before its own handling, so that Enter, Esc, F3 and the option keys are not
    /// taken as text.
    /// </summary>
    private sealed class SearchField : TextField
    {
        private readonly Func<Key, bool> _handleKey;

        /// <param name="handleKey">Called with every key first. Returns true when the key is handled and the field must not see it.</param>
        public SearchField(Func<Key, bool> handleKey)
        {
            _handleKey = handleKey;
        }

        /// <inheritdoc/>
        protected override bool OnKeyDown(Key key) => _handleKey(key) || base.OnKeyDown(key);
    }
}
