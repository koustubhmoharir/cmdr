using Logvu.Core.Parsing;
using Logvu.Core.Reading;
using Logvu.Core.Store;
using Logvu.Core.Timeline;

namespace Logvu.Core.Tests.Timeline;

public sealed class TimelineModelTests
{
    [Fact]
    public void ThreeRowViewportFollowsTheDesignAcceptance()
    {
        var model = Model(Store());
        model.Start(3);
        Assert.Equal("r97 r98 [r99]", Render(model));
        Assert.Equal(TimelineMode.Follow, model.Mode);

        model.Execute(TimelineCommand.Up);
        Assert.Equal("r97 [r98] r99", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);

        model.Execute(TimelineCommand.ShiftUp);
        Assert.Equal("r96 r97 [r98]", Render(model));
        model.Execute(TimelineCommand.ShiftUp);
        Assert.Equal("r95 r96 [r97]", Render(model));
    }

    [Fact]
    public void ShiftDownMirrorsShiftUpAndClampsTheSelection()
    {
        var model = Model(Store());
        model.Start(3);
        Seek(model, 50);
        model.Execute(TimelineCommand.Down);
        Assert.Equal("r50 [r51] r52", Render(model));

        model.Execute(TimelineCommand.ShiftDown);
        Assert.Equal("[r51] r52 r53", Render(model));
        model.Execute(TimelineCommand.ShiftDown);
        Assert.Equal("[r52] r53 r54", Render(model));
    }

    [Fact]
    public void ArrowsAtThePageEdgesShiftTheViewportByOneRow()
    {
        var model = Model(Store());
        model.Start(3);
        Seek(model, 50);
        Assert.Equal("[r50] r51 r52", Render(model));
        model.Execute(TimelineCommand.Up);
        Assert.Equal("[r49] r50 r51", Render(model));

        var bottom = Model(Store());
        bottom.Start(3);
        Seek(bottom, 50);
        bottom.Execute(TimelineCommand.Down);
        bottom.Execute(TimelineCommand.Down);
        Assert.Equal("r50 r51 [r52]", Render(bottom));
        bottom.Execute(TimelineCommand.Down);
        Assert.Equal("r51 r52 [r53]", Render(bottom));
    }

    [Fact]
    public void SingleRowViewportMovesOneRowPerCommand()
    {
        var model = Model(Store());
        model.Start(1);
        Assert.Equal("[r99]", Render(model));

        model.Execute(TimelineCommand.Up);
        Assert.Equal("[r98]", Render(model));
        model.Execute(TimelineCommand.Down);
        Assert.Equal("[r99]", Render(model));
        model.Execute(TimelineCommand.ShiftUp);
        Assert.Equal("[r98]", Render(model));
        model.Execute(TimelineCommand.ShiftDown);
        Assert.Equal("[r99]", Render(model));
        model.Execute(TimelineCommand.PageUp);
        Assert.Equal("[r98]", Render(model));
        model.Execute(TimelineCommand.PageDown);
        Assert.Equal("[r99]", Render(model));
    }

    [Fact]
    public void MovementStopsAtTheStartOfHistory()
    {
        var model = Model(Store());
        model.Start(3);
        Seek(model, 0);
        Assert.Equal("[r0] r1 r2", Render(model));
        Assert.True(model.AtStart);

        model.Execute(TimelineCommand.Up);
        Assert.Equal("[r0] r1 r2", Render(model));
        model.Execute(TimelineCommand.ShiftUp);
        Assert.Equal("[r0] r1 r2", Render(model));
        model.Execute(TimelineCommand.PageUp);
        Assert.Equal("[r0] r1 r2", Render(model));
    }

    [Fact]
    public void MovementStopsAtTheNewestRowAndScrollingBackStaysPaused()
    {
        var model = Model(Store());
        model.Start(3);
        model.Execute(TimelineCommand.Down);
        Assert.Equal("r97 r98 [r99]", Render(model));
        Assert.True(model.AtEnd);

        model.Execute(TimelineCommand.Down);
        model.Execute(TimelineCommand.ShiftDown);
        model.Execute(TimelineCommand.PageDown);
        Assert.Equal("r97 r98 [r99]", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void PageUpKeepsTheScreenRowAndSelectsTheFirstRowAtTheStart()
    {
        var model = Model(Store());
        model.Start(3);
        Seek(model, 50);
        model.Execute(TimelineCommand.Down);
        Assert.Equal("r50 [r51] r52", Render(model));
        model.Execute(TimelineCommand.PageUp);
        Assert.Equal("r47 [r48] r49", Render(model));

        var start = Model(Store());
        start.Start(3);
        Seek(start, 1);
        start.Execute(TimelineCommand.Down);
        Assert.Equal("r1 [r2] r3", Render(start));
        start.Execute(TimelineCommand.PageUp);
        Assert.Equal("r0 [r1] r2", Render(start));
        start.Execute(TimelineCommand.PageUp);
        Assert.Equal("[r0] r1 r2", Render(start));
    }

    [Fact]
    public void PageDownKeepsTheScreenRowAndSelectsTheLastRowAtTheEnd()
    {
        var model = Model(Store());
        model.Start(3);
        Seek(model, 96);
        model.Execute(TimelineCommand.Down);
        Assert.Equal("r96 [r97] r98", Render(model));
        model.Execute(TimelineCommand.PageDown);
        Assert.Equal("r97 [r98] r99", Render(model));
        model.Execute(TimelineCommand.PageDown);
        Assert.Equal("r97 r98 [r99]", Render(model));
    }

    [Fact]
    public void FewerRowsThanTheHeightShowsAllOfThem()
    {
        var model = Model(Store(2));
        model.Start(5);

        Assert.Equal("r0 [r1]", Render(model));
        Assert.True(model.AtStart);
        Assert.True(model.AtEnd);
        Assert.Equal(TimelineMode.Follow, model.Mode);
    }

    [Fact]
    public void FollowArrivalsAppendAndKeepTheNewestSelectedAtTheBottom()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);

        Arrive(model, source, 100, 101);

        Assert.Equal("r99 r100 [r101]", Render(model));
        Assert.Equal(TimelineMode.Follow, model.Mode);
        Assert.Equal(0, model.NewCount);
    }

    [Fact]
    public void PausedArrivalsAreCountedAndAppendedOnlyAtTheEnd()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        model.Execute(TimelineCommand.Up);
        Assert.Equal("r97 [r98] r99", Render(model));
        Assert.True(model.AtEnd);

        Arrive(model, source, 100);
        Assert.Equal("r97 [r98] r99", Render(model));
        Assert.Equal(1, model.NewCount);

        model.Arrive([], [Row(99, "r99+")]);
        Assert.Equal("r97 [r98] r99+", Render(model));
        Assert.Equal(1, model.NewCount);

        model.Execute(TimelineCommand.Down);
        model.Execute(TimelineCommand.Down);
        Assert.Equal("r98 r99+ [r100]", Render(model));
        Assert.Equal(1, model.NewCount);
    }

    [Fact]
    public void PausedArrivalsOnlyCountWhenTheWindowIsNotAtTheEnd()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        Seek(model, 50);
        Assert.False(model.AtEnd);
        string before = Render(model);

        Arrive(model, source, 100);

        Assert.Equal(before, Render(model));
        Assert.Equal(1, model.NewCount);
        Assert.False(model.AtEnd);
    }

    [Fact]
    public void EndResumesFollowAndClearsTheCount()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        Seek(model, 50);
        Arrive(model, source, 100, 101, 102, 103, 104);
        Assert.Equal(5, model.NewCount);

        model.Execute(TimelineCommand.End);

        Assert.Equal(TimelineMode.Follow, model.Mode);
        Assert.Equal(0, model.NewCount);
        Assert.Equal("r102 r103 [r104]", Render(model));
    }

    [Fact]
    public void TogglePauseTwiceReturnsToTheNewestRow()
    {
        var model = Model(Store());
        model.Start(3);

        model.Execute(TimelineCommand.TogglePause);
        Assert.Equal(TimelineMode.Paused, model.Mode);
        Assert.Equal("r97 r98 [r99]", Render(model));

        model.Execute(TimelineCommand.TogglePause);
        Assert.Equal(TimelineMode.Follow, model.Mode);
        Assert.Equal("r97 r98 [r99]", Render(model));
    }

    [Theory]
    [InlineData(TimelineCommand.Up)]
    [InlineData(TimelineCommand.Down)]
    [InlineData(TimelineCommand.ShiftUp)]
    [InlineData(TimelineCommand.ShiftDown)]
    [InlineData(TimelineCommand.PageUp)]
    [InlineData(TimelineCommand.PageDown)]
    public void ManualNavigationPausesFollow(TimelineCommand command)
    {
        var model = Model(Store());
        model.Start(3);

        model.Execute(command);

        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void CommandsNeedingUnloadedRowsWaitForTheLoadInOrder()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        source.Held.Add(ReadKind.Older);
        model.Execute(TimelineCommand.TogglePause);
        for (int i = 0; i < 11; i++)
        {
            model.Execute(TimelineCommand.Up);
        }

        Assert.Equal("[r88] r89 r90", Render(model));
        Assert.Equal(1, source.Waiting(ReadKind.Older));

        model.Execute(TimelineCommand.Up);
        model.Execute(TimelineCommand.Up);
        model.Execute(TimelineCommand.Up);
        Assert.Equal("[r88] r89 r90", Render(model));

        source.CompleteOldest(ReadKind.Older);

        Assert.Equal("[r85] r86 r87", Render(model));
        Assert.False(model.IsLoading);
    }

    [Fact]
    public void ResultOfASupersededLoadIsDiscarded()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        source.Held.Add(ReadKind.Older);
        source.Held.Add(ReadKind.Newer);
        model.Execute(TimelineCommand.TogglePause);
        for (int i = 0; i < 11; i++)
        {
            model.Execute(TimelineCommand.Up);
        }

        // The stale older load is outstanding, and the reload's newer load is the one that seeds the window.
        model.Reset();
        Assert.Equal(1, source.Waiting(ReadKind.Older));
        Assert.Equal(1, source.Waiting(ReadKind.Newer));
        Assert.Equal(string.Empty, Render(model));

        source.CompleteOldest(ReadKind.Older);
        Assert.Equal(string.Empty, Render(model));
        Assert.True(model.IsLoading);

        source.CompleteOldest(ReadKind.Newer);
        Assert.Equal("[r88] r89 r90", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void ReloadKeepsTheSelectionOnTheOldestRowOfHistory()
    {
        var model = Model(Store());
        model.Start(3);
        Seek(model, 0);
        Assert.Equal("[r0] r1 r2", Render(model));

        model.Reset();

        Assert.Equal("[r0] r1 r2", Render(model));
        Assert.True(model.AtStart);
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void SetFilterThatExcludesTheSelectedRowSelectsTheNearestFollowingMatch()
    {
        var model = Model(Store());
        model.Start(3);
        model.Execute(TimelineCommand.Up);
        Assert.Equal("r97 [r98] r99", Render(model));

        model.SetFilter(new OddRowFilter());

        Assert.Equal("r95 r97 [r99]", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void MarginKeepsRowsLoadedAndTrimsTheWindow()
    {
        var model = Model(Store(1000));
        model.Start(10);

        for (int i = 0; i < 20; i++)
        {
            model.Execute(TimelineCommand.PageUp);
        }

        Assert.False(model.AtEnd);
        Assert.True(model.LoadedCount <= 2 * 8 * 10 + 10, $"Window holds {model.LoadedCount} rows.");
        Assert.Equal("r799", model.SelectedRow!.Preview);

        for (int i = 0; i < 20; i++)
        {
            model.Execute(TimelineCommand.PageDown);
        }

        Assert.True(model.AtEnd);
        Assert.Equal("r999", model.SelectedRow!.Preview);
    }

    [Fact]
    public void ResizeKeepsTheSelectedRowAtItsScreenRow()
    {
        var model = Model(Store());
        model.Start(3);
        Seek(model, 50);
        model.Execute(TimelineCommand.Down);
        Assert.Equal("r50 [r51] r52", Render(model));

        model.Resize(5);
        Assert.Equal("r50 [r51] r52 r53 r54", Render(model));

        model.Resize(1);
        Assert.Equal("[r51]", Render(model));
    }

    [Fact]
    public void SetFilterWhilePausedReloadsAroundTheSelectedRow()
    {
        var model = Model(Store());
        model.Start(3);
        model.Execute(TimelineCommand.Up);
        Assert.Equal("r97 [r98] r99", Render(model));

        model.SetFilter(new EvenRowFilter());

        Assert.Equal("r94 r96 [r98]", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void SetFilterInFollowLoadsTheNewestMatchingRows()
    {
        var model = Model(Store());
        model.Start(3);

        model.SetFilter(new EvenRowFilter());

        Assert.Equal("r94 r96 [r98]", Render(model));
        Assert.Equal(TimelineMode.Follow, model.Mode);
    }

    [Fact]
    public void PauseFromFollowOnlyPauses()
    {
        var model = Model(Store());
        model.Start(3);

        model.Pause();

        Assert.Equal(TimelineMode.Paused, model.Mode);
        Assert.Equal("r97 r98 [r99]", Render(model));
        Assert.Equal(0, model.NewCount);
        Assert.True(model.AtEnd);
    }

    [Fact]
    public void SelectVisibleRow_pauses_and_selects_that_row_without_moving_the_viewport()
    {
        var model = Model(Store());
        model.Start(3);
        Assert.Equal("r97 r98 [r99]", Render(model));

        model.SelectVisibleRow(0);

        Assert.Equal("[r97] r98 r99", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);

        model.SelectVisibleRow(2);
        Assert.Equal("r97 r98 [r99]", Render(model));
    }

    [Fact]
    public void SelectVisibleRow_in_a_paused_viewport_keeps_the_viewport()
    {
        var model = Model(Store());
        model.Start(3);
        Seek(model, 50);
        Assert.Equal("[r50] r51 r52", Render(model));

        model.SelectVisibleRow(2);

        Assert.Equal("r50 r51 [r52]", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);
        Assert.Equal(0, model.NewCount);
    }

    [Fact]
    public void SelectVisibleRow_ignores_a_row_outside_the_viewport()
    {
        var model = Model(Store());
        model.Start(3);
        int changes = 0;
        model.Changed += () => changes++;

        model.SelectVisibleRow(-1);
        model.SelectVisibleRow(3);
        model.SelectVisibleRow(int.MaxValue);

        Assert.Equal("r97 r98 [r99]", Render(model));
        Assert.Equal(TimelineMode.Follow, model.Mode);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void SelectVisibleRow_does_nothing_while_there_are_no_rows()
    {
        var model = Model(new FakeTimelineSource([]));
        model.Start(3);

        model.SelectVisibleRow(0);

        Assert.Null(model.SelectedRow);
        Assert.Equal(TimelineMode.Follow, model.Mode);
    }

    [Fact]
    public void JumpToSelectsTheRowWithItsScreenRowCentred()
    {
        var model = Model(Store());
        model.Start(5);

        model.JumpTo(Key(50));

        Assert.Equal("r48 r49 [r50] r51 r52", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);
        Assert.Equal(2, model.SelectedScreenRow);
    }

    [Fact]
    public void JumpToNearTheStartKeepsTheRowAtTheStartOfTheViewport()
    {
        var model = Model(Store());
        model.Start(5);

        model.JumpTo(Key(1));

        Assert.Equal("r0 [r1] r2 r3 r4", Render(model));
        Assert.True(model.AtStart);
    }

    [Fact]
    public void JumpToNearTheEndKeepsTheRowAtTheEndOfTheViewport()
    {
        var model = Model(Store());
        model.Start(5);

        model.JumpTo(Key(99));
        Assert.Equal("r95 r96 r97 r98 [r99]", Render(model));
        Assert.True(model.AtEnd);

        model.JumpTo(Key(98));
        Assert.Equal("r95 r96 r97 [r98] r99", Render(model));
    }

    [Fact]
    public void JumpToFromFollowPauses()
    {
        var model = Model(Store());
        model.Start(3);
        Assert.Equal(TimelineMode.Follow, model.Mode);

        model.JumpTo(Key(50));

        Assert.Equal(TimelineMode.Paused, model.Mode);
        Assert.Equal("r49 [r50] r51", Render(model));
        Assert.Equal(0, model.NewCount);
    }

    [Fact]
    public void AStaleJumpIsSupersededByALaterOne()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        source.Held.Add(ReadKind.Newer);
        model.JumpTo(Key(50));
        model.JumpTo(Key(20));
        Assert.Equal(2, source.Waiting(ReadKind.Newer));

        // The first jump's read completes first, and its result is dropped.
        source.CompleteOldest(ReadKind.Newer);
        Assert.Equal(string.Empty, Render(model));
        Assert.Equal(1, source.Waiting(ReadKind.Newer));

        source.CompleteOldest(ReadKind.Newer);
        Assert.Equal("r19 [r20] r21", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void NavigationAfterAJumpMovesNormally()
    {
        var model = Model(Store());
        model.Start(5);
        model.JumpTo(Key(50));

        model.Execute(TimelineCommand.Up);
        Assert.Equal("r48 [r49] r50 r51 r52", Render(model));

        model.Execute(TimelineCommand.Down);
        model.Execute(TimelineCommand.Down);
        Assert.Equal("r48 r49 r50 [r51] r52", Render(model));

        model.Execute(TimelineCommand.ShiftDown);
        Assert.Equal("r49 r50 [r51] r52 r53", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);
    }

    [Fact]
    public void JumpToALoadedRowOnTheViewportSelectsItWithoutLoadingOrMoving()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        Seek(model, 50);
        Assert.Equal("[r50] r51 r52", Render(model));
        int requests = source.Requests.Count;

        model.JumpTo(Key(51));

        Assert.Equal("r50 [r51] r52", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);
        Assert.Equal(requests, source.Requests.Count);
    }

    [Fact]
    public void JumpToALoadedRowOffTheViewportCentresItWithinTheLoadedRows()
    {
        var source = Store();
        var model = Model(source);
        model.Start(5);
        int requests = source.Requests.Count;

        model.JumpTo(Key(90));

        Assert.Equal("r88 r89 [r90] r91 r92", Render(model));
        Assert.Equal(TimelineMode.Paused, model.Mode);
        Assert.Equal(2, model.SelectedScreenRow);
        Assert.DoesNotContain(source.Requests.Skip(requests), request => request.Kind == ReadKind.Newest || request.Kind == ReadKind.Newer);
    }

    [Fact]
    public void JumpToAnUnloadedRowStillReloadsAroundIt()
    {
        var source = Store();
        var model = Model(source);
        model.Start(3);
        source.Held.Add(ReadKind.Newer);

        model.JumpTo(Key(50));

        Assert.Equal(1, source.Waiting(ReadKind.Newer));
        Assert.True(model.IsHolding);
        Assert.Equal("r97 r98 [r99]", Shown(model));

        source.CompleteOldest(ReadKind.Newer);

        Assert.Equal("r49 [r50] r51", Render(model));
        Assert.False(model.IsHolding);
    }

    private static EventRow Row(int index, string? preview = null) => new(
        new RowKey(1, index * 100L),
        new EventExtent(0, 0, 0, false, false),
        null,
        null,
        LogLevel.Unknown,
        null,
        preview ?? $"r{index}",
        false,
        false,
        false);

    private static RowKey Key(int index) => Row(index).Key;

    private static FakeTimelineSource Store(int count = 100) => new(Enumerable.Range(0, count).Select(index => Row(index)));

    private static TimelineModel Model(FakeTimelineSource source) =>
        new(source, action => action(), FakeTimelineSource.CompareKeys);

    private static int IndexOf(EventRow row) => (int)(row.Key.Offset / 100);

    /// <summary>Renders the viewport with the selected row in brackets, for example "r7 r8 [r9]".</summary>
    private static string Render(TimelineModel model) =>
        string.Join(' ', model.VisibleRows.Select((row, screenRow) => screenRow == model.SelectedScreenRow ? $"[{row.Preview}]" : row.Preview));

    /// <summary>Renders the rows the owner shows, held or not, in the same form as <see cref="Render"/>.</summary>
    private static string Shown(TimelineModel model) =>
        string.Join(' ', model.ShownRows.Select((row, screenRow) => screenRow == model.ShownSelectedScreenRow ? $"[{row.Preview}]" : row.Preview));

    /// <summary>Moves the selection, pausing, until it reaches the row with the index.</summary>
    private static void Seek(TimelineModel model, int index)
    {
        while (IndexOf(model.SelectedRow!) != index)
        {
            model.Execute(IndexOf(model.SelectedRow!) > index ? TimelineCommand.Up : TimelineCommand.Down);
        }
    }

    /// <summary>Adds rows to the source, as the store would, and reports them to the model as arrivals.</summary>
    private static void Arrive(TimelineModel model, FakeTimelineSource source, params int[] indices)
    {
        List<EventRow> rows = indices.Select(index => Row(index)).ToList();
        source.Rows.AddRange(rows);
        model.Arrive(rows, []);
    }
}
