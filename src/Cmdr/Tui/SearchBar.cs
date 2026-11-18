using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Cmdr.Tui;

/// <summary>
/// The one-row search bar above the status line: the find field, the option toggles <c>Aa</c>, <c>Word</c> and <c>.*</c>, and the
/// status of the search. While the field has the focus the toggles follow it; once the focus is back in a pane they give way to
/// the hint <c>F3 next · Shift+F3 prev</c> in the same place. The bar is modal while it has the focus: it takes every key except
/// Ctrl+Q and Ctrl+S, which pass on to the main window. It knows nothing of the panes; the controller reacts to its events and
/// sets its status.
/// <para>
/// Esc clears the field first and closes the bar only when the field is already empty. The ↑ and ↓ keys step through the terms
/// remembered by this bar (see <see cref="SearchHistory"/>). A term is remembered when the search is left with Enter, Tab, Shift+Tab,
/// F3 or Shift+F3, by Esc clearing the field, or by closing the bar (see <see cref="RememberQuery"/>). Any other change of the text
/// ends a history navigation.
/// </para>
/// </summary>
internal sealed class SearchBar : View
{
    /// <summary>The hint shown in place of the toggles while the focus is in a pane.</summary>
    private const string NextPrevHint = "F3 next · Shift+F3 prev";

    // The toggles are drawn with one space of padding on each side, so that the ON colour reads as a chip. The widths are the
    // padded texts' lengths.
    private const int CaptionWidth = 6;
    private const int CaseWidth = 4;
    private const int WordWidth = 6;
    private const int RegexWidth = 4;
    private const int OptionSpacing = 1;
    private const int OptionsWidth = CaseWidth + OptionSpacing + WordWidth + OptionSpacing + RegexWidth;
    private const int TailGap = 1;
    private const int Gap = 2;
    private const int MinFieldWidth = 8;

    private readonly SearchField _field;
    private readonly OptionToggle _caseBox;
    private readonly OptionToggle _wordBox;
    private readonly OptionToggle _regexBox;
    private readonly Label _status;
    private readonly Label _hint;
    private readonly SearchHistory _history = new();
    private bool _quiet;
    private bool _fromHistory;

    /// <summary>Raised when the user changes the text or an option. <see cref="SetQuery"/> does not raise it.</summary>
    public event Action<SearchQuery>? QueryChanged;

    /// <summary>Raised by Enter, Tab and Shift+Tab: the focus should go back to the pane.</summary>
    public event Action? FocusPaneRequested;

    /// <summary>Raised by Esc when the field is empty: the search should close.</summary>
    public event Action? Closed;

    /// <summary>Raised by F3 (false) and Shift+F3 (true): the next (or previous) match is wanted.</summary>
    public event Action<bool>? FindRequested;

    /// <summary>
    /// Creates the bar with its field, toggles, status and hint. The bar is focusable only so that its field can take the focus (a
    /// view cannot gain it under a parent that cannot take it); it is kept out of the Tab order, and the field is the one to focus.
    /// The toggles never take the focus; they are clicked with the mouse or toggled with Alt+C, Alt+W and Alt+R.
    /// </summary>
    public SearchBar()
    {
        CanFocus = true;
        TabStop = TabBehavior.NoStop;
        _field = new SearchField(HandleFieldKey) { X = CaptionWidth, Y = 0, Width = MinFieldWidth, Height = 1 };
        _caseBox = new OptionToggle(" Aa ", CaseWidth);
        _wordBox = new OptionToggle(" Word ", WordWidth);
        _regexBox = new OptionToggle(" .* ", RegexWidth);
        _status = new Label { X = 0, Y = 0, Width = 0, Height = 1, Text = string.Empty, CanFocus = false };
        _hint = new Label { X = 0, Y = 0, Width = 0, Height = 1, Text = NextPrevHint, CanFocus = false };
        var caption = new Label { X = 0, Y = 0, Width = CaptionWidth, Height = 1, Text = "Find: ", CanFocus = false };

        _field.TextChanged += (_, _) => OnFieldTextChanged();
        _field.HasFocusChanged += (_, _) => Relayout();
        _caseBox.ValueChanged += RaiseQueryChanged;
        _wordBox.ValueChanged += RaiseQueryChanged;
        _regexBox.ValueChanged += RaiseQueryChanged;

        Add(caption, _field, _caseBox, _wordBox, _regexBox, _hint, _status);
    }

    /// <summary>The query in the field and the state of the three toggles.</summary>
    public SearchQuery Query => new(_field.Text ?? string.Empty, _caseBox.On, _wordBox.On, _regexBox.On);

    /// <summary>The status text shown at the right end of the bar, after the toggles or the hint. Empty when there is nothing to report.</summary>
    public string Status => _status.Text ?? string.Empty;

    /// <summary>True when the field has the focus, which is where the keys go while the bar is open.</summary>
    public bool HasFieldFocus => _field.HasFocus;

    /// <summary>
    /// Sets the text and the options shown, without raising <see cref="QueryChanged"/>. The text is not a history step, so a
    /// navigation in progress ends.
    /// </summary>
    public void SetQuery(SearchQuery query)
    {
        _quiet = true;
        try
        {
            _field.Text = query.Text;
            _caseBox.On = query.CaseSensitive;
            _wordBox.On = query.WholeWord;
            _regexBox.On = query.Regex;
        }
        finally
        {
            _quiet = false;
        }
    }

    /// <summary>
    /// Remembers the text in the field as a search term, for the ↑ and ↓ keys. The controller calls it when the search closes by any
    /// route. Empty text is not remembered.
    /// </summary>
    public void RememberQuery() => _history.Add(_field.Text ?? string.Empty);

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
    /// Places the parts for the bar's width and the focus. While the field has the focus the toggles follow it, otherwise the hint
    /// does, both right after the field. The field takes the room left after that and the status, but never less than its minimum,
    /// and the status is cut to what remains. The toggles or the hint are hidden when the bar is too narrow for the caption, the
    /// field's minimum and them.
    /// </summary>
    private void Relayout()
    {
        int width = Viewport.Width;
        bool typing = _field.HasFocus;
        int tailWidth = typing ? OptionsWidth : TextWidth.Columns(NextPrevHint);
        bool showTail = CaptionWidth + MinFieldWidth + TailGap + tailWidth <= width;
        int tailSpace = showTail ? TailGap + tailWidth : 0;
        int room = Math.Max(0, width - CaptionWidth - tailSpace);
        int statusWidth = TextWidth.Columns(Status);
        int fieldWidth = Math.Min(Math.Max(MinFieldWidth, room - Gap - statusWidth), room);
        int tailX = CaptionWidth + fieldWidth + TailGap;
        int statusX = CaptionWidth + fieldWidth + tailSpace + Gap;

        _field.X = CaptionWidth;
        _field.Width = fieldWidth;
        _caseBox.Visible = showTail && typing;
        _wordBox.Visible = showTail && typing;
        _regexBox.Visible = showTail && typing;
        _hint.Visible = showTail && !typing;
        _caseBox.X = tailX;
        _wordBox.X = tailX + CaseWidth + OptionSpacing;
        _regexBox.X = _wordBox.X + WordWidth + OptionSpacing;
        _hint.X = tailX;
        _hint.Width = TextWidth.Columns(NextPrevHint);
        _status.X = statusX;
        _status.Width = Math.Max(0, width - statusX);
    }

    /// <summary>
    /// The keys the field must not consume: Enter, Tab and Shift+Tab, Esc, F3, Shift+F3, ↑, ↓ and Alt+C, Alt+W and Alt+R. Called before
    /// the field's own handling. Returns true when the key is handled.
    /// <para>
    /// Enter, Tab, Shift+Tab, F3 and Shift+F3 remember the text before they act. Esc with text remembers it and clears the field, which
    /// raises <see cref="QueryChanged"/> and leaves the bar open; Esc with an empty field raises <see cref="Closed"/>. ↑ and ↓ step
    /// through the remembered terms, and are consumed even when there is no older or newer one.
    /// </para>
    /// </summary>
    private bool HandleFieldKey(Key key)
    {
        if (key == Key.Enter || key == Key.Tab || key == Key.Tab.WithShift)
        {
            RememberQuery();
            FocusPaneRequested?.Invoke();
            return true;
        }

        if (key == Key.Esc)
        {
            if (_field.Text is not { Length: > 0 })
            {
                Closed?.Invoke();
                return true;
            }

            RememberQuery();
            _field.Text = string.Empty;
            return true;
        }

        if (key == Key.F3)
        {
            RememberQuery();
            FindRequested?.Invoke(false);
            return true;
        }

        if (key == Key.F3.WithShift)
        {
            RememberQuery();
            FindRequested?.Invoke(true);
            return true;
        }

        if (key == Key.CursorUp)
        {
            ShowHistoryText(_history.Older(_field.Text ?? string.Empty));
            return true;
        }

        if (key == Key.CursorDown)
        {
            ShowHistoryText(_history.Newer());
            return true;
        }

        if (key == Key.C.WithAlt)
        {
            _caseBox.Toggle();
            return true;
        }

        if (key == Key.W.WithAlt)
        {
            _wordBox.Toggle();
            return true;
        }

        if (key == Key.R.WithAlt)
        {
            _regexBox.Toggle();
            return true;
        }

        return false;
    }

    /// <remarks>
    /// Runs after the focused field has declined the key, so every key the field does not use ends here. Ctrl+Q and Ctrl+S are
    /// the only keys that pass on to the main window.
    /// </remarks>
    protected override bool OnKeyDown(Key key) => !(key == Key.Q.WithCtrl || key == Key.S.WithCtrl);

    /// <summary>
    /// Shows a remembered term in the field, with the cursor at its end. A null text (no term that way) leaves the field as it is.
    /// </summary>
    private void ShowHistoryText(string? text)
    {
        if (text is null)
        {
            return;
        }

        _fromHistory = true;
        try
        {
            _field.Text = text;
            _field.InsertionPoint = GraphemeHelper.GetGraphemeCount(text);
        }
        finally
        {
            _fromHistory = false;
        }
    }

    /// <summary>
    /// Any change of the field's text. A history step keeps a navigation in progress; any other change (typing, deleting, Esc
    /// clearing, <see cref="SetQuery"/>) ends it. Then the query is raised.
    /// </summary>
    private void OnFieldTextChanged()
    {
        if (!_fromHistory)
        {
            _history.Reset();
        }

        RaiseQueryChanged();
    }

    private void RaiseQueryChanged()
    {
        if (!_quiet)
        {
            QueryChanged?.Invoke(Query);
        }
    }

    /// <summary>
    /// One option toggle: its label, padded by a space on each side, drawn in the active colour while it is on and in the bar's
    /// normal colour while it is off. It never takes the focus and has no hover effect. A left press flips it.
    /// </summary>
    private sealed class OptionToggle : View
    {
        private readonly string _label;
        private bool _on;

        /// <param name="label">The padded label drawn in the toggle.</param>
        /// <param name="width">The width of the padded label, in columns.</param>
        public OptionToggle(string label, int width)
        {
            _label = label;
            Width = width;
            Height = 1;
            CanFocus = false;
        }

        /// <summary>True while the option is on. A change redraws the toggle and raises <see cref="ValueChanged"/>.</summary>
        public bool On
        {
            get => _on;
            set
            {
                if (value == _on)
                {
                    return;
                }

                _on = value;
                SetNeedsDraw();
                ValueChanged?.Invoke();
            }
        }

        /// <summary>Raised when <see cref="On"/> changes, by a click, by Alt+C, Alt+W or Alt+R, or by code.</summary>
        public event Action? ValueChanged;

        /// <summary>Flips the option.</summary>
        public void Toggle() => On = !On;

        protected override bool OnDrawingContent(DrawContext? context)
        {
            Attribute normal = GetAttributeForRole(VisualRole.Normal);
            SetAttribute(On ? SearchColors.Active(normal) : normal);
            Move(0, 0);
            AddStr(_label);
            return true;
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
            {
                return base.OnMouseEvent(mouse);
            }

            Toggle();
            return true;
        }
    }

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
