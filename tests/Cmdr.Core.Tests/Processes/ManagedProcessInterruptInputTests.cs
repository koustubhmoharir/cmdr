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
            p.Start(Sh("trap 'read a; echo got-$a; exit 0' INT; while :; do sleep 0.1; done", dir.Path));
            WaitUntil(() => p.Snapshot.State == ProcessState.Running, "running", p);
            Thread.Sleep(200); // Let the trap be installed.

            p.RequestStop();
            WaitForExited(p);

            Assert.Equal(0, p.Snapshot.ExitCode);
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
