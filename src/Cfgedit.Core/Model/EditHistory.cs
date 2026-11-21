using Cfgedit.Core.Text;

namespace Cfgedit.Core.Model;

/// <summary>
/// The current document of an editing session, with its undo and redo stacks. A step holds the text before and after an
/// edit, and the focus on either side. Undo and redo reparse the stored text, and a new edit clears the redo stack.
/// </summary>
/// <remarks>
/// The focus before an edit is the path the operation acted on in the old document: the node itself, or for an insert the
/// parent, and for an attribute operation the element. A no-op edit records nothing.
/// </remarks>
public sealed class EditHistory
{
    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];
    private byte[] _saved;

    /// <summary>Starts a history at a document that is saved as it is.</summary>
    /// <param name="initial">The document as loaded.</param>
    public EditHistory(ConfigDocument initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        Current = initial;
        _saved = initial.Source.Encode();
    }

    /// <summary>The current document.</summary>
    public ConfigDocument Current { get; private set; }

    /// <summary>True when an edit can be undone.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>True when an undone edit can be redone.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// True when the current text differs from the text at the last <see cref="MarkSaved"/> (or the loaded text, or the reload).
    /// It compares the encoded bytes, so undoing back to the saved text is clean.
    /// </summary>
    public bool IsDirty => !Current.Source.Encode().AsSpan().SequenceEqual(_saved);

    /// <summary>Applies an edit to the current document and records it as one undo step.</summary>
    /// <exception cref="CfgeditException">The edit is invalid or cannot be applied. The history is unchanged.</exception>
    public EditResult Apply(EditOp op)
    {
        EditResult result = Current.Apply(op);
        if (ReferenceEquals(result.Document, Current))
        {
            return result;
        }

        Push(Current, result.Document, FocusBefore(op), result.Focus);
        return result;
    }

    /// <summary>Undoes the last edit: the text is reparsed from the text before it.</summary>
    /// <returns>The focus before the edit, or null when there is nothing to undo.</returns>
    public NodePath? Undo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        Step step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Current = ConfigDocument.Parse(step.Before, Current.Format);
        _redo.Add(step);
        return step.FocusBefore;
    }

    /// <summary>Redoes the last undone edit: the text is reparsed from the text after it.</summary>
    /// <returns>The focus after the edit, or null when there is nothing to redo.</returns>
    public NodePath? Redo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        Step step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Current = ConfigDocument.Parse(step.After, Current.Format);
        _undo.Add(step);
        return step.FocusAfter;
    }

    /// <summary>Replaces the current document with a reload from disk. Both stacks are cleared and the reload is saved.</summary>
    /// <param name="document">The reloaded document.</param>
    public void Reset(ConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Current = document;
        _undo.Clear();
        _redo.Clear();
        _saved = document.Source.Encode();
    }

    /// <summary>Marks the current text as saved, so that <see cref="IsDirty"/> is false until the text changes again.</summary>
    public void MarkSaved()
    {
        _saved = Current.Source.Encode();
    }

    /// <summary>
    /// Records an edit made outside the operations, such as a text-mode edit, as one undo step. Nothing is recorded when the
    /// text is unchanged.
    /// </summary>
    /// <param name="after">The document after the edit.</param>
    /// <param name="focusBefore">The focus before the edit.</param>
    /// <param name="focusAfter">The focus after the edit.</param>
    public void RecordExternal(ConfigDocument after, NodePath? focusBefore, NodePath? focusAfter)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (Current.Source.Encode().AsSpan().SequenceEqual(after.Source.Encode()))
        {
            return;
        }

        Push(Current, after, focusBefore, focusAfter);
    }

    private void Push(ConfigDocument before, ConfigDocument after, NodePath? focusBefore, NodePath? focusAfter)
    {
        _undo.Add(new Step(before.Source, after.Source, focusBefore, focusAfter));
        _redo.Clear();
        Current = after;
    }

    private static NodePath? FocusBefore(EditOp op)
    {
        return op switch
        {
            SetPart set => set.Node,
            InsertNode insert => insert.Parent,
            InsertAttribute insertAttribute => insertAttribute.Element,
            DeleteNode delete => delete.Node,
            DeleteAttribute deleteAttribute => deleteAttribute.Element,
            MoveNode move => move.Node,
            MoveAttribute moveAttribute => moveAttribute.Element,
            ChangeJsonType change => change.Node,
            DuplicateNode duplicate => duplicate.Node,
            _ => null,
        };
    }

    private sealed record Step(SourceText Before, SourceText After, NodePath? FocusBefore, NodePath? FocusAfter);
}
