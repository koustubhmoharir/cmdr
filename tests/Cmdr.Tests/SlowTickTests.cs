using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Shared.Processes;

namespace Cmdr.Tests;

/// <summary>The once-a-second tick redraws the list only while a visible command shows an elapsed time.</summary>
public sealed class SlowTickTests
{
    [Fact]
    public async Task Only_a_running_command_in_view_shows_an_elapsed_time()
    {
        // A command that keeps running until it is killed.
        CommandSpec spec = OperatingSystem.IsWindows()
            ? new CommandSpec { Name = "long", Program = "ping", Args = { "-n", "30", "127.0.0.1" } }
            : new CommandSpec { Name = "long", Program = "sleep", Args = { "30" } };
        using TempSession temp = TempSession.Open(new GroupSpec { Name = "g", Items = { spec } });
        CommandEntry entry = temp.Session.AllEntries.Single();
        IReadOnlyList<ListRow> rows = temp.Session.BuildRows();

        Assert.False(MainController.ShowsElapsedTime(rows));

        entry.Process.Start(CommandLaunch.From(Array.Empty<GroupSpec>(), spec, temp.Session.ConfigDir, Array.Empty<EnvLevel>()));
        try
        {
            Assert.Equal(ProcessState.Running, entry.Process.Snapshot.State);
            Assert.True(MainController.ShowsElapsedTime(rows));

            // Hidden under a collapsed group (only the header row is visible), the command shows no time.
            Assert.False(MainController.ShowsElapsedTime(rows.Where(row => row is GroupRow).ToList()));
        }
        finally
        {
            entry.Process.ForceKill();
            await entry.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(ProcessState.Exited, entry.Process.Snapshot.State);
        Assert.False(MainController.ShowsElapsedTime(rows));
    }
}
