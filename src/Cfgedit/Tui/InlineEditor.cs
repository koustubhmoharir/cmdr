using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cfgedit.Tui;

/// <summary>
/// The single-line field over a part of the tree. Every key it receives goes to <paramref name="onKey"/> first; a key the handler
/// returns false for is edited as text, as a <see cref="TextField"/> does. The handler decides Enter, Esc, Tab and F4, so the window
/// keeps the commit and the navigation.
/// </summary>
internal sealed class InlineEditor : TextField
{
    private readonly Func<Key, bool> _onKey;

    /// <summary>Creates the field showing a text. It is laid out by the tree, which places it over the part.</summary>
    /// <param name="text">The text the field starts with.</param>
    /// <param name="onKey">Handles a key first. Returns true when the key was handled.</param>
    public InlineEditor(string text, Func<Key, bool> onKey)
    {
        ArgumentNullException.ThrowIfNull(onKey);
        _onKey = onKey;
        Text = text;
        Height = 1;
        Width = Dim.Fill();
    }

    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key) => _onKey(key) || base.OnKeyDown(key);
}
