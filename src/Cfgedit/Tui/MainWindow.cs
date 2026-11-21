using Shared.Tui;
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
/// The borderless main window: the tree in a frame whose title is the file name and its format, the detail line of the focused part,
/// and the key hints on the last row. Ctrl+O shows the picker over the document; a file chosen there replaces the document when the
/// window stops (see <see cref="SwitchTo"/>). Ctrl+Q stops the window with no switch.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly Func<(PickOutcome Outcome, OpenedFile? File)> _pickFile;
    private readonly TreeView _tree;
    private readonly DetailLine _detail;
    private readonly HintsLine _hints;

    /// <summary>Creates the window over a document, with the tree's cursor at its start.</summary>
    /// <param name="file">The document to show.</param>
    /// <param name="pickFile">Shows the picker for Ctrl+O, and reports what it did.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public MainWindow(OpenedFile file, Func<(PickOutcome Outcome, OpenedFile? File)> pickFile)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(pickFile);

        _pickFile = pickFile;

        // No outer frame: the tree's frame, the detail line and the hints fill the window.
        BorderStyle = LineStyle.None;

        _tree = new TreeView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        var frame = new FrameView
        {
            Title = $"{Path.GetFileName(file.Path)} [{file.Document.Format.Name}]",
            BorderStyle = LineStyle.Rounded,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
        };
        frame.Add(_tree);

        _detail = new DetailLine { X = 0, Y = Pos.AnchorEnd(2), Width = Dim.Fill(), Height = 1 };
        _hints = new HintsLine { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };
        Add(frame, _detail, _hints);

        _tree.CursorChanged += ShowDetail;
        _tree.SetDocument(file.Document, null);
        _hints.Show(CfgeditHints.Tree);
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

    /// <inheritdoc/>
    /// <remarks>
    /// Esc is swallowed: Terminal.Gui's default Esc binding would close the window, and the window has nothing to go back to.
    /// </remarks>
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q.WithCtrl)
        {
            Stop();
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

    private void ShowDetail() => _detail.Show(_tree.Document, _tree.CursorNode, _tree.CursorDocPart);

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
                Stop();
                break;
        }
    }

    private void Stop() => App?.RequestStop(this);
}
