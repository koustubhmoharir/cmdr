using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Logvu.Core.Config;
using Shared.Core;

namespace Logvu.Core.Tests.Config;

public sealed class FileSetTests
{
    private const string DesignPattern = "^SheetKraft(?:[0-9]+)?[.]log$";

    /// <summary>Writes a configuration file in the test directory whose logs directory is <paramref name="logsDirectory"/> and loads it.</summary>
    private static LogvuConfig Load(ConfigTestDirectory dir, string prefix, string pattern, string logsDirectory = "Logs")
    {
        string json = "{ \"logsDirectory\": " + JsonSerializer.Serialize(logsDirectory)
            + ", \"filePrefix\": " + JsonSerializer.Serialize(prefix)
            + ", \"fileNamePattern\": " + JsonSerializer.Serialize(pattern) + " }";
        return LogvuConfig.Load(dir.Write("logvu.json", json));
    }

    /// <summary>The basenames of the enumerated members, in the order they are returned.</summary>
    private static string[] Names(LogvuConfig config) =>
        FileSet.Enumerate(config).Select(Path.GetFileName).Select(name => name!).ToArray();

    [Fact]
    public void Design_pattern_admits_exactly_the_numbered_regular_files_and_symlinks_to_them()
    {
        using var dir = new ConfigTestDirectory();
        string logs = dir.Full("Logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "SheetKraft.log"), "a");
        File.WriteAllText(Path.Combine(logs, "SheetKraft1.log"), "b");
        File.WriteAllText(Path.Combine(logs, "SheetKraft12.log"), "c");
        File.WriteAllText(Path.Combine(logs, "SheetKraftWeb.log"), "d");
        File.WriteAllText(Path.Combine(logs, "SheetKraft.log.gz"), "e");
        File.WriteAllText(Path.Combine(logs, "SheetKraft2.log.zip"), "f");
        File.WriteAllText(Path.Combine(logs, "Other.log"), "g");
        Directory.CreateDirectory(Path.Combine(logs, "SheetKraft3.log"));
        File.WriteAllText(Path.Combine(logs, "SheetKraft3.log", "SheetKraft9.log"), "nested");
        string[] expected = ["SheetKraft.log", "SheetKraft1.log", "SheetKraft12.log"];

        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(Path.Combine(logs, "SheetKraft4.log"), "SheetKraft1.log");
            File.CreateSymbolicLink(Path.Combine(logs, "SheetKraft5.log"), "missing.log");
            File.CreateSymbolicLink(Path.Combine(logs, "SheetKraft6.log"), "SheetKraft3.log");
            expected = [.. expected, "SheetKraft4.log"];
        }

        LogvuConfig config = Load(dir, "SheetKraft", DesignPattern);

        Assert.Equal(expected, Names(config));
        Assert.Equal(Path.Combine(logs, "SheetKraft1.log"), FileSet.Enumerate(config)[1]);
    }

    [Fact]
    public void Compressed_files_are_excluded_even_when_the_pattern_admits_them()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("Logs/SheetKraft.log", "a");
        dir.Write("Logs/SheetKraft.log.gz", "b");
        dir.Write("Logs/SheetKraft.log.GZ", "c");
        dir.Write("Logs/SheetKraft2.log.zip", "d");
        dir.Write("Logs/SheetKraft3.tar.BZ2", "e");
        dir.Write("Logs/SheetKraft4.log.xz", "f");
        dir.Write("Logs/SheetKraft5.log.zst", "g");
        dir.Write("Logs/SheetKraft6.7z", "h");
        dir.Write("Logs/SheetKraft7.log.br", "i");
        dir.Write("Logs/SheetKraft8.log.lz4", "j");

        LogvuConfig config = Load(dir, "SheetKraft", "^SheetKraft.*$");

        Assert.Equal(["SheetKraft.log"], Names(config));
    }

    [Fact]
    public void Missing_directory_is_an_empty_set()
    {
        using var dir = new ConfigTestDirectory();
        LogvuConfig config = Load(dir, "SheetKraft", DesignPattern, logsDirectory: "NotCreatedYet");

        Assert.Empty(FileSet.Enumerate(config));
    }

    [Fact]
    public void Directory_that_is_a_file_is_an_empty_set()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("Logs", "not a directory");
        LogvuConfig config = Load(dir, "SheetKraft", DesignPattern);

        Assert.Empty(FileSet.Enumerate(config));
    }

    [Fact]
    public void Unreadable_directory_throws_a_file_set_exception_naming_it()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new ConfigTestDirectory();
        string logs = dir.Full("Logs");
        Directory.CreateDirectory(logs);
        LogvuConfig config = Load(dir, "SheetKraft", DesignPattern);
        File.SetUnixFileMode(logs, UnixFileMode.None);
        try
        {
            if (!CanEnumerate(logs))
            {
                FileSetException ex = Assert.Throws<FileSetException>(() => FileSet.Enumerate(config));
                Assert.Contains(logs, ex.Message);
            }
        }
        finally
        {
            File.SetUnixFileMode(logs, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Tells whether the directory can be listed; a process running as root can list a directory without permissions.</summary>
    private static bool CanEnumerate(string directory)
    {
        try
        {
            _ = new DirectoryInfo(directory).EnumerateFileSystemInfos().ToList();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Fact]
    public void Dated_names_are_admitted_without_an_active_file()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("Logs/TaskScheduler.log2026-11-17.log", "a");
        dir.Write("Logs/TaskScheduler.log2026-11-18.log", "b");
        dir.Write("Logs/TaskScheduler.log2026-11-18.log.gz", "c");
        dir.Write("Logs/TaskScheduler.logtoday.log", "d");
        LogvuConfig config = Load(dir, "TaskScheduler", "^TaskScheduler[.]log[0-9]{4}-[0-9]{2}-[0-9]{2}[.]log$");

        Assert.Null(config.ActiveFile);
        Assert.Equal(["TaskScheduler.log2026-11-17.log", "TaskScheduler.log2026-11-18.log"], Names(config));
    }

    [Fact]
    public void Serilog_day_and_sequence_names_are_admitted()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("Logs/log20261118.json", "a");
        dir.Write("Logs/log20261118_001.json", "b");
        dir.Write("Logs/log20261118_0010.json", "c");
        dir.Write("Logs/logX.json", "d");
        LogvuConfig config = Load(dir, "log", "^log[0-9]{8}(?:_[0-9]{3})?[.]json$");

        Assert.Equal(["log20261118.json", "log20261118_001.json"], Names(config));
    }

    [Fact]
    public void Prefix_matches_case_insensitively_only_where_the_file_system_does()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("Logs/sheetkraft.log", "a");
        dir.Write("Logs/SHEETKRAFT.log", "b");
        LogvuConfig config = Load(dir, "SheetKraft", "^(?i)SheetKraft[.]log$");

        string[] expected = PathComparison.Comparer.Equals("a", "A")
            ? ["SHEETKRAFT.log", "sheetkraft.log"]
            : [];

        Assert.Equal(expected, Names(config));
    }

    [Fact]
    public void Prefix_must_match_not_just_the_pattern()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("Logs/Other1.log", "a");
        LogvuConfig config = Load(dir, "SheetKraft", "^[A-Za-z]+[0-9][.]log$");

        Assert.Empty(Names(config));
    }

    [Fact]
    public void Members_are_listed_only_from_the_directory_itself()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("Logs/Sub/SheetKraft.log", "a");
        dir.Write("Logs/SheetKraft.log", "b");
        LogvuConfig config = Load(dir, "SheetKraft", DesignPattern);

        Assert.Equal(["SheetKraft.log"], Names(config));
    }

    [Fact]
    public void Full_path_of_each_member_is_in_the_logs_directory()
    {
        using var dir = new ConfigTestDirectory();
        dir.Write("Logs/SheetKraft.log", "a");
        LogvuConfig config = Load(dir, "SheetKraft", DesignPattern);

        Assert.Equal([Path.Combine(config.LogsDirectory, "SheetKraft.log")], FileSet.Enumerate(config));
    }

    /// <summary>Creates a named pipe with mkfifo(1). Returns false when mkfifo is not available, so the caller can skip.</summary>
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

    [Fact]
    public void Fifos_and_symbolic_links_to_them_are_not_members()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // named pipes are created with mkfifo(1), which only Linux is tested with
        }

        using var dir = new ConfigTestDirectory();
        string logs = dir.Full("Logs");
        Directory.CreateDirectory(logs);
        dir.Write("Logs/SheetKraft.log", "a");
        if (!TryCreateFifo(Path.Combine(logs, "SheetKraft1.log")))
        {
            return; // mkfifo is not available here
        }

        File.CreateSymbolicLink(Path.Combine(logs, "SheetKraft2.log"), "SheetKraft1.log");
        LogvuConfig config = Load(dir, "SheetKraft", DesignPattern);

        Assert.Equal(["SheetKraft.log"], Names(config));
    }
}
