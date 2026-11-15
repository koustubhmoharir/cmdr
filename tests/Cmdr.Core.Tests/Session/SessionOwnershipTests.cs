using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

public class SessionOwnershipTests
{
    [Fact]
    public async Task CommandListActionsRejectForeignTargetsBeforeSideEffects()
    {
        using var aDir = new TempDir();
        using var bDir = new TempDir();
        CmdrSession a = OpenWith(aDir, Group("a", GroupMode.Parallel, Sh("local", "echo local")));
        CmdrSession b = OpenWith(bDir, Group("b", GroupMode.Parallel, Sleeper("foreign")));
        try
        {
            CommandEntry local = a.Groups[0].Commands[0];
            CommandEntry foreign = b.Groups[0].Commands[0];

            Assert.Throws<ArgumentException>(() => a.Activate(new[] { local, foreign }));
            Assert.Throws<ArgumentException>(() => a.Activate(new[] { foreign }));
            Assert.Throws<ArgumentException>(() => a.Stop(new[] { foreign }));
            Assert.Throws<ArgumentException>(() => a.ForceKill(new[] { foreign }));
            Assert.Throws<ArgumentException>(() => a.InputsForActivate(new[] { foreign }));
            Assert.Throws<ArgumentException>(() => a.InputsForRestart(new[] { foreign }));
            await Assert.ThrowsAsync<ArgumentException>(async () => await a.RestartAsync(new[] { foreign }));
            Assert.Throws<ArgumentException>(() => a.ToggleLog(foreign));

            Assert.Equal(ProcessState.NotStarted, local.Process.Snapshot.State);
            Assert.Equal(ProcessState.NotStarted, foreign.Process.Snapshot.State);
            Assert.False(local.IsQueued);
            Assert.False(foreign.IsQueued);
            Assert.False(foreign.Process.Output.IsLogging);
        }
        finally
        {
            Cleanup(a);
            Cleanup(b);
        }
    }

    [Fact]
    public void CommandListActionsRejectRemovedTargets()
    {
        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("removed", "echo never")));
        try
        {
            CommandEntry removed = session.Groups[0].Commands[0];
            session.RemoveCommand(removed);

            Assert.Throws<ArgumentException>(() => session.Activate(new[] { removed }));
            Assert.Throws<ArgumentException>(() => session.Stop(new[] { removed }));
            Assert.Throws<ArgumentException>(() => session.ForceKill(new[] { removed }));
            Assert.Throws<ArgumentException>(() => session.InputsForActivate(new[] { removed }));
            Assert.Throws<ArgumentException>(() => session.InputsForRestart(new[] { removed }));
            Assert.Throws<ArgumentException>(() => session.ToggleLog(removed));
            Assert.Equal(ProcessState.NotStarted, removed.Process.Snapshot.State);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Theory]
    [InlineData(new[] { 1, 0 })]
    [InlineData(new[] { 2, 0 })]
    [InlineData(new[] { 0, 2, 1 })]
    [InlineData(new[] { 0, 0 })]
    [InlineData(new[] { 1, 1, 2 })]
    public async Task CommandListActionsRejectTargetsOutOfFileOrderOrRepeated(int[] picks)
    {
        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Sequential, Sh("a", "echo a"), Sh("b", "echo b"), Sh("c", "echo c")));
        try
        {
            CommandEntry[] commands = session.Groups[0].Commands.ToArray();
            IReadOnlyList<CommandEntry> targets = picks.Select(i => commands[i]).ToList();

            Assert.Throws<ArgumentException>(() => session.Activate(targets));
            Assert.Throws<ArgumentException>(() => session.Stop(targets));
            Assert.Throws<ArgumentException>(() => session.ForceKill(targets));
            await Assert.ThrowsAsync<ArgumentException>(async () => await session.RestartAsync(targets));
            Assert.Throws<ArgumentException>(() => session.InputsForActivate(targets));
            Assert.Throws<ArgumentException>(() => session.InputsForRestart(targets));

            Assert.All(commands, command =>
            {
                Assert.Equal(ProcessState.NotStarted, command.Process.Snapshot.State);
                Assert.False(command.IsQueued);
            });
            Assert.False(session.Groups[0].IsSequenceRunning);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void CommandListActionsAcceptDistinctSubsetInFileOrder()
    {
        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Sequential, Sh("a", "echo a"), Sh("b", "echo b"), Sh("c", "echo c")));
        try
        {
            CommandEntry[] commands = session.Groups[0].Commands.ToArray();
            IReadOnlyList<CommandEntry> targets = new[] { commands[0], commands[2] };

            Assert.Empty(session.InputsForActivate(targets));
            Assert.Empty(session.InputsForRestart(targets));
        }
        finally
        {
            Cleanup(session);
        }
    }
}
