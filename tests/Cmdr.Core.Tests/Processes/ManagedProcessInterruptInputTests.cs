using System.Text;
using Cmdr.Core.Config;
using Cmdr.Core.Output;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;

namespace Cmdr.Core.Tests.Processes;

/// <summary>Tests the <c>InputAfterInterrupt</c> write of <see cref="ManagedProcess"/> against real processes (Linux only).</summary>
public class ManagedProcessInterruptInputTests
{
    private const int BufferLines = 1000;

    [Fact]
    public void InputAfterInterruptIsWrittenToStdinAfterCtrlC()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var p = new ManagedProcess(BufferLines, new InputAfterInterruptPlatform(new LinuxProcessPlatform(), "y\n"));
        try
        {
            // Single quotes keep $a for the trap body: it is expanded when the trap runs, not when it is set.
            p.Start(Sh("trap 'read a; echo got-$a; exit 0' INT; echo ready; while :; do sleep 0.1; done", dir.Path));
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);
            // Wait for the trap to be installed; an earlier Ctrl+C would kill the shell with the default action.
            WaitUntil(() => Text(p).Contains("ready", StringComparison.Ordinal), "shell ready", p);

            p.RequestStop();
            WaitForExited(p);

            Assert.Equal(0, p.Snapshot.ExitCode);
            // The exit marker can be written before the trap's output is pumped (the drain is bounded), so wait for it.
            WaitUntil(() => Text(p).Contains("got-y", StringComparison.Ordinal), "trap output 'got-y'", p);
            Assert.Contains("got-y", Text(p));
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public async Task ConcurrentInputStopsAreSerializedAndDeliverEveryByte()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        const int workers = 12;
        string input = new string('x', 128 * 1024) + "\n";
        string gate = Path.Combine(dir.Path, "read-stdin");
        string captured = Path.Combine(dir.Path, "stdin.txt");
        int expectedLength = input.Length * workers;
        string script = $"echo ready; while [ ! -f '{gate}' ]; do sleep 0.01; done; head -c {expectedLength} > '{captured}'";
        using var barrier = new Barrier(workers + 1);
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh(script, dir.Path, stopMethod: StopMethod.Input, stopInput: input));
            WaitForText(p, "ready");

            Task[] stops = Enumerable.Range(0, workers)
                .Select(_ => Task.Factory.StartNew(() =>
                {
                    barrier.SignalAndWait();
                    p.RequestStop();
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
                .ToArray();
            barrier.SignalAndWait();

            // Keep the child from draining stdin briefly so several callers contend on the same writer.
            await Task.Delay(100);
            File.WriteAllText(gate, "go");
            await Task.WhenAll(stops).WaitAsync(TimeSpan.FromSeconds(10));
            await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            string actual = File.ReadAllText(captured, Encoding.UTF8);
            Assert.Equal(string.Concat(Enumerable.Repeat(input, workers)), actual);
        }
        finally
        {
            Cleanup(p);
        }
    }

    [Fact]
    public async Task ForceKillRemainsAvailableWhileStopInputIsBlocked()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        string input = new string('x', 2 * 1024 * 1024);
        var p = new ManagedProcess(BufferLines);
        try
        {
            p.Start(Sh("echo ready; sleep 30", dir.Path, stopMethod: StopMethod.Input, stopInput: input));
            WaitForText(p, "ready");

            Task stop = Task.Factory.StartNew(p.RequestStop, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            WaitUntil(() => p.Snapshot.State == ProcessState.Stopping, "input stop dispatch", p);
            await Task.Run(p.ForceKill).WaitAsync(TimeSpan.FromSeconds(2));
            await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await stop.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            Cleanup(p);
        }
    }

    /// <summary>Delegates to Linux launching but supplies an input to write after Ctrl+C.</summary>
    private sealed class InputAfterInterruptPlatform : IProcessPlatform
    {
        private readonly IProcessPlatform _inner;
        private readonly string _input;

        public InputAfterInterruptPlatform(IProcessPlatform inner, string input)
        {
            _inner = inner;
            _input = input;
        }

        public PreparedLaunch Prepare(LaunchSpec spec)
        {
            PreparedLaunch launch = _inner.Prepare(spec);
            return new PreparedLaunch(launch.StartInfo, _input);
        }

        public LaunchedTree Launch(PreparedLaunch launch, Action<ILaunchedProcess> onExited) => _inner.Launch(launch, onExited);
    }
}
