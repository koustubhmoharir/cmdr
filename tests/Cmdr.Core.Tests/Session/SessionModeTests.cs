using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;
using static Cmdr.Core.Tests.Session.SessionTestHelpers;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Core.Tests.Session;

/// <summary>The run and edit modes: what each allows, and how the session switches between them.</summary>
public class SessionModeTests
{
    private const string EditOnly = "Groups and commands can only be changed in edit mode.";
    private const string RunOnly = "Commands can only be started in run mode.";
    private const string LogRunOnly = "The log can only be turned on or off in run mode.";

    [Theory]
    [InlineData(SessionMode.Run)]
    [InlineData(SessionMode.Edit)]
    public void Load_SetsTheRequestedMode(SessionMode mode)
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenEmpty(dir, mode);

        Assert.Equal(mode, session.Mode);
    }

    [Theory]
    [InlineData("AddGroup")]
    [InlineData("UpdateGroup")]
    [InlineData("RemoveGroup")]
    [InlineData("AddCommand")]
    [InlineData("UpdateCommand")]
    [InlineData("RemoveCommand")]
    [InlineData("MoveItem")]
    [InlineData("MoveCommand")]
    public void EditOperation_ThrowsInRunMode_AndChangesNothing(string operation)
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "true"), Sh("b", "true")));
        GroupEntry group = session.Groups[0];
        CommandEntry first = group.Commands[0];
        string before = Shape(session);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => EditCall(session, operation, group, first)());

        Assert.Equal(EditOnly, ex.Message);
        Assert.Equal(before, Shape(session));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Activate_ThrowsInEditMode_AndStartsNothing()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));
        CommandEntry a = session.Groups[0].Commands[0];

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => session.Activate(new[] { a }));

        Assert.Equal(RunOnly, ex.Message);
        Assert.Equal(ProcessState.NotStarted, a.Process.Snapshot.State);
    }

    [Fact]
    public void ActivateGroup_ThrowsInEditMode()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => session.Activate(session.Groups[0]));

        Assert.Equal(RunOnly, ex.Message);
    }

    [Fact]
    public void PrepareActivate_ThrowsInEditMode()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));
        CommandEntry a = session.Groups[0].Commands[0];

        Assert.Equal(RunOnly, Assert.Throws<InvalidOperationException>(() => session.PrepareActivate(new[] { a })).Message);
        Assert.Equal(RunOnly, Assert.Throws<InvalidOperationException>(() => session.PrepareActivate(session.Groups[0])).Message);
    }

    [Fact]
    public void PreparedActivation_MadeInRunMode_IsRefusedAfterSwitchingToEdit()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "true")));
        CommandEntry a = session.Groups[0].Commands[0];
        PreparedActivation prepared = session.PrepareActivate(new[] { a });

        Assert.Null(session.SwitchToEdit());
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => session.Activate(prepared));

        Assert.Equal(RunOnly, ex.Message);
        Assert.Equal(ProcessState.NotStarted, a.Process.Snapshot.State);
    }

    [Fact]
    public async Task RestartAsync_ThrowsInEditMode_ForCommandsAndForGroups()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));
        CommandEntry a = session.Groups[0].Commands[0];

        InvalidOperationException commands = await Assert.ThrowsAsync<InvalidOperationException>(() => session.RestartAsync(new[] { a }));
        InvalidOperationException group = await Assert.ThrowsAsync<InvalidOperationException>(() => session.RestartAsync(session.Groups[0]));

        Assert.Equal(RunOnly, commands.Message);
        Assert.Equal(RunOnly, group.Message);
        Assert.Equal(ProcessState.NotStarted, a.Process.Snapshot.State);
    }

    [Fact]
    public void PrepareRestart_ThrowsInEditMode_AndRegistersNothing()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));
        CommandEntry a = session.Groups[0].Commands[0];

        Assert.Equal(RunOnly, Assert.Throws<InvalidOperationException>(() => session.PrepareRestart(new[] { a })).Message);
        Assert.Equal(RunOnly, Assert.Throws<InvalidOperationException>(() => session.PrepareRestart(session.Groups[0])).Message);
        Assert.Empty(session.PendingRestartsOf(new[] { a }));
    }

    [Fact]
    public async Task RunRestartAsync_ThrowsInEditMode_EvenForAPreparedRestartWithNothingToDo()
    {
        // A restart of no commands registers nothing, so the session can be switched to edit mode with it prepared.
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "true")));
        PreparedRestart prepared = session.PrepareRestart(Array.Empty<CommandEntry>());
        Assert.Null(session.SwitchToEdit());

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunRestartAsync(prepared));

        Assert.Equal(RunOnly, ex.Message);
    }

    [Fact]
    public void ToggleLog_ThrowsInEditMode_AndLeavesTheLogOff()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));
        CommandEntry a = session.Groups[0].Commands[0];

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => session.ToggleLog(a));

        Assert.Equal(LogRunOnly, ex.Message);
        Assert.False(a.Process.Output.IsLogging);
    }

    [Fact]
    public void StopForceKillAndQueries_WorkInEditMode()
    {
        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));
        try
        {
            GroupEntry group = session.Groups[0];
            CommandEntry a = group.Commands[0];

            Assert.Null(session.Stop(new[] { a }));
            Assert.Null(session.Stop(group));
            session.ForceKill(new[] { a });
            session.StopAll();
            session.ForceKillAll();

            Assert.Empty(session.InputsForActivate(new[] { a }));
            Assert.Empty(session.InputsForRestart(group));
            Assert.Empty(session.PendingRestartsOf(new[] { a }));
            Assert.Empty(session.ActiveEntries);
            Assert.Empty(session.Validate());
            Assert.Single(session.BuildConfig().Groups);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void SaveAndBuildConfig_WorkInBothModes()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenEmpty(dir);

        Assert.Empty(session.BuildConfig().Groups);
        session.Save();
        Assert.Null(session.SwitchToEdit());
        session.AddGroup(null, Settings("build"));
        session.Save();

        Assert.Equal("build", ConfigStore.Load(dir.File("cmdr.json")).Groups.Single().Name);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void SwitchToEdit_WhenAlreadyInEditMode_ReturnsNull()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenEmpty(dir, SessionMode.Edit);

        Assert.Null(session.SwitchToEdit());
        Assert.Equal(SessionMode.Edit, session.Mode);
    }

    [Fact]
    public void SwitchToEdit_WhenIdle_SwitchesAndEditsWork()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "true")));

        Assert.Null(session.SwitchToEdit());

        Assert.Equal(SessionMode.Edit, session.Mode);
        session.AddCommand(session.Groups[0], Sh("b", "true"));
        Assert.Equal(2, session.Groups[0].Commands.Count);
    }

    [Fact]
    public void SwitchToRun_FromEditMode_AllowsStartsAgain()
    {
        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, SessionMode.Edit, Group("g", GroupMode.Parallel, Sh("a", "true")));
        try
        {
            session.SwitchToRun();

            Assert.Equal(SessionMode.Run, session.Mode);
            Assert.Null(session.Activate(session.Groups[0]));
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void SwitchToRun_KeepsUnsavedChangesAndTheDirtyFlag()
    {
        using var dir = new TempDir();
        using CmdrSession session = OpenEmpty(dir, SessionMode.Edit);
        session.AddGroup(null, Settings("build"));

        session.SwitchToRun();

        Assert.Equal(SessionMode.Run, session.Mode);
        Assert.True(session.IsDirty);
        Assert.Equal("build", Assert.Single(session.Groups).Name);
        Assert.Equal("build", Assert.Single(session.BuildConfig().Groups).Name);
    }

    [Fact]
    public void SwitchToEdit_RefusedWhileACommandRuns_AndChangesNothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sleeper("loop")));
        try
        {
            CommandEntry loop = session.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { loop }));
            WaitUntil(() => loop.Process.Snapshot.State == ProcessState.Running, "loop running", loop.Process);

            string? refusal = session.SwitchToEdit();

            Assert.Equal("Cannot switch to edit mode: 1 command(s) are running or have leftover processes.", refusal);
            Assert.Equal(SessionMode.Run, session.Mode);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void SwitchToEdit_RefusedWhileACommandHasLeftoverProcesses()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        // The leader exits at once and leaves its background sleep behind; with the default "track" leftovers, that is tracked.
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("left", "sleep 30 & echo started")));
        try
        {
            CommandEntry left = session.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { left }));
            WaitUntil(() => left.Process.Snapshot.HasLeftovers, "leftovers tracked", left.Process);

            string? refusal = session.SwitchToEdit();

            Assert.Equal("Cannot switch to edit mode: 1 command(s) are running or have leftover processes.", refusal);
            Assert.Equal(SessionMode.Run, session.Mode);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void SwitchToEdit_RefusedWhileASequenceHasAQueuedCommand()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        CmdrSession session = OpenWith(
            dir,
            Group("seq", GroupMode.Sequential, Sleeper("loop"), Sh("next", "true")));
        try
        {
            GroupEntry seq = session.Groups[0];
            CommandEntry next = seq.Commands[1];
            Assert.Null(session.Activate(seq.Commands));
            WaitUntil(() => next.IsQueued, "next queued");

            // The running first command is counted before the sequence itself, so the message names the command.
            string? refusal = session.SwitchToEdit();

            Assert.NotNull(refusal);
            Assert.StartsWith("Cannot switch to edit mode:", refusal, StringComparison.Ordinal);
            Assert.Equal(SessionMode.Run, session.Mode);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public async Task SwitchToEdit_RefusedWhileARestartIsPending_UntilItRuns()
    {
        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("a", "true")));
        try
        {
            CommandEntry a = session.Groups[0].Commands[0];
            PreparedRestart prepared = session.PrepareRestart(new[] { a });

            Assert.Equal("Cannot switch to edit mode: a restart is pending.", session.SwitchToEdit());
            Assert.Equal(SessionMode.Run, session.Mode);

            await session.RunRestartAsync(prepared);
        }
        finally
        {
            Cleanup(session);
        }
    }

    [Fact]
    public void SwitchToEdit_AllowedOnceTheCommandHasExited()
    {
        using var dir = new TempDir();
        CmdrSession session = OpenWith(dir, Group("g", GroupMode.Parallel, Sh("quick", "true")));
        try
        {
            CommandEntry quick = session.Groups[0].Commands[0];
            Assert.Null(session.Activate(new[] { quick }));
            WaitUntil(() => quick.Process.Snapshot.State == ProcessState.Exited, "quick exited", quick.Process);
            Assert.False(quick.IsActive);

            Assert.Null(session.SwitchToEdit());

            Assert.Equal(SessionMode.Edit, session.Mode);
        }
        finally
        {
            Cleanup(session);
        }
    }

    /// <summary>The visible tree and the dirty flag: what an operation that is refused must leave as it was.</summary>
    private static string Shape(CmdrSession session) =>
        string.Join("|", session.BuildRows().Select(row => row switch
        {
            GroupRow group => $"G:{group.Group.Name}",
            CommandRow command => $"C:{command.Entry.Name}",
            _ => "?",
        })) + (session.IsDirty ? "*" : string.Empty);

    /// <summary>The edit operation named by <paramref name="operation"/>, as a call that takes no arguments.</summary>
    private static Action EditCall(CmdrSession session, string operation, GroupEntry group, CommandEntry command) => operation switch
    {
        "AddGroup" => () => session.AddGroup(null, Settings("new")),
        "UpdateGroup" => () => session.UpdateGroup(group, Settings("renamed"), null),
        "RemoveGroup" => () => session.RemoveGroup(group),
        "AddCommand" => () => session.AddCommand(group, Sh("new", "true")),
        "UpdateCommand" => () => session.UpdateCommand(command, Sh("renamed", "true"), group),
        "RemoveCommand" => () => session.RemoveCommand(command),
        "MoveItem" => () => session.MoveItem(command, 1),
        "MoveCommand" => () => session.MoveCommand(command, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown edit operation."),
    };
}
