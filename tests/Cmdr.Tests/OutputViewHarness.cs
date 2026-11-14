using System.Reflection;
using Cmdr.Core.Output;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Cmdr.Tests;

/// <summary>
/// An <see cref="OutputView"/> of a sample command, sized to a 4 × 3 viewport and driven through its private input methods
/// (the mouse goes through the protected <c>OnMouseEvent</c>). Reads the private scroll state by reflection.
/// </summary>
internal sealed class OutputViewHarness : IDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly TempSession _temp;
    private readonly MainController _controller;

    private OutputViewHarness(TempSession temp, MainController controller, OutputView view, CommandEntry entry)
    {
        _temp = temp;
        _controller = controller;
        View = view;
        Entry = entry;
    }

    public OutputView View { get; }

    public CommandEntry Entry { get; }

    /// <summary>Creates the view for a sample command whose output is <paramref name="lines"/>, with a 4 × 3 viewport.</summary>
    public static OutputViewHarness Create(IApplication app, params string[] lines)
    {
        TempSession temp = TempSession.Sample();
        var controller = new MainController(app, temp.Session);
        CommandEntry entry = temp.Session.AllEntries.First();
        foreach (string line in lines)
        {
            entry.Process.Output.WriteSystem(line);
        }

        var view = new OutputView(controller);
        view.Width = 4;
        view.Height = 3;
        view.Show(entry);
        view.Layout();
        return new OutputViewHarness(temp, controller, view, entry);
    }

    public bool Following => View.Following;

    /// <summary>The stored top (the one used once following is paused).</summary>
    public ScrollPosition Top => (ScrollPosition)Field("_top")!;

    /// <summary>The top the view actually draws from: the last page while following, otherwise the stored top.</summary>
    public ScrollPosition EffectiveTop
    {
        get
        {
            object geometry = Measure();
            return (ScrollPosition)Method("TopPosition", geometry)!;
        }
    }

    /// <summary>The absolute number of the cursor line.</summary>
    public long CursorLine
    {
        get
        {
            object geometry = Measure();
            return (long)Method("CursorLine", geometry)!;
        }
    }

    /// <summary>The command's output pipeline, for writing output in a test.</summary>
    public CommandOutput Output => Entry.Process.Output;

    /// <summary>The selection's anchor and active points, or null when there is no selection. Read after the view checked its pins, as a draw would.</summary>
    public (SelectionPoint Anchor, SelectionPoint Active)? Selection
    {
        get
        {
            Measure();
            object? state = Field("_selection");
            if (state is null)
            {
                return null;
            }

            Type type = state.GetType();
            var anchor = (SelectionPoint)type.GetProperty("Anchor", BindingFlags.Instance | BindingFlags.Public)!.GetValue(state)!;
            var active = (SelectionPoint)type.GetProperty("Active", BindingFlags.Instance | BindingFlags.Public)!.GetValue(state)!;
            return (anchor, active);
        }
    }

    /// <summary>The text of the selection, extracted from the buffer lines as copy does. Null when there is no selection.</summary>
    public string? SelectedText()
    {
        (SelectionPoint anchor, SelectionPoint active)? selection = Selection;
        if (selection is null)
        {
            return null;
        }

        object state = Field("_selection")!;
        bool lineMode = (bool)state.GetType().GetProperty("LineMode", BindingFlags.Instance | BindingFlags.Public)!.GetValue(state)!;
        OutputBuffer buffer = Entry.Process.Output.Buffer;
        var texts = buffer.GetLines(0, buffer.Count).Select(l => l.PlainText).ToList();
        return SelectionText.Extract(texts, buffer.FirstLineNumber,
            SelectionRange.Between(selection.Value.anchor, selection.Value.active, lineMode));
    }

    /// <summary>Resizes the view and lays it out again.</summary>
    public void Resize(int width, int height)
    {
        View.Width = width;
        View.Height = height;
        View.Layout();
    }

    /// <summary>Runs the view's tick, as the controller does every 100 ms.</summary>
    public void Tick() => View.Tick();

    /// <summary>One wheel event: the view scrolls <c>WheelLines</c> (3) screen rows per event, up for negative, down for positive.</summary>
    public void Wheel(int delta) => Send(new Mouse { Flags = delta < 0 ? MouseFlags.WheeledUp : MouseFlags.WheeledDown });

    public void Press(int x, int y) => Send(Position(MouseFlags.LeftButtonPressed, x, y));

    // While a drag is in progress every event that is not a release is a motion, so the pressed flag stands in for motion.
    public void Drag(int x, int y) => Send(Position(MouseFlags.LeftButtonPressed, x, y));

    public void Release(int x = 0, int y = 0) => Send(Position(MouseFlags.LeftButtonReleased, x, y));

    public void CursorDown() => Method("MoveCursor", 1, false, false);

    public void Home() => Method("MoveToFirstLine");

    public void End() => Method("ResumeFollowing");

    public void Dispose()
    {
        View.Dispose();
        _controller.Dispose();
        _temp.Dispose();
    }

    private static Mouse Position(MouseFlags flags, int x, int y) => new()
    {
        Flags = flags,
        Position = new(x, y),
    };

    private void Send(Mouse mouse) => Method("OnMouseEvent", mouse);

    private object Measure() => Method("Measure", Entry.Process.Output.Buffer)!;

    private object? Method(string name, params object[] args)
    {
        MethodInfo method = typeof(OutputView).GetMethod(name, Members)!;
        return method.Invoke(View, args);
    }

    private object? Field(string name) => typeof(OutputView).GetField(name, Members)!.GetValue(View);
}
