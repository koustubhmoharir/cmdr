using Shared.Tui;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cfgedit.Tui;

/// <summary>
/// The single-line field over a part of the tree or a cell of the table. Every key it receives goes to <paramref name="onKey"/> first; a
/// key the handler returns false for is edited as text, as a <see cref="TextField"/> does. The handler decides Enter, Esc, Tab and F4, so
/// the window keeps the commit and the navigation.
/// </summary>
/// <remarks>
/// The field is as wide as its text plus two columns (the cursor and one spare), and never narrower than <see cref="MinWidth"/>. It is
/// capped at the columns left in its superview from its X, so it never runs past the view's right edge; text wider than that scrolls
/// inside the field. The width is a <see cref="Dim.Func"/>, so the layout recomputes it on a text change, a move and a resize.
/// </remarks>
internal sealed class InlineEditor : TextField
{
    /// <summary>The narrowest the field is, in columns, however short its text.</summary>
    internal const int MinWidth = 10;

    private readonly Func<Key, bool> _onKey;

    /// <summary>Creates the field showing a text. The view that holds it places it over the part or the cell.</summary>
    /// <param name="text">The text the field starts with.</param>
    /// <param name="onKey">Handles a key first. Returns true when the key was handled.</param>
    public InlineEditor(string text, Func<Key, bool> onKey)
    {
        ArgumentNullException.ThrowIfNull(onKey);
        _onKey = onKey;
        Text = text;
        Height = 1;
        Width = Dim.Func(_ => WidthForText());
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key) => _onKey(key) || base.OnKeyDown(key);

    /// <inheritdoc/>
    protected override void OnTextChanged()
    {
        base.OnTextChanged();
        SetNeedsLayout();
    }

    /// <summary>
    /// The width for the current text: its display columns plus two (the cursor and one spare), at least <see cref="MinWidth"/>, and at
    /// most the columns left in the superview from the field's X (at least 1).
    /// </summary>
    /// <returns>The width in columns.</returns>
    private int WidthForText()
    {
        // Two columns past the text: one for the cursor and one spare. TextField scrolls back to the start only when the text plus one
        // is narrower than the field, so a field of exactly text plus one keeps the first characters scrolled away as it widens.
        int wanted = Math.Max(MinWidth, TextWidth.Columns(Text) + 2);
        if (SuperView is null)
        {
            return wanted;
        }

        int x = X.Has(out PosAbsolute absolute) ? absolute.Position : 0;
        int left = Math.Max(1, SuperView.Viewport.Width - x);
        return Math.Min(wanted, left);
    }
}
