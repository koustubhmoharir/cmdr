using System.Collections.Immutable;
using Mdedit.Core.Render;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Editor.Document;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Point = System.Drawing.Point;

namespace Mdedit.Tui;

/// <summary>
/// The borderless main window: the document in a rounded frame whose title is the file name, the message line (the heading path and the
/// scroll position, or a message in their place), and the key hints on the last row. The frame holds the rendered view, or the source
/// editor in edit mode; both show the one source buffer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Quit and switch.</b> Ctrl+Q stops the window, and Ctrl+O shows the picker over the document; a file chosen there replaces the
/// document when the window stops (see <see cref="SwitchTo"/>). Both ask about unsaved changes first (see <b>Saving</b>). Esc is swallowed in
/// view mode: the window has nothing to go back to here (see <b>Links</b> for Alt+Left).
/// </para>
/// <para>
/// <b>Links.</b> In view mode Tab and Shift+Tab move between links, Enter and a Ctrl+click follow one, and Alt+Left goes back to the file a
/// followed link left. A file link stops the window with <see cref="SwitchTo"/> set, as Ctrl+O does; the host shows the file in a new window
/// (see <c>MainWindow.Navigation.cs</c>).
/// </para>
/// <para>
/// <b>Modes.</b> The window opens in view mode, except for a new file (one that does not exist yet), which opens in edit mode, and a large file
/// (over <see cref="LargeFileBytes"/> on disk), which opens in edit mode with <see cref="LargeFileOpened"/>. Ctrl+T switches between the modes, and Esc goes from edit mode to view mode. The view is
/// rendered from the buffer when the buffer's version moved since its last render (see <see cref="RenderBuffer"/>). Ctrl+Z and Ctrl+Y undo
/// and redo the buffer in view mode; in edit mode the editor's own keys do it.
/// </para>
/// <para>
/// <b>Messages.</b> A message stays for <see cref="MessageDuration"/> or until the next key, whichever is first. The delay runs through
/// the scheduler, so tests drive the expiry.
/// </para>
/// <para>
/// <b>Saving.</b> Ctrl+S in either mode writes the buffer's text to the file (see <see cref="DocumentController.SaveFlow"/>). A file that
/// changed on disk asks Overwrite, Reload or Cancel first. Quitting and Ctrl+O ask Save, Discard or Cancel when the document is dirty, and a
/// failed save keeps the window open. A new file is dirty until its first save, and its first successful save calls the first-save action.
/// </para>
/// <para>
/// <b>External changes.</b> A timer checks the file once a second while the window runs (see <see cref="CheckExternalChange"/>). The check waits
/// while a dialog, the context menu, the picker or a save or close flow is up, and is then done as soon as they close. A deleted file that the
/// user closes stops the window with <see cref="ReturnToPicker"/> set.
/// </para>
/// </remarks>
internal sealed partial class MainWindow : Window, ISearchHost
{
    /// <summary>How long a message stays on the message line when no key clears it.</summary>
    internal static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(4);

    /// <summary>The separator between the headings of the heading path.</summary>
    internal const string PathSeparator = " › ";

    /// <summary>The message after the plain text of a selection or block was copied.</summary>
    internal const string CopiedText = "Copied text";

    /// <summary>The message after the Markdown of a selection or block was copied.</summary>
    internal const string CopiedMarkdown = "Copied Markdown";

    /// <summary>The message of a copy whose text is over <see cref="Osc52.MaxBytes"/>, which is not copied.</summary>
    internal const string TooLargeToCopy = "Too large to copy";

    /// <summary>The size, in bytes on disk, above which a file opens in edit mode, and a view render asks first.</summary>
    internal const long LargeFileBytes = 2 * 1024 * 1024;

    /// <summary>The message after a large file opened in edit mode.</summary>
    internal const string LargeFileOpened = "Large file: opened as text";

    /// <summary>How often the window checks its file for a change on disk while it runs (see <see cref="CheckExternalChange"/>).</summary>
    internal static readonly TimeSpan ExternalCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>The message after a save.</summary>
    internal const string Saved = "Saved";

    /// <summary>The message after the text was replaced by the file's text, by a reload from a save or from a change on disk.</summary>
    internal const string Reloaded = "Reloaded";

    /// <summary>The message after the user kept the text over a changed file: the next save overwrites the file.</summary>
    internal const string KeptMine = "Kept your changes; Ctrl+S overwrites the file";

    /// <summary>The message after the user kept the text over a deleted file: the next save creates the file.</summary>
    internal const string KeptDeleted = "The file was deleted on disk";

    /// <summary>
    /// How long the editor's foldings wait after a change that may affect them: one update runs for a batch of changes (see
    /// <see cref="ScheduleFoldUpdate"/>).
    /// </summary>
    internal static readonly TimeSpan FoldUpdateDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>The message after a file changed on disk was reloaded, when there was undo history to go back to.</summary>
    internal const string ReloadedFromDisk = "Reloaded: the file changed on disk";

    // The title of the question that a large file's view render asks first.
    private const string LargeFileTitle = "Large file";

    private readonly Func<(PickOutcome Outcome, OpenedFile? File)> _pickFile;
    private readonly Func<TimeSpan, Action, Action> _schedule;
    private readonly OpenedFile _opened;
    private readonly DocumentController _controller;
    private readonly Action? _onFirstSave;
    private readonly long _largeFileBytes;
    private readonly FrameView _frame;
    private readonly RenderedView _view;
    private readonly SourceEditor _editor;
    private readonly MessageLine _message;
    private readonly HintsLine _hints;
    private readonly EditorFolds _folds;
    private readonly DocumentSearch _search;
    private ImmutableArray<OutlineEntry> _outline;
    private PopoverMenu? _menu;
    private Action? _cancelExpiry;

    // The timer of the folds' update that is pending, or null (see ScheduleFoldUpdate).
    private object? _foldTimer;

    // The timer that calls CheckExternalChange. It runs from the window's start to its stop.
    private object? _externalTimer;

    // True in edit mode, when the editor is shown instead of the view.
    private bool _editing;

    // The buffer's version that the view was rendered from, or -1 before the first render of a large file.
    private int _renderedVersion;

    // True while the document is dirty (see DocumentController.IsDirty), kept up to date by RefreshDirty.
    private bool _dirty;

    // True while a save or close flow runs (see InFlow). The external check waits for it.
    private bool _flowing;

    /// <summary>
    /// Creates the window over a file, with the view at the document's start.
    /// </summary>
    /// <param name="file">The file to show.</param>
    /// <param name="pickFile">Shows the picker for Ctrl+O, and reports what it did.</param>
    /// <param name="schedule">
    /// Runs an action once after a delay on the UI thread, and returns a function that cancels it. Null uses the application's timers.
    /// Tests pass a fake, so that a message's expiry is driven without waiting.
    /// </param>
    /// <param name="largeFileBytes">
    /// The size above which the file opens in edit mode. Tests lower it; the application uses <see cref="LargeFileBytes"/>.
    /// </param>
    /// <param name="onFirstSave">
    /// Runs after the first successful save of a new file, when the host records the file in the recent list. Null for none.
    /// </param>
    /// <param name="loadFile">
    /// Loads a file that a followed link or a Back names, as the picker does: the file, or the message why it does not load. Null uses
    /// <see cref="MdeditHost.TryLoad"/>.
    /// </param>
    /// <param name="back">The back stack shared with the host for the run of documents. Null gives the window a stack of its own.</param>
    /// <param name="fragment">The fragment of the link that opened the window: the window starts at that heading. Null for none.</param>
    /// <param name="restore">The place a Back returned to: the window starts with its caret and top line. Null for none.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public MainWindow(
        OpenedFile file,
        Func<(PickOutcome Outcome, OpenedFile? File)> pickFile,
        Func<TimeSpan, Action, Action>? schedule = null,
        long largeFileBytes = LargeFileBytes,
        Action? onFirstSave = null,
        Func<string, (OpenedFile? Value, string? Error)>? loadFile = null,
        BackStack? back = null,
        string? fragment = null,
        BackEntry? restore = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(pickFile);

        _opened = file;
        _controller = new DocumentController(file.File);
        _onFirstSave = onFirstSave;
        _pickFile = pickFile;
        _schedule = schedule ?? ScheduleOnApp;
        _largeFileBytes = largeFileBytes;
        _loadFile = loadFile ?? MdeditHost.TryLoad;
        _back = back ?? new BackStack();

        // No outer frame: the rendered view's frame, the message line and the hints fill the window.
        BorderStyle = LineStyle.None;

        // The buffer holds the text from the start. A large file's view is rendered only when the view is first shown.
        _editor = new SourceEditor { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Visible = false, LineBreak = file.File.Text.Newline };
        _editor.Text = file.File.Text.Text;
        _editor.Buffer.UndoStack.ClearAll();
        bool large = IsLarge;

        RenderDocument document = large ? RenderDocument.Parse(string.Empty) : RenderDocument.Parse(file.File.Text.Text, TextMateColorizer.Shared);
        _outline = Outline.Of(document);
        _view = new RenderedView(document, new AnchorOffsetTracker(_editor.Buffer)) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _frame = new FrameView
        {
            BorderStyle = LineStyle.Rounded,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
        };
        _frame.Add(_view, _editor);

        _message = new MessageLine { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill(), Height = 1 };
        _hints = new HintsLine { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
        _folds = new EditorFolds(_editor, _view);
        _folds.UpdateRequested += ScheduleFoldUpdate;
        _search = new DocumentSearch(this, new ModeSearchTarget(_view, _editor, _folds, () => _editing));

        // The search bar sits above the message line. It is shown with the frame one row shorter (see ShowSearchBar).
        _search.Bar.X = 0;
        _search.Bar.Y = Pos.AnchorEnd(3);
        _search.Bar.Width = Dim.Fill();
        _search.Bar.Height = 1;
        _search.Bar.Visible = false;
        Add(_frame, _search.Bar, _message, _hints);
        WriteRaw = sequence => App?.Driver?.WriteRaw(sequence);

        _view.PositionChanged += ShowStatus;
        _view.KeyPressed += ClearMessage;
        _view.ContextMenuRequested += ShowContextMenu;
        _view.LinkActivated += FollowLink;
        _editor.BufferChanged += OnBufferChanged;
        _editor.CaretChanged += (_, _) =>
        {
            if (_editing)
            {
                ShowStatus();
            }
        };
        _editor.KeyPressed += ClearMessage;

        // A new file (one that does not exist yet) opens in edit mode, as does a large file, which says so.
        bool editing = large || !file.File.Exists;
        _renderedVersion = large ? -1 : _editor.Version;
        if (editing)
        {
            _editing = true;
            _view.Visible = false;
            _editor.Visible = true;
            _editor.SetFocus();
            EnterEditFolds();
            if (large)
            {
                ShowMessage(LargeFileOpened, error: false);
            }
        }
        else
        {
            _view.SetFocus();
        }

        ApplyStart(restore, fragment, file.File.Text.Text.Length);
        _dirty = _controller.IsDirty(_editor.Buffer.Text);
        UpdateTitle();
        ShowHints();
        ShowStatus();
    }

    /// <summary>The rendered view, for the tests.</summary>
    internal RenderedView View => _view;

    /// <summary>The source editor of edit mode, for the tests.</summary>
    internal SourceEditor Editor => _editor;

    /// <summary>Whether the window is in edit mode (the editor is shown), for the tests.</summary>
    internal bool InEditMode => _editing;

    /// <summary>Whether the file is larger than the size that opens in edit mode.</summary>
    private bool IsLarge => _opened.File.Bytes.Length > _largeFileBytes;

    /// <summary>The message line, for the tests.</summary>
    internal MessageLine Message => _message;

    /// <summary>The search of the window, in both modes, for the tests.</summary>
    internal DocumentSearch Search => _search;

    /// <summary>
    /// Writes a raw control sequence to the terminal: the OSC 52 of a copy. The application's driver by default; a test replaces it to
    /// read what would be written.
    /// </summary>
    internal Action<string> WriteRaw { get; set; }

    /// <summary>The key hints, for the tests.</summary>
    internal HintsLine Hints => _hints;

    /// <summary>The file chosen with Ctrl+O before the window stopped, or null. The host opens it next.</summary>
    public OpenedFile? SwitchTo { get; private set; }

    /// <summary>True once the window has stopped because of a quit or a switch.</summary>
    internal bool Stopped { get; private set; }

    /// <summary>
    /// True when the window stopped because the file it showed was deleted and the user closed it: the host then shows the recent-files
    /// picker, as at startup, instead of a document.
    /// </summary>
    internal bool ReturnToPicker { get; private set; }

    /// <summary>The name of the file, as the frame's title shows it.</summary>
    private string FileName => Path.GetFileName(_opened.Path);

    /// <summary>The application, for the questions of edit mode. Only used while the window runs.</summary>
    private IApplication Ui => App ?? throw new InvalidOperationException("The window is not running in an application.");

    /// <summary>
    /// The window's keys that the view and the editor do not take. Ctrl+Q quits, Ctrl+O opens the picker and Ctrl+T switches the mode, in
    /// both modes, and Alt+O opens the outline (see <see cref="HandleOutlineKey"/>). In edit mode Esc closes an open search or else
    /// returns to view mode, and Ctrl+F, F3 and Shift+F3 search (see <see cref="HandleEditKey"/>). In view mode Esc is swallowed (or closes an
    /// open search), Ctrl+Z and Ctrl+Y undo and redo, and the view's search and copy keys apply.
    /// </summary>
    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            Quit();
            return true;
        }

        if (_cellField is not null)
        {
            // The inline field has the keys that it does not take (see HandleTableKey): none of the window's other keys run under it.
            return true;
        }

        if (key == Key.O.WithCtrl)
        {
            OpenPicker();
            return true;
        }

        if (key == Key.T.WithCtrl)
        {
            ToggleMode();
            return true;
        }

        if (key == Key.S.WithCtrl)
        {
            Save();
            return true;
        }

        if (HandleOutlineKey(key))
        {
            return true;
        }

        if (_editing)
        {
            return HandleEditKey(key) || base.OnKeyDown(key);
        }

        // The table keys come before the links, so that Alt+Left moves the column in a table, and Enter edits a cell unless it is on a link.
        if (HandleTableKey(key))
        {
            return true;
        }

        if (HandleLinkKey(key))
        {
            return true;
        }

        if (key == Key.Esc)
        {
            // Esc closes an open search (its bar is not focused here, or Esc would have been taken by it). Otherwise it is swallowed.
            _search.Close();
            return true;
        }

        if (key == Key.Z.WithCtrl || key == Key.Y.WithCtrl)
        {
            UndoView(redo: key == Key.Y.WithCtrl);
            return true;
        }

        if (HandleSearchKey(key))
        {
            return true;
        }

        if (key == PopoverMenu.DefaultKey)
        {
            ShowContextMenu(_view.CursorScreenPosition);
            return true;
        }

        return base.OnKeyDown(key);
    }

    // ----- Modes -----

    /// <summary>
    /// The keys of edit mode that the editor does not take. Esc closes an open search, and otherwise returns to view mode. Ctrl+F opens the
    /// search with the focus in its field; F3 and Shift+F3 go to the next and previous match. <c>/</c>, <c>n</c> and <c>N</c> are text here.
    /// </summary>
    private bool HandleEditKey(Key key)
    {
        if (key == Key.Esc)
        {
            if (_search.IsOpen)
            {
                _search.Close();
            }
            else
            {
                ShowView();
            }

            return true;
        }

        if (key == Key.F.WithCtrl)
        {
            _search.Open(focusBar: true);
            return true;
        }

        if (key == Key.F3 || key == Key.F3.WithShift)
        {
            _search.Find(next: key == Key.F3);
            return true;
        }

        return false;
    }

    /// <summary>Ctrl+T: edit mode from view mode, and view mode from edit mode.</summary>
    private void ToggleMode()
    {
        if (_editing)
        {
            ShowView();
        }
        else
        {
            ShowEdit();
        }
    }

    /// <summary>
    /// Shows the editor, with its caret and selection where the view's are: the caret at the view's cursor offset, and the selection (if any)
    /// from the view's selection anchor to the cursor.
    /// </summary>
    private void ShowEdit()
    {
        if (_editing)
        {
            return;
        }

        if (_renderedVersion != _editor.Version)
        {
            RenderBuffer();
        }

        int offset = _view.CursorOffset;
        int? anchor = _view.SelectionAnchor;
        _search.Close();
        _editing = true;
        _view.Visible = false;
        _editor.Visible = true;
        _editor.SetFocus();
        EnterEditFolds();
        _editor.PlaceCaret(offset, anchor);
        UpdateTitle();
        ShowHints();
        ShowStatus();
    }

    /// <summary>
    /// Shows the view, rendered from the buffer when its text moved since the last render, with the cursor at the editor's caret and the
    /// selection carried over. The editor's folds are given to the view first, so the view shows the folds the editor has. A large file that
    /// needs a render asks first, and stays in edit mode when the answer is no. The view keeps the caret visible; it does not keep the
    /// caret's screen row, which the editor does not report.
    /// </summary>
    private void ShowView()
    {
        if (!_editing)
        {
            return;
        }

        bool rendering = _renderedVersion != _editor.Version;
        if (rendering && IsLarge && Confirm.Choose(Ui, LargeFileTitle, $"Render {_opened.File.Bytes.Length / (1024.0 * 1024.0):0.#} MB?", "Render", "Cancel") != 0)
        {
            return;
        }

        _search.Close();
        int offset = _editor.CaretOffset;
        int? anchor = _editor.SelectionAnchor;
        CancelFoldTimer();
        _folds.Update();
        _folds.GiveViewState();
        if (rendering)
        {
            RenderBuffer();
        }
        else
        {
            _view.SetDocument(_view.Document);
        }

        _editing = false;
        _editor.Visible = false;
        _view.Visible = true;
        _view.SetFocus();
        _view.PlaceCaret(offset, anchor);
        UpdateTitle();
        ShowHints();
        ShowStatus();
    }

    /// <summary>
    /// Renders the view from the buffer's text, and gives the view the new document (see <see cref="RenderedView.SetDocument"/>). The outline
    /// and the search follow the document.
    /// </summary>
    private void RenderBuffer()
    {
        RenderDocument document = RenderDocument.Parse(_editor.Buffer.Text, TextMateColorizer.Shared);
        _outline = Outline.Of(document);
        _view.SetDocument(document);
        _search.Refresh();
        _renderedVersion = _editor.Version;
    }

    /// <summary>
    /// Makes the editor's foldings those of the buffer's current text, with the view's folded units folded (see <see cref="EditorFolds"/>).
    /// Any update that was pending is done now, since the view's state is taken from the foldings' starts.
    /// </summary>
    private void EnterEditFolds()
    {
        CancelFoldTimer();
        _folds.Update();
        _folds.TakeViewState();
    }

    /// <summary>
    /// Asks for the folds' update after <see cref="FoldUpdateDelay"/>: the timer of a batch of changes is started by its first change, and later
    /// changes of the batch join it. Without a running application (the window's construction) the update runs now.
    /// </summary>
    private void ScheduleFoldUpdate()
    {
        if (_foldTimer is not null)
        {
            return;
        }

        if (App is null)
        {
            FlushFolds();
            return;
        }

        _foldTimer = App.AddTimeout(FoldUpdateDelay, () =>
        {
            _foldTimer = null;
            FlushFolds();
            return false;
        });
    }

    /// <summary>Updates the folds from the buffer, and in edit mode gives the view the result.</summary>
    private void FlushFolds()
    {
        _folds.Update();
        if (_editing)
        {
            _folds.GiveViewState();
        }
    }

    /// <summary>Stops the pending folds' update, if there is one, since the caller updates the folds itself.</summary>
    private void CancelFoldTimer()
    {
        if (_foldTimer is { } timer)
        {
            App?.RemoveTimeout(timer);
            _foldTimer = null;
        }
    }

    /// <summary>
    /// Undoes or redoes the buffer from view mode, and shows the result: the view is rendered again, and the cursor goes to the place of
    /// the change. Does nothing when there is nothing to undo or redo.
    /// </summary>
    /// <param name="redo">True to redo, false to undo.</param>
    private void UndoView(bool redo)
    {
        if (_editor.Undo(redo) is not { } caret)
        {
            return;
        }

        RenderBuffer();
        _view.PlaceCaret(caret, null);
        ShowStatus();
    }

    /// <summary>
    /// Called after each change of the buffer: the dirty state and the title follow the text, and in edit mode an open search searches again.
    /// </summary>
    private void OnBufferChanged()
    {
        RefreshDirty();
        if (_editing)
        {
            _search.Refresh();
        }
    }

    /// <summary>Recomputes the dirty state from the buffer and the file, and updates the title.</summary>
    private void RefreshDirty()
    {
        _dirty = _controller.IsDirty(_editor.Buffer.Text);
        UpdateTitle();
    }

    /// <summary>The frame's title: the file name, <c> *</c> when the text differs from the file, and <c> [edit]</c> in edit mode.</summary>
    private void UpdateTitle() => _frame.Title = FileName + (_dirty ? " *" : string.Empty) + (_editing ? " [edit]" : string.Empty);

    /// <summary>The key hints of the current mode, with Quit at the right.</summary>
    private void ShowHints() => _hints.Show(_editing ? MdeditHints.Edit : MdeditHints.View, MdeditHints.Quit);

    // ----- Search and copy -----

    /// <summary>
    /// The keys of the view's search and copy: <c>/</c> and Ctrl+F open the bar, <c>n</c> and F3 go to the next match, <c>N</c> and
    /// Shift+F3 to the previous one, <c>y</c> and Ctrl+C copy the selection (or the block under the cursor) as plain text, and <c>Y</c>
    /// as Markdown. Returns false for a key they do not bind.
    /// </summary>
    private bool HandleSearchKey(Key key)
    {
        if (IsPlain(key, 'y') || key == Key.C.WithCtrl)
        {
            CopyView(markdown: false);
        }
        else if (IsPlain(key, 'Y'))
        {
            CopyView(markdown: true);
        }
        else if (key == Key.F.WithCtrl || IsPlain(key, '/'))
        {
            _search.Open(focusBar: true);
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

    /// <summary>
    /// Writes the selection (or the block under the cursor) to the clipboard with OSC 52, as plain text or as Markdown, and says what was
    /// copied. A copy with nothing in it does nothing; a text over <see cref="Osc52.MaxBytes"/> is not copied.
    /// </summary>
    /// <param name="markdown">True for the Markdown of the source, false for the plain rendered text.</param>
    internal void CopyView(bool markdown)
    {
        RenderDocument document = _view.Document;
        if (ViewCopy.RangeOf(document, _view.SelectionRange, _view.CursorOffset) is not { } range)
        {
            return;
        }

        string text = markdown
            ? TextExtraction.Markdown(document, range.Start, range.End)
            : TextExtraction.PlainText(document, range.Start, range.End);
        if (text.Length == 0)
        {
            return;
        }

        if (!Osc52.Fits(text))
        {
            ShowMessage(TooLargeToCopy, error: true);
            return;
        }

        WriteRaw(Osc52.Build(text));
        ShowMessage(markdown ? CopiedMarkdown : CopiedText, error: false);
    }

    /// <summary>
    /// Opens the context menu at a screen position, the mouse's for a right-click and the cursor's for the menu key. The entries are Copy
    /// text and Copy Markdown, Fold or Unfold (labelled by the state of the unit at that position) when a unit is there, and the table items
    /// (see <see cref="TableItems"/>) when the caret is in a table cell. The menu is built for each opening, since its entries depend on the
    /// position.
    /// </summary>
    /// <param name="screen">The position of the menu's top-left corner, on the screen.</param>
    private void ShowContextMenu(Point screen)
    {
        var items = new List<MenuItem>
        {
            new MenuItem(commandText: "Copy text", action: () => CopyView(markdown: false)),
            new MenuItem(commandText: "Copy Markdown", action: () => CopyView(markdown: true)),
        };
        if (_view.FoldUnitAtScreen(screen) is { } unit)
        {
            string label = _view.IsFolded(unit) ? "Unfold" : "Fold";
            items.Add(new MenuItem(commandText: label, action: () => _view.ToggleFold(unit)));
        }

        if (_view.TableCellAtCaret() is { } cell)
        {
            items.AddRange(TableItems(cell));
        }

        ShowMenu(items, screen);
    }

    /// <summary>
    /// Shows a menu at a screen position, in place of the one that is open. It is registered with the application's popovers, which take the
    /// keys while it is open.
    /// </summary>
    /// <param name="items">The entries.</param>
    /// <param name="screen">The position of the menu's top-left corner, on the screen.</param>
    private void ShowMenu(List<MenuItem> items, Point screen)
    {
        DisposeMenu();
        _menu = new PopoverMenu(items);
        App?.Popovers?.Register(_menu);
        _menu.MakeVisible(screen);
    }

    /// <summary>Hides the context menu, if one was opened, and removes it from the application's popovers.</summary>
    private void DisposeMenu()
    {
        if (_menu is { } menu)
        {
            menu.Visible = false;
            App?.Popovers?.DeRegister(menu);
            menu.Dispose();
            _menu = null;
        }
    }

    /// <inheritdoc/>
    void ISearchHost.ShowSearchBar(bool open)
    {
        _frame.Height = Dim.Fill(open ? 3 : 2);
        _search.Bar.Visible = open;
        SetNeedsLayout();
    }

    /// <inheritdoc/>
    void ISearchHost.FocusDocument()
    {
        if (_editing)
        {
            _editor.SetFocus();
        }
        else
        {
            _view.SetFocus();
        }
    }

    /// <inheritdoc/>
    void ISearchHost.ShowMessage(string text, bool error) => ShowMessage(text, error);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopExternalTimer();
            CancelFoldTimer();
            DisposeMenu();
        }

        base.Dispose(disposing);
    }

    /// <summary>A key with no modifier, that types the character.</summary>
    private static bool IsPlain(Key key, char ch) => !key.IsCtrl && !key.IsAlt && key.AsRune.Value == ch;

    /// <summary>
    /// The heading path above the top line: the headings that start at or before the top line's text, outermost first, each heading
    /// replacing the headings of its level or deeper.
    /// </summary>
    internal string HeadingPath()
    {
        int top = _view.TopOffset;
        var path = new List<OutlineEntry>();
        foreach (OutlineEntry entry in _outline)
        {
            if (entry.Start > top)
            {
                break;
            }

            while (path.Count > 0 && path[^1].Level >= entry.Level)
            {
                path.RemoveAt(path.Count - 1);
            }

            path.Add(entry);
        }

        return string.Join(PathSeparator, path.Select(entry => entry.Text));
    }

    /// <summary>
    /// The scroll position: <c>All</c> when every line fits, <c>Top</c> and <c>Bot</c> at the ends, else the percentage of the way down.
    /// </summary>
    /// <param name="top">The top line.</param>
    /// <param name="lineCount">The number of display lines.</param>
    /// <param name="pageLines">The number of lines the view shows.</param>
    internal static string ScrollPosition(int top, int lineCount, int pageLines)
    {
        int max = lineCount - pageLines;
        if (max <= 0)
        {
            return "All";
        }

        if (top <= 0)
        {
            return "Top";
        }

        if (top >= max)
        {
            return "Bot";
        }

        return $"{top * 100 / max}%";
    }

    // Shows the message line's left part (the caret's link or the heading path) and the scroll position in view mode, and the caret's line and
    // column in edit mode, unless a message is shown (the message line keeps the message then).
    private void ShowStatus()
    {
        if (_editing)
        {
            _message.SetStatus(string.Empty, CaretLocation(_editor));
            return;
        }

        _message.SetStatus(LeftStatus(), ScrollPosition(_view.TopLine, _view.LineCount, _view.PageLines));
    }

    /// <summary>The caret's 1-based line and column in the editor's text, as <c>line:col</c>, for the edit-mode status.</summary>
    internal static string CaretLocation(SourceEditor editor)
    {
        var location = editor.Buffer.GetLocation(editor.CaretOffset);
        return $"{location.Line}:{location.Column}";
    }

    /// <summary>Shows a message in place of the status for <see cref="MessageDuration"/>, or until the next key.</summary>
    /// <param name="text">The message.</param>
    /// <param name="error">Whether the message reports an error.</param>
    internal void ShowMessage(string text, bool error)
    {
        _cancelExpiry?.Invoke();
        _message.ShowMessage(text, error);
        _cancelExpiry = _schedule(MessageDuration, ExpireMessage);
    }

    private void ClearMessage()
    {
        if (!_message.HasMessage)
        {
            return;
        }

        _cancelExpiry?.Invoke();
        _cancelExpiry = null;
        _message.ClearMessage();
    }

    private void ExpireMessage()
    {
        _cancelExpiry = null;
        _message.ClearMessage();
    }

    // ----- Quit and switch -----

    /// <summary>Ctrl+Q: the document closes when the unsaved-changes flow lets it, and the window stops.</summary>
    private void Quit()
    {
        if (CloseOrKeep("Quit") == CloseOutcome.Closed)
        {
            Stop();
        }
    }

    /// <summary>
    /// Ctrl+O: the picker is shown, and a file chosen there replaces the document once the unsaved-changes flow lets the document close. A
    /// quit in the picker quits the application.
    /// </summary>
    private void OpenPicker()
    {
        (PickOutcome outcome, OpenedFile? file) = _pickFile();
        switch (outcome)
        {
            case PickOutcome.Chosen when file is not null:
                if (CloseOrKeep("Open") == CloseOutcome.Closed)
                {
                    _back.Clear();
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
    /// The unsaved-changes flow of a quit or a switch: Closed means the document may be left. Otherwise the window stays, and a reload or a
    /// failed save is shown. A new file that the flow saves is recorded in the recent list, as a save from the window is.
    /// </summary>
    /// <param name="action">The title of the question: what the user started.</param>
    private CloseOutcome CloseOrKeep(string action)
    {
        bool wasNew = !_opened.File.Exists;
        CloseResult result = InFlow(() => _controller.CloseFlow(_editor.Buffer.Text, () => AskUnsaved(action), AskExternalChange));
        RefreshDirty();
        switch (result.Outcome)
        {
            case CloseOutcome.Closed when wasNew && _opened.File.Exists:
                _onFirstSave?.Invoke();
                break;
            case CloseOutcome.Reloaded:
                ReplaceBufferWithFile();
                ShowMessage(Reloaded, error: false);
                break;
            case CloseOutcome.Failed:
                ShowMessage(result.Error ?? "the file cannot be saved", error: true);
                break;
        }

        return result.Outcome;
    }

    /// <summary>The question of the unsaved-changes flow. Save is the default, and Esc cancels.</summary>
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

    // ----- Save -----

    /// <summary>
    /// Ctrl+S: writes the text to the file, after the question about a change on disk. The result is shown; a first save of a new file calls
    /// the first-save action.
    /// </summary>
    private void Save()
    {
        bool wasNew = !_opened.File.Exists;
        SaveResult result = InFlow(() => _controller.SaveFlow(_editor.Buffer.Text, AskExternalChange));
        RefreshDirty();
        switch (result.Outcome)
        {
            case SaveOutcome.Saved:
                if (wasNew)
                {
                    _onFirstSave?.Invoke();
                }

                ShowMessage(Saved, error: false);
                break;
            case SaveOutcome.Reloaded:
                ReplaceBufferWithFile();
                ShowMessage(Reloaded, error: false);
                break;
            case SaveOutcome.Failed:
                ShowMessage(result.Error ?? "the file cannot be saved", error: true);
                break;
        }
    }

    /// <summary>The question for a file that changed on disk before a save. Overwrite is the default, and Esc cancels.</summary>
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

    // ----- Text and file -----

    /// <summary>
    /// Replaces the buffer's text with the file's text, as one undo step: only the part that differs is replaced, so the caret, the selection
    /// and the folds (which are anchors in the buffer) keep their offsets where the text around them did not change. In view mode the view is
    /// rendered from the new text now; in edit mode it is rendered when the view is shown again. Returns true when the undo history had steps.
    /// </summary>
    private bool ReplaceBufferWithFile()
    {
        TextDocument buffer = _editor.Buffer;
        bool hadHistory = buffer.UndoStack.CanUndo;
        string current = buffer.Text;
        string target = _opened.File.Text.Text;
        (int start, int removed, string inserted) = DiffMiddle(current, target);

        buffer.UndoStack.StartUndoGroup();
        try
        {
            if (removed > 0 || inserted.Length > 0)
            {
                buffer.Replace(start, removed, inserted);
            }
        }
        finally
        {
            buffer.UndoStack.EndUndoGroup();
        }

        if (!_editing)
        {
            RenderBuffer();
        }

        RefreshDirty();
        ShowStatus();
        return hadHistory;
    }

    /// <summary>
    /// The one replacement that turns <paramref name="current"/> into <paramref name="target"/>: the text before and after the first and last
    /// difference is kept, so the rest of the text does not move. A surrogate pair is never split.
    /// </summary>
    /// <returns>The offset of the replacement, how many characters it removes, and the text it inserts.</returns>
    internal static (int Start, int Removed, string Inserted) DiffMiddle(string current, string target)
    {
        int shorter = Math.Min(current.Length, target.Length);
        int prefix = 0;
        while (prefix < shorter && current[prefix] == target[prefix])
        {
            prefix++;
        }

        if (prefix > 0 && prefix < current.Length && char.IsHighSurrogate(current[prefix - 1]))
        {
            prefix--;
        }

        int suffix = 0;
        while (suffix < shorter - prefix && current[^(suffix + 1)] == target[^(suffix + 1)])
        {
            suffix++;
        }

        if (suffix > 0 && char.IsLowSurrogate(current[^suffix]))
        {
            suffix--;
        }

        int removed = current.Length - prefix - suffix;
        string inserted = target.Substring(prefix, target.Length - prefix - suffix);
        return (prefix, removed, inserted);
    }

    // ----- External changes -----

    /// <summary>
    /// Checks the file for a change on disk, as the timer does, and shows what the controller did. A check that is not idle is skipped
    /// without reading the file, so the change is seen by a later check.
    /// </summary>
    internal void CheckExternalChange()
    {
        if (Stopped || !IsIdle)
        {
            return;
        }

        ExternalResult result = _controller.CheckExternal(() => _dirty, AskReloadOverMine, AskKeepDeleted);
        switch (result.Outcome)
        {
            case ExternalOutcome.Reloaded:
                bool hadHistory = ReplaceBufferWithFile();
                if (hadHistory)
                {
                    ShowMessage(ReloadedFromDisk, error: false);
                }

                break;
            case ExternalOutcome.KeptMine:
                RefreshDirty();
                ShowMessage(KeptMine, error: false);
                break;
            case ExternalOutcome.KeptDeleted:
                RefreshDirty();
                ShowMessage(KeptDeleted, error: false);
                break;
            case ExternalOutcome.Close:
                ReturnToPicker = true;
                Stop();
                break;
            case ExternalOutcome.Failed:
                RefreshDirty();
                ShowMessage(result.Error ?? "the file cannot be read", error: true);
                break;
        }
    }

    /// <summary>
    /// True when no save or close flow runs, no dialog, picker, context menu or table cell field is open, and this window is the application's top runnable.
    /// A dialog or the picker is a runnable on top of it, so none of them is interrupted by a reload.
    /// </summary>
    private bool IsIdle => !_flowing && _cellField is null && _menu is not { Visible: true } && App?.TopRunnableView == this;

    /// <summary>Runs a save or close flow, with the external check held off while it runs.</summary>
    private T InFlow<T>(Func<T> flow)
    {
        _flowing = true;
        try
        {
            return flow();
        }
        finally
        {
            _flowing = false;
        }
    }

    /// <summary>The question for a file changed on disk while the document is dirty. Keep mine is the default; true means Reload.</summary>
    private bool AskReloadOverMine()
    {
        int? choice = Confirm.Choose(Ui, "Changed on disk", $"{FileName} changed on disk. Reload it or keep your changes?", "Keep mine", "Reload");
        return choice == 1;
    }

    /// <summary>The question for a deleted or moved file. Keep is the default; true means Keep.</summary>
    private bool AskKeepDeleted()
    {
        string message = $"{FileName} was deleted or moved on disk.";
        if (_dirty)
        {
            message += " Close discards your unsaved changes.";
        }

        return Confirm.Choose(Ui, "Deleted on disk", message, "Keep", "Close") != 1;
    }

    // The timer runs while the window runs: it starts when the window starts running and stops when it stops.
    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
        {
            _externalTimer = App?.AddTimeout(ExternalCheckInterval, OnExternalTick);
            return;
        }

        StopExternalTimer();
    }

    private bool OnExternalTick()
    {
        if (Stopped)
        {
            return false;
        }

        CheckExternalChange();
        return true;
    }

    private void StopExternalTimer()
    {
        if (_externalTimer is not null)
        {
            App?.RemoveTimeout(_externalTimer);
            _externalTimer = null;
        }
    }

    private void Stop()
    {
        Stopped = true;
        App?.RequestStop(this);
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
