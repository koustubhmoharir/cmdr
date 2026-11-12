using System.Diagnostics;
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

        public IProcessGroup Attach(Process process) => _inner.Attach(process);
    }
}
