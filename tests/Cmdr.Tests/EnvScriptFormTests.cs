using Cmdr.Core.Config;
using Cmdr.Tui;

namespace Cmdr.Tests;

/// <summary>The environment script dialog's values: the program is required, the timeout is empty or a positive whole number, and the rest is built as typed.</summary>
public sealed class EnvScriptFormTests
{
    private static EnvScriptFormValues Values(
        string program = "sh",
        string args = "-c\necho hi",
        string cwd = "",
        string timeout = "",
        EnvScriptRerun rerun = EnvScriptRerun.Once) =>
        new(program, args, cwd, timeout, rerun);

    [Fact]
    public void Valid_values_have_no_errors()
    {
        Assert.Empty(EnvScriptForm.Validate(Values()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_program_is_an_error(string program)
    {
        Assert.Equal(new[] { "Program must not be blank." }, EnvScriptForm.Validate(Values(program: program)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("1")]
    [InlineData(" 45 ")]
    public void Empty_or_a_positive_whole_timeout_is_valid(string timeout)
    {
        Assert.Empty(EnvScriptForm.Validate(Values(timeout: timeout)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("soon")]
    public void Any_other_timeout_is_an_error(string timeout)
    {
        Assert.Single(EnvScriptForm.Validate(Values(timeout: timeout)));
    }

    [Fact]
    public void Timeout_text_parses_to_seconds_or_null_for_the_default()
    {
        Assert.True(EnvScriptForm.TryParseTimeout("", out int? empty));
        Assert.Null(empty);
        Assert.True(EnvScriptForm.TryParseTimeout(" 12 ", out int? twelve));
        Assert.Equal(12, twelve);
        Assert.False(EnvScriptForm.TryParseTimeout("0", out _));
        Assert.Equal(string.Empty, EnvScriptForm.TimeoutText(null));
        Assert.Equal("30", EnvScriptForm.TimeoutText(30));
    }

    [Fact]
    public void Build_trims_the_program_reads_the_args_by_line_and_drops_a_blank_working_dir()
    {
        EnvScriptSpec spec = EnvScriptForm.Build(Values(program: "  node  ", args: "tools/env.js\n--flag", cwd: "   ", timeout: " 9 ", rerun: EnvScriptRerun.EachStart));

        Assert.Equal("node", spec.Program);
        Assert.Equal(new[] { "tools/env.js", "--flag" }, spec.Args);
        Assert.Null(spec.Cwd);
        Assert.Equal(9, spec.TimeoutSeconds);
        Assert.Equal(EnvScriptRerun.EachStart, spec.Rerun);
    }

    [Fact]
    public void Build_keeps_the_working_dir_and_an_empty_timeout_is_the_default()
    {
        EnvScriptSpec spec = EnvScriptForm.Build(Values(cwd: " scripts ", timeout: ""));

        Assert.Equal("scripts", spec.Cwd);
        Assert.Null(spec.TimeoutSeconds);
    }

    [Fact]
    public void Kept_program_args_and_working_dir_are_saved_exactly_and_not_parsed()
    {
        var values = new EnvScriptFormValues(
            Program: "line one\nline two",
            Args: "ignored",
            Cwd: "ignored",
            Timeout: "",
            Rerun: EnvScriptRerun.Once,
            KeptProgram: new KeptValue("line one\nline two"),
            KeptCwd: new KeptValue(" padded "),
            KeptArgs: new[] { "a\r", "" });

        Assert.Empty(EnvScriptForm.Validate(values));
        EnvScriptSpec spec = EnvScriptForm.Build(values);

        Assert.Equal("line one\nline two", spec.Program);
        Assert.Equal(new[] { "a\r", "" }, spec.Args);
        Assert.Equal(" padded ", spec.Cwd);
    }

    [Fact]
    public void Summary_is_none_without_a_script_and_the_program_with_its_args_otherwise()
    {
        Assert.Equal("none", EnvScriptForm.Summary(null, 30));
        Assert.Equal("sh -c true", EnvScriptForm.Summary(new EnvScriptSpec { Program = "sh", Args = new List<string> { "-c", "true" } }, 30));
    }

    [Fact]
    public void Summary_cuts_a_long_text_to_the_width_with_an_ellipsis_and_shows_line_breaks_as_spaces()
    {
        var spec = new EnvScriptSpec { Program = "a\nb", Args = new List<string> { "a very long argument" } };

        string summary = EnvScriptForm.Summary(spec, 10);

        Assert.Equal(10, summary.Length);
        Assert.EndsWith("…", summary, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', summary);
    }
}
