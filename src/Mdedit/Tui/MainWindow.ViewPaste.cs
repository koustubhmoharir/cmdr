using Mdedit.Core.Editing;
using Mdedit.Core.Render;
using Shared.Tui;
using Terminal.Gui.Input;

namespace Mdedit.Tui;

/// <summary>
/// Paste and cut in view mode. A paste writes text as Markdown source (see <see cref="ViewEdit.Paste"/>), over the selection when there is one. A paste
/// comes from the terminal's bracketed paste, which the view hands over as <see cref="RenderedView.PasteRequested"/>, or from Ctrl+V and the
/// right-click menu, which read the application clipboard. A cut copies the selection as Markdown and deletes it.
/// </summary>
/// <remarks>
/// <para>
/// <b>How a paste arrives.</b> The driver raises a bracketed paste as one string to the application, which hands it to the focused view as
/// <c>Command.Paste</c>. The view has no text model, so it sanitizes the text and calls <see cref="RenderedView.OnPaste"/>, which raises
/// <see cref="RenderedView.PasteRequested"/> and consumes the paste. The keys of the window never see it, so a paste is never typed key by key.
/// The view binds neither Ctrl+V nor Ctrl+X, so both reach <see cref="HandleClipboardKey"/> through the window's keys.
/// </para>
/// <para>
/// <b>The clipboard.</b> The application's clipboard is read with <c>TryGetClipboardData</c>. On Windows, macOS and WSL it is the system clipboard.
/// On Linux it is the <c>xclip</c> clipboard when that tool is present, and otherwise an in-process clipboard that holds what this session copied
/// or cut. When the driver has no clipboard at all, or the platform's reports that it cannot be read, Ctrl+V says so. A copy (Ctrl+C, Alt+C) and a
/// cut also write the application clipboard, when there is one, so that Ctrl+V pastes them back in the same session.
/// </para>
/// <para>
/// <b>Undo.</b> A paste and a cut are each one undo step, and each ends the typing run. A cut that is refused (or too large to copy) changes nothing.
/// </para>
/// </remarks>
internal sealed partial class MainWindow
{
    /// <summary>The message of Ctrl+V when the application has no clipboard to read.</summary>
    internal const string ClipboardUnavailable = "The clipboard isn't available here; use the terminal's paste";

    /// <summary>The message of Ctrl+V when the clipboard holds no text.</summary>
    internal const string NothingToPaste = "Nothing to paste";

    /// <summary>The message of Ctrl+X with no selection.</summary>
    internal const string NothingSelected = "Nothing is selected";

    /// <summary>The message of a cut that was made.</summary>
    internal const string CutDone = "Cut the selection; its Markdown is copied";

    /// <summary>
    /// The clipboard keys of view mode: Ctrl+V pastes the clipboard's text, and Ctrl+X cuts the selection. Returns false for any other key.
    /// </summary>
    /// <param name="key">The key.</param>
    private bool HandleClipboardKey(Key key)
    {
        if (key == Key.V.WithCtrl)
        {
            PasteFromClipboard();
            return true;
        }

        if (key == Key.X.WithCtrl)
        {
            CutSelection();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Pastes the application clipboard's text at the caret, or over the selection. Says that the clipboard is not available when there is none or it
    /// cannot be read, and that there is nothing to paste when it holds no text.
    /// </summary>
    private void PasteFromClipboard()
    {
        EndRun();
        if (!TryReadClipboard(out string text))
        {
            ShowMessage(ClipboardUnavailable, error: true);
            return;
        }

        string sanitized = _view.SanitizePaste(text);
        if (sanitized.Length == 0)
        {
            ShowMessage(NothingToPaste, error: true);
            return;
        }

        PasteInView(sanitized);
    }

    /// <summary>
    /// Writes text at the caret, or over the selection, as Markdown source (see <see cref="ViewEdit.Paste"/>), as one undo step that ends the typing
    /// run. The pending insertion is written in front of the text. A refusal with an empty message (empty text) changes nothing and shows nothing.
    /// A paste into a folded head is refused, as typing there is.
    /// </summary>
    /// <param name="text">The sanitized text to paste.</param>
    private void PasteInView(string text)
    {
        EndRun();
        if (text.Length == 0 || RefusedOnFoldedHead())
        {
            return;
        }

        ViewEditResult.Pending? pending = _view.Pending;
        _view.SetPending(null);
        RenderDocument document = _view.Document;
        ViewEditResult result = _view.SelectionRange is { } selection
            ? ViewEdit.Paste(document, selection.Start, selection.End, text, pending)
            : ViewEdit.Paste(document, _view.CursorOffset, _view.CursorOffset, text, pending);
        if (result is ViewEditResult.Refusal { Message.Length: 0 })
        {
            return;
        }

        ApplyViewEdit(result, RunKind.Structure, shared: null, typedSpace: false);
        EndRun();
    }

    /// <summary>
    /// Cuts the selection: it is copied as Markdown (with OSC 52, and to the application clipboard when there is one), then deleted as one undo step.
    /// With no selection it shows <see cref="NothingSelected"/>. When the deletion is refused, the refusal is shown and nothing is copied; when the
    /// Markdown is too large to copy, nothing is cut.
    /// </summary>
    private void CutSelection()
    {
        EndRun();
        if (_view.SelectionRange is not { } selection)
        {
            ShowMessage(NothingSelected, error: true);
            return;
        }

        if (RefusedOnFoldedHead())
        {
            return;
        }

        ViewEditResult result = ViewEdit.ReplaceSelection(_view.Document, selection.Start, selection.End, string.Empty);
        if (result is not ViewEditResult.Replacement edit)
        {
            if (result is ViewEditResult.Refusal { Message.Length: > 0 } refusal)
            {
                ShowMessage(refusal.Message, error: true);
            }

            return;
        }

        string markdown = TextExtraction.Markdown(_view.Document, selection.Start, selection.End);
        if (!Osc52.Fits(markdown))
        {
            ShowMessage(TooLargeToCopy, error: true);
            return;
        }

        WriteRaw(Osc52.Build(markdown));
        WriteApplicationClipboard(markdown);
        ApplyViewEdit(edit, RunKind.Structure, shared: null, typedSpace: false);
        ShowMessage(CutDone, error: false);
    }

    /// <summary>
    /// Reads the application clipboard's text. Returns false, with an empty text, when the application has no clipboard or it cannot be read.
    /// </summary>
    /// <param name="text">The text read, or empty.</param>
    private bool TryReadClipboard(out string text)
    {
        if (App?.Clipboard is { } clipboard)
        {
            return clipboard.TryGetClipboardData(out text);
        }

        text = string.Empty;
        return false;
    }

    /// <summary>Whether the application clipboard holds text, for the right-click menu's Paste entry.</summary>
    private bool ClipboardHasText() => TryReadClipboard(out string text) && text.Length > 0;

    /// <summary>
    /// Writes text to the application clipboard, when there is one, so that a paste in this session reads it back. Where the application has no
    /// clipboard this does nothing.
    /// </summary>
    /// <param name="text">The text to write.</param>
    private void WriteApplicationClipboard(string text) => App?.Clipboard?.TrySetClipboardData(text);
}
