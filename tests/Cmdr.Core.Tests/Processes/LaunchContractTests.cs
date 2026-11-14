using Cmdr.Core.Config;
using Cmdr.Core.Processes;
using Cmdr.Core.Tests.Config;
using static Cmdr.Core.Tests.Processes.ProcessTestHelpers;

namespace Cmdr.Core.Tests.Processes;

/// <summary>The <see cref="IProcessPlatform.Launch"/> contract, checked against the Linux platform.</summary>
public class LaunchContractTests
{
    [Fact]
    public async Task LaunchInvokesTheExitCallbackOnceWithTheReturnedInstance()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var platform = new LinuxProcessPlatform();
        PreparedLaunch launch = platform.Prepare(Sh("exit 3", dir.Path));

        int calls = 0;
        ILaunchedProcess? callbackArg = null;
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LaunchedTree tree = platform.Launch(launch, p =>
        {
            Interlocked.Increment(ref calls);
            callbackArg = p;
            exited.TrySetResult();
        });
        try
        {
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(200); // a second invocation, if any, would have run by now

            Assert.Equal(1, Volatile.Read(ref calls));
            Assert.Same(tree.Process, callbackArg);
            Assert.Equal(3, tree.Process.ExitCode);
            Assert.True(tree.Process.Id > 0);
        }
        finally
        {
            tree.Group.Dispose();
            tree.Process.Dispose();
        }
    }

    [Fact]
    public async Task LaunchThatFailsToStartThrowsAndNeverInvokesTheExitCallback()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var platform = new LinuxProcessPlatform();
        // A missing working directory makes the start itself fail. A missing program does not: setsid starts and exits non-zero.
        PreparedLaunch launch = platform.Prepare(Sh("exit 0", Path.Combine(dir.Path, "does-not-exist")));

        int calls = 0;
        Assert.ThrowsAny<System.ComponentModel.Win32Exception>(() => platform.Launch(launch, _ => Interlocked.Increment(ref calls)));
        await Task.Delay(100);
        Assert.Equal(0, Volatile.Read(ref calls));
    }
}
