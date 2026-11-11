using Cmdr.Core.Processes;
using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class EnterHintTests
{
    private static EnterTarget Startable() => new(ProcessState.NotStarted, 0, CanStart: true);
    private static EnterTarget Running() => new(ProcessState.Running, 0, CanStart: false);
    private static EnterTarget Stopping() => new(ProcessState.Stopping, 0, CanStart: false);
    private static EnterTarget ExitedWithLeftovers() => new(ProcessState.Exited, 2, CanStart: false);

    [Fact]
    public void Startable_targets_say_Start()
    {
        Assert.Equal("Start", EnterHint.Describe(new[] { Startable() }, sequenceBlocksStart: false));
    }

    [Fact]
    public void Stopping_and_leftover_targets_say_Kill()
    {
        Assert.Equal("Kill", EnterHint.Describe(new[] { Stopping() }, sequenceBlocksStart: false));
        Assert.Equal("Kill", EnterHint.Describe(new[] { ExitedWithLeftovers() }, sequenceBlocksStart: false));
    }

    [Fact]
    public void When_both_kinds_are_present_Enter_only_kills()
    {
        Assert.Equal("Kill", EnterHint.Describe(new[] { Startable(), Stopping() }, sequenceBlocksStart: false));
    }

    [Fact]
    public void Exited_without_leftovers_can_be_started()
    {
        var exited = new EnterTarget(ProcessState.Exited, 0, CanStart: true);

        Assert.Equal("Start", EnterHint.Describe(new[] { exited }, sequenceBlocksStart: false));
    }

    [Fact]
    public void Only_running_targets_or_no_targets_show_no_hint()
    {
        Assert.Null(EnterHint.Describe(new[] { Running() }, sequenceBlocksStart: false));
        Assert.Null(EnterHint.Describe(Array.Empty<EnterTarget>(), sequenceBlocksStart: false));
    }

    [Fact]
    public void A_running_sequence_refuses_starts_but_still_kills()
    {
        Assert.Null(EnterHint.Describe(new[] { Startable() }, sequenceBlocksStart: true));
        Assert.Equal("Kill", EnterHint.Describe(new[] { Startable(), Stopping() }, sequenceBlocksStart: true));
    }
}
