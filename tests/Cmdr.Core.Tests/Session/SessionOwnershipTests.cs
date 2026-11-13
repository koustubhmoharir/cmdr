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
}
