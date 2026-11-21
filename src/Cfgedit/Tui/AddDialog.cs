using Cfgedit.Core.Model;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cfgedit.Tui;

/// <summary>
/// The Add dialog: a kind chosen from the kinds the place allows, then the key or name the kind takes and the text a comment takes.
/// Left and Right (or Space) on the kind change it. OK passes the request to the apply function; an error it returns is shown above
/// the buttons and the dialog stays open with the values as typed. Cancel and Esc close it. OK is the default.
/// </summary>
/// <remarks>
/// The kind is a single row, not a list, so that the key field, the text field and the error all fit a 40 by 12 screen. The dialog is
/// sized from the screen at every layout.
/// </remarks>
internal sealed class AddDialog : Dialog<bool>
{
    /// <summary>The width limit: the dialog is no wider than this, and no wider than the screen.</summary>
    internal const int MaxWidth = 60;

    /// <summary>The height the dialog asks for: the kind, the name, the text, a blank row and the error, above the buttons.</summary>
    internal const int WantedHeight = 10;

    private const int LabelWidth = 7;

    private readonly IApplication _app;
    private readonly DocNode _parent;
    private readonly Func<AddRequest, string?> _apply;
    private readonly KindChooser _kind;
    private readonly Label _nameLabel;
    private readonly TextField _name;
    private readonly Label _textLabel;
    private readonly TextField _text;
    private readonly Label _error;

    private AddDialog(IApplication app, string title, IReadOnlyList<AddKind> kinds, DocNode parent, Func<AddRequest, string?> apply)
    {
        _app = app;
        _parent = parent;
        _apply = apply;
        Title = title;

        // Sized from the screen at every layout, so the dialog follows the terminal when it is resized.
        Width = Dim.Func(_ => Math.Max(1, Math.Min(MaxWidth, _app.Screen.Width)));
        Height = Dim.Func(_ => Math.Max(1, Math.Min(WantedHeight, _app.Screen.Height)));

        _kind = new KindChooser(kinds) { X = 0, Y = 0, Width = Dim.Fill() };
        _nameLabel = new Label { X = 0, Y = 1, Width = LabelWidth, Height = 1 };
        _name = new TextField { X = LabelWidth, Y = 1, Width = Dim.Fill(), Height = 1 };
        _textLabel = new Label { X = 0, Y = 2, Width = LabelWidth, Height = 1, Text = "Text:" };
        _text = new TextField { X = LabelWidth, Y = 2, Width = Dim.Fill(), Height = 1 };
        _error = new Label { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1, Text = string.Empty };
        Add(_kind, _nameLabel, _name, _textLabel, _text, _error);

        var cancel = new Button { Text = "_Cancel" };
        var ok = new Button { Text = "_OK" };
        cancel.Accepting += (_, e) =>
        {
            e.Handled = true;
            Cancel();
        };
        AddButton(cancel);
        AddButton(ok);

        // Terminal.Gui makes the last button added the default; Enter means OK.
        ok.IsDefault = true;
        cancel.IsDefault = false;

        _kind.Changed += ShowFieldsForKind;
        ShowFieldsForKind();

        // The key or name comes first when the kind takes one; otherwise the kind is chosen first.
        if (_name.Visible)
        {
            _name.SetFocus();
        }
        else
        {
            _kind.SetFocus();
        }
    }

    /// <summary>The kind the dialog has chosen, for the tests.</summary>
    internal AddKind Kind => _kind.Kind;

    /// <summary>The error shown above the buttons, or empty when there is none. For the tests.</summary>
    internal string ErrorText => _error.Text;

    /// <summary>The key or name field, for the tests.</summary>
    internal TextField NameField => _name;

    /// <summary>The text field, for the tests.</summary>
    internal TextField TextField => _text;

    /// <summary>The kind chooser, for the tests.</summary>
    internal View KindView => _kind;

    /// <summary>
    /// Shows the dialog and returns when it closes. Returns true when the apply function accepted the request, and false when the
    /// dialog was cancelled.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="title">The title, such as "Add after $.items[1]".</param>
    /// <param name="kinds">The kinds the place allows, in the order they are offered. Must not be empty.</param>
    /// <param name="parent">The node the new node goes into. Its kind decides whether the kind takes a key.</param>
    /// <param name="apply">Applies the request. Returns the error to show, or null when it was applied.</param>
    public static bool Ask(IApplication app, string title, IReadOnlyList<AddKind> kinds, DocNode parent, Func<AddRequest, string?> apply)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(apply);
        if (kinds.Count == 0)
        {
            throw new ArgumentException("At least one kind is needed.", nameof(kinds));
        }

        using var dialog = new AddDialog(app, title, kinds, parent, apply);
        app.Run(dialog);
        return dialog.Result == true;
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

    /// <summary>OK, or Enter in a field: apply the request, and close only when it is accepted.</summary>
    protected override bool OnAccepting(CommandEventArgs args)
    {
        args.Handled = true;
        TryAccept();
        return true;
    }

    private void TryAccept()
    {
        string? error = _apply(new AddRequest(_kind.Kind, _name.Text, _text.Text));
        if (error is null)
        {
            Result = true;
            _app.RequestStop(this);
            return;
        }

        _error.Text = error;
    }

    private void Cancel()
    {
        Result = false;
        _app.RequestStop(this);
    }

    // The name and text rows follow the kind. A row that a kind does not take is hidden, and the focus moves off a hidden field.
    private void ShowFieldsForKind()
    {
        string? nameLabel = AddKinds.NameLabel(_parent, _kind.Kind);
        _nameLabel.Text = nameLabel is null ? string.Empty : nameLabel + ":";
        _nameLabel.Visible = nameLabel is not null;
        _name.Visible = nameLabel is not null;
        bool takesText = AddKinds.TakesText(_kind.Kind);
        _textLabel.Visible = takesText;
        _text.Visible = takesText;
        _error.Text = string.Empty;
        if ((!_name.Visible && _name.HasFocus) || (!_text.Visible && _text.HasFocus))
        {
            _kind.SetFocus();
        }

        SetNeedsDraw();
    }

    /// <summary>
    /// The kind row: the name of the kind between angle brackets, changed with Left, Right or Space. It takes the focus with Tab like a field.
    /// </summary>
    private sealed class KindChooser : View
    {
        private readonly IReadOnlyList<AddKind> _kinds;
        private int _index;

        public KindChooser(IReadOnlyList<AddKind> kinds)
        {
            _kinds = kinds;
            Height = 1;
            CanFocus = true;
            TabStop = TabBehavior.TabStop;
        }

        /// <summary>The kind shown.</summary>
        public AddKind Kind => _kinds[_index];

        /// <summary>Raised after the kind changes.</summary>
        public event Action? Changed;

        /// <inheritdoc/>
        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.CursorRight || key == Key.Space)
            {
                Step(1);
                return true;
            }

            if (key == Key.CursorLeft)
            {
                Step(-1);
                return true;
            }

            return base.OnKeyDown(key);
        }

        /// <inheritdoc/>
        protected override bool OnDrawingContent(DrawContext? context)
        {
            SetAttribute(GetAttributeForRole(HasFocus ? VisualRole.Focus : VisualRole.Normal));
            Move(0, 0);
            AddStr("Kind: < " + AddKinds.Label(Kind) + " >");
            return true;
        }

        private void Step(int delta)
        {
            _index = (_index + delta + _kinds.Count) % _kinds.Count;
            SetNeedsDraw();
            Changed?.Invoke();
        }
    }
}
