using System.Collections.Immutable;
using Mdedit.Core.Render;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

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
internal sealed class MainWindow : Window
{
    /// <summary>How long a message stays on the message line when no key clears it.</summary>
    internal static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(4);

    /// <summary>The separator between the headings of the heading path.</summary>
    internal const string PathSeparator = " › ";

    private readonly Func<(PickOutcome Outcome, OpenedFile? File)> _pickFile;
    private readonly Func<TimeSpan, Action, Action> _schedule;
    private readonly OpenedFile _opened;
    private readonly ImmutableArray<OutlineEntry> _outline;
    private readonly FrameView _frame;
    private readonly RenderedView _view;
    private readonly MessageLine _message;
    private readonly HintsLine _hints;
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

        RenderDocument document = RenderDocument.Parse(file.File.Text.Text);
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
        Add(_frame, _message, _hints);

        _view.PositionChanged += ShowStatus;
        _view.KeyPressed += ClearMessage;
        _hints.Show(MdeditHints.View, MdeditHints.Quit);
        ShowStatus();
        _view.SetFocus();
    }

    /// <summary>The rendered view, for the tests.</summary>
    internal RenderedView View => _view;

    /// <summary>The message line, for the tests.</summary>
    internal MessageLine Message => _message;

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
            return true;
        }

        return base.OnKeyDown(key);
    }

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
