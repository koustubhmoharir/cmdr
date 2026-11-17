using System.Diagnostics;
using Cmdr.Tui;

namespace Cmdr.Tests;

public sealed class CommandLineFormatterTests
{
    [Theory]
    [InlineData("sh", "sh")]
    [InlineData("/usr/bin/env-x_1.2", "/usr/bin/env-x_1.2")]
    [InlineData("a@b%c+d=e:f,g", "a@b%c+d=e:f,g")]
    public void Posix_bare_tokens_are_not_quoted(string token, string expected)
    {
        Assert.Equal(expected, CommandLineFormatter.QuotePosix(token));
    }

    [Fact]
    public void Posix_token_with_spaces_is_single_quoted()
    {
        Assert.Equal("'while :; do date; sleep 1; done'", CommandLineFormatter.QuotePosix("while :; do date; sleep 1; done"));
    }

    [Fact]
    public void Posix_embedded_single_quote_is_closed_escaped_and_reopened()
    {
        Assert.Equal("'it'\\''s'", CommandLineFormatter.QuotePosix("it's"));
    }

    [Fact]
    public void Posix_empty_token_is_two_single_quotes()
    {
        Assert.Equal("''", CommandLineFormatter.QuotePosix(""));
    }

    [Theory]
    [InlineData("$HOME")]
    [InlineData("*.txt")]
    [InlineData("a&b")]
    [InlineData("a\"b")]
    [InlineData("back\\slash")]
    public void Posix_special_characters_are_quoted(string token)
    {
        string quoted = CommandLineFormatter.QuotePosix(token);

        Assert.StartsWith("'", quoted);
        Assert.EndsWith("'", quoted);
        Assert.Equal(token, quoted[1..^1]);
    }

    [Fact]
    public void Posix_non_ascii_letters_are_quoted()
    {
        Assert.Equal("'é'", CommandLineFormatter.QuotePosix("é"));
    }

    [Fact]
    public void Format_joins_the_quoted_program_and_arguments()
    {
        string line = CommandLineFormatter.Format("sh", new[] { "-c", "while :; do date; sleep 1; done" }, powerShell: false);

        Assert.Equal("sh -c 'while :; do date; sleep 1; done'", line);
    }

    [Fact]
    public void Format_with_no_arguments_is_the_program_alone()
    {
        Assert.Equal("ls", CommandLineFormatter.Format("ls", Array.Empty<string>(), powerShell: false));
    }

    [Theory]
    [InlineData("ls", "ls")]
    [InlineData("./run.sh", "./run.sh")]
    [InlineData("If", "If")] // sh's reserved words are case-sensitive
    [InlineData("if", "'if'")]
    [InlineData("do", "'do'")]
    [InlineData("coproc", "'coproc'")]
    [InlineData("X=1", "'X=1'")]
    public void Posix_program_in_command_position_is_quoted_only_when_sh_would_read_it_as_syntax(string program, string expected)
    {
        Assert.Equal(expected, CommandLineFormatter.Format(program, Array.Empty<string>(), powerShell: false));
    }

    [Theory]
    [InlineData("if")]
    [InlineData("do")]
    [InlineData("X=1")]
    public void Posix_program_named_like_sh_syntax_runs_when_the_copied_line_is_run_by_sh(string name)
    {
        // Runs the formatted line in /bin/sh with a directory of scripts first on PATH. Skipped on Windows, or when /bin/sh is absent.
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
        {
            return;
        }

        string dir = Path.Combine(Path.GetTempPath(), "cmdr-posix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string script = Path.Combine(dir, name);
            File.WriteAllText(script, "#!/bin/sh\necho EXECUTED\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            string line = CommandLineFormatter.Format(name, Array.Empty<string>(), powerShell: false);
            var info = new ProcessStartInfo("/bin/sh")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(line);
            info.Environment["PATH"] = dir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

            (string stdout, string stderr, int exitCode) = RunBounded(info, TimeSpan.FromSeconds(20));

            Assert.True(
                exitCode == 0 && stdout == "EXECUTED\n",
                $"line: {line}\nexit: {exitCode}\nstdout: {stdout}\nstderr: {stderr}");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("tool.exe")]
    [InlineData("--flag=1")]
    [InlineData("C:\\bin\\x.exe")]
    [InlineData("-v")]
    public void PowerShell_bare_tokens_are_not_quoted(string token)
    {
        Assert.Equal(token, CommandLineFormatter.QuotePowerShell(token));
    }

    [Theory]
    [InlineData("a&b", "'a&b'")]
    [InlineData("x|y", "'x|y'")]
    [InlineData("x>out", "'x>out'")]
    [InlineData("%TEMP%", "'%TEMP%'")]
    [InlineData("!x", "'!x'")]
    [InlineData("$(Write-Output unexpected)", "'$(Write-Output unexpected)'")]
    [InlineData("$env:PATH", "'$env:PATH'")]
    [InlineData("a b", "'a b'")]
    [InlineData("", "''")]
    [InlineData("it's", "'it''s'")]
    [InlineData("it\u2019s", "'it\u2019\u2019s'")]
    [InlineData("\"quoted\"", "'\"quoted\"'")]
    [InlineData("caf\u00e9", "'caf\u00e9'")]
    public void PowerShell_tokens_with_special_characters_are_single_quoted(string token, string expected)
    {
        Assert.Equal(expected, CommandLineFormatter.QuotePowerShell(token));
    }

    [Fact]
    public void PowerShell_trailing_backslash_stays_bare()
    {
        Assert.Equal("C:\\dir\\", CommandLineFormatter.QuotePowerShell("C:\\dir\\"));
    }

    [Fact]
    public void PowerShell_format_quotes_each_argument_and_the_program_is_invoked_with_the_call_operator_when_quoted()
    {
        Assert.Equal("& 'my tool' 'a b' c", CommandLineFormatter.Format("my tool", new[] { "a b", "c" }, powerShell: true));
    }

    [Fact]
    public void PowerShell_program_with_a_space_is_invoked_with_the_call_operator()
    {
        Assert.Equal(
            "& 'C:\\Program Files\\x\\x.exe' arg",
            CommandLineFormatter.Format("C:\\Program Files\\x\\x.exe", new[] { "arg" }, powerShell: true));
    }

    [Fact]
    public void PowerShell_bare_program_has_no_call_operator()
    {
        Assert.Equal("tool.exe a", CommandLineFormatter.Format("tool.exe", new[] { "a" }, powerShell: true));
    }

    [Fact]
    public void PowerShell_batch_command_runs_through_cmd_from_an_environment_variable_saved_and_restored()
    {
        const string command = @"C:\bin\pnpm.cmd ^""a^&ver^"" 'q'";
        Assert.Equal(
            @"& { $saved = $env:CMDR_CMD; $env:CMDR_CMD = 'C:\bin\pnpm.cmd ^""a^&ver^"" ''q'''; try { cmd /d /v:off /c '%CMDR_CMD%' } finally { $env:CMDR_CMD = $saved } }",
            CommandLineFormatter.Format("pnpm", new[] { "a&ver" }, powerShell: true, batchCommand: command));
    }

    [Fact]
    public void PowerShell_argument_equal_to_the_stop_parsing_token_is_passed_in_an_array_after_the_program()
    {
        Assert.Equal(
            "& /usr/bin/printf @('%s\\n', '--%', 'a b', '%CMDR_PROBE%')",
            CommandLineFormatter.Format("/usr/bin/printf", new[] { "%s\\n", "--%", "a b", "%CMDR_PROBE%" }, powerShell: true));
    }

    [Fact]
    public void PowerShell_literal_stop_parsing_argument_reaches_a_native_program_unchanged()
    {
        // The plain form would lose "--%" and everything after it to PowerShell's stop-parsing. Skipped when pwsh (or
        // /usr/bin/printf) is not installed.
        string? pwsh = FindOnPath("pwsh");
        if (pwsh is null || !File.Exists("/usr/bin/printf"))
        {
            return;
        }

        string line = CommandLineFormatter.Format("/usr/bin/printf", new[] { "%s\\n", "--%", "a b", "%CMDR_PROBE%" }, powerShell: true);
        string actual = RunPwsh(pwsh, line, new Dictionary<string, string?> { ["CMDR_PROBE"] = "EXPANDED" });

        Assert.Equal("--%\na b\n%CMDR_PROBE%\n", actual);
    }

    [Theory]
    [InlineData("original", "original")]
    [InlineData(null, "<absent>")]
    public void PowerShell_batch_form_runs_the_cmd_command_and_restores_the_variable(string? preset, string expectedAfter)
    {
        // A function named cmd stands in for cmd.exe (it is found before any native command): it prints the value the
        // variable has while the command runs, and the script then prints the variable's value after the block.
        string? pwsh = FindOnPath("pwsh");
        if (pwsh is null)
        {
            return;
        }

        const string command = @"C:\Program^ Files\x\pnpm.cmd ^""a^&ver^"" ^""%TEMP%^"" ^""it's^"" ^""it’s^"" ^""x^|y^"" $(boom) `n";
        string line = CommandLineFormatter.Format("pnpm", new[] { "a&ver" }, powerShell: true, batchCommand: command);
        string script = "function cmd { [Console]::Out.Write($env:CMDR_CMD) }; " + line
            + "; [Console]::Out.Write('|' + $(if (Test-Path Env:CMDR_CMD) { $env:CMDR_CMD } else { '<absent>' }))";

        string actual = RunPwsh(pwsh, script, new Dictionary<string, string?> { ["CMDR_CMD"] = preset });

        Assert.Equal(command + "|" + expectedAfter, actual);
    }

    [Fact]
    public void Batch_command_is_ignored_for_posix()
    {
        Assert.Equal("pnpm 'a&ver'", CommandLineFormatter.Format("pnpm", new[] { "a&ver" }, powerShell: false, batchCommand: "x"));
    }

    [Theory]
    [InlineData("foreach", "& 'foreach'")]
    [InlineData("If", "& 'If'")]
    [InlineData("123", "& '123'")]
    [InlineData("-x", "& '-x'")]
    [InlineData("tool", "tool")]
    [InlineData(@".\tool.exe", @".\tool.exe")]
    [InlineData("../bin/tool", "../bin/tool")]
    [InlineData(@"C:\bin\tool.exe", @"C:\bin\tool.exe")]
    public void PowerShell_program_that_would_not_parse_as_a_command_is_invoked_with_the_call_operator(string program, string expected)
    {
        Assert.Equal(expected, CommandLineFormatter.Format(program, Array.Empty<string>(), powerShell: true));
    }

    [Fact]
    public void PowerShell_format_with_no_arguments_is_the_program_alone()
    {
        Assert.Equal("tool.exe", CommandLineFormatter.Format("tool.exe", Array.Empty<string>(), powerShell: true));
    }

    [Fact]
    public void PowerShell_format_quotes_the_shell_metacharacters_of_arguments()
    {
        string line = CommandLineFormatter.Format("tool.exe", new[] { "a&b", "x|y", "%TEMP%" }, powerShell: true);

        Assert.Equal("tool.exe 'a&b' 'x|y' '%TEMP%'", line);
    }

    [Fact]
    public void PowerShell_native_program_receives_tricky_arguments_unchanged()
    {
        // Runs the formatted line in pwsh and checks the arguments that printf prints back, byte for byte.
        // Skipped when pwsh (or /usr/bin/printf) is not installed.
        string? pwsh = FindOnPath("pwsh");
        if (pwsh is null || !File.Exists("/usr/bin/printf"))
        {
            return;
        }

        string[] args =
        {
            "a&b", "x|y", "x>out", "%TEMP%", "!x", "$(Write-Output unexpected)", "$env:PATH", "a b", "",
            "it's", "it\u2019s", "\"quoted\"", "C:\\dir\\", "caf\u00e9", "0x10", "-v",
        };
        string line = CommandLineFormatter.Format("/usr/bin/printf", new[] { "%s\\n" }.Concat(args).ToArray(), powerShell: true);
        string expected = string.Concat(args.Select(arg => arg + "\n"));

        string actual = RunPwsh(pwsh, line);
        if (actual != expected)
        {
            string mode = RunPwsh(pwsh, "$PSNativeCommandArgumentPassing").Trim();
            Assert.Fail(
                $"PSNativeCommandArgumentPassing={mode}\nline: {line}\nexpected: {Escape(expected)}\nactual:   {Escape(actual)}");
        }
    }

    [Fact]
    public void Wrap_posix_without_environment_runs_the_command_in_a_subshell_in_the_directory()
    {
        Assert.Equal(
            "(cd /work && prog arg)",
            CommandLineFormatter.Wrap("/work", Array.Empty<KeyValuePair<string, string?>>(), "prog arg", powerShell: false));
    }

    [Fact]
    public void Wrap_posix_quotes_a_directory_with_a_space()
    {
        Assert.Equal(
            "(cd '/my work' && prog)",
            CommandLineFormatter.Wrap("/my work", Array.Empty<KeyValuePair<string, string?>>(), "prog", powerShell: false));
    }

    [Fact]
    public void Wrap_posix_puts_removals_first_then_assignments_each_in_environment_order()
    {
        var env = new KeyValuePair<string, string?>[]
        {
            new("A", "x y"),
            new("GONE", null),
            new("B", "it's"),
        };

        Assert.Equal(
            "(cd /work && env -u GONE 'A=x y' 'B=it'\\''s' prog arg)",
            CommandLineFormatter.Wrap("/work", env, "prog arg", powerShell: false));
    }

    [Fact]
    public void Wrap_powershell_without_environment_pushes_the_location_and_pops_it_in_finally()
    {
        Assert.Equal(
            @"& { Push-Location -LiteralPath C:\work; try { tool.exe a } finally { Pop-Location } }",
            CommandLineFormatter.Wrap(@"C:\work", Array.Empty<KeyValuePair<string, string?>>(), "tool.exe a", powerShell: true));
    }

    [Fact]
    public void Wrap_powershell_with_environment_sets_removes_and_restores_each_name_always_quoted()
    {
        var env = new KeyValuePair<string, string?>[]
        {
            new("GONE", null),
            new("A", "it's"),
        };

        Assert.Equal(
            @"& { $saved = @{}; foreach ($name in @('GONE', 'A')) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }; " +
            @"Push-Location -LiteralPath C:\work; try { [Environment]::SetEnvironmentVariable('GONE', $null); " +
            @"[Environment]::SetEnvironmentVariable('A', 'it''s'); tool.exe a } finally { foreach ($name in $saved.Keys) { " +
            @"[Environment]::SetEnvironmentVariable($name, $saved[$name]) }; Pop-Location } }",
            CommandLineFormatter.Wrap(@"C:\work", env, "tool.exe a", powerShell: true));
    }

    private static string RunPwsh(string pwsh, string command, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var info = new ProcessStartInfo(pwsh)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(command);
        if (environment is not null)
        {
            foreach (KeyValuePair<string, string?> entry in environment)
            {
                info.Environment[entry.Key] = entry.Value;
            }
        }

        (string stdout, string stderr, int exitCode) = RunBounded(info, TimeSpan.FromSeconds(60));
        Assert.True(exitCode == 0, $"pwsh exited with {exitCode}: {stderr}");
        return stdout;
    }

    /// <summary>
    /// Runs a process with stdout and stderr read concurrently, and waits at most <paramref name="timeout"/>. A process that
    /// is still running (or whose output stays open) at the deadline is killed with its children, and the test fails.
    /// </summary>
    private static (string Stdout, string Stderr, int ExitCode) RunBounded(ProcessStartInfo info, TimeSpan timeout)
    {
        using Process process = Process.Start(info) ?? throw new InvalidOperationException($"{info.FileName} did not start");
        process.StandardInput.Close();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        bool finished = process.WaitForExit(timeout) && Task.WaitAll([stdout, stderr], timeout);
        if (!finished)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // already exited
            }

            Assert.Fail($"{info.FileName} did not finish within {timeout.TotalSeconds} s");
        }

        return (stdout.Result, stderr.Result, process.ExitCode);
    }

    private static string? FindOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists);

    private static string Escape(string text) => text.Replace("\n", "\\n", StringComparison.Ordinal);
}
