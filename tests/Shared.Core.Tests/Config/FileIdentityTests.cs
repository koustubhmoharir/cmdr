using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace Shared.Core.Tests.Config;

public class FileIdentityTests
{
    // Creates a hard link with ln(1). The output is drained and the wait is bounded, so a stuck ln cannot hang the test run.
    internal static void HardLink(string existing, string link)
    {
        var info = new ProcessStartInfo("ln") { RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(existing);
        info.ArgumentList.Add(link);

        using Process process = Process.Start(info)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"'ln {existing} {link}' did not finish.");
        }

        stdout.GetAwaiter().GetResult();
        string error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'ln {existing} {link}' exited with {process.ExitCode}: {error}");
        }
    }

    // Creates a named pipe with mkfifo(1). Returns false when mkfifo is not available, so the test can skip itself.
    private static bool TryCreateFifo(string path)
    {
        var info = new ProcessStartInfo("mkfifo") { RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(path);

        try
        {
            using Process process = Process.Start(info)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"'mkfifo {path}' did not finish.");
            }

            stdout.GetAwaiter().GetResult();
            stderr.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    // Opens a file the way the identity is compared with a path: shared read, so that a rename or delete of the path is allowed.
    private static SafeFileHandle OpenShared(string path) =>
        File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    [Fact]
    public void MissingFile_HasNoIdentity()
    {
        using var dir = new TempDir();

        Assert.Null(FileIdentity.TryGet(dir.File("missing.log")));
    }

    [Fact]
    public void Directory_HasNoIdentity()
    {
        using var dir = new TempDir();

        Assert.Null(FileIdentity.TryGet(dir.Path));
    }

    [Fact]
    public void DifferentFiles_HaveDifferentIdentities()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            return; // no identity is read on other systems
        }

        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.log"), "");
        File.WriteAllText(dir.File("b.log"), "");

        string? a = FileIdentity.TryGet(dir.File("a.log"));
        string? b = FileIdentity.TryGet(dir.File("b.log"));

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HardLinks_HaveTheSameIdentity()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // hard links are created with ln(1) here, which only Linux is tested with
        }

        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.log"), "");
        HardLink(dir.File("a.log"), dir.File("b.log"));

        string? a = FileIdentity.TryGet(dir.File("a.log"));

        Assert.NotNull(a);
        Assert.Equal(a, FileIdentity.TryGet(dir.File("b.log")));
    }

    [Fact]
    public void Handle_and_path_identities_of_one_file_are_equal()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            return; // no identity is read on other systems
        }

        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.log"), "");
        using SafeFileHandle handle = OpenShared(dir.File("a.log"));

        string? fromPath = FileIdentity.TryGet(dir.File("a.log"));

        Assert.NotNull(fromPath);
        Assert.Equal(fromPath, FileIdentity.TryGet(handle));
    }

    [Fact]
    public void Handle_identities_of_different_files_are_different()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            return; // no identity is read on other systems
        }

        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.log"), "");
        File.WriteAllText(dir.File("b.log"), "");
        using SafeFileHandle a = OpenShared(dir.File("a.log"));
        using SafeFileHandle b = OpenShared(dir.File("b.log"));

        string? identityA = FileIdentity.TryGet(a);

        Assert.NotNull(identityA);
        Assert.NotNull(FileIdentity.TryGet(b));
        Assert.NotEqual(identityA, FileIdentity.TryGet(b));
    }

    [Fact]
    public void Hard_linked_handle_and_path_have_the_same_identity()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // hard links are created with ln(1) here, which only Linux is tested with
        }

        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.log"), "");
        HardLink(dir.File("a.log"), dir.File("b.log"));
        using SafeFileHandle handle = OpenShared(dir.File("a.log"));

        string? fromHandle = FileIdentity.TryGet(handle);

        Assert.NotNull(fromHandle);
        Assert.Equal(fromHandle, FileIdentity.TryGet(dir.File("a.log")));
        Assert.Equal(fromHandle, FileIdentity.TryGet(dir.File("b.log")));
    }

    [Fact]
    public void Renamed_file_keeps_the_identity_of_its_open_handle()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            return; // no identity is read on other systems
        }

        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.log"), "");
        using SafeFileHandle handle = OpenShared(dir.File("a.log"));
        string? before = FileIdentity.TryGet(handle);

        File.Move(dir.File("a.log"), dir.File("b.log"));

        Assert.NotNull(before);
        Assert.Equal(before, FileIdentity.TryGet(handle));
        Assert.Equal(before, FileIdentity.TryGet(dir.File("b.log")));
        Assert.Null(FileIdentity.TryGet(dir.File("a.log")));
    }

    [Fact]
    public void Recreated_file_at_the_same_path_has_a_different_identity()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the old inode is kept alive with a hard link, which is created with ln(1) here, only tested on Linux
        }

        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.log"), "old");
        HardLink(dir.File("a.log"), dir.File("kept.log")); // keeps the old inode alive, so the new file cannot reuse its number
        using SafeFileHandle old = OpenShared(dir.File("a.log"));
        string? oldIdentity = FileIdentity.TryGet(old);

        File.Delete(dir.File("a.log"));
        File.WriteAllText(dir.File("a.log"), "new");
        string? newIdentity = FileIdentity.TryGet(dir.File("a.log"));

        Assert.NotNull(oldIdentity);
        Assert.NotNull(newIdentity);
        Assert.NotEqual(oldIdentity, newIdentity);
        Assert.Equal(oldIdentity, FileIdentity.TryGet(old));
        Assert.Equal(oldIdentity, FileIdentity.TryGet(dir.File("kept.log")));
    }

    [Fact]
    public void File_re_created_on_a_reused_inode_has_a_different_identity_when_birth_time_is_reported()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the birth time is read with statx, which only Linux is tested with
        }

        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.log"), "old");
        string? identity = FileIdentity.TryGet(dir.File("a.log"));
        Assert.NotNull(identity);
        if (identity.Split(':').Length < 4)
        {
            return; // this file system reports no birth time, so the identity is device and inode only
        }

        // Delete and re-create until the new file takes the old inode number. Birth times come from a coarse clock, so each cycle waits a little.
        // File systems that never reuse a freed inode leave the loop without a reuse, and the test then has nothing to check.
        for (int cycle = 0; cycle < 10; cycle++)
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(20));
            File.Delete(dir.File("a.log"));
            File.WriteAllText(dir.File("a.log"), "new");
            string? recreated = FileIdentity.TryGet(dir.File("a.log"));
            Assert.NotNull(recreated);
            if (string.Join(':', identity.Split(':')[..3]) == string.Join(':', recreated.Split(':')[..3]))
            {
                // Device and inode match: only the birth time tells the files apart.
                Assert.NotEqual(identity, recreated);
                return;
            }

            identity = recreated;
        }
    }

    [Fact]
    public void Symbolic_link_has_the_identity_of_its_target()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // symbolic links are created here only on Linux
        }

        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.log"), "");
        File.CreateSymbolicLink(dir.File("link.log"), "a.log");

        string? target = FileIdentity.TryGet(dir.File("a.log"));

        Assert.NotNull(target);
        Assert.Equal(target, FileIdentity.TryGet(dir.File("link.log"))); // the path variant follows links, as statx does without AT_SYMLINK_NOFOLLOW
    }

    [Fact]
    public void Fifo_has_no_identity()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // a named pipe is created with mkfifo(1), which only Linux is tested with
        }

        using var dir = new TempDir();
        if (!TryCreateFifo(dir.File("pipe.log")))
        {
            return; // mkfifo is not available here
        }

        Assert.Null(FileIdentity.TryGet(dir.File("pipe.log")));
    }

    [Fact]
    public void Fifo_handle_has_no_identity()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // a named pipe is created with mkfifo(1), which only Linux is tested with
        }

        using var dir = new TempDir();
        if (!TryCreateFifo(dir.File("pipe.log")))
        {
            return; // mkfifo is not available here
        }

        // Read-write, because opening a FIFO read-only would block until a writer opens it; on Linux O_RDWR does not block.
        using SafeFileHandle handle = File.OpenHandle(dir.File("pipe.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

        Assert.Null(FileIdentity.TryGet(handle));
    }

    [Fact]
    public void Closed_handle_has_no_identity()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            return; // no identity is read on other systems
        }

        using var dir = new TempDir();
        File.WriteAllText(dir.File("a.log"), "");
        SafeFileHandle handle = OpenShared(dir.File("a.log"));
        handle.Dispose();

        Assert.Null(FileIdentity.TryGet(handle));
    }
}
