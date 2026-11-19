using System.Drawing;
using System.IO;
using Logvu.Core.Detail;
using Logvu.Core.Store;
using Logvu.Tui;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Logvu.Tests;

/// <summary>
/// The timeline's copy through the controller over a real store: <c>c</c> copies the selected text with its characters noted, or the whole
/// selected event as its detail shows it with everything expanded, and a read that fails says why. The status hints name the copy that <c>c</c>
/// makes.
/// </summary>
/// <remarks>
/// The timeline shows the newest three rows of a 100-event log, so the selected row is <c>r99</c>, at 10:01:39 in UTC: its first five characters
/// are <c>10:01</c>.
/// </remarks>
[Collection("Terminal GUI")]
public sealed class TimelineControllerCopyTests : IDisposable
{
    private readonly LogSet _set = new(100);
    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly HeldDetailSource _details;
    private readonly PostQueue _posted = new();
    private readonly TimelineController _controller;
    private readonly List<string> _written = [];
    private bool _disposed;

    public TimelineControllerCopyTests()
    {
        _app = Application.Create();
        _app.Init("ansi");
        _store = new LogStore(_set.Config);
        _details = new HeldDetailSource(new LogStoreDetailSource(_store));
        _controller = new TimelineController(_app, _store, _details, _set.Config, _posted.Post);
        _controller.WriteRaw = _written.Add;
        _controller.Start();
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count > 0, "the first rows");
        _controller.Model.Resize(3);
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 3, "three visible rows");

        // A laid-out timeline: the header and three rows, 60 columns wide, which the controller tests do not draw.
        _controller.Timeline.Viewport = new Rectangle(0, 0, 60, 4);
    }

    [Fact]
    public void C_with_a_selection_writes_the_selected_text_and_notes_its_characters()
    {
        Select(fromX: 1, toX: 6, y: 3);

        _controller.Timeline.HandleKey(Key.C);

        // The drag to column 6 ends on the colon at offset 5, which is included: "10:01:" is six characters.
        Assert.Equal(new[] { Osc52.Build("10:01:") }, _written);
        Assert.Contains("Copied 6 characters", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void C_without_a_selection_copies_the_whole_event_as_its_detail_shows_it_expanded()
    {
        Assert.True(_controller.Timeline.HandleKey(Key.C));

        HeldDetailSource.HeldRead read = Assert.Single(_details.Reads);
        Assert.Equal(_controller.Model.SelectedRow!.Key, read.Key);
        Assert.Empty(_written);

        read.Complete(DetailFixtures.Json(DetailFixtures.JsonEvent));
        _posted.PumpUntil(() => _written.Count > 0, "the copy");

        DetailDocument document = DetailDocument.Build(DetailFixtures.Json(DetailFixtures.JsonEvent), _set.Config, DetailOptions.Default);
        string expected = CopyText.Unindent(string.Join('\n', document.FullLines.Select(line => line.Text)));
        Assert.Equal(new[] { Osc52.Build(expected) }, _written);
        Assert.Contains("\n", expected, StringComparison.Ordinal);
        Assert.Contains($"Copied {expected.Length} characters", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void C_on_an_event_whose_read_fails_shows_the_reason_and_copies_nothing()
    {
        _controller.Timeline.HandleKey(Key.C);
        HeldDetailSource.HeldRead read = _details.Reads.Single();

        read.Complete(new EventDetail.Unavailable(read.Key, "the file is gone"));
        _posted.PumpUntil(() => _controller.StatusLeft.Contains("the file is gone", StringComparison.Ordinal), "the notice");

        Assert.Empty(_written);
    }

    [Fact]
    public void C_on_an_event_whose_read_throws_shows_the_message_and_copies_nothing()
    {
        _controller.Timeline.HandleKey(Key.C);
        HeldDetailSource.HeldRead read = _details.Reads.Single();

        read.Completion.TrySetException(new IOException("disk gone"));
        _posted.PumpUntil(() => _controller.StatusLeft.Contains("disk gone", StringComparison.Ordinal), "the notice");

        Assert.Empty(_written);
    }

    [Fact]
    public void The_hints_name_the_copy_that_c_makes_and_switch_with_the_selection()
    {
        Assert.Contains("c copy event", _controller.StatusRight, StringComparison.Ordinal);

        Select(fromX: 1, toX: 6, y: 3);
        Assert.Contains("c copy selection", _controller.StatusRight, StringComparison.Ordinal);
        Assert.DoesNotContain("c copy event", _controller.StatusRight, StringComparison.Ordinal);

        Assert.True(_controller.Timeline.HandleKey(Key.Esc));
        Assert.Contains("c copy event", _controller.StatusRight, StringComparison.Ordinal);
    }

    /// <summary>A press at <paramref name="fromX"/> on the screen row <paramref name="y"/>, a drag to <paramref name="toX"/> and a release.</summary>
    private void Select(int fromX, int toX, int y)
    {
        _controller.Timeline.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new(fromX, y) });
        _controller.Timeline.HandleMouse(new Mouse { Flags = MouseFlags.PositionReport, Position = new(toX, y) });
        _controller.Timeline.HandleMouse(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new(toX, y) });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.Dispose();
        _store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _app.Dispose();
        _set.Dispose();
    }
}
