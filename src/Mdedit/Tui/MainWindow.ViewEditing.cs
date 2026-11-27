using Mdedit.Core.Editing;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Input;

namespace Mdedit.Tui;

/// <summary>
/// Editing in the view, in view mode: typed characters, Enter, Backspace and Delete are the edits of <see cref="ViewEdit"/>. Each replacement is
/// applied to the buffer as one change and the view is rendered from it, with the caret where the edit put it. A pending insertion (see
/// <see cref="RenderedView.Pending"/>) is handed back to the next typed character or Enter. A refusal shows its message and changes nothing; a
/// refusal with an empty message changes nothing and shows nothing.
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
/// <b>Structure.</b> Enter is a step of its own, and so is a Backspace or Delete at the edge of a display line (see <see cref="DeleteInView"/>), since
/// those edits join, split or unwrap blocks. Such an edit never continues a run, and no run continues from it.
/// </para>
/// <para>
/// <b>Pending insertions.</b> A typed space that would not show is held by the view, with no step of its own; the next typed character writes it
/// in front of its own text, so the held space joins that character's step. A block pending (from Enter) is written by the next typed character.
/// </para>
/// <para>
/// <b>The empty document.</b> A document with no block has a caret at its start, which every typed character writes into: when the view holds no
/// pending, the window gives <see cref="ViewEdit"/> an empty inline pending there (see <see cref="TypingPending"/>). Enter, Backspace and Delete do
/// nothing in it.
/// </para>
/// </remarks>
internal sealed partial class MainWindow
{
    /// <summary>The message of an edit on the head of a folded unit, which the view does not edit while it is folded.</summary>
    internal const string FoldedHeadRefusal = "Unfold to edit here; Ctrl+T edits the source";

    // The kinds of edit that an undo run can hold. Enter is not a run: it is a step of its own, so the kind only marks it.
    private enum RunKind
    {
        Typing,
        Backspace,
        Delete,
        Structure,
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

        if (key == Key.Enter)
        {
            EnterInView();
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

    /// <summary>
    /// The pending insertion that a typed character is written with: the view's pending, or in a document with no block, the empty inline pending at
    /// its start (the document's caret, which has no caret stop of its own).
    /// </summary>
    private ViewEditResult.Pending? TypingPending() =>
        _view.Pending ?? (_view.Document.Blocks.Length == 0 ? new ViewEditResult.Pending(0, string.Empty) : null);

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
        ViewEditResult result = ViewEdit.Insert(_view.Document, _view.CursorOffset, typed, TypingPending());
        ApplyViewEdit(result, RunKind.Typing, shared, typedSpace: typed == " ");
    }

    /// <summary>
    /// Enter: splits the block at the caret, holds a new block below or above it, ends a list or a quote, or replaces the selection first (see
    /// <see cref="ViewEdit.Enter"/> and <see cref="ViewEdit.EnterOver"/>). Each Enter is an undo step of its own, and it ends the typing run. Nothing
    /// happens in a document with no block.
    /// </summary>
    private void EnterInView()
    {
        EndRun();
        if (RefusedOnFoldedHead() || _view.Document.Blocks.Length == 0)
        {
            return;
        }

        ViewEditResult result = _view.SelectionRange is { } selection
            ? ViewEdit.EnterOver(_view.Document, selection.Start, selection.End)
            : ViewEdit.Enter(_view.Document, _view.CursorOffset, _view.Pending);
        _view.SetPending(null);
        ApplyViewEdit(result, RunKind.Structure, shared: null, typedSpace: false);
        EndRun();
    }

    /// <summary>
    /// Deletes the mapped character before the caret (Backspace) or after it (Delete), or the selection. An edit at the edge of a display line
    /// (Backspace at the first character of a line, Delete at its end) may join or unwrap blocks, so it is a step of its own; any other one
    /// continues the run. A refusal with an empty message does nothing: the pending insertion stays and nothing is shown. Nothing happens in a
    /// document with no block. With a pending insertion (a held space, or the new block of an Enter) the key takes that back and does nothing else,
    /// since what it would delete is not written yet.
    /// </summary>
    /// <param name="backward">True for Backspace.</param>
    private void DeleteInView(bool backward)
    {
        RunKind kind = backward ? RunKind.Backspace : RunKind.Delete;
        if (_view.Pending is not null)
        {
            _view.SetPending(null);
            EndRun();
            return;
        }

        if (RefusedOnFoldedHead() || _view.Document.Blocks.Length == 0)
        {
            return;
        }

        ViewEditResult result;
        bool edge = _view.CursorAtLineEdge(start: backward);
        if (_view.SelectionRange is { } selection)
        {
            result = ViewEdit.ReplaceSelection(_view.Document, selection.Start, selection.End, string.Empty);
        }
        else
        {
            result = backward
                ? ViewEdit.DeleteBackward(_view.Document, _view.CursorOffset)
                : ViewEdit.DeleteForward(_view.Document, _view.CursorOffset);
        }

        if (result is ViewEditResult.Refusal { Message.Length: 0 })
        {
            return;
        }

        _view.SetPending(null);
        object? shared = (edge || _view.SelectionRange is not null) ? null : SharedGroup(kind, breaksWord: false);
        ApplyViewEdit(result, kind, shared, typedSpace: false);
        if (edge)
        {
            EndRun();
        }
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

        // A pending block that the edit holds (the block its last character was) is drawn where the caret is, and the next typed character writes it.
        // A run does not go on from it: the next edit starts a step of its own.
        _view.SetPending(edit.Then);
        _run = edit.Then is null ? new ViewRun(kind, group, _view.CursorOffset, typedSpace) : null;
        ShowStatus();
    }
}
