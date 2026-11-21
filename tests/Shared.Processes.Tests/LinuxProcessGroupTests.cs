using static Shared.Processes.Tests.ProcessTestHelpers;

namespace Shared.Processes.Tests;

/// <summary>Linux process group behaviour: detaching, and how unreadable /proc entries are classified.</summary>
public class LinuxProcessGroupTests
{
    /// <summary>True when the process no longer exists or is a zombie (killed, not yet reaped).</summary>
    internal static bool IsProcessGone(int pid)
    {
        try
        {
            string stat = File.ReadAllText($"/proc/{pid}/stat");
            int close = stat.LastIndexOf(')');
            return close >= 0 && close + 2 < stat.Length && stat[close + 2] is 'Z' or 'X';
        }
        catch (IOException)
        {
            return true;
        }
    }

    [Fact]
    public void InterruptAndKillDoNothingAfterDetach()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var dir = new TempDir();
        var platform = new LinuxProcessPlatform();
        LaunchedTree tree = platform.Launch(platform.Prepare(Exec("/bin/sleep", new[] { "30" }, dir.Path)), _ => { });
        int pid = tree.Process.Id;
        try
        {
            tree.Group.Detach();

            // SIGINT would end sleep (default disposition), and SIGKILL certainly would: both must be dropped.
            tree.Group.Interrupt();
            tree.Group.Kill();
            Thread.Sleep(500);

            Assert.False(IsProcessGone(pid), $"sleep (pid {pid}) was signalled after Detach");
        }
        finally
        {
            // Detached, so the group cannot be used to kill it: signal the group id (the leader's pid, from setsid) directly.
            LinuxNative.Kill(-pid, 9);
            WaitUntil(() => IsProcessGone(pid), "detached sleep to die", timeout: TimeSpan.FromSeconds(5));
            tree.Group.Dispose();
            tree.Process.Dispose();
        }
    }

    [Fact]
    public void UnreadableStatCountsAsUnknownAndAVanishedProcessIsSkipped()
    {
        using var dir = new TempDir();
        foreach (string name in new[] { "100", "200", "300" })
        {
            Directory.CreateDirectory(Path.Combine(dir.Path, name));
        }

        // 100 is not readable, 200 exits during the read, 300 is a live member of group 42.
        Func<string, string> read = statPath => Path.GetFileName(Path.GetDirectoryName(statPath)) switch
        {
            "100" => throw new UnauthorizedAccessException("permission denied"),
            "200" => throw new FileNotFoundException("gone", statPath),
            _ => "300 (sh) S 1 42 42 0 -1",
        };

        IOException ex = Assert.Throws<IOException>(() => LinuxProcessGroup.CountLiveProcesses(42, dir.Path, read));
        Assert.Contains("100", ex.Message, StringComparison.Ordinal);

        // Without the unreadable entry, the vanished one is skipped and the live one is counted.
        Directory.Delete(Path.Combine(dir.Path, "100"));
        Assert.Equal(1, LinuxProcessGroup.CountLiveProcesses(42, dir.Path, read));
    }
}
