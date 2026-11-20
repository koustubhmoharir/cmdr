using System.Diagnostics;
using Cmdr.Core.Config;
using Cmdr.Core.Session;
using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>The copied command line: it sets the working directory and the environment of the command, and nothing else.</summary>
public sealed class CopyCommandTests
{
    private static readonly IReadOnlyDictionary<string, string?> BaseEnvironment = new Dictionary<string, string?>
    {
        ["GONE"] = "was",
        ["KEEP"] = "k",
    };

    [Fact]
    public void Copy_posix_sets_the_directory_and_the_group_and_command_environment_for_the_command_only()
    {
        using TempSession temp = TempSession.Open(new GroupSpec
        {
            Name = "top",
            Cwd = "src",
            Env = new Dictionary<string, string?> { ["TOP"] = "t", ["GONE"] = null },
            Items =
            {
                new CommandSpec
                {
                    Name = "run",
                    Program = "tool",
                    Args = { "a b" },
                    Cwd = "bin",
                    Env = new Dictionary<string, string?> { ["SHARED"] = "${cwd}" },
                },
            },
        });
        CommandEntry entry = temp.Session.AllEntries.Single();
        string bin = Path.GetFullPath(Path.Combine(temp.Session.ConfigDir, "src", "bin"));

        string line = CommandLineFormatter.ForCopy(entry, powerShell: false, BaseEnvironment, ':');

        string expected = $"(cd {CommandLineFormatter.QuotePosix(bin)} && env -u GONE TOP=t SHARED={bin} tool 'a b')";
        Assert.Equal(expected, line);
    }

    [Fact]
    public void Copy_expands_env_placeholders_in_arguments_from_group_env_and_the_base_environment_and_cwd()
    {
        using TempSession temp = TempSession.Open(new GroupSpec
        {
            Name = "top",
            Env = new Dictionary<string, string?> { ["TOP"] = "t" },
            Items =
            {
                new CommandSpec
                {
                    Name = "run",
                    Program = "tool",
                    Args = { "${env:TOP}", "${env:KEEP}", "${env:BASE_ONLY}", "${cwd}", "in${input:x}", "$${env:TOP}" },
                },
            },
        });
        CommandEntry entry = temp.Session.AllEntries.Single();
        string dir = Path.GetFullPath(temp.Session.ConfigDir);
        var environment = new Dictionary<string, string?>(BaseEnvironment) { ["BASE_ONLY"] = "b" };

        string line = CommandLineFormatter.ForCopy(entry, powerShell: false, environment, ':');

        string q = CommandLineFormatter.QuotePosix(dir);
        Assert.Equal(
            $"(cd {q} && env TOP=t tool t k b {q} {CommandLineFormatter.QuotePosix("in${input:x}")} {CommandLineFormatter.QuotePosix("${env:TOP}")})",
            line);
    }

    [Fact]
    public void Copy_arguments_read_the_commands_own_env_override()
    {
        using TempSession temp = TempSession.Open(new GroupSpec
        {
            Name = "top",
            Env = new Dictionary<string, string?> { ["V"] = "group" },
            Items = { new CommandSpec { Name = "run", Program = "echo", Args = { "${env:V}" }, Env = new Dictionary<string, string?> { ["V"] = "own" } } },
        });
        CommandEntry entry = temp.Session.AllEntries.Single();
        string dir = Path.GetFullPath(temp.Session.ConfigDir);

        string line = CommandLineFormatter.ForCopy(entry, powerShell: false, BaseEnvironment, ':');

        Assert.Equal($"(cd {CommandLineFormatter.QuotePosix(dir)} && env V=own echo own)", line);
    }

    [Fact]
    public void Copy_of_a_command_without_environment_only_sets_the_directory()
    {
        using TempSession temp = TempSession.Open(new GroupSpec
        {
            Name = "g",
            Items = { new CommandSpec { Name = "run", Program = "echo", Args = { "hi" } } },
        });
        CommandEntry entry = temp.Session.AllEntries.Single();
        string dir = Path.GetFullPath(temp.Session.ConfigDir);

        string line = CommandLineFormatter.ForCopy(entry, powerShell: false, BaseEnvironment, ':');

        Assert.Equal($"(cd {CommandLineFormatter.QuotePosix(dir)} && echo hi)", line);
    }

    [Fact]
    public void Copy_with_an_unresolvable_cwd_is_the_plain_command_line()
    {
        using TempSession temp = TempSession.Open(new GroupSpec
        {
            Name = "bad",
            Cwd = "bad\0path",
            Env = new Dictionary<string, string?> { ["X"] = "1" },
            Items = { new CommandSpec { Name = "cmd", Program = "echo", Args = { "hi" } } },
        });
        CommandEntry entry = temp.Session.AllEntries.Single();

        Assert.Equal(CommandLineFormatter.ForEntry(entry, powerShell: false), CommandLineFormatter.ForCopy(entry, false, BaseEnvironment, ':'));
        Assert.Equal(CommandLineFormatter.ForEntry(entry, powerShell: true), CommandLineFormatter.ForCopy(entry, true, BaseEnvironment, ';'));
        Assert.Equal("echo hi", CommandLineFormatter.ForCopy(entry, false, BaseEnvironment, ':'));
    }

    [Fact]
    public void Copy_powershell_of_a_batch_file_that_needs_cmd_keeps_the_command_in_the_variable()
    {
        // Batch files and their cmd.exe form are Windows only (the program is resolved with Windows path rules), so this is
        // skipped elsewhere. The argument has a & that PowerShell would mangle.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string dir = Directory.CreateTempSubdirectory("cmdr-copy-batch-").FullName;
        try
        {
            string batch = Path.Combine(dir, "tool.cmd");
            File.WriteAllText(batch, "@echo off\r\n");
            using TempSession temp = TempSession.Open(new GroupSpec
            {
                Name = "g",
                Items = { new CommandSpec { Name = "run", Program = batch, Args = { "a&b" } } },
            });
            CommandEntry entry = temp.Session.AllEntries.Single();

            string line = CommandLineFormatter.ForCopy(entry, powerShell: true, BaseEnvironment, ';');

            Assert.StartsWith("& { $saved = @{}; foreach ($name in @('CMDR_CMD')) ", line, StringComparison.Ordinal);
            Assert.Contains("[Environment]::SetEnvironmentVariable('CMDR_CMD', '", line, StringComparison.Ordinal);
            Assert.Contains("; cmd /d /v:off /c '%CMDR_CMD%' } finally", line, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Copied_posix_line_runs_in_the_directory_with_the_environment_and_leaves_the_parent_shell_unchanged()
    {
        // Runs the copied line in /bin/sh, with a base environment that the copy edits (GONE is removed, CMDR_T1 is set), then
        // checks the directory and the variables after the line, in the same shell. Skipped on Windows, or when /bin/sh is absent.
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
        {
            return;
        }

        string work = Directory.CreateTempSubdirectory("cmdr-copy-work-").FullName;
        string origin = Directory.CreateTempSubdirectory("cmdr-copy-origin-").FullName;
        try
        {
            using TempSession temp = TempSession.Open(new GroupSpec
            {
                Name = "g",
                Items =
                {
                    new CommandSpec
                    {
                        Name = "run",
                        Program = "sh",
                        Args = { "-c", @"printf ""%s|%s|%s"" ""$PWD"" ""$CMDR_T1"" ""${GONE-unset}""" },
                        Cwd = work,
                        Env = new Dictionary<string, string?> { ["CMDR_T1"] = "hello world", ["GONE"] = null },
                    },
                },
            });
            CommandEntry entry = temp.Session.AllEntries.Single();

            var environment = new Dictionary<string, string?>
            {
                ["PATH"] = Environment.GetEnvironmentVariable("PATH"),
                ["PWD"] = origin,
                ["GONE"] = "was",
            };
            string copied = CommandLineFormatter.ForCopy(entry, powerShell: false, environment, ':');
            string script = copied + @"; printf ""|%s|%s"" ""$PWD"" ""${GONE-unset}""";

            string actual = RunSh(script, origin, environment);

            Assert.Equal($"{Path.GetFullPath(work)}|hello world|unset|{Path.GetFullPath(origin)}|was", actual);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
            Directory.Delete(origin, recursive: true);
        }
    }

    /// <summary>Runs a script in /bin/sh from the given directory, with exactly the given environment, and returns its stdout.</summary>
    private static string RunSh(string script, string directory, IReadOnlyDictionary<string, string?> environment)
    {
        var info = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = directory,
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(script);
        info.Environment.Clear();
        foreach (KeyValuePair<string, string?> entry in environment)
        {
            info.Environment[entry.Key] = entry.Value;
        }

        using Process process = Process.Start(info) ?? throw new InvalidOperationException("/bin/sh did not start");
        process.StandardInput.Close();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeSpan.FromSeconds(20)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The copied line did not finish within 20 s.");
        }

        Task.WaitAll(stdout, stderr);
        Assert.True(process.ExitCode == 0, $"exit {process.ExitCode}: {stderr.Result}");
        return stdout.Result;
    }
}
