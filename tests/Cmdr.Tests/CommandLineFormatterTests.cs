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

    private static string RunPwsh(string pwsh, string command)
    {
        var info = new ProcessStartInfo(pwsh)
        {
            RedirectStandardOutput = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(command);

        using Process process = Process.Start(info) ?? throw new InvalidOperationException("pwsh did not start");
        string output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "pwsh did not exit in time");
        return output;
    }

    private static string? FindOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists);

    private static string Escape(string text) => text.Replace("\n", "\\n", StringComparison.Ordinal);
}
