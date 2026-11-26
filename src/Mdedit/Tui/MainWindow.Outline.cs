using System.Collections.Immutable;
using Mdedit.Core.Render;
using Terminal.Gui.Input;

namespace Mdedit.Tui;

/// <summary>
/// The outline of the window, in both modes: Alt+O, and <c>o</c> in view mode, open the <see cref="OutlineDialog"/> over the document's
/// headings. The chosen heading is gone to as an anchor link is in view mode, and in edit mode the editor's caret goes to it with the folds
/// that hide it opened.
/// </summary>
/// <remarks>
/// <para>
/// <b>The document.</b> In view mode the outline is that of the view's document, which is always the buffer's text. In edit mode the view may be
/// stale (see <see cref="RenderBuffer"/>): the outline is then taken from a parse of the buffer's current text, so that its offsets are the
/// editor's.
/// </para>
/// <para>
/// <b>External changes.</b> While the dialog runs it is the application's top runnable, so the external-change check waits (see
/// <see cref="IsIdle"/>), as it does for the picker.
/// </para>
/// </remarks>
internal sealed partial class MainWindow
{
    /// <summary>The key of the outline in both modes.</summary>
    internal static readonly Key OutlineKey = Key.O.WithAlt;

    /// <summary>The message when the document has no headings, so there is no outline to show.</summary>
    internal const string NoHeadings = "No headings";

    /// <summary>
    /// The window's outline key: Alt+O in both modes, and <c>o</c> in view mode (in edit mode it is text). Returns false for a key it does not bind.
    /// </summary>
    private bool HandleOutlineKey(Key key)
    {
        if (key != OutlineKey && (_editing || !IsPlain(key, 'o')))
        {
            return false;
        }

        ShowOutline();
        return true;
    }

    /// <summary>
    /// Opens the outline of the document and goes to the heading that is chosen. A document with no headings says so, and no dialog is shown.
    /// </summary>
    private void ShowOutline()
    {
        ImmutableArray<OutlineEntry> outline = CurrentOutline();
        if (outline.IsEmpty)
        {
            ShowMessage(NoHeadings, error: true);
            return;
        }

        int caret = _editing ? _editor.CaretOffset : _view.CursorOffset;
        if (OutlineDialog.Ask(Ui, outline, caret) is { } start)
        {
            GoToHeading(start);
        }
    }

    /// <summary>
    /// The headings of the text the window shows: the view's document, or in edit mode the buffer's text when the view is stale (parsed here,
    /// so that the view keeps its document until it is rendered).
    /// </summary>
    private ImmutableArray<OutlineEntry> CurrentOutline()
    {
        if (!_editing || _renderedVersion == _editor.Version)
        {
            return Outline.Of(_view.Document);
        }

        return Outline.Of(RenderDocument.Parse(_editor.Buffer.Text));
    }

    /// <summary>
    /// Goes to a heading's start: in view mode the view's caret and top line go there, as an anchor link does (see <see cref="FollowAnchor"/>).
    /// In edit mode the editor's caret goes there, and the editor unfolds the foldings that hide it.
    /// </summary>
    /// <param name="start">The heading's start offset.</param>
    private void GoToHeading(int start)
    {
        if (_editing)
        {
            // The editor unfolds the foldings around a caret it is given (its EnsureCaretNotInFold), so a heading in a folded
            // section shows without an unfold here.
            _editor.PlaceCaret(start, null);
        }
        else
        {
            _view.GoTo(start, start);
        }
    }
}
