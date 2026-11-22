using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Session;
using Cmdr.Tui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using CmdrSession = Cmdr.Core.Session.Session;

namespace Cmdr.Tests;

/// <summary>
/// Starting commands whose group has an environment script, in the main window: Enter and F5 run the script in the background first
/// (the row reads "preparing env…" meanwhile), Enter or F5 again says they are already running, Ctrl+C cancels the start, Ctrl+E is
/// refused, and a refused start runs no script. The scripts are real <c>sh</c> scripts, so these tests run on Linux only.
/// </summary>
/// <remarks>
/// Completions reach the controller through the application's main loop, so the tests run the loop (<see cref="RunLoop"/>) until the
/// preparation is over.
/// </remarks>
[Collection("Terminal GUI")]
public sealed class EnvScriptStartTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Width = 120;
    private const int Height = 40;
    private const string AlreadyPreparing = "Environment scripts are already running for this start";
    private const string Cancelled = "Start cancelled";
    private const string PreparingRefusal = "Cannot switch to edit mode: environment scripts are running.";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public void Enter_runs_the_group_script_and_the_command_starts_with_its_values()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(dir => new[]
        {
            new GroupSpec
            {
                Name = "g",
                EnvScript = Shell("printf '%s' '{\"GREETING\":\"hello\"}'"),
                Items = { Prints("c", Path.Combine(dir, "out.txt"), "GREETING") },
            },
        });
        CommandEntry c = f.Session.AllEntries.Single();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.True(controller.OnListKey(Key.Enter));

        // Nothing has started yet: the script is still to run.
        Assert.Equal(1, Preparing(controller));
        Assert.Equal(ProcessState.NotStarted, State(c));
        RunLoop(app, controller, () => Preparing(controller) == 0 && State(c) == ProcessState.Exited, "the command to exit");
        Assert.Null(Message(controller));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(f.Dir, "out.txt")));
    }

    [Fact]
    public void Enter_without_scripts_starts_at_once_and_creates_no_preparation()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(_ => new[] { new GroupSpec { Name = "g", Items = { Sleeper("c") } } });
        CommandEntry c = f.Session.AllEntries.Single();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.True(controller.OnListKey(Key.Enter));

        Assert.Equal(0, Preparing(controller));
        Assert.Equal(ProcessState.Running, State(c));
    }

    [Fact]
    public void A_failing_script_shows_its_failure_and_starts_nothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(_ => new[]
        {
            new GroupSpec { Name = "g", EnvScript = Shell("echo broken >&2; exit 3"), Items = { Sleeper("c") } },
        });
        CommandEntry c = f.Session.AllEntries.Single();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.True(controller.OnListKey(Key.Enter));
        RunLoop(app, controller, () => Preparing(controller) == 0, "the preparation to fail");

        Assert.StartsWith("env script of group 'g' failed: ", Message(controller), StringComparison.Ordinal);
        Assert.Equal(ProcessState.NotStarted, State(c));
        Assert.Contains(Lines(c), line => line.Contains("broken", StringComparison.Ordinal));
    }

    [Fact]
    public void While_the_script_runs_the_row_reads_preparing_env_and_Enter_and_F5_say_so_and_start_nothing()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(dir => new[] { new GroupSpec { Name = "g", EnvScript = Slow(dir), Items = { Sleeper("c") } } });
        CommandEntry c = f.Session.AllEntries.Single();
        using IApplication app = Application.Create();
        app.Init("ansi");
        app.Driver!.SetScreenSize(Width, Height);
        using var controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();
        Assert.True(controller.OnListKey(Key.Enter));

        RunLoop(app, controller, () =>
        {
            app.LayoutAndDraw();
            return ScreenText(app).Contains(RowLayout.PreparingText, StringComparison.Ordinal);
        }, "the row to read preparing env");

        Assert.Equal(ProcessState.NotStarted, State(c));
        Assert.True(controller.OnListKey(Key.Enter));
        Assert.Equal(AlreadyPreparing, Message(controller));
        ClearMessage(controller);
        Assert.True(controller.OnListKey(Key.F5));
        Assert.Equal(AlreadyPreparing, Message(controller));
        Assert.Equal(1, Preparing(controller));
        Drain(app, controller, f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ctrl_C_cancels_the_start_kills_the_script_and_starts_nothing(bool fromOutput)
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(dir => new[] { new GroupSpec { Name = "g", EnvScript = Slow(dir), Items = { Sleeper("c") } } });
        CommandEntry c = f.Session.AllEntries.Single();
        string pidFile = Path.Combine(f.Dir, "pid.txt");
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();
        Assert.True(controller.OnListKey(Key.Enter));

        bool pressed = false;
        RunLoop(app, controller, () =>
        {
            // Ctrl+C once the script has written its pid, so that it is running; then wait for the completion.
            if (!pressed)
            {
                if (!TryReadPid(pidFile, out _))
                {
                    return false;
                }

                pressed = true;
                if (fromOutput)
                {
                    OutputView output = Field<OutputView>(controller, "_output");
                    output.SetFocus();
                    Assert.True(output.NewKeyDownEvent(Key.C.WithCtrl));
                }
                else
                {
                    Assert.True(controller.OnListKey(Key.C.WithCtrl));
                }

                return false;
            }

            return Preparing(controller) == 0;
        }, "the cancelled preparation to complete");

        Assert.Equal(Cancelled, Message(controller));
        Assert.Equal(ProcessState.NotStarted, State(c));
        Assert.True(TryReadPid(pidFile, out int pid));
        WaitUntil(() => !Alive(pid), "the script process to be gone");
    }

    [Fact]
    public void Ctrl_E_is_refused_while_the_scripts_run()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(dir => new[] { new GroupSpec { Name = "g", EnvScript = Slow(dir), Items = { Sleeper("c") } } });
        CommandEntry c = f.Session.AllEntries.Single();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();
        Assert.True(controller.OnListKey(Key.Enter));
        Assert.Equal(1, Preparing(controller));

        Assert.True(controller.OnGlobalKey(Key.E.WithCtrl));

        Assert.Equal(SessionMode.Run, f.Session.Mode);
        Assert.Equal(PreparingRefusal, Message(controller));
        Drain(app, controller, f);
    }

    [Fact]
    public async Task A_refused_start_runs_no_script()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(dir => new[]
        {
            new GroupSpec
            {
                Name = "g",
                Mode = GroupMode.Sequential,
                EnvScript = Shell("printf '%s\\n' ran >> '" + Path.Combine(dir, "log.txt") + "'; printf '{}'"),
                Items = { Sleeper("a"), Sleeper("b") },
            },
        });
        GroupEntry group = f.Session.AllGroups.Single();
        string log = Path.Combine(f.Dir, "log.txt");

        // A running sequence: its start needs the script's values, which a preparation made here provides.
        EnvPreparation prepared = await f.Session.PrepareEnvAsync(group.AllCommands, null, CancellationToken.None);
        Assert.Null(f.Session.Activate(group, null, prepared.Values));
        int runs = LogLines(log).Count;
        CommandEntry b = f.Session.AllEntries.Last();
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, f.Session);
        Focus(controller, b);
        Field<CommandListView>(controller, "_list").SetFocus();

        Assert.True(controller.OnListKey(Key.Enter));

        Assert.NotNull(Message(controller));
        Assert.Equal(0, Preparing(controller));
        Assert.Equal(runs, LogLines(log).Count);
        Assert.Equal(ProcessState.NotStarted, State(b));
    }

    [Fact]
    public void Closing_the_controller_cancels_the_preparation_and_kills_its_script()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(dir => new[] { new GroupSpec { Name = "g", EnvScript = Slow(dir), Items = { Sleeper("c") } } });
        CommandEntry c = f.Session.AllEntries.Single();
        string pidFile = Path.Combine(f.Dir, "pid.txt");
        using IApplication app = Application.Create();
        app.Init("ansi");
        MainController controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();
        Assert.True(controller.OnListKey(Key.Enter));
        WaitUntil(() => TryReadPid(pidFile, out _), "the script to start");
        Assert.True(TryReadPid(pidFile, out int pid));

        controller.Dispose();

        Assert.Equal(0, Preparing(controller));
        WaitUntil(() => !Alive(pid), "the script process to be gone");
        Assert.Equal(ProcessState.NotStarted, State(c));
    }

    [Fact]
    public void F5_runs_the_script_before_the_stop_and_restarts_the_command_when_it_succeeds()
    {
        if (!OperatingSystem.IsLinux()) return;

        // The script waits for the gate file, which the test creates: until then the stop has not happened.
        using var f = Fixture.Open(dir => new[] { new GroupSpec { Name = "g", EnvScript = Gated(dir), Items = { Sleeper("c") } } });
        CommandEntry c = f.Session.AllEntries.Single();
        string gate = Path.Combine(f.Dir, "gate");
        Start(f, c, gate);
        DateTimeOffset first = c.Process.Snapshot.StartedAt!.Value;
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();

        AnswerConfirmations(app, controller);
        Assert.True(controller.OnListKey(Key.F5));
        RunLoop(app, controller, () =>
        {
            if (Preparing(controller) == 1)
            {
                // The script is still running: the command is neither stopped nor started again.
                Assert.Equal(ProcessState.Running, State(c));
                Assert.Equal(first, c.Process.Snapshot.StartedAt);
                File.WriteAllText(gate, string.Empty);
                return false;
            }

            return Preparing(controller) == 0 && State(c) == ProcessState.Running && c.Process.Snapshot.StartedAt != first;
        }, "the command to restart after its script");

        Assert.Null(Message(controller));
    }

    [Fact]
    public void F5_with_a_failing_script_leaves_the_running_command_running()
    {
        if (!OperatingSystem.IsLinux()) return;

        // The script fails while the fail file exists; it does not when the command starts.
        using var f = Fixture.Open(dir => new[]
        {
            new GroupSpec
            {
                Name = "g",
                EnvScript = new EnvScriptSpec
                {
                    Program = "sh",
                    Args = new List<string> { "-c", "if [ -f '" + Path.Combine(dir, "fail") + "' ]; then echo bad >&2; exit 1; fi; printf '{}'" },
                    Rerun = EnvScriptRerun.EachStart,
                },
                Items = { Sleeper("c") },
            },
        });
        CommandEntry c = f.Session.AllEntries.Single();
        Start(f, c, gate: null);
        DateTimeOffset first = c.Process.Snapshot.StartedAt!.Value;
        File.WriteAllText(Path.Combine(f.Dir, "fail"), string.Empty);
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();

        AnswerConfirmations(app, controller);
        Assert.True(controller.OnListKey(Key.F5));
        RunLoop(app, controller, () => Preparing(controller) == 0, "the failed preparation to complete");

        Assert.StartsWith("env script of group 'g' failed: ", Message(controller), StringComparison.Ordinal);
        Assert.Equal(ProcessState.Running, State(c));
        Assert.Equal(first, c.Process.Snapshot.StartedAt);
        Drain(app, controller, f);
    }

    [Fact]
    public void Backing_out_of_the_save_prompt_keeps_the_start()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(dir => new[] { new GroupSpec { Name = "g", EnvScript = Gated(dir), Items = { Sleeper("c") } } });
        CommandEntry c = f.Session.AllEntries.Single();
        MakeDirty(f.Session);
        string gate = Path.Combine(f.Dir, "gate");
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();
        Assert.True(controller.OnListKey(Key.Enter));
        Assert.Equal(1, Preparing(controller));

        // The script finishes while the Save prompt is open: its completion waits there, and the start does not happen yet.
        bool gateWritten = false;
        using var prompt = new DialogDriver(app, dialog => HasButton(dialog, "Discard"), () =>
        {
            if (!gateWritten)
            {
                File.WriteAllText(gate, string.Empty);
                gateWritten = true;
                return false;
            }

            if (Deferred(controller) == 0)
            {
                return false;
            }

            Assert.Equal(ProcessState.NotStarted, State(c));
            Assert.Equal(1, Preparing(controller));
            return true;
        }, "_Cancel", "the completion to wait for the Save prompt");

        Assert.True(controller.OnGlobalKey(Key.Q.WithCtrl));

        prompt.Finish();
        Assert.Equal(ProcessState.Running, State(c));
        Assert.Equal(0, Preparing(controller));
        Assert.Equal(0, Deferred(controller));
        Assert.Null(Message(controller));
    }

    [Fact]
    public void Backing_out_of_the_stop_prompt_keeps_the_start()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(dir => new[]
        {
            new GroupSpec { Name = "running", Items = { Sleeper("r") } },
            new GroupSpec { Name = "g", EnvScript = Gated(dir), Items = { Sleeper("c") } },
        });
        CommandEntry running = f.Session.AllEntries.Single(entry => entry.Name == "r");
        CommandEntry c = f.Session.AllEntries.Single(entry => entry.Name == "c");
        Start(f, running, gate: null);
        string gate = Path.Combine(f.Dir, "gate");
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();
        Assert.True(controller.OnListKey(Key.Enter));
        Assert.Equal(1, Preparing(controller));

        // The script finishes while the Stop prompt is open, which Ctrl+Q shows because a command is running.
        bool gateWritten = false;
        using var prompt = new DialogDriver(app, dialog => HasButton(dialog, "Stop and wait"), () =>
        {
            if (!gateWritten)
            {
                File.WriteAllText(gate, string.Empty);
                gateWritten = true;
                return false;
            }

            if (Deferred(controller) == 0)
            {
                return false;
            }

            Assert.Equal(ProcessState.NotStarted, State(c));
            return true;
        }, "_Cancel", "the completion to wait for the Stop prompt");

        Assert.True(controller.OnGlobalKey(Key.Q.WithCtrl));

        prompt.Finish();
        Assert.Equal(ProcessState.Running, State(running));
        Assert.Equal(ProcessState.Running, State(c));
        Assert.Equal(0, Preparing(controller));
        Assert.Equal(0, Deferred(controller));
        Assert.Null(Message(controller));
    }

    [Fact]
    public void A_committed_quit_cancels_the_start()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(dir => new[] { new GroupSpec { Name = "g", EnvScript = Gated(dir), Items = { Sleeper("c") } } });
        CommandEntry c = f.Session.AllEntries.Single();
        string gate = Path.Combine(f.Dir, "gate");
        using IApplication app = Application.Create();
        app.Init("ansi");
        using var controller = Build(app, f.Session);
        Focus(controller, c);
        Field<CommandListView>(controller, "_list").SetFocus();
        Assert.True(controller.OnListKey(Key.Enter));
        Assert.Equal(1, Preparing(controller));

        // Nothing is running and nothing is unsaved, so Ctrl+Q quits at once: the loop runs until the quit stops it.
        bool quitRequested = false;
        RunLoop(app, controller, () =>
        {
            if (!quitRequested)
            {
                quitRequested = true;
                Assert.True(controller.OnGlobalKey(Key.Q.WithCtrl));
            }

            return false;
        }, "the main loop to stop after the quit");

        Assert.True(PreparationsCancelled(controller));
        Assert.Equal(ProcessState.NotStarted, State(c));

        // The script finishes after the cancel: its completion shows the cancellation and starts nothing.
        File.WriteAllText(gate, string.Empty);
        RunLoop(app, controller, () => Preparing(controller) == 0, "the cancelled preparation to complete");
        Assert.Equal(Cancelled, Message(controller));
        Assert.Equal(ProcessState.NotStarted, State(c));
    }

    [Fact]
    public void QuitFlow_calls_onStopping_before_it_stops_the_commands()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var f = Fixture.Open(_ => new[] { new GroupSpec { Name = "g", Items = { Sleeper("c") } } });
        CommandEntry c = f.Session.AllEntries.Single();
        Start(f, c, gate: null);
        using IApplication app = Application.Create();
        app.Init("ansi");
        var seen = new List<ProcessState>();
        using var prompt = new DialogDriver(app, dialog => HasButton(dialog, "Stop and wait"), () => true, "_Stop and wait", "the Stop prompt");
        var quit = new QuitFlow(app, f.Session, () => true, () => seen.Add(State(c)));

        Assert.True(quit.Run());

        prompt.Finish();
        // The command was still running when onStopping ran; the stop came after it.
        Assert.Equal(new[] { ProcessState.Running }, seen);
        Assert.Empty(f.Session.ActiveEntries);
    }

    /// <summary>
    /// Answers, with Enter (its default, Yes), the confirmation that a call shows while it runs: the call blocks in the dialog's own loop, which
    /// runs this timer. Used for F5 pressed outside <see cref="RunLoop"/>. The timer stays until the application is disposed.
    /// </summary>
    private static void AnswerConfirmations(IApplication app, MainController controller) =>
        app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
        {
            if (app.TopRunnable is { } top && !ReferenceEquals(top, controller.Window))
            {
                app.TopRunnableView?.NewKeyDownEvent(Key.Enter);
            }

            return true;
        });

    #region Fixture and scripts

    /// <summary>A session in a temporary folder, which holds the configuration and the files the scripts and commands write.</summary>
    private sealed class Fixture : IDisposable
    {
        private Fixture(string dir, CmdrSession session)
        {
            Dir = dir;
            Session = session;
        }

        public string Dir { get; }

        public CmdrSession Session { get; }

        /// <summary>Writes the groups that <paramref name="groups"/> makes for the folder, and loads them in run mode.</summary>
        public static Fixture Open(Func<string, GroupSpec[]> groups)
        {
            string dir = Directory.CreateTempSubdirectory("cmdr-env-start-").FullName;
            string path = Path.Combine(dir, "cmdr.json");
            ConfigStore.Save(new CmdrConfig { Groups = groups(dir).ToList() }, path);
            return new Fixture(dir, CmdrSession.Load(path, SessionMode.Run));
        }

        public void Dispose()
        {
            Session.ForceKillAll();
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (Session.ActiveEntries.Count > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }

            Session.Dispose();
            try
            {
                Directory.Delete(Dir, recursive: true);
            }
            catch (IOException)
            {
                // Best effort: the temporary folder is left behind when it cannot be removed.
            }
        }
    }

    /// <summary>A script that runs <c>sh -c shell</c>.</summary>
    private static EnvScriptSpec Shell(string shell) => new() { Program = "sh", Args = new List<string> { "-c", shell } };

    /// <summary>A script that writes its pid to <c>pid.txt</c> in the folder, then sleeps until it is killed. <c>exec</c> makes the pid the sleep's.</summary>
    private static EnvScriptSpec Slow(string dir) => Shell($"echo $$ > '{Path.Combine(dir, "pid.txt")}'; exec sleep 30");

    /// <summary>A script that waits until the <c>gate</c> file exists, then prints an empty object.</summary>
    private static EnvScriptSpec Gated(string dir)
    {
        // EachStart: a restart runs the script again rather than reusing the result of the start.
        EnvScriptSpec script = Shell($"while [ ! -f '{Path.Combine(dir, "gate")}' ]; do sleep 0.05; done; printf '{{}}'");
        script.Rerun = EnvScriptRerun.EachStart;
        return script;
    }

    /// <summary>A command that prints the variable's value into the file, then exits.</summary>
    private static CommandSpec Prints(string name, string file, string variable) =>
        new() { Name = name, Program = "sh", Args = new List<string> { "-c", $"printf '%s' \"${variable}\" > '{file}'" } };

    /// <summary>A command that keeps running until it is stopped or killed.</summary>
    private static CommandSpec Sleeper(string name) => new() { Name = name, Program = "sleep", Args = new List<string> { "30" } };

    /// <summary>
    /// Starts the command with the values of its group's script, which runs first. A gated script needs its gate file, which is made here
    /// and removed again after the run.
    /// </summary>
    private static void Start(Fixture f, CommandEntry c, string? gate)
    {
        if (gate is not null)
        {
            File.WriteAllText(gate, string.Empty);
        }

        EnvPreparation prepared = f.Session.PrepareEnvAsync(new[] { c }, null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(prepared.Succeeded, prepared.Error);
        Assert.Null(f.Session.Activate(new[] { c }, null, prepared.Values));
        Assert.Equal(ProcessState.Running, State(c));
        if (gate is not null)
        {
            File.Delete(gate);
        }
    }

    #endregion Fixture and scripts

    /// <summary>
    /// Cancels a preparation still in progress, and runs the loop until none is left, so that nothing completes after the test. Does nothing
    /// when none is in progress (Ctrl+C would stop the running commands).
    /// </summary>
    private static void Drain(IApplication app, MainController controller, Fixture f)
    {
        if (Preparing(controller) == 0)
        {
            return;
        }

        bool cancelled = false;
        RunLoop(app, controller, () =>
        {
            if (!cancelled)
            {
                cancelled = true;
                Assert.True(controller.OnListKey(Key.C.WithCtrl));
                return false;
            }

            return Preparing(controller) == 0;
        }, "the preparation to be cancelled");
    }

    /// <summary>
    /// Runs the main loop, calling <paramref name="tick"/> every 20 ms on the UI thread, until it returns true. A runnable shown above the
    /// main window (a confirmation) is answered with Enter, its default button (Yes), and the tick waits for it. Fails when the loop runs past
    /// the timeout. Exceptions of the tick are rethrown after the loop stops.
    /// </summary>
    private static void RunLoop(IApplication app, MainController controller, Func<bool> tick, string waitingFor)
    {
        ExceptionDispatchInfo? failure = null;
        DateTime deadline = DateTime.UtcNow + Timeout;
        app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
        {
            try
            {
                if (app.TopRunnable is { } top && !ReferenceEquals(top, controller.Window))
                {
                    app.TopRunnableView?.NewKeyDownEvent(Key.Enter);
                }
                else if (tick())
                {
                    StopLoop(app, controller);
                    return false;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail("Timed out waiting for " + waitingFor + ".");
                }
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
                StopLoop(app, controller);
                return false;
            }

            return true;
        });
        app.Run(controller.Window);
        failure?.Throw();
    }

    /// <summary>Stops the runnable above the main window, if any, and then the main window.</summary>
    private static void StopLoop(IApplication app, MainController controller)
    {
        if (app.TopRunnable is { } top && !ReferenceEquals(top, controller.Window))
        {
            app.RequestStop(top);
        }

        app.RequestStop(controller.Window);
    }

    private static MainController Build(IApplication app, CmdrSession session)
    {
        var controller = new MainController(app, session);
        controller.Build();
        return controller;
    }

    /// <summary>Points the panes at the row of the entry, as the list's focus does.</summary>
    private static void Focus(MainController controller, object key) =>
        typeof(MainController).GetMethod("FocusKey", Members)!.Invoke(controller, new[] { key });

    /// <summary>The number of preparations in progress.</summary>
    private static int Preparing(MainController controller) =>
        ((System.Collections.ICollection)Field<object>(controller, "_preparations")).Count;

    /// <summary>The number of completions the controller holds while the quit dialogs are open.</summary>
    private static int Deferred(MainController controller) =>
        ((System.Collections.ICollection)Field<object>(controller, "_deferredCompletions")).Count;

    /// <summary>True when every preparation in progress has its cancellation requested (and there is at least one, when the test expects it).</summary>
    private static bool PreparationsCancelled(MainController controller)
    {
        var preparations = (System.Collections.IEnumerable)Field<object>(controller, "_preparations");
        int count = 0;
        foreach (object preparation in preparations)
        {
            var cancel = (CancellationTokenSource)preparation.GetType().GetProperty("Cancel")!.GetValue(preparation)!;
            if (!cancel.IsCancellationRequested)
            {
                return false;
            }

            count++;
        }

        return count > 0;
    }

    /// <summary>Makes the session unsaved the way the user does: a change in edit mode, then run mode again.</summary>
    private static void MakeDirty(CmdrSession session)
    {
        Assert.Null(session.SwitchToEdit());
        session.AddGroup(null, new GroupSettings("extra", null, GroupMode.Parallel, null, null));
        session.SwitchToRun();
        Assert.True(session.IsDirty);
    }

    private static bool HasButton(Dialog dialog, string text) => dialog.Buttons.Any(button => button.Text.TrimStart('_') == text);

    /// <summary>
    /// Watches for a dialog above the main window that <paramref name="matches"/> recognises. While it is open, <paramref name="whileOpen"/> runs
    /// on the UI thread every 20 ms (the dialog's own loop runs the timer), until it returns true; then the button <paramref name="answer"/> is
    /// pressed. Used for quit prompts, which block the test thread: the test calls the quit and the timer plays the user. The timer stops
    /// after the answer. A failure in <paramref name="whileOpen"/> or a timeout still presses the button, so that the quit returns, and
    /// <see cref="Finish"/> rethrows it.
    /// </summary>
    private sealed class DialogDriver : IDisposable
    {
        private readonly IApplication _app;
        private readonly object? _timer;
        private ExceptionDispatchInfo? _failure;

        public DialogDriver(IApplication app, Func<Dialog, bool> matches, Func<bool> whileOpen, string answer, string waitingFor)
        {
            _app = app;
            string label = answer.TrimStart('_');
            DateTime deadline = DateTime.UtcNow + Timeout;
            _timer = app.AddTimeout(TimeSpan.FromMilliseconds(20), () =>
            {
                if (app.TopRunnableView is not Dialog dialog || !matches(dialog))
                {
                    return true;
                }

                try
                {
                    if (!whileOpen())
                    {
                        if (DateTime.UtcNow >= deadline)
                        {
                            Assert.Fail("Timed out waiting for " + waitingFor + ".");
                        }

                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _failure = ExceptionDispatchInfo.Capture(ex);
                }

                dialog.Buttons.Single(button => button.Text.TrimStart('_') == label).InvokeCommand(Command.Accept);
                return false;
            });
        }

        /// <summary>Stops the watching and rethrows what <paramref name="whileOpen"/> failed with, if anything.</summary>
        public void Finish()
        {
            Dispose();
            _failure?.Throw();
        }

        public void Dispose()
        {
            if (_timer is not null)
            {
                _app.RemoveTimeout(_timer);
            }
        }
    }

    /// <summary>The transient message in the status line, or null when none is shown.</summary>
    private static string? Message(MainController controller) => (string?)typeof(MainController).GetField("_message", Members)!.GetValue(controller);

    private static void ClearMessage(MainController controller) => typeof(MainController).GetField("_message", Members)!.SetValue(controller, null);

    private static T Field<T>(MainController controller, string name) => (T)typeof(MainController).GetField(name, Members)!.GetValue(controller)!;

    private static ProcessState State(CommandEntry entry) => entry.Process.Snapshot.State;

    /// <summary>The text of every row of the screen, as the driver draws it.</summary>
    private static string ScreenText(IApplication app)
    {
        var driver = app.Driver!;
        var text = new StringBuilder();
        for (int r = 0; r < Height; r++)
        {
            for (int c = 0; c < Width; c++)
            {
                text.Append(driver.Contents![r, c].Grapheme);
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The text of the command's retained output lines.</summary>
    private static List<string> Lines(CommandEntry entry) =>
        entry.Process.Output.Buffer.GetLines(0, int.MaxValue).Select(line => line.PlainText).ToList();

    private static List<string> LogLines(string log) => File.Exists(log) ? File.ReadAllLines(log).ToList() : new List<string>();

    private static bool TryReadPid(string file, out int pid)
    {
        pid = 0;
        return File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out pid) && pid > 0;
    }

    /// <summary>True while a process with the pid exists and has not exited.</summary>
    private static bool Alive(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void WaitUntil(Func<bool> condition, string description)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                Assert.Fail("Timed out waiting for " + description + ".");
            }

            Thread.Sleep(20);
        }
    }
}
