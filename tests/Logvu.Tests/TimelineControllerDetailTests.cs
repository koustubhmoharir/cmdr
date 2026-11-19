using Logvu.Core.Store;
using Logvu.Core.Timeline;
using Logvu.Tui;
using Shared.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Logvu.Tests;

/// <summary>
/// The controller's detail flow over a real store and a held detail read: Enter opens and pauses, Esc restores the timeline as it was,
/// store updates keep reaching the model, and a read that arrives after the detail was closed is ignored.
/// </summary>
[Collection("Terminal GUI")]
public sealed class TimelineControllerDetailTests : IDisposable
{
    private readonly LogSet _set = new(100);
    private readonly IApplication _app;
    private readonly LogStore _store;
    private readonly HeldDetailSource _details;
    private readonly PostQueue _posted = new();
    private readonly TimelineController _controller;
    private bool _disposed;

    public TimelineControllerDetailTests()
    {
        _app = Application.Create();
        _app.Init("ansi");
        _store = new LogStore(_set.Config);
        _details = new HeldDetailSource(new LogStoreDetailSource(_store));
        _controller = new TimelineController(_app, _store, _details, _set.Config, _posted.Post);
        _controller.Start();
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count > 0, "the first rows");
        _controller.Model.Resize(3);
        _posted.PumpUntil(() => _controller.Model.VisibleRows.Count == 3, "three visible rows");
    }

    [Fact]
    public void Enter_pauses_the_timeline_and_shows_loading_until_the_detail_arrives()
    {
        _controller.Timeline.HandleKey(Key.Enter);

        Assert.Same(_controller.Detail, _controller.Window.Content);
        Assert.Equal(TimelineMode.Paused, _controller.Model.Mode);
        Assert.Equal("Loading…", _controller.Detail!.Placeholder);
        Assert.Null(_controller.Detail.Document);
        Assert.Equal("DETAIL", _controller.StatusLeft);

        HeldDetailSource.HeldRead read = Assert.Single(_details.Reads);
        Assert.Equal(_controller.Model.SelectedRow!.Key, read.Key);

        read.Complete(DetailFixtures.Text(DetailFixtures.StackEvent));
        _posted.PumpUntil(() => _controller.Detail!.Document is not null, "the detail");
        Assert.Equal("DETAIL · app.log", _controller.StatusLeft);
    }

    [Fact]
    public void Right_and_l_open_the_selected_event_like_enter()
    {
        Assert.True(_controller.Timeline.HandleKey(Key.CursorRight));

        Assert.Same(_controller.Detail, _controller.Window.Content);
        Assert.Single(_details.Reads);
        Assert.Equal(_controller.Model.SelectedRow!.Key, _details.Reads[0].Key);

        Assert.True(_controller.Detail!.HandleKey(Key.Esc));
        _posted.Pump();
        Assert.True(_controller.Timeline.HandleKey(Key.L));

        Assert.Same(_controller.Detail, _controller.Window.Content);
        Assert.Equal(2, _details.Reads.Count);
    }

    [Fact]
    public void Left_and_h_close_the_detail_like_escape_while_it_loads()
    {
        _controller.Timeline.HandleKey(Key.Enter);
        HeldDetailSource.HeldRead read = _details.Reads.Single();

        Assert.True(_controller.Detail!.HandleKey(Key.CursorLeft));

        Assert.Null(_controller.Detail);
        Assert.Same(_controller.Timeline, _controller.Window.Content);
        Assert.True(read.Token.IsCancellationRequested);

        _controller.Timeline.HandleKey(Key.Enter);
        HeldDetailSource.HeldRead second = _details.Reads[1];

        Assert.True(_controller.Detail!.HandleKey(Key.H));

        Assert.Null(_controller.Detail);
        Assert.Same(_controller.Timeline, _controller.Window.Content);
        Assert.True(second.Token.IsCancellationRequested);
    }

    [Fact]
    public void Escape_restores_the_timeline_with_the_same_rows_and_selection()
    {
        _controller.Timeline.HandleKey(Key.CursorUp);
        _controller.Timeline.HandleKey(Key.CursorUp);
        List<RowKey> rows = VisibleKeys();
        RowKey selected = _controller.Model.SelectedRow!.Key;

        _controller.Timeline.HandleKey(Key.Enter);
        _controller.Detail!.HandleKey(Key.Esc);
        _posted.Pump();

        Assert.Same(_controller.Timeline, _controller.Window.Content);
        Assert.Null(_controller.Detail);
        Assert.Equal(rows, VisibleKeys());
        Assert.Equal(selected, _controller.Model.SelectedRow!.Key);
        Assert.Equal(TimelineMode.Paused, _controller.Model.Mode);
        Assert.StartsWith("PAUSED", _controller.StatusLeft, StringComparison.Ordinal);
        Assert.True(_details.Reads.Single().Token.IsCancellationRequested);
    }

    [Fact]
    public void Leaving_before_the_detail_arrives_cancels_the_read_and_ignores_the_late_result()
    {
        _controller.Timeline.HandleKey(Key.Enter);
        HeldDetailSource.HeldRead read = _details.Reads.Single();

        _controller.Detail!.HandleKey(Key.Esc);
        Assert.True(read.Token.IsCancellationRequested);

        int posted = _posted.Posted;
        read.Complete(DetailFixtures.Text(DetailFixtures.StackEvent));
        _posted.PumpUntil(() => _posted.Posted > posted, "the late result to reach the UI thread");
        _posted.Pump();

        Assert.Null(_controller.Detail);
        Assert.Same(_controller.Timeline, _controller.Window.Content);
        Assert.StartsWith("PAUSED", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void Store_updates_keep_reaching_the_model_while_the_detail_is_open()
    {
        _controller.Timeline.HandleKey(Key.Enter);

        _set.Append(100);
        _posted.PumpUntil(() => _controller.Model.NewCount > 0, "the appended event to arrive");

        Assert.Same(_controller.Detail, _controller.Window.Content);
        Assert.Equal(TimelineMode.Paused, _controller.Model.Mode);
        Assert.StartsWith("DETAIL", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_while_a_detail_is_open_does_not_start_a_second_read()
    {
        _controller.Timeline.HandleKey(Key.Enter);

        _controller.Timeline.HandleKey(Key.Enter);

        Assert.Single(_details.Reads);
    }

    [Fact]
    public void C_in_the_detail_writes_the_clipboard_sequence_of_the_cursor_line_and_notes_its_characters()
    {
        OpenJsonDetail();
        var written = new List<string>();
        _controller.WriteRaw = written.Add;

        Assert.True(_controller.Detail!.HandleKey(Key.C));

        const string expected = "Timestamp: \"2026-11-18T10:20:31.007Z\"";
        Assert.Equal(new[] { Osc52.Build(expected) }, written);
        Assert.Contains($"Copied {expected.Length} characters", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void A_copy_too_large_for_the_clipboard_is_refused_with_a_notice()
    {
        var written = new List<string>();
        _controller.WriteRaw = written.Add;

        _controller.CopyToClipboard(new string('x', Osc52.MaxBytes + 1), "Copied");

        Assert.Empty(written);
        Assert.Contains("Selection too large to copy", _controller.StatusLeft, StringComparison.Ordinal);
    }

    [Fact]
    public void The_detail_hint_names_the_copy_that_c_makes_and_switches_with_the_selection()
    {
        OpenJsonDetail();
        DetailView detail = _controller.Detail!;
        Assert.Contains("c copy line", _controller.StatusRight, StringComparison.Ordinal);

        Assert.True(detail.HandleKey(Key.CursorDown.WithShift));
        Assert.Contains("c copy selection", _controller.StatusRight, StringComparison.Ordinal);
        Assert.DoesNotContain("c copy line", _controller.StatusRight, StringComparison.Ordinal);

        Assert.True(detail.HandleKey(Key.Esc));
        Assert.Contains("c copy line", _controller.StatusRight, StringComparison.Ordinal);
    }

    /// <summary>Opens the detail of the selected row with the Serilog JSON event, and waits until it is loaded.</summary>
    private void OpenJsonDetail()
    {
        _controller.Timeline.HandleKey(Key.Enter);
        _details.Reads.Single().Complete(DetailFixtures.Json(DetailFixtures.JsonEvent));
        _posted.PumpUntil(() => _controller.Detail!.Document is not null, "the detail");
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

    private List<RowKey> VisibleKeys() => _controller.Model.VisibleRows.Select(row => row.Key).ToList();
}
