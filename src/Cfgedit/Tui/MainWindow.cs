using Cfgedit.Core.Model;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Cfgedit.Tui;

/// <summary>What the picker shown with Ctrl+O did: a file chosen, the picker left with Esc, or the application quit with Ctrl+Q.</summary>
internal enum PickOutcome
{
    /// <summary>Esc: the picker was left and the document stays.</summary>
    Back,

    /// <summary>A file was opened in the picker.</summary>
    Chosen,

    /// <summary>Ctrl+Q in the picker: the application quits.</summary>
    Quit,
}

/// <summary>
/// The borderless main window: the tree in a frame whose title is the file name, a star when it has unsaved changes, and its format;
/// the detail line of the focused part, or a message in its place; and the key hints on the last row.
/// </summary>
/// <remarks>
/// <para>
/// <b>Editing.</b> Enter or F2 edits the focused part: a single-line field over it, or the multi-line editor when the text needs it. While
/// the field is open, the field takes the keys (see <see cref="HandleFieldKey"/>). Ctrl+Q, Ctrl+O, Ctrl+S, Ctrl+Z, Ctrl+Y and Ctrl+R
/// commit the field first, and when the commit fails they do nothing.
/// </para>
/// <para>
/// <b>Quit and switch.</b> Ctrl+Q stops the window. Ctrl+O shows the picker over the document, and a file chosen there replaces the
/// document when the window stops (see <see cref="SwitchTo"/>). Both ask about unsaved changes first, through the controller's flows.
/// </para>
/// </remarks>
internal sealed class MainWindow : Window
{
    /// <summary>How long a message stays on the detail line when no key clears it.</summary>
    internal static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(4);

    private readonly Func<(PickOutcome Outcome, OpenedFile? File)> _pickFile;
    private readonly Func<TimeSpan, Action, Action> _schedule;
    private readonly OpenedFile _opened;
    private readonly EditController _controller;
    private readonly FrameView _frame;
    private readonly TreeView _tree;
    private readonly DetailLine _detail;
    private readonly HintsLine _hints;
    private Action? _cancelExpiry;
    private string? _shownHints;

    /// <summary>
    /// Creates the window over a file, with the tree's cursor at its start.
    /// </summary>
    /// <param name="file">The file to show.</param>
    /// <param name="pickFile">Shows the picker for Ctrl+O, and reports what it did.</param>
    /// <param name="schedule">
    /// Runs an action once after a delay on the UI thread, and returns a function that cancels it. Null uses the application's timers.
    /// Tests pass a fake, so that a message's expiry is driven without waiting.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public MainWindow(OpenedFile file, Func<(PickOutcome Outcome, OpenedFile? File)> pickFile, Func<TimeSpan, Action, Action>? schedule = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(pickFile);

        _opened = file;
        _pickFile = pickFile;
        _schedule = schedule ?? ScheduleOnApp;
        _controller = new EditController(file.File);

        // No outer frame: the tree's frame, the detail line and the hints fill the window.
        BorderStyle = LineStyle.None;

        _tree = new TreeView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _frame = new FrameView
        {
            BorderStyle = LineStyle.Rounded,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
        };
        _frame.Add(_tree);

        _detail = new DetailLine { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill(), Height = 1 };
        _hints = new HintsLine { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
        Add(_frame, _detail, _hints);

        _tree.CursorChanged += ShowDetail;
        _tree.KeyPressed += ClearMessage;
        _tree.SetDocument(file.Document, null);
        UpdateTitle();
        ShowHints();
        _tree.SetFocus();
    }

    /// <summary>The tree, for the tests.</summary>
    internal TreeView Tree => _tree;

    /// <summary>The detail line, for the tests.</summary>
    internal DetailLine Detail => _detail;

    /// <summary>The key hints, for the tests.</summary>
    internal HintsLine Hints => _hints;

    /// <summary>The file chosen with Ctrl+O before the window stopped, or null. The host opens it next.</summary>
    public OpenedFile? SwitchTo { get; private set; }

    /// <summary>True once the window has stopped because of a quit or a switch that the flows allowed.</summary>
    internal bool Stopped { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// Esc is swallowed: Terminal.Gui's default Esc binding would close the window, and the window has nothing to go back to.
    /// </remarks>
    protected override bool OnKeyDown(Key key)
    {
        ClearMessage();
        if (_tree.IsEditing && IsLeavingKey(key) && !CommitInline())
        {
            return true;
        }

        if (key == Key.Q.WithCtrl)
        {
            Quit();
            return true;
        }

        if (key == Key.O.WithCtrl)
        {
            OpenPicker();
            return true;
        }

        if (key == Key.S.WithCtrl)
        {
            Save();
            return true;
        }

        if (key == Key.Z.WithCtrl || IsPlain(key, 'u'))
        {
            Undo();
            return true;
        }

        if (key == Key.Y.WithCtrl || key == Key.R.WithCtrl)
        {
            Redo();
            return true;
        }

        if (key == Key.Enter || key == Key.F2)
        {
            BeginEdit(multiline: false);
            return true;
        }

        if (key == Key.Enter.WithCtrl)
        {
            BeginEdit(multiline: true);
            return true;
        }

        if (key == Key.Tab)
        {
            MoveEditable(1);
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            MoveEditable(-1);
            return true;
        }

        if (key == Key.Esc)
        {
            return true;
        }

        return base.OnKeyDown(key);
    }

    private static bool IsLeavingKey(Key key) =>
        key == Key.Q.WithCtrl || key == Key.O.WithCtrl || key == Key.S.WithCtrl || key == Key.Z.WithCtrl || key == Key.Y.WithCtrl
        || key == Key.R.WithCtrl;

    private static bool IsPlain(Key key, char ch) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value == ch;

    private static bool IsEditableTarget(DocNode node, DocPart part) => EditRules.KindOf(node, part) != EditorKind.None;

    private IApplication Ui => App ?? throw new InvalidOperationException("The window is not running in an application.");

    private string FileName => Path.GetFileName(_opened.Path);

    private void ShowDetail() => _detail.Show(_tree.Document, _tree.CursorNode, _tree.CursorDocPart);

    // ----- Editing -----

    /// <summary>
    /// Enter, F2 or Ctrl+Enter on the cursor's part. A boolean flips. A null says to use the type key. Otherwise the part opens the
    /// inline field, or the multi-line editor when the value needs it, or Ctrl+Enter, which always uses the multi-line editor when the
    /// value fits it. A part with no editor shows why.
    /// </summary>
    private void BeginEdit(bool multiline)
    {
        DocNode? node = _tree.CursorNode;
        DocPart? part = _tree.CursorDocPart;
        if (node is null || part is null)
        {
            return;
        }

        switch (EditRules.KindOf(node, part))
        {
            case EditorKind.Flip:
                ApplyAndShow(
                    new ChangeJsonType(node.Path, node.JsonKind == JsonValueKind.True ? JsonValueKind.False : JsonValueKind.True, null),
                    node.Path);
                break;
            case EditorKind.Type:
                ShowMessage(EditRules.UseTypeKey, error: false);
                break;
            case EditorKind.None:
                ShowMessage(EditRules.RefusalOf(part), error: false);
                break;
            case EditorKind.Multiline:
                OpenMultiline(node, part, part.Value);
                break;
            case EditorKind.Inline when multiline && EditRules.CanEditMultiline(part.Value):
                OpenMultiline(node, part, part.Value);
                break;
            case EditorKind.Inline:
                OpenInline(part.Value);
                break;
        }
    }

    private void OpenInline(string text)
    {
        _tree.OpenEditor(text, HandleFieldKey);
        ShowHints();
    }

    /// <summary>The keys of the open field. Returns false for a key the field edits as text.</summary>
    private bool HandleFieldKey(Key key)
    {
        ClearMessage();
        if (key == Key.Enter)
        {
            CommitInline();
            return true;
        }

        if (key == Key.Esc)
        {
            CloseInline();
            return true;
        }

        if (key == Key.Tab)
        {
            MoveEditable(1);
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            MoveEditable(-1);
            return true;
        }

        if (key == Key.F4 || key == Key.Enter.WithCtrl)
        {
            EditMultilineFromField();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Commits the open field to the cursor's part. Returns true when the field closed. An error keeps the field open with the text as
    /// typed, and shows the error on the detail line.
    /// </summary>
    private bool CommitInline()
    {
        DocNode node = _tree.CursorNode!;
        DocPart part = _tree.CursorDocPart!;
        string? error = SetValue(node.Path, part, _tree.EditorText!, out NodePath? focus);
        if (error is not null)
        {
            ShowMessage(error, error: true);
            return false;
        }

        CloseInline();
        if (focus is not null)
        {
            Refresh(focus);
        }

        return true;
    }

    private void CloseInline()
    {
        _tree.CloseEditor();
        ShowHints();
    }

    /// <summary>F4 or Ctrl+Enter in the field: the text as typed goes to the multi-line editor. A cancel brings the field back with that text.</summary>
    private void EditMultilineFromField()
    {
        DocNode node = _tree.CursorNode!;
        DocPart part = _tree.CursorDocPart!;
        string text = _tree.EditorText!;
        CloseInline();
        if (!OpenMultiline(node, part, text))
        {
            OpenInline(text);
        }
    }

    /// <summary>
    /// Shows the multi-line editor for a part. Returns true when it committed (or found nothing to change), and false when it was cancelled.
    /// </summary>
    private bool OpenMultiline(DocNode node, DocPart part, string text)
    {
        string title = $"Edit {PathText.KindName(node, part)} {PathText.Format(_controller.Document, node, part)}";
        return MultilineEditor.Ask(Ui, title, text, value =>
        {
            string? error = SetValue(node.Path, part, value, out NodePath? focus);
            if (error is null && focus is not null)
            {
                Refresh(focus);
            }

            return error;
        });
    }

    /// <summary>
    /// Moves the cursor to the next (or previous) part that has an editor, in visible-row order. When a field is open it is committed
    /// first, and an error keeps the cursor where it is. The next part opens its inline field or multi-line editor, when it has one.
    /// </summary>
    private void MoveEditable(int direction)
    {
        bool editing = _tree.IsEditing;
        if (editing && !CommitInline())
        {
            return;
        }

        TreeView.PartPosition? next = _tree.FindPart(direction, IsEditableTarget);
        if (next is null || !_tree.MoveCursorTo(next.Path, next.IsEnd, next.Part))
        {
            return;
        }

        if (editing && _tree.CursorNode is { } node && _tree.CursorDocPart is { } part
            && EditRules.KindOf(node, part) is EditorKind.Inline or EditorKind.Multiline)
        {
            BeginEdit(multiline: false);
        }
    }

    /// <summary>
    /// Sets a part to a text, as one edit. Returns the error to show, or null. <paramref name="focus"/> is the node to focus after the edit,
    /// or null when the text was unchanged and no edit was made.
    /// </summary>
    private string? SetValue(NodePath node, DocPart part, string text, out NodePath? focus)
    {
        focus = null;
        if (text == part.Value)
        {
            return null;
        }

        EditResult? result = _controller.Apply(new SetPart(node, part.Kind, part.AttributeIndex, text), out string? error);
        if (result is null)
        {
            return error ?? "the value cannot be changed";
        }

        focus = result.Focus ?? node;
        return null;
    }

    private void ApplyAndShow(EditOp op, NodePath fallback)
    {
        EditResult? result = _controller.Apply(op, out string? error);
        if (result is null)
        {
            ShowMessage(error ?? "the edit cannot be applied", error: true);
            return;
        }

        Refresh(result.Focus ?? fallback);
    }

    // ----- Undo, redo, save -----

    private void Undo()
    {
        bool hadStep = _controller.CanUndo;
        NodePath? focus = _controller.Undo();
        if (!hadStep)
        {
            ShowMessage(_controller.Message ?? "Nothing to undo", error: false);
            return;
        }

        Refresh(focus ?? _tree.CursorNode?.Path);
    }

    private void Redo()
    {
        bool hadStep = _controller.CanRedo;
        NodePath? focus = _controller.Redo();
        if (!hadStep)
        {
            ShowMessage(_controller.Message ?? "Nothing to redo", error: false);
            return;
        }

        Refresh(focus ?? _tree.CursorNode?.Path);
    }

    private void Save()
    {
        SaveResult result = _controller.SaveFlow(AskExternalChange, ConfirmReload);
        switch (result.Outcome)
        {
            case SaveOutcome.Saved:
                Refresh(null);
                ShowMessage("Saved", error: false);
                break;
            case SaveOutcome.Reloaded:
                Refresh(null);
                ShowMessage("Reloaded", error: false);
                break;
            case SaveOutcome.Failed:
                UpdateTitle();
                ShowMessage(result.Error ?? "the file cannot be saved", error: true);
                break;
        }
    }

    // ----- Quit and switch -----

    private void Quit()
    {
        if (CloseOrKeep("Quit") == CloseOutcome.Closed)
        {
            Stop();
        }
    }

    private void OpenPicker()
    {
        (PickOutcome outcome, OpenedFile? file) = _pickFile();
        switch (outcome)
        {
            case PickOutcome.Chosen when file is not null:
                if (CloseOrKeep("Open") == CloseOutcome.Closed)
                {
                    SwitchTo = file;
                    Stop();
                }

                break;
            case PickOutcome.Quit:
                Quit();
                break;
        }
    }

    /// <summary>
    /// The unsaved-changes flow of a quit or a switch. Closed means the file may be left; otherwise the window stays, and a reload or a
    /// failed save is shown.
    /// </summary>
    private CloseOutcome CloseOrKeep(string action)
    {
        CloseResult result = _controller.CloseFlow(() => AskUnsaved(action), AskExternalChange, ConfirmReload);
        switch (result.Outcome)
        {
            case CloseOutcome.Reloaded:
                Refresh(null);
                ShowMessage("Reloaded", error: false);
                break;
            case CloseOutcome.Failed:
                UpdateTitle();
                ShowMessage(result.Error ?? "the file cannot be saved", error: true);
                break;
        }

        return result.Outcome;
    }

    private UnsavedChoice AskUnsaved(string action)
    {
        int? choice = Confirm.Choose(Ui, action, $"Save changes to {FileName}?", "Save", "Discard", "Cancel");
        return choice switch
        {
            0 => UnsavedChoice.Save,
            1 => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };
    }

    private ExternalChangeChoice AskExternalChange()
    {
        int? choice = Confirm.Choose(
            Ui,
            "Save",
            $"{FileName} changed on disk since it was opened. Overwrite it, reload it, or cancel?",
            "Overwrite",
            "Reload",
            "Cancel");
        return choice switch
        {
            0 => ExternalChangeChoice.Overwrite,
            1 => ExternalChangeChoice.Reload,
            _ => ExternalChangeChoice.Cancel,
        };
    }

    private bool ConfirmReload() =>
        Confirm.Choose(Ui, "Reload", "Reloading discards the undo history. Reload anyway?", "Reload", "Cancel") == 0;

    private void Stop()
    {
        Stopped = true;
        App?.RequestStop(this);
    }

    // ----- Document state on screen -----

    /// <summary>Shows the document after a change, and updates the title.</summary>
    private void Refresh(NodePath? focus)
    {
        _tree.SetDocument(_controller.Document, focus);
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string dirty = _controller.IsDirty ? "*" : string.Empty;
        _frame.Title = $"{FileName}{dirty} [{_controller.Document.Format.Name}]";
    }

    // ----- Messages -----

    /// <summary>Shows a message on the detail line, and starts its expiry. A newer message replaces it and restarts the expiry.</summary>
    private void ShowMessage(string text, bool error)
    {
        _cancelExpiry?.Invoke();
        _detail.ShowMessage(text, error);
        _cancelExpiry = _schedule(MessageDuration, ExpireMessage);
    }

    /// <summary>Clears the message on the next key, or at its expiry. The detail line comes back, and the hints are shown again.</summary>
    private void ClearMessage()
    {
        if (!_detail.HasMessage)
        {
            return;
        }

        _cancelExpiry?.Invoke();
        _cancelExpiry = null;
        _detail.ClearMessage();
        InvalidateHints();
    }

    private void ExpireMessage()
    {
        _cancelExpiry = null;
        _detail.ClearMessage();
        InvalidateHints();
    }

    /// <summary>The hints are cached by their text. A message's end clears the cache, so that the hints are drawn again (the 06-06 lesson).</summary>
    private void InvalidateHints()
    {
        _shownHints = null;
        ShowHints();
    }

    private void ShowHints()
    {
        Hint hint = _tree.IsEditing ? CfgeditHints.Editing : CfgeditHints.Tree;
        if (hint.Text == _shownHints)
        {
            return;
        }

        _shownHints = hint.Text;
        _hints.Show(hint, CfgeditHints.Quit);
    }

    private Action ScheduleOnApp(TimeSpan delay, Action expire)
    {
        object? timer = App?.AddTimeout(delay, () =>
        {
            expire();
            return false;
        });
        return () =>
        {
            if (timer is not null)
            {
                App?.RemoveTimeout(timer);
            }
        };
    }
}
