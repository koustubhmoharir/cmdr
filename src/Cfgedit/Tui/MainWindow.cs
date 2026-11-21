using Cfgedit.Core;
using Cfgedit.Core.Json;
using Cfgedit.Core.Model;
using Cfgedit.Core.Text;
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
internal sealed class MainWindow : Window, ISearchHost
{
    /// <summary>How long a message stays on the detail line when no key clears it.</summary>
    internal static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(4);

    /// <summary>The message of a copy, when the value is the logical value or the path.</summary>
    internal const string CopiedValue = "Copied value";

    /// <summary>The message of a copy, when the path is copied.</summary>
    internal const string CopiedPath = "Copied path";

    /// <summary>The message of a copy whose text is over <see cref="Osc52.MaxBytes"/>, which is not copied.</summary>
    internal const string TooLargeToCopy = "Too large to copy";

    private readonly Func<(PickOutcome Outcome, OpenedFile? File)> _pickFile;
    private readonly Func<TimeSpan, Action, Action> _schedule;
    private readonly OpenedFile _opened;
    private readonly EditController _controller;
    private readonly FrameView _frame;
    private readonly TreeView _tree;
    private readonly TextModeView _textView;
    private readonly DetailLine _detail;
    private readonly HintsLine _hints;
    private readonly TreeSearch _search;
    private readonly Action? _onFirstSave;
    private Action? _cancelExpiry;
    private string? _shownHints;

    // True while the text is shown read-only: the view's text does not round-trip to the file's text (mixed line breaks).
    private bool _textReadOnly;

    // True while the window sets the view's text itself, so that the change is not read back as an edit.
    private bool _showingText;

    /// <summary>
    /// Creates the window over a file, with the tree's cursor at its start.
    /// </summary>
    /// <param name="file">The file to show.</param>
    /// <param name="pickFile">Shows the picker for Ctrl+O, and reports what it did.</param>
    /// <param name="schedule">
    /// Runs an action once after a delay on the UI thread, and returns a function that cancels it. Null uses the application's timers.
    /// Tests pass a fake, so that a message's expiry is driven without waiting.
    /// </param>
    /// <param name="onFirstSave">Called after the file's first successful save, when it was new. The host records it in the recent list then.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public MainWindow(
        OpenedFile file,
        Func<(PickOutcome Outcome, OpenedFile? File)> pickFile,
        Func<TimeSpan, Action, Action>? schedule = null,
        Action? onFirstSave = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(pickFile);

        _opened = file;
        _pickFile = pickFile;
        _schedule = schedule ?? ScheduleOnApp;
        _onFirstSave = onFirstSave;
        _controller = new EditController(file.File);

        // No outer frame: the tree's frame, the detail line and the hints fill the window.
        BorderStyle = LineStyle.None;

        _tree = new TreeView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _textView = new TextModeView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Visible = false };
        _frame = new FrameView
        {
            BorderStyle = LineStyle.Rounded,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
        };
        _frame.Add(_tree, _textView);

        _detail = new DetailLine { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill(), Height = 1 };
        _hints = new HintsLine { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
        _search = new TreeSearch(this, _tree, _controller);

        // The search bar sits above the detail line. It is shown with the frame one row shorter (see ShowSearchBar).
        _search.Bar.X = 0;
        _search.Bar.Y = Pos.AnchorEnd(3);
        _search.Bar.Width = Dim.Fill();
        _search.Bar.Height = 1;
        _search.Bar.Visible = false;
        Add(_frame, _search.Bar, _detail, _hints);
        WriteRaw = sequence => App?.Driver?.WriteRaw(sequence);

        _tree.CursorChanged += ShowDetail;
        _tree.KeyPressed += ClearMessage;
        _textView.ContentsChanged += (_, _) =>
        {
            if (_showingText)
            {
                return;
            }

            SyncText();
            UpdateTitle();
        };

        if (_controller.Mode == EditMode.Tree)
        {
            _tree.SetDocument(_controller.Document, null);
            _tree.SetFocus();
            UpdateTitle();
            ShowHints();
            return;
        }

        // A file that does not parse opens in text mode, with the cursor at the error.
        ShowText(file.File.ParseError is { } error ? ErrorOffset(error) : 0);
        if (file.File.ParseError is { } parseError)
        {
            ShowMessage(EditController.Describe(parseError), error: true);
        }
    }

    /// <summary>The tree, for the tests.</summary>
    internal TreeView Tree => _tree;

    /// <summary>The text view of text mode, for the tests.</summary>
    internal TextModeView TextView => _textView;

    /// <summary>The controller, for the tests.</summary>
    internal EditController Controller => _controller;

    /// <summary>The detail line, for the tests.</summary>
    internal DetailLine Detail => _detail;

    /// <summary>The key hints, for the tests.</summary>
    internal HintsLine Hints => _hints;

    /// <summary>
    /// Writes a raw sequence to the terminal: the application's driver by default. A copy writes its OSC 52 sequence here, and a test
    /// replaces it to capture the sequence.
    /// </summary>
    internal Action<string> WriteRaw { get; set; }

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
        if (_controller.Mode == EditMode.Text)
        {
            return HandleTextKey(key);
        }

        if (_tree.IsEditing && IsLeavingKey(key) && !CommitInline())
        {
            return true;
        }

        if (key == Key.T.WithCtrl)
        {
            EnterTextMode();
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

        if (!_tree.IsEditing && HandleStructureKey(key))
        {
            return true;
        }

        if (!_tree.IsEditing && HandleSearchKey(key))
        {
            return true;
        }

        if (key == Key.Esc)
        {
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <summary>
    /// The keys that leave an open inline field: each commits it first, and when the commit fails the key does nothing. Opening the search
    /// (Ctrl+F, F3 and Shift+F3, which reopens the bar) is one of them, since the bar and the editor are never open together.
    /// </summary>
    private static bool IsLeavingKey(Key key) =>
        key == Key.Q.WithCtrl || key == Key.O.WithCtrl || key == Key.S.WithCtrl || key == Key.Z.WithCtrl || key == Key.Y.WithCtrl
        || key == Key.R.WithCtrl || key == Key.T.WithCtrl || key == Key.F.WithCtrl || key == Key.F3 || key == Key.F3.WithShift;

    // ----- Search and copy -----

    /// <summary>
    /// The keys of the tree's search and copy: <c>/</c> and Ctrl+F open the bar, <c>n</c> and F3 go to the next match, <c>N</c> and Shift+F3
    /// to the previous one, <c>y</c> copies the focused value and <c>Y</c> its path. Returns false for a key they do not bind.
    /// </summary>
    private bool HandleSearchKey(Key key)
    {
        if (IsPlain(key, 'y'))
        {
            CopyFocused(valueOnly: true);
        }
        else if (IsPlain(key, 'Y'))
        {
            CopyFocused(valueOnly: false);
        }
        else if (key == Key.F.WithCtrl || IsPlain(key, '/'))
        {
            OpenSearch(focusBar: true);
        }
        else if (key == Key.F3 || IsPlain(key, 'n'))
        {
            _search.Find(next: true);
        }
        else if (key == Key.F3.WithShift || IsPlain(key, 'N'))
        {
            _search.Find(next: false);
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>Opens the search bar. An inline field is committed first; when that fails, the bar does not open.</summary>
    private void OpenSearch(bool focusBar)
    {
        if (_tree.IsEditing && !CommitInline())
        {
            return;
        }

        _search.Open(focusBar);
    }

    /// <summary>
    /// <c>y</c> and <c>Y</c>: writes the focused part's logical value, or its path, to the clipboard with OSC 52. The value is copied as it
    /// is, with its control characters. A text over <see cref="Osc52.MaxBytes"/> is not copied.
    /// </summary>
    private void CopyFocused(bool valueOnly)
    {
        if (!TryCursor(out DocNode node, out DocPart part))
        {
            return;
        }

        string text = valueOnly ? part.Value : PathText.Format(_controller.Document, node, part);
        if (!Osc52.Fits(text))
        {
            ShowMessage(TooLargeToCopy, error: true);
            return;
        }

        WriteRaw(Osc52.Build(text));
        ShowMessage(valueOnly ? CopiedValue : CopiedPath, error: false);
    }

    /// <summary>The search bar's row and the frame's height: the frame gives the bar one row while it is open.</summary>
    void ISearchHost.ShowSearchBar(bool open)
    {
        _frame.Height = Dim.Fill(open ? 3 : 2);
        _search.Bar.Visible = open;
        SetNeedsLayout();
    }

    void ISearchHost.FocusTree() => _tree.SetFocus();

    void ISearchHost.ShowMessage(string text, bool error) => ShowMessage(text, error);

    /// <summary>
    /// The keys in text mode. F5 and Ctrl+T reparse (Ctrl+T also switches back to the tree when the text parses), Ctrl+S saves, Ctrl+Q and
    /// Ctrl+O use the unsaved-changes flow, and Esc does nothing. Every other key is the text view's: typing, its own undo and redo.
    /// </summary>
    private bool HandleTextKey(Key key)
    {
        SyncText();
        if (key == Key.F5 || key == Key.T.WithCtrl)
        {
            ReparseText();
            return true;
        }

        if (key == Key.S.WithCtrl)
        {
            SaveFromText();
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

        if (key == Key.Esc)
        {
            return true;
        }

        return base.OnKeyDown(key);
    }

    // ----- Text mode -----

    /// <summary>Ctrl+T in the tree: the text of the document, with the cursor at the focused part's place in it.</summary>
    private void EnterTextMode()
    {
        int offset = _tree.CursorDocPart?.Start ?? _tree.CursorNode?.Start ?? 0;
        _controller.EnterText(offset);
        ShowText(_controller.CursorOffset);
    }

    /// <summary>
    /// Shows the text in the text view, with the cursor at an offset. The text is read-only when the view's text does not come back
    /// as the file's text, which is the case when the file mixes line breaks.
    /// </summary>
    private void ShowText(int offset)
    {
        _search.Close();
        SourceText text = _controller.Text!;
        _showingText = true;
        try
        {
            _textView.Text = text.Text;
            _textReadOnly = TextModeRules.WithNewline(_textView.Text, _controller.Newline) != text.Text;
            _textView.ReadOnly = _textReadOnly;
            MoveTextCursor(offset);
        }
        finally
        {
            _showingText = false;
        }

        _tree.Visible = false;
        _textView.Visible = true;
        _textView.SetFocus();
        UpdateTitle();
        ShowHints();
        if (_textReadOnly)
        {
            ShowMessage("text mode is read-only: the file mixes line endings, and editing it here would change them", error: false);
        }
    }

    /// <summary>Shows the tree, with the focus on a node (or the document when null), and the document as it is.</summary>
    private void ShowTree(NodePath? focus)
    {
        _textReadOnly = false;
        _textView.Visible = false;
        _tree.Visible = true;
        _tree.SetDocument(_controller.Document, focus);
        _search.OnDocumentChanged();
        _tree.SetFocus();
        UpdateTitle();
        ShowHints();
    }

    /// <summary>Shows whichever mode the controller is in: the tree with its focus, or the text.</summary>
    private void ShowMode(NodePath? focus)
    {
        if (_controller.Mode == EditMode.Tree)
        {
            ShowTree(focus);
            return;
        }

        ShowText(_controller.CursorOffset);
    }

    /// <summary>Takes the view's text and cursor into the controller. Nothing is taken while the text is read-only, so it stays the file's text.</summary>
    private void SyncText()
    {
        if (_controller.Mode != EditMode.Text || _textReadOnly)
        {
            return;
        }

        _controller.SetText(_textView.Text, _textView.CurrentRow, _textView.CurrentColumn);
    }

    /// <summary>Moves the text view's cursor to an offset in the text.</summary>
    private void MoveTextCursor(int offset)
    {
        (int row, int column) = TextModeRules.PositionOf(_controller.Text!.Text, _controller.Newline, offset);
        _textView.InsertionPoint = new (column, row);
    }

    /// <summary>The offset in the text of a parse error's line and column, or the end of the text when the position is past it.</summary>
    private int ErrorOffset(CfgeditException error)
    {
        SourceText text = _controller.Text!;
        if (error.Line is not int line)
        {
            return 0;
        }

        try
        {
            return text.GetOffset(new LinePosition(line, error.Column ?? 1));
        }
        catch (ArgumentOutOfRangeException)
        {
            return text.Text.Length;
        }
    }

    /// <summary>
    /// F5 and Ctrl+T: parses the text. A text that parses shows the tree, with the focus on the node under the cursor. One that does not
    /// stays in text mode, with the cursor at the error and its message on the message line.
    /// </summary>
    private void ReparseText()
    {
        NodePath? focus = _controller.Reparse(out CfgeditException? error);
        if (error is null)
        {
            ShowTree(focus);
            return;
        }

        MoveTextCursor(ErrorOffset(error));
        ShowMessage(EditController.Describe(error), error: true);
    }

    /// <summary>
    /// Ctrl+S in text mode: the save flow of the text. The text parses, or the user agrees to save it as it is, which is then asked for
    /// with the error in the question.
    /// </summary>
    private void SaveFromText()
    {
        bool wasNew = !_controller.File.Exists;
        SaveResult result = _controller.TextSaveFlow(AskExternalChange, ConfirmReload, AskUnparsedSave);
        ShowSaveResult(result, wasNew);
        SyncMode();
    }

    /// <summary>
    /// Shows the mode the controller is in when the window shows the other one. A flow can parse the text (which switches to the tree) and
    /// then be cancelled, so the window checks after each flow.
    /// </summary>
    private void SyncMode()
    {
        if (_textView.Visible != (_controller.Mode == EditMode.Text))
        {
            ShowMode(null);
        }
    }

    /// <summary>The question for text that does not parse. The cursor goes to the error, and Save is the default.</summary>
    private bool AskUnparsedSave(CfgeditException error)
    {
        string description = EditController.Describe(error);
        MoveTextCursor(ErrorOffset(error));
        ShowMessage(description, error: true);
        return Confirm.Choose(Ui, "Save", $"{description}. Save anyway?", "Cancel", "Save") == 1;
    }

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
            case EditorKind.Type:
                ChangeTypeOf(node);
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
        _search.Close();
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
        _search.Close();
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

    // ----- Structure keys -----

    /// <summary>
    /// The structure keys of the tree: add (a, A, i, +), delete (d, Delete), move (Alt+Up/Down, K, J, Alt+Left/Right), change the type (t),
    /// and duplicate (Ctrl+D). Returns false for a key they do not bind. They apply only while no field is open.
    /// </summary>
    private bool HandleStructureKey(Key key)
    {
        if (IsPlain(key, 'a'))
        {
            AddSibling(after: true);
        }
        else if (IsPlain(key, 'A'))
        {
            AddSibling(after: false);
        }
        else if (IsPlain(key, 'i'))
        {
            AddChild();
        }
        else if (IsPlain(key, '+'))
        {
            AddAttribute();
        }
        else if (IsPlain(key, 'd') || key == Key.Delete)
        {
            DeleteFocused();
        }
        else if (key == Key.CursorUp.WithAlt || IsPlain(key, 'K'))
        {
            MoveNodeBy(-1);
        }
        else if (key == Key.CursorDown.WithAlt || IsPlain(key, 'J'))
        {
            MoveNodeBy(1);
        }
        else if (key == Key.CursorLeft.WithAlt)
        {
            MoveAttributeBy(-1);
        }
        else if (key == Key.CursorRight.WithAlt)
        {
            MoveAttributeBy(1);
        }
        else if (IsPlain(key, 't'))
        {
            ChangeType();
        }
        else if (key == Key.D.WithCtrl)
        {
            DuplicateFocused();
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>Gets the node and part under the cursor. Returns false, with nothing set, when the tree shows nothing.</summary>
    private bool TryCursor(out DocNode node, out DocPart part)
    {
        DocNode? cursorNode = _tree.CursorNode;
        DocPart? cursorPart = _tree.CursorDocPart;
        if (cursorNode is null || cursorPart is null)
        {
            node = null!;
            part = null!;
            return false;
        }

        node = cursorNode;
        part = cursorPart;
        return true;
    }

    private static bool IsAttributePart(DocPart part) => part.Kind is PartKind.AttributeName or PartKind.AttributeValue;

    private static int AttributeCount(DocNode element) => element.Parts.Count(part => part.Kind == PartKind.AttributeName);

    private string Describe(DocNode node, DocPart part) => PathText.Format(_controller.Document, node, part);

    /// <summary>
    /// a and A: a sibling after or before the cursor's node. On an attribute part, an attribute after or before that attribute. A new
    /// element, container item or scalar item copies its sibling's shape with no dialog. Anything else asks for its kind.
    /// </summary>
    private void AddSibling(bool after)
    {
        if (!TryCursor(out DocNode node, out DocPart part))
        {
            return;
        }

        if (IsAttributePart(part))
        {
            AskAttribute(node, part.AttributeIndex + (after ? 1 : 0));
            return;
        }

        DocNode? parent = node.Parent;
        if (parent is null)
        {
            ShowMessage("the root has no siblings", error: false);
            return;
        }

        int index = after ? _controller.Document.InsertionIndexAfter(node.Path) : _controller.Document.InsertionIndexBefore(node.Path);
        if (IsShapeSource(node, parent))
        {
            AddShaped(node, parent, index);
            return;
        }

        AskAdd(parent, index, (after ? "Add after " : "Add before ") + Describe(node, part));
    }

    /// <summary>i: a child at the end of the cursor's container or element, or an attribute when the cursor is on an element's name.</summary>
    private void AddChild()
    {
        // i always adds a child, whichever part the cursor is on: the name part is where the cursor starts on an element,
        // so giving i another meaning there would leave <a/> without a way to get a child. Attributes are added with +.
        if (!TryCursor(out DocNode node, out DocPart part))
        {
            return;
        }

        if (node.Kind is NodeKind.XmlElement or NodeKind.JsonObject or NodeKind.JsonArray)
        {
            AskAdd(node, node.Children.Count, "Add to " + Describe(node, part));
            return;
        }

        ShowMessage("this node has no children", error: false);
    }

    /// <summary>+: an attribute at the end of the element the cursor is in.</summary>
    private void AddAttribute()
    {
        if (!TryCursor(out DocNode node, out _))
        {
            return;
        }

        if (node.Kind != NodeKind.XmlElement)
        {
            ShowMessage("attributes apply to XML elements only", error: false);
            return;
        }

        AskAttribute(node, AttributeCount(node));
    }

    /// <summary>
    /// True when a new node copies the shape of <paramref name="node"/>: an XML element inside an element, or a JSON scalar, object or
    /// array inside an array. A JSON member has a key, so it asks for one.
    /// </summary>
    private static bool IsShapeSource(DocNode node, DocNode parent)
    {
        bool xmlElement = node.Kind == NodeKind.XmlElement && parent.Kind == NodeKind.XmlElement;
        bool jsonItem = (node.Kind is NodeKind.JsonScalar or NodeKind.JsonObject or NodeKind.JsonArray) && parent.Kind == NodeKind.JsonArray;
        return xmlElement || jsonItem;
    }

    /// <summary>Adds a node shaped like the sibling, and opens the editor on its first value.</summary>
    private void AddShaped(DocNode node, DocNode parent, int index)
    {
        ShapedInsert shaped;
        try
        {
            shaped = _controller.Document.ShapeLike(node.Path, parent.Path, index);
        }
        catch (ArgumentException ex)
        {
            ShowMessage(ex.Message, error: true);
            return;
        }

        if (_controller.Apply(shaped.Edit, out string? error) is null)
        {
            ShowMessage(error ?? "the node cannot be added", error: true);
            return;
        }

        // The edit's focus is its last step's, so the new node is named by the shape, not by the result.
        Refresh(shaped.Node);
        OpenFirstValue(shaped.Node);
    }

    /// <summary>Shows the add dialog for a parent and inserts what it asks for. The new node's first value opens in the editor.</summary>
    private void AskAdd(DocNode parent, int index, string title)
    {
        IReadOnlyList<AddKind> kinds = AddKinds.For(_controller.Document, parent);
        if (kinds.Count == 0)
        {
            ShowMessage("nothing can be added here", error: false);
            return;
        }

        NodePath? added = null;
        bool done = AddDialog.Ask(Ui, title, kinds, parent, request =>
        {
            if (_controller.Apply(AddKinds.Build(parent, index, request), out string? error) is not { } result)
            {
                return error ?? "the node cannot be added";
            }

            added = result.Focus;
            return null;
        });
        if (done && added is not null)
        {
            Refresh(added);
            OpenFirstValue(added);
        }
    }

    /// <summary>Asks for an attribute's name, inserts the attribute with an empty value, and opens the value's editor.</summary>
    private void AskAttribute(DocNode element, int index)
    {
        bool done = TextPromptDialog.Ask(Ui, "Add attribute", "Name", string.Empty, name =>
        {
            if (_controller.Apply(new InsertAttribute(element.Path, index, name, string.Empty), out string? error) is null)
            {
                return error ?? "the attribute cannot be added";
            }

            return null;
        });
        if (!done)
        {
            return;
        }

        Refresh(element.Path);
        DocNode? current = _controller.Document.Resolve(element.Path);
        FirstValue.Target? value = current is null ? null : FirstValue.AttributeValue(current, index);
        if (value is { } target && _tree.MoveCursorTo(target.Node.Path, isEnd: false, part: target.Part))
        {
            BeginEdit(multiline: false);
        }
    }

    /// <summary>Moves the cursor to the first value of a node that was just inserted, and opens its editor, when it has one.</summary>
    private void OpenFirstValue(NodePath node)
    {
        DocNode? created = _controller.Document.Resolve(node);
        FirstValue.Target? target = created is null ? null : FirstValue.In(created);
        if (target is { } value && _tree.MoveCursorTo(value.Node.Path, isEnd: false, part: value.Part))
        {
            BeginEdit(multiline: false);
        }
    }

    /// <summary>d and Delete: the attribute under the cursor, or else the node, with no confirmation. Undo restores it.</summary>
    private void DeleteFocused()
    {
        if (!TryCursor(out DocNode node, out DocPart part))
        {
            return;
        }

        if (IsAttributePart(part))
        {
            ApplyAndShow(new DeleteAttribute(node.Path, part.AttributeIndex), node.Path);
            return;
        }

        ApplyAndShow(new DeleteNode(node.Path), node.Parent?.Path ?? node.Path);
    }

    /// <summary>Alt+Up/Down and K/J: the node moves among its siblings. At the end it says so.</summary>
    private void MoveNodeBy(int delta)
    {
        if (!TryCursor(out DocNode node, out _))
        {
            return;
        }

        MoveNode? move = _controller.Document.MoveBy(node.Path, delta);
        if (move is null)
        {
            ShowAtEnd(delta);
            return;
        }

        ApplyAndShow(move, node.Path);
    }

    /// <summary>Alt+Left/Right: the attribute under the cursor moves among its element's attributes. At the end it says so.</summary>
    private void MoveAttributeBy(int delta)
    {
        if (!TryCursor(out DocNode node, out DocPart part))
        {
            return;
        }

        if (!IsAttributePart(part))
        {
            ShowMessage("only an attribute moves with Alt+Left and Alt+Right", error: false);
            return;
        }

        MoveAttribute? move = _controller.Document.MoveAttributeBy(node.Path, part.AttributeIndex, delta);
        if (move is null)
        {
            ShowAtEnd(delta);
            return;
        }

        ApplyAndShow(move, node.Path);
    }

    private void ShowAtEnd(int delta) => ShowMessage(delta < 0 ? "already first" : "already last", error: false);

    private static readonly JsonValueKind[] TypeKinds =
    [
        JsonValueKind.String,
        JsonValueKind.Number,
        JsonValueKind.True,
        JsonValueKind.False,
        JsonValueKind.Null,
        JsonValueKind.Object,
        JsonValueKind.Array,
    ];

    private static readonly string[] TypeNames = ["string", "number", "true", "false", "null", "object", "array"];

    /// <summary>
    /// t: the type menu of a JSON value, with the current type chosen. A container that has children asks before its contents are
    /// dropped. A number keeps the text when it is a valid number, else 0; a string keeps the text of a scalar, else is empty.
    /// </summary>
    private void ChangeType()
    {
        if (!TryCursor(out DocNode node, out _))
        {
            return;
        }

        ChangeTypeOf(node);
    }

    /// <summary>The type menu of a node, as <see cref="ChangeType"/> shows it. Enter on a JSON true, false or null opens it too.</summary>
    private void ChangeTypeOf(DocNode node)
    {
        if (node.JsonKind is not { } current)
        {
            ShowMessage("types apply to JSON only", error: false);
            return;
        }

        ConfigDocument doc = _controller.Document;
        string target = PathText.Format(doc, node, null);
        int? choice = ChoiceDialog.Ask(Ui, "Type of " + target, TypeNames, Array.IndexOf(TypeKinds, current));
        if (choice is not { } picked || TypeKinds[picked] == current)
        {
            return;
        }

        JsonValueKind kind = TypeKinds[picked];
        bool container = node.Kind is NodeKind.JsonObject or NodeKind.JsonArray;
        if (container && node.Children.Count > 0
            && Confirm.Choose(Ui, "Type", $"Discard the {node.Children.Count} children of {target}?", "Change", "Cancel") != 0)
        {
            return;
        }

        string? value = kind switch
        {
            JsonValueKind.Number => NumberText(doc, node),
            JsonValueKind.String => ScalarText(node) ?? string.Empty,
            _ => null,
        };
        ApplyAndShow(new ChangeJsonType(node.Path, kind, value), node.Path);
    }

    private static string NumberText(ConfigDocument doc, DocNode node)
    {
        string? text = ScalarText(node);
        JsonDialect dialect = doc.Format is JsonFormat json ? json.Dialect : JsonDialect.Json;
        return text is not null && JsonSyntax.IsValidNumber(text, dialect) ? text : "0";
    }

    /// <summary>The value text of a scalar, as the tree shows it, or null for a container and for an XML node.</summary>
    private static string? ScalarText(DocNode node) => node.Parts.FirstOrDefault(part => part.Kind == PartKind.Value)?.Value;

    /// <summary>Ctrl+D: a copy of the node after it.</summary>
    private void DuplicateFocused()
    {
        if (!TryCursor(out DocNode node, out _))
        {
            return;
        }

        ApplyAndShow(new DuplicateNode(node.Path), node.Path);
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
        bool wasNew = !_controller.File.Exists;
        ShowSaveResult(_controller.SaveFlow(AskExternalChange, ConfirmReload), wasNew);
    }

    /// <summary>
    /// Shows what a save flow did: the mode the controller is in now (a reload of a file that no longer parses returns to text), and the
    /// message. A save of text that does not parse says so, in the error style.
    /// </summary>
    private void ShowSaveResult(SaveResult result, bool wasNew)
    {
        switch (result.Outcome)
        {
            case SaveOutcome.Saved:
                ShowMode(null);
                if (wasNew)
                {
                    _onFirstSave?.Invoke();
                }

                if (_controller.File.ParseError is { } parseError)
                {
                    ShowMessage("Saved, but it does not parse: " + EditController.Describe(parseError), error: true);
                    break;
                }

                ShowMessage("Saved", error: false);
                break;
            case SaveOutcome.Reloaded:
                ShowMode(null);
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
        CloseResult result = _controller.CloseFlow(() => AskUnsaved(action), AskExternalChange, ConfirmReload, AskUnparsedSave);
        switch (result.Outcome)
        {
            case CloseOutcome.Reloaded:
                ShowMode(null);
                ShowMessage("Reloaded", error: false);
                break;
            case CloseOutcome.Failed:
                UpdateTitle();
                ShowMessage(result.Error ?? "the file cannot be saved", error: true);
                break;
        }

        SyncMode();
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
        _search.OnDocumentChanged();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string dirty = _controller.IsDirty ? "*" : string.Empty;
        string mode = _controller.Mode == EditMode.Tree ? string.Empty : _textReadOnly ? " [text, read-only]" : " [text]";
        _frame.Title = $"{FileName}{dirty} [{_controller.File.Format.Name}]{mode}";
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
        Hint hint = _controller.Mode == EditMode.Text
            ? CfgeditHints.TextMode
            : _tree.IsEditing ? CfgeditHints.Editing : CfgeditHints.Tree;
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
