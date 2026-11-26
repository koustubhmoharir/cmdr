using System.Collections.Immutable;
using Mdedit.Core.Render;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Point = System.Drawing.Point;

namespace Mdedit.Tui;

/// <summary>
/// The borderless main window: the rendered document in a rounded frame whose title is the file name, the message line (the heading
/// path and the scroll position, or a message in their place), and the key hints on the last row.
/// </summary>
/// <remarks>
/// <para>
/// <b>Quit and switch.</b> Ctrl+Q stops the window. Ctrl+O shows the picker over the document, and a file chosen there replaces the
/// document when the window stops (see <see cref="SwitchTo"/>). Esc is swallowed: the window has nothing to go back to.
/// </para>
/// <para>
/// <b>Messages.</b> A message stays for <see cref="MessageDuration"/> or until the next key, whichever is first. The delay runs through
/// the scheduler, so tests drive the expiry.
/// </para>
/// </remarks>
internal sealed class MainWindow : Window, ISearchHost
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

    private readonly Func<(PickOutcome Outcome, OpenedFile? File)> _pickFile;
    private readonly Func<TimeSpan, Action, Action> _schedule;
    private readonly OpenedFile _opened;
    private readonly ImmutableArray<OutlineEntry> _outline;
    private readonly FrameView _frame;
    private readonly RenderedView _view;
    private readonly MessageLine _message;
    private readonly HintsLine _hints;
    private readonly ViewSearch _search;
    private PopoverMenu? _menu;
    private Action? _cancelExpiry;

    /// <summary>
    /// Creates the window over a file, with the view at the document's start.
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

        // No outer frame: the rendered view's frame, the message line and the hints fill the window.
        BorderStyle = LineStyle.None;

        RenderDocument document = RenderDocument.Parse(file.File.Text.Text, TextMateColorizer.Shared);
        _outline = Outline.Of(document);
        _view = new RenderedView(document) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _frame = new FrameView
        {
            BorderStyle = LineStyle.Rounded,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            Title = FileName,
        };
        _frame.Add(_view);

        _message = new MessageLine { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill(), Height = 1 };
        _hints = new HintsLine { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
        _search = new ViewSearch(this, _view);

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
        _hints.Show(MdeditHints.View, MdeditHints.Quit);
        ShowStatus();
        _view.SetFocus();
    }

    /// <summary>The rendered view, for the tests.</summary>
    internal RenderedView View => _view;

    /// <summary>The message line, for the tests.</summary>
    internal MessageLine Message => _message;

    /// <summary>The view search, for the tests.</summary>
    internal ViewSearch Search => _search;

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

    /// <summary>The name of the file, as the frame's title shows it.</summary>
    private string FileName => Path.GetFileName(_opened.Path);

    /// <summary>
    /// The window's keys that the view does not take: Ctrl+Q quits, Ctrl+O opens the picker, and Esc is swallowed.
    /// </summary>
    /// <inheritdoc/>
    protected override bool OnKeyDown(Key key)
    {
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
            // Esc closes an open search (its bar is not focused here, or Esc would have been taken by it). Otherwise it is swallowed.
            _search.Close();
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
    /// text and Copy Markdown, and Fold or Unfold (labelled by the state of the unit at that position) when a unit is there. The menu is
    /// built for each opening, since its entries depend on the position, and it is registered with the application's popovers, which take
    /// the keys while it is open.
    /// </summary>
    /// <param name="screen">The position of the menu's top-left corner, on the screen.</param>
    private void ShowContextMenu(Point screen)
    {
        DisposeMenu();
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
    void ISearchHost.FocusView() => _view.SetFocus();

    /// <inheritdoc/>
    void ISearchHost.ShowMessage(string text, bool error) => ShowMessage(text, error);

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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

    // Shows the heading path and the scroll position, unless a message is shown (the message line keeps the message then).
    private void ShowStatus() => _message.SetStatus(HeadingPath(), ScrollPosition(_view.TopLine, _view.LineCount, _view.PageLines));

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

    private void Quit() => Stop();

    private void OpenPicker()
    {
        (PickOutcome outcome, OpenedFile? file) = _pickFile();
        switch (outcome)
        {
            case PickOutcome.Chosen when file is not null:
                SwitchTo = file;
                Stop();
                break;
            case PickOutcome.Quit:
                Quit();
                break;
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
