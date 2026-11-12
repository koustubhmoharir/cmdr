using Cmdr.Core.Session;
using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class GroupSummaryTests
{
    [Fact]
    public void No_kinds_gives_an_empty_summary()
    {
        Assert.Equal(string.Empty, GroupSummary.Compose(Array.Empty<StatusKind>()));
    }

    [Fact]
    public void Idle_and_success_are_not_counted()
    {
        Assert.Equal(string.Empty, GroupSummary.Compose(new[] { StatusKind.Idle, StatusKind.Success, StatusKind.Idle }));
    }

    [Fact]
    public void Stopped_is_not_counted()
    {
        Assert.Equal(string.Empty, GroupSummary.Compose(new[] { StatusKind.Stopped, StatusKind.Stopped }));
        Assert.Equal("1 running", GroupSummary.Compose(new[] { StatusKind.Stopped, StatusKind.Running }));
    }

    [Fact]
    public void Counts_are_given_per_kind_with_zero_counts_left_out()
    {
        string text = GroupSummary.Compose(new[] { StatusKind.Running, StatusKind.Running, StatusKind.Failure });

        Assert.Equal("2 running · 1 failed", text);
    }

    [Fact]
    public void Kinds_appear_in_the_fixed_order()
    {
        string text = GroupSummary.Compose(new[]
        {
            StatusKind.Warning, StatusKind.Failure, StatusKind.Queued, StatusKind.Stopping, StatusKind.Running,
        });

        Assert.Equal("1 running · 1 stopping · 1 queued · 1 failed · 1 with leftovers", text);
    }

    [Fact]
    public void Queued_and_running_example()
    {
        string text = GroupSummary.Compose(new[] { StatusKind.Running, StatusKind.Queued, StatusKind.Queued, StatusKind.Idle });

        Assert.Equal("1 running · 2 queued", text);
    }

    [Fact]
    public void Idle_and_success_do_not_hide_the_counted_kinds()
    {
        string text = GroupSummary.Compose(new[] { StatusKind.Success, StatusKind.Queued, StatusKind.Idle });

        Assert.Equal("1 queued", text);
    }

    [Fact]
    public void Starting_and_ready_are_counted_after_running()
    {
        string text = GroupSummary.Compose(new[]
        {
            StatusKind.Ready, StatusKind.Stopping, StatusKind.Starting, StatusKind.Running, StatusKind.Ready,
        });

        Assert.Equal("1 running · 1 starting · 2 ready · 1 stopping", text);
    }

    [Fact]
    public void Stopped_starting_and_ready_example()
    {
        string text = GroupSummary.Compose(new[] { StatusKind.Starting, StatusKind.Ready, StatusKind.Stopped });

        Assert.Equal("1 starting · 1 ready", text);
    }
}
