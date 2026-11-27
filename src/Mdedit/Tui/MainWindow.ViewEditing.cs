using Mdedit.Core.Editing;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Input;

namespace Mdedit.Tui;

/// <summary>
/// Editing in the view, in view mode: typed characters, Backspace and Delete are the edits of <see cref="ViewEdit"/>. Each replacement is applied
/// to the buffer as one change and the view is rendered from it, with the caret where the edit put it. A pending insertion (see
/// <see cref="RenderedView.Pending"/>) is handed back to the next typed character. A refusal shows its message and changes nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Undo runs.</b> A run is a series of one kind of edit (typing, Backspace or Delete) in which each edit starts where the last one left the caret.
/// Each run is one undo step, with one group descriptor. A typed character that is not a space, after a typed space, starts a new step, so that a
/// word and the space after it are one step. A selection being replaced starts a new step. Any other key, a mouse caret move, a table edit, an
/// undo or a mode switch ends the run. The buffer's undo stack continues its last group whenever that group was closed (it does not compare
/// descriptors), so a new run starts its group with <c>StartUndoGroup</c> and only a run that continues uses <c>StartContinuedUndoGroup</c>.
/// </para>
/// <para>
/// <b>Pending insertions.</b> A typed space that would not show is held by the view, with no step of its own; the next typed character writes it
/// in front of its own text, so the held space joins that character's step.
/// </para>
/// </remarks>
internal sealed partial class MainWindow
{
    /// <summary>The message of an edit on the head of a folded unit, which the view does not edit while it is folded.</summary>
    internal const string FoldedHeadRefusal = "Unfold to edit here; Ctrl+T edits the source";

    // The kinds of edit that an undo run can hold.
    private enum RunKind
    {
        Typing,
        Backspace,
        Delete,
    }

    // An undo run in progress: its kind, the group descriptor of its undo step (null while it has none, after a pending insertion), the caret
    // after its last edit, and whether its last typed character was a space.
    private sealed record ViewRun(RunKind Kind, object? Group, int Caret, bool AfterSpace);

    // The run in progress, or null when there is none.
    private ViewRun? _run;

    /// <summary>
    /// The window's text keys in view mode: a typed character, Backspace and Delete. Returns false for any other key, so that the links and
    /// the search keep theirs.
    /// </summary>
    /// <param name="key">The key.</param>
    private bool HandleViewEditKey(Key key)
    {
        if (key == Key.Backspace)
        {
            DeleteInView(backward: true);
            return true;
        }

        if (key == Key.Delete)
        {
            DeleteInView(backward: false);
            return true;
        }

        if (!RenderedView.IsTyping(key))
        {
            return false;
        }

        TypeInView(key.AsRune.ToString());
        return true;
    }

    /// <summary>
    /// Ends the window's handling of a key that is not a text edit, as a command does: the undo run ends, and the pending insertion is dropped.
    /// Ctrl+Z drops a pending insertion only, with no undo, so it returns true when that is what it did (and the key is then taken).
    /// </summary>
    /// <param name="key">The key.</param>
    private bool ReleaseTypingFor(Key key)
    {
        if (RenderedView.IsEditKey(key))
        {
            return false;
        }

        bool droppedPending = key == Key.Z.WithCtrl && _view.Pending is not null;
        EndRun();
        _view.SetPending(null);
        return droppedPending;
    }

    /// <summary>Ends the undo run in progress, so that the next edit starts a step of its own.</summary>
    private void EndRun() => _run = null;

    /// <summary>Types a character at the caret, or over the selection, which it replaces.</summary>
    /// <param name="typed">The typed text: one character.</param>
    private void TypeInView(string typed)
    {
        if (RefusedOnFoldedHead())
        {
            return;
        }

        if (_view.SelectionRange is { } selection)
        {
            ViewEditResult replacing = ViewEdit.ReplaceSelection(_view.Document, selection.Start, selection.End, typed);
            ApplyViewEdit(replacing, RunKind.Typing, shared: null, typedSpace: typed == " ");
            return;
        }

        object? shared = SharedGroup(RunKind.Typing, breaksWord: typed != " ");
        ViewEditResult result = ViewEdit.Insert(_view.Document, _view.CursorOffset, typed, _view.Pending);
        ApplyViewEdit(result, RunKind.Typing, shared, typedSpace: typed == " ");
    }

    /// <summary>
    /// Deletes the mapped character before the caret (Backspace) or after it (Delete), or the selection. The pending insertion is dropped first.
    /// </summary>
    /// <param name="backward">True for Backspace.</param>
    private void DeleteInView(bool backward)
    {
        RunKind kind = backward ? RunKind.Backspace : RunKind.Delete;
        _view.SetPending(null);
        if (RefusedOnFoldedHead())
        {
            return;
        }

        if (_view.SelectionRange is { } selection)
        {
            ViewEditResult deleting = ViewEdit.ReplaceSelection(_view.Document, selection.Start, selection.End, string.Empty);
            ApplyViewEdit(deleting, kind, shared: null, typedSpace: false);
            return;
        }

        object? shared = SharedGroup(kind, breaksWord: false);
        int caret = _view.CursorOffset;
        ViewEditResult result = backward
            ? ViewEdit.DeleteBackward(_view.Document, caret)
            : ViewEdit.DeleteForward(_view.Document, caret);
        ApplyViewEdit(result, kind, shared, typedSpace: false);
    }

    /// <summary>
    /// Refuses an edit on the head of a folded unit, whose text is not edited in the view while it is folded: the message is shown, the run ends,
    /// and nothing changes. Returns true when it refused.
    /// </summary>
    private bool RefusedOnFoldedHead()
    {
        if (!_view.CursorOnFoldedHead)
        {
            return false;
        }

        EndRun();
        ShowMessage(FoldedHeadRefusal, error: true);
        return true;
    }

    /// <summary>
    /// The group descriptor of the run in progress, when the next edit continues it: the same kind, the caret where the run left it, the run has an
    /// undo step, and the edit does not start a new word (a typed character that is not a space, after a typed space). Null when the edit starts
    /// a new step.
    /// </summary>
    /// <param name="kind">The kind of the edit.</param>
    /// <param name="breaksWord">Whether the edit is a typed character other than a space.</param>
    private object? SharedGroup(RunKind kind, bool breaksWord) =>
        _run is { Group: { } group } run && run.Kind == kind && run.Caret == _view.CursorOffset && !(breaksWord && run.AfterSpace)
            ? group
            : null;

    /// <summary>
    /// Applies the result of a view edit: a refusal shows its message and ends the run; a pending insertion is held by the view; a replacement is
    /// written as one undo step (continuing <paramref name="shared"/>'s when it is not null).
    /// </summary>
    /// <param name="result">The result of a <see cref="ViewEdit"/> operation.</param>
    /// <param name="kind">The kind of the edit.</param>
    /// <param name="shared">The group descriptor of the run that the edit continues, or null for a new step.</param>
    /// <param name="typedSpace">Whether the typed text is a space.</param>
    private void ApplyViewEdit(ViewEditResult result, RunKind kind, object? shared, bool typedSpace)
    {
        switch (result)
        {
            case ViewEditResult.Refusal refusal:
                EndRun();
                ShowMessage(refusal.Message, error: true);
                break;
            case ViewEditResult.Pending pending:
                _view.SetPending(pending);
                _run = new ViewRun(kind, Group: null, _view.CursorOffset, typedSpace);
                break;
            case ViewEditResult.Replacement replacement:
                WriteReplacement(replacement, kind, shared, typedSpace);
                break;
        }
    }

    /// <summary>
    /// Writes a replacement of the buffer as one undo step, renders the view from the new text, and puts the caret where the edit left it. The
    /// step continues <paramref name="shared"/>'s group when it is not null.
    /// </summary>
    /// <param name="edit">The replacement.</param>
    /// <param name="kind">The kind of the edit.</param>
    /// <param name="shared">The group descriptor of the run that the edit continues, or null for a new step.</param>
    /// <param name="typedSpace">Whether the typed text is a space.</param>
    private void WriteReplacement(ViewEditResult.Replacement edit, RunKind kind, object? shared, bool typedSpace)
    {
        object group = shared ?? new object();
        UndoStack stack = _editor.Buffer.UndoStack;
        if (shared is null)
        {
            stack.StartUndoGroup(group);
        }
        else
        {
            stack.StartContinuedUndoGroup(group);
        }

        _editor.Buffer.Replace(edit.Start, edit.Length, edit.Text);
        stack.EndUndoGroup();
        RenderBuffer();
        RefreshDirty();
        _view.PlaceCaret(edit.Caret, null);
        _run = new ViewRun(kind, group, _view.CursorOffset, typedSpace);
        ShowStatus();
    }
}
